using System.Xml;

using System.Xml.Linq;
namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Reads legacy packages.config files (XML) without executing NuGet operations.
/// Path containment and size caps mirror ProjectInventoryReader.
/// </summary>
public static class PackagesConfigReader
{
    /// <summary>Reads package id/version pairs from a packages.config file. Returns error entry on malformed XML instead of throwing.</summary>
    public static (IReadOnlyList<PackageEntry> Packages, ToolError? Error) Read(string workspaceRoot, string configPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(configPath);
            if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.Ordinal))
            {
                return (Array.Empty<PackageEntry>(), new ToolError { Code = "DG1001", Path = configPath, Message = "packages.config resolves outside the requested workspace" });
            }

            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                return (Array.Empty<PackageEntry>(), new ToolError { Code = "DG1002", Path = configPath, Message = "packages.config not found" });
            }

            if (fileInfo.Length > ProjectInventoryReader.MaxFileBytes)
            {
                return (Array.Empty<PackageEntry>(), new ToolError { Code = "DG1290", Path = configPath, Message = $"packages.config exceeds {ProjectInventoryReader.MaxFileBytes} bytes cap" });
            }

            XDocument doc;
            using (var stream = fileInfo.OpenRead())
            {
                doc = XDocument.Load(stream, LoadOptions.None);
            }

            var packages = doc.Root?.Elements("package")
                .Select(e => new PackageEntry
                {
                    Id = (string?)e.Attribute("id") ?? string.Empty,
                    Version = (string?)e.Attribute("version") ?? string.Empty,
                    TargetFramework = (string?)e.Attribute("targetFramework"),
                })
                .Where(p => p.Id.Length > 0)
                .ToList() ?? new List<PackageEntry>();

            return (packages, null);
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidOperationException)
        {
            return (Array.Empty<PackageEntry>(), new ToolError { Code = "DG1003", Path = configPath, Message = $"invalid packages.config: {ex.GetType().Name}" });
        }
    }
}

/// <summary>One package row from packages.config.</summary>
public sealed record PackageEntry
{
    /// <summary>Package id.</summary>
    required public string Id { get; init; }

    /// <summary>Version string as written.</summary>
    required public string Version { get; init; }

    /// <summary>Optional targetFramework attribute as written.</summary>
    public string? TargetFramework { get; init; }
}
