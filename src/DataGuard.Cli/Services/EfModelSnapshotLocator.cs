using System.Security;
using System.Text;

namespace DataGuard.Cli.Services;

/// <summary>
/// Result of <see cref="EfModelSnapshotLocator.TryLocate"/>: the snapshot source when exactly one usable candidate exists,
/// otherwise the <c>ACQUISITION:</c> line bodies explaining why none was used (empty when there was no candidate at all).
/// </summary>
/// <param name="Source">Text of the located <c>*ModelSnapshot.cs</c>, or null.</param>
/// <param name="RelativePath">Forward-slash path relative to the project root, or null.</param>
/// <param name="FullPath">Absolute path of the located file, or null.</param>
/// <param name="Diagnostics">Line bodies for stderr, already relative, not yet escaped.</param>
internal sealed record EfModelSnapshotLocation(string? Source, string? RelativePath, string? FullPath, IReadOnlyList<string> Diagnostics)
{
    internal static readonly EfModelSnapshotLocation None = new(null, null, null, Array.Empty<string>());

    internal bool Found => Source is not null;

    internal static EfModelSnapshotLocation Skipped(string diagnostic) => new(null, null, null, new[] { diagnostic });
}

/// <summary>
/// Finds the EF Core design-time <c>*ModelSnapshot.cs</c> under <c>--project</c> for the IDE-safe auto-detect. It is a filter
/// over the file list the same run already walked (<c>ProjectCSharpSqlSource.DiscoverSourceFiles</c>): no second walker, no
/// caps of its own. The file is opened once with a bounded read, so a file swapped between the check and the read cannot
/// exceed the parser's size limit. It never throws.
/// </summary>
internal static class EfModelSnapshotLocator
{
    internal const string SnapshotSuffix = "ModelSnapshot.cs";

    private const string SkippedPrefix = "ef model auto-detect skipped: ";

    /// <summary>True when the file name ends with <c>ModelSnapshot.cs</c> and no path segment is <c>node_modules</c>.</summary>
    internal static bool IsCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.GetFileName(path).EndsWith(SnapshotSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Selects, checks and reads the single <c>*ModelSnapshot.cs</c> among <paramref name="candidateFiles"/>.</summary>
    /// <param name="projectRoot">Directory the candidates must live under (the <c>--project</c> directory, or the directory of its file).</param>
    /// <param name="candidateFiles">Source files of the run; only <c>*ModelSnapshot.cs</c> entries are considered.</param>
    /// <param name="maximumSourceBytes">Read bound; a file longer than this is skipped without reading past the bound.</param>
    /// <param name="fileInfo">Seam for the attribute, link and length checks; defaults to <see cref="FileInfo"/>.</param>
    internal static EfModelSnapshotLocation TryLocate(
        string projectRoot,
        IEnumerable<string> candidateFiles,
        int maximumSourceBytes,
        Func<string, FileInfo>? fileInfo = null)
    {
        fileInfo ??= path => new FileInfo(path);
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
            var rootPrefix = root + Path.DirectorySeparatorChar;
            var matches = candidateFiles
                .Where(IsCandidate)
                .Select(Path.GetFullPath)
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 0)
            {
                return EfModelSnapshotLocation.None;
            }

            if (matches.Count > 1)
            {
                var named = string.Join(", ", matches.Take(2).Select(path => Relativize(root, path, comparison)));
                return EfModelSnapshotLocation.Skipped(
                    $"{matches.Count} ModelSnapshot.cs files found under --project ({named}); commit .dataguard-snapshot.json or keep one design-time snapshot per solution");
            }

            var fullPath = matches[0];
            var relativePath = Relativize(root, fullPath, comparison);
            if (!fullPath.StartsWith(rootPrefix, comparison))
            {
                return EfModelSnapshotLocation.Skipped($"{relativePath}: EF model not used: outside --project");
            }

            var info = fileInfo(fullPath);
            if (!info.Exists)
            {
                // On Unix the attributes of a missing file are -1 (every bit set) rather than an exception.
                return EfModelSnapshotLocation.Skipped($"{relativePath}: EF model not used: file not found");
            }

            var attributes = info.Attributes;
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 || info.ResolveLinkTarget(returnFinalTarget: false) is not null)
            {
                return EfModelSnapshotLocation.Skipped($"{relativePath}: EF model not used: symbolic link, junction or device");
            }

            // st_size is 0 for an empty file and for a FIFO/socket; neither can hold an entity, and not opening a FIFO
            // avoids blocking on a reader-less pipe.
            var length = info.Length;
            if (length == 0)
            {
                return EfModelSnapshotLocation.Skipped($"{relativePath}: EF model not used: empty file");
            }

            if (length > maximumSourceBytes)
            {
                return EfModelSnapshotLocation.Skipped(
                    $"{relativePath}: EF model not used: {length} bytes exceeds the {maximumSourceBytes}-byte limit");
            }

            // Open once, read at most bound + 1 bytes: the stat above is advisory, this is the enforced limit.
            var buffer = new byte[maximumSourceBytes + 1];
            var total = 0;
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            {
                int read;
                while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
                {
                    total += read;
                }
            }

            if (total > maximumSourceBytes)
            {
                return EfModelSnapshotLocation.Skipped(
                    $"{relativePath}: EF model not used: longer than the {maximumSourceBytes}-byte limit");
            }

            using var reader = new StreamReader(new MemoryStream(buffer, 0, total, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return new EfModelSnapshotLocation(reader.ReadToEnd(), relativePath, fullPath, Array.Empty<string>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            return EfModelSnapshotLocation.Skipped(SkippedPrefix + ex.Message);
        }
    }

    // Forward-slash path relative to the root; a path outside it is reduced to its file name so no absolute path is echoed.
    private static string Relativize(string root, string fullPath, StringComparison comparison)
    {
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            return Path.GetFileName(fullPath);
        }

        return Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
