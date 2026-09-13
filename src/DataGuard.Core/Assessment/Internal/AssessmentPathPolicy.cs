namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Resolves assessment input paths below a workspace root without trusting string prefixes.
/// </summary>
internal static class AssessmentPathPolicy
{
    /// <summary>
    /// Validates lexical containment and every existing link target between the root and file.
    /// This is a best-effort check at open time; hostile concurrent filesystem changes remain a TOCTOU limitation.
    /// </summary>
    internal static bool TryResolveInsideRoot(
        string workspaceRoot,
        string candidatePath,
        out string resolvedPath,
        out string relativePath)
    {
        resolvedPath = string.Empty;
        relativePath = string.Empty;

        try
        {
            var lexicalRoot = Path.GetFullPath(workspaceRoot);
            var lexicalCandidate = Path.IsPathFullyQualified(candidatePath)
                ? Path.GetFullPath(candidatePath)
                : Path.GetFullPath(candidatePath, lexicalRoot);
            var relative = Path.GetRelativePath(lexicalRoot, lexicalCandidate);
            if (!IsRelativeDescendant(relative))
            {
                return false;
            }

            var resolvedRoot = ResolveExistingTarget(new DirectoryInfo(lexicalRoot));
            if (resolvedRoot is null || !Directory.Exists(resolvedRoot))
            {
                return false;
            }

            var current = resolvedRoot;
            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (Directory.Exists(current))
                {
                    current = ResolveExistingTarget(new DirectoryInfo(current)) ?? current;
                }
                else if (File.Exists(current))
                {
                    current = ResolveExistingTarget(new FileInfo(current)) ?? current;
                }

                if (!IsPathWithin(resolvedRoot, current))
                {
                    return false;
                }
            }

            resolvedPath = current;
            relativePath = relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsRelativeDescendant(string relative) =>
        relative != ".."
        && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
        && !Path.IsPathFullyQualified(relative);

    private static bool IsPathWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return IsRelativeDescendant(relative);
    }

    private static string? ResolveExistingTarget(FileSystemInfo info)
    {
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target?.FullName ?? info.FullName;
    }
}
