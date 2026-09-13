namespace DataGuard.Core.Assessment.Internal;

/// <summary>Bounds filesystem discovery so assessment cannot materialize an unbounded tree.</summary>
internal static class BoundedAssessmentEnumerator
{
    internal const int MaxDiscoveredFiles = 10_000;

    internal static IReadOnlyList<string> EnumerateFiles(string root, string pattern)
        => EnumerateFilesWithStatus(root, pattern).Files;

    internal static BoundedEnumerationResult EnumerateFilesWithStatus(string root, string pattern)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            var candidates = Directory.EnumerateFiles(root, pattern, options)
                .Take(MaxDiscoveredFiles + 1)
                .ToArray();
            return new BoundedEnumerationResult(
                candidates.Take(MaxDiscoveredFiles).ToArray(),
                candidates.Length > MaxDiscoveredFiles);
        }
        catch (IOException)
        {
            return new BoundedEnumerationResult(Array.Empty<string>(), false);
        }
        catch (UnauthorizedAccessException)
        {
            return new BoundedEnumerationResult(Array.Empty<string>(), false);
        }
    }
}

internal sealed record BoundedEnumerationResult(IReadOnlyList<string> Files, bool Truncated);
