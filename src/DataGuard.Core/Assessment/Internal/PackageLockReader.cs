using System.Text.Json;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>Extracts resolved NuGet identities from supported lock-file shapes without retaining source provenance.</summary>
internal static class PackageLockReader
{
    internal sealed record PackageLockInventory(
        IReadOnlyList<PackageCoordinate> Coordinates,
        bool IsComplete,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> TargetFrameworks);

    public static IReadOnlyList<PackageCoordinate> Extract(string workspaceRoot)
        => ExtractInventory(workspaceRoot).Coordinates;

    public static PackageLockInventory ExtractInventory(string workspaceRoot)
    {
        var coordinates = new List<PackageCoordinate>();
        var reasons = new List<string>();
        var targetFrameworks = new List<string>();
        IEnumerable<string> lockPaths;
        try
        {
            var enumeration = BoundedAssessmentEnumerator.EnumerateFilesWithStatus(workspaceRoot, "packages.lock.json");
            if (enumeration.Truncated)
            {
                reasons.Add("Lock-file discovery reached its safety cap.");
            }

            lockPaths = enumeration.Files
                .Where(path => !IsBuildOutput(path) && IsNonLinkedFile(workspaceRoot, path)).ToArray();
        }
        catch (IOException)
        {
            return new PackageLockInventory(Array.Empty<PackageCoordinate>(), false, new[] { "Lock-file discovery failed." }, Array.Empty<string>());
        }
        catch (UnauthorizedAccessException)
        {
            return new PackageLockInventory(Array.Empty<PackageCoordinate>(), false, new[] { "Lock-file discovery was unauthorized." }, Array.Empty<string>());
        }

        foreach (var path in lockPaths)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                var frameworks = root.TryGetProperty("dependencies", out var dependencySection) && dependencySection.ValueKind == JsonValueKind.Object
                    ? dependencySection
                    : root.TryGetProperty("targets", out var targetSection) && targetSection.ValueKind == JsonValueKind.Object
                        ? targetSection
                        : default;
                if (frameworks.ValueKind != JsonValueKind.Object)
                {
                    reasons.Add($"Lock file '{Path.GetFileName(path)}' has no supported framework sections.");
                    continue;
                }

                foreach (var framework in frameworks.EnumerateObject())
                {
                    targetFrameworks.Add(framework.Name);
                    if (framework.Value.ValueKind != JsonValueKind.Object)
                    {
                        reasons.Add($"Lock file '{Path.GetFileName(path)}' has malformed framework metadata.");
                        continue;
                    }

                    foreach (var dependency in framework.Value.EnumerateObject())
                    {
                        if (dependency.Value.ValueKind == JsonValueKind.Object
                            && dependency.Value.TryGetProperty("resolved", out var version)
                            && version.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(version.GetString()))
                        {
                            coordinates.Add(new PackageCoordinate(dependency.Name, version.GetString()!));
                        }
                        else
                        {
                            reasons.Add($"Lock file '{Path.GetFileName(path)}' has an unresolved dependency entry.");
                        }
                    }
                }
            }
            catch (IOException)
            {
                reasons.Add($"Lock file '{Path.GetFileName(path)}' could not be read.");
            }
            catch (JsonException)
            {
                reasons.Add($"Lock file '{Path.GetFileName(path)}' is malformed JSON.");
            }
        }

        return new PackageLockInventory(
            coordinates.Distinct().OrderBy(coordinate => coordinate.Name, StringComparer.Ordinal)
                .ThenBy(coordinate => coordinate.Version, StringComparer.Ordinal).ToArray(),
            reasons.Count == 0,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            targetFrameworks.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsNonLinkedFile(string workspaceRoot, string path)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.Equals(root, StringComparison.Ordinal)
                && !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            var current = root;
            if (File.ResolveLinkTarget(current, returnFinalTarget: false) is not null)
            {
                return false;
            }

            foreach (var segment in Path.GetRelativePath(root, fullPath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.ResolveLinkTarget(current, returnFinalTarget: false) is not null)
                {
                    return false;
                }
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
