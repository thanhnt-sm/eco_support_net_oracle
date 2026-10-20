using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using Microsoft.CodeAnalysis;

namespace DataGuard.Cli.Services;

/// <summary>Opt-in presentation filtering; never narrows acquisition, rule inputs or exit status.</summary>
internal static class ScopedReporting
{
    internal static bool TryResolve(string? requested, string? context, string format, bool verbose, out string? file)
    {
        file = null;
        if (requested is null)
        {
            return true;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(context) ||
                !string.Equals(format.Trim(), "text", StringComparison.OrdinalIgnoreCase) || verbose ||
                (!Directory.Exists(context) && (!File.Exists(context) ||
                    !(context.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || context.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))))
            {
                Console.Error.WriteLine("--scope-file requires a directory/.csproj/.sln --project context, --format text, and no --verbose.");
                return false;
            }

            var root = Directory.Exists(context) ? Path.GetFullPath(context) : Path.GetDirectoryName(Path.GetFullPath(context))!;
            var selected = Path.GetFullPath(requested, root);
            if (!ProjectCSharpSqlSource.DiscoverSourceFiles(context).Any(candidate => SamePath(candidate, selected)))
            {
                Console.Error.WriteLine("--scope-file must match a discovered C# file in the full --project context.");
                return false;
            }

            file = selected;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine("--scope-file or --project could not be resolved.");
            return false;
        }
    }

    internal static bool Matches(Location? location, string file) =>
        location is not null && !string.IsNullOrEmpty(location.GetLineSpan().Path) && SamePath(location.GetLineSpan().Path, file);

    internal static IReadOnlyList<ContractViolation> Select(IReadOnlyList<ContractViolation> violations, string? file) =>
        file is null ? violations : violations.Where(v => v.Location is null || string.IsNullOrEmpty(v.Location.GetLineSpan().Path) || Matches(v.Location, file)).ToArray();

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
