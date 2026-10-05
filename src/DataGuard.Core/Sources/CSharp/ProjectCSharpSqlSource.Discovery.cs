using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// Source-file discovery, the syntax type index and compilation references.
/// </summary>
public sealed partial class ProjectCSharpSqlSource
{
    private static readonly ConcurrentDictionary<string, MetadataReference> MetadataReferenceCache = new(StringComparer.Ordinal);

    public static List<string> DiscoverSourceFiles(string path)
    {
        var files = new List<string>();

        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cs")
            {
                files.Add(path);
                return files;
            }

            if (ext is ".csproj" or ".sln")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    ScanDirectory(dir, files, "*.cs");
                }

                return files;
            }
        }

        if (Directory.Exists(path))
        {
            ScanDirectory(path, files, "*.cs");
        }

        return files;
    }

    /// <summary>
    /// Returns the compilation references for the scanned project in a deterministic (sorted) order. When the project
    /// (or any project under a scanned directory/solution) has been restored, the compile assets listed in its
    /// <c>obj/project.assets.json</c> are used, restricted to files under the NuGet packages folder
    /// (<c>NUGET_PACKAGES</c> or <c>~/.nuget/packages</c>), plus the running .NET shared framework for the BCL.
    /// Otherwise the trusted platform assemblies of the running host are used.
    /// </summary>
    internal static IReadOnlyList<MetadataReference> ResolveMetadataReferences(string projectOrPath)
    {
        return ResolveReferencePaths(projectOrPath)
            .Select(path => MetadataReferenceCache.GetOrAdd(path, p => MetadataReference.CreateFromFile(p)))
            .ToList();
    }

    internal static IReadOnlyList<string> ResolveReferencePaths(string projectOrPath)
    {
        var platform = GetTrustedPlatformAssemblies();
        var fromAssets = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetsFile in FindAssetsFiles(projectOrPath))
        {
            foreach (var path in ReadCompileAssets(assetsFile))
            {
                fromAssets.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        if (fromAssets.Count == 0)
        {
            return platform;
        }

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var result = new List<string>(fromAssets.Values);
        foreach (var path in platform)
        {
            if (string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.Ordinal) &&
                !fromAssets.ContainsKey(Path.GetFileNameWithoutExtension(path)))
            {
                result.Add(path);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> GetTrustedPlatformAssemblies()
    {
        var paths = new List<string>();
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa && !string.IsNullOrWhiteSpace(tpa))
        {
            paths.AddRange(tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(p)));
        }

        if (paths.Count == 0)
        {
            // Single-file hosts have no TPA list; use what is loaded.
            paths.AddRange(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location) && File.Exists(a.Location))
                .Select(a => a.Location));
        }

        return paths
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(p => p, StringComparer.Ordinal).First())
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> FindAssetsFiles(string projectOrPath)
    {
        var candidates = new SortedSet<string>(StringComparer.Ordinal);
        var full = Path.GetFullPath(projectOrPath);
        var root = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return new List<string>();
        }

        candidates.Add(Path.Combine(root, "obj", "project.assets.json"));
        if (!full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var projects = new List<string>();
            ScanDirectory(root, projects, "*.csproj");
            foreach (var project in projects)
            {
                var projectDir = Path.GetDirectoryName(project);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    candidates.Add(Path.Combine(projectDir, "obj", "project.assets.json"));
                }
            }
        }

        return candidates.Where(File.Exists).ToList();
    }

    private static List<string> GetNuGetPackageRoots()
    {
        var roots = new List<string>();
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(env))
        {
            roots.Add(Path.GetFullPath(env));
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            roots.Add(Path.GetFullPath(Path.Combine(home, ".nuget", "packages")));
        }

        return roots
            .Select(r => r.EndsWith(Path.DirectorySeparatorChar) ? r : r + Path.DirectorySeparatorChar)
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reads the compile-time assets of one framework target (the ordinally greatest non-RID target) of a restored
    /// project. Malformed files yield nothing.
    /// </summary>
    private static List<string> ReadCompileAssets(string assetsFile)
    {
        var result = new List<string>();
        var roots = GetNuGetPackageRoots();
        if (roots.Count == 0)
        {
            return result;
        }

        try
        {
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream);
            var rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object ||
                !rootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            var target = targets.EnumerateObject()
                .Where(t => !t.Name.Contains('/', StringComparison.Ordinal))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .LastOrDefault();
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var library in target.Value.EnumerateObject().OrderBy(l => l.Name, StringComparer.Ordinal))
            {
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package" ||
                    !library.Value.TryGetProperty("compile", out var compile) || compile.ValueKind != JsonValueKind.Object ||
                    !libraries.TryGetProperty(library.Name, out var libraryInfo) ||
                    !libraryInfo.TryGetProperty("path", out var libraryPath) || libraryPath.GetString() is not { Length: > 0 } relativeLibraryPath)
                {
                    continue;
                }

                foreach (var asset in compile.EnumerateObject())
                {
                    if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var packageRoot in roots)
                    {
                        var candidate = Path.GetFullPath(Path.Combine(packageRoot, relativeLibraryPath, asset.Name));
                        if (candidate.StartsWith(packageRoot, StringComparison.Ordinal) && File.Exists(candidate))
                        {
                            result.Add(candidate);
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new List<string>();
        }

        return result;
    }

    private static void ScanDirectory(string rootDir, List<string> files, string pattern)
    {
        var stack = new Stack<string>();
        var pathComparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var visited = new HashSet<string>(pathComparer);
        stack.Push(rootDir);
        visited.Add(Path.GetFullPath(rootDir));

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(currentDir, pattern))
                {
                    var rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                    if (rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                        rel.Contains("/bin/") ||
                        rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                        rel.Contains("/obj/") ||
                        rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                        rel.Contains("/.git/") ||
                        rel.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) ||
                        rel.Contains("/.vs/"))
                    {
                        continue;
                    }

                    files.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Non-fatal if files in a specific directory cannot be enumerated
            }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    var dirInfo = new DirectoryInfo(subDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(subDir);
                    if (!visited.Add(fullPath))
                    {
                        continue;
                    }

                    var dirName = Path.GetFileName(subDir);
                    if (dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(".vs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    stack.Push(subDir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Non-fatal if subdirectories cannot be enumerated
            }
        }
    }

    private static Dictionary<string, List<TypeDeclarationSyntax>> IndexSyntaxTypes(
        IEnumerable<SyntaxTree> trees,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<TypeDeclarationSyntax>>(StringComparer.Ordinal);
        var disambiguatedByName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tree in trees)
        {
            var root = tree.GetRoot(cancellationToken);
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var name = typeDecl.Identifier.ValueText;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var outerTypes = typeDecl.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().ToList();
                var typeHierarchy = outerTypes.Count > 0
                    ? string.Join(".", outerTypes.Select(t => t.Identifier.ValueText)) + "." + name
                    : name;
                var ns = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;
                var qualifiedName = string.IsNullOrEmpty(ns) ? typeHierarchy : $"{ns}.{typeHierarchy}";

                // 1. Index under qualified name
                if (!result.TryGetValue(qualifiedName, out var qList))
                {
                    qList = new List<TypeDeclarationSyntax>();
                    result[qualifiedName] = qList;
                }

                qList.Add(typeDecl);

                // 2. Index under type hierarchy if nested (e.g. Parent.Child)
                if (!string.Equals(typeHierarchy, qualifiedName, StringComparison.Ordinal))
                {
                    if (!result.TryGetValue(typeHierarchy, out var hList))
                    {
                        hList = new List<TypeDeclarationSyntax>();
                        result[typeHierarchy] = hList;
                    }

                    hList.Add(typeDecl);
                }

                // 3. Index under simple name if unqualified
                if (!string.Equals(name, qualifiedName, StringComparison.Ordinal) && !string.Equals(name, typeHierarchy, StringComparison.Ordinal))
                {
                    if (!disambiguatedByName.TryGetValue(name, out var existingNs))
                    {
                        disambiguatedByName[name] = ns;
                        if (!result.TryGetValue(name, out var sList))
                        {
                            sList = new List<TypeDeclarationSyntax>();
                            result[name] = sList;
                        }

                        sList.Add(typeDecl);
                    }
                    else if (string.Equals(existingNs, ns, StringComparison.Ordinal))
                    {
                        // Same namespace partial class part
                        result[name].Add(typeDecl);
                    }
                    else
                    {
                        // Ambiguous simple name across different namespaces
                        result.Remove(name);
                        disambiguatedByName[name] = "<ambiguous>";
                    }
                }
            }
        }

        return result;
    }
}
