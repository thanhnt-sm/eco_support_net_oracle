using System.Xml;

using System.Xml.Linq;
using DataGuard.Core.Assessment;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Reads MSBuild project files (SDK-style and legacy) using the standard XML API.
/// Never executes builds or restores; purely declarative reads with path containment.
/// </summary>
public static class ProjectInventoryReader
{
    /// <summary>Maximum file size accepted for a single project/config file (bytes).</summary>
    public const long MaxFileBytes = 2_000_000;

    /// <summary>Reads inventory facts for one project file. Returns an error result instead of throwing on malformed input.</summary>
    public static ProjectFacts Read(string workspaceRoot, string projectPath)
    {
        var facts = new ProjectFacts { ProjectPath = Path.GetRelativePath(workspaceRoot, projectPath) };
        try
        {
            var fullPath = Path.GetFullPath(projectPath);
            if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.Ordinal))
            {
                return facts.WithError("DG1001", "project path resolves outside the requested workspace");
            }

            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                return facts.WithError("DG1002", "project file not found");
            }

            if (fileInfo.Length > MaxFileBytes)
            {
                return facts.WithError("DG1290", $"project file exceeds {MaxFileBytes} bytes cap");
            }

            XDocument doc;
            using (var stream = fileInfo.OpenRead())
            {
                doc = XDocument.Load(stream, LoadOptions.None);
            }

            var root = doc.Root ?? throw new InvalidOperationException("empty XML document");
            var isSdkStyle = root.Attribute("Sdk") is not null;
            var sdks = new List<string>();
            if (root.Attribute("Sdk")?.Value is { Length: > 0 } sdkAttr)
            {
                sdks.AddRange(sdkAttr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            var tfms = root.Descendants("TargetFramework").Select(e => e.Value.Trim())
                .Concat(root.Descendants("TargetFrameworks").SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var packageRefs = root.Descendants("PackageReference")
                .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"))
                .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct().ToList();

            var projRefs = root.Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();

            var packageConfig = root.Descendants().Any() && File.Exists(Path.Combine(fullPath, "..", "packages.config"));

            facts = facts with
            {
                IsSdkStyle = isSdkStyle,
                Sdks = sdks,
                TargetFrameworks = tfms,
                PackageReferences = packageRefs,
                ProjectReferences = projRefs,
                UsesPackagesConfig = packageConfig,
                HasLockFile = File.Exists(Path.Combine(fullPath, "..", "packages.lock.json")),
            };
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidOperationException)
        {
            return facts.WithError("DG1003", $"invalid project metadata: {ex.GetType().Name}");
        }

        return facts;
    }
}

/// <summary>Immutable inventory facts extracted from one project file.</summary>
public sealed record ProjectFacts
{
    /// <summary>Workspace-relative path of the project file.</summary>
    required public string ProjectPath { get; init; }

    /// <summary>True when the csproj has an Sdk attribute (SDK-style).</summary>
    public bool? IsSdkStyle { get; init; }

    /// <summary>Sdk attribute values when present.</summary>
    public IReadOnlyList<string> Sdks { get; init; } = Array.Empty<string>();

    /// <summary>All TargetFramework/TargetFrameworks values.</summary>
    public IReadOnlyList<string> TargetFrameworks { get; init; } = Array.Empty<string>();

    /// <summary>Distinct PackageReference Include values.</summary>
    public IReadOnlyList<string> PackageReferences { get; init; } = Array.Empty<string>();

    /// <summary>ProjectReference Include values as written in the file.</summary>
    public IReadOnlyList<string> ProjectReferences { get; init; } = Array.Empty<string>();

    /// <summary>True when a sibling packages.config exists (legacy format).</summary>
    public bool? UsesPackagesConfig { get; init; }

    /// <summary>True when a sibling packages.lock.json exists.</summary>
    public bool? HasLockFile { get; init; }

    /// <summary>Operational error encountered while reading this project, if any.</summary>
    public ToolError? Error { get; init; }

    /// <summary>Returns a copy carrying an operational error so callers can keep assessing siblings.</summary>
    public ProjectFacts WithError(string code, string message) => this with { Error = new ToolError { Code = code, Path = ProjectPath, Message = message } };

    /// <summary>Solution-style marker: unknown until read succeeds.</summary>
    public bool ReadFailed => Error is not null;
}
