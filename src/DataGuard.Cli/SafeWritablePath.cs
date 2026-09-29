namespace DataGuard.Cli;

/// <summary>
/// Write-path policy shared by every CLI file sink (SARIF, summary.json, <c>init</c>/<c>config</c> output).
/// The target and its immediate parent may never be reparse points. Further ancestors are walked only while
/// they lie strictly inside the workspace root: a committed link such as <c>reports -&gt; ~/.ssh</c>
/// inside a hostile checkout is rejected, while a junctioned <c>%TEMP%</c> (host-chosen, outside the workspace)
/// stays writable. The walk never reaches the workspace root itself or anything above it, because those
/// directories are chosen by the host, not by the repository.
/// </summary>
public static class SafeWritablePath
{
    /// <summary>Returns true when <paramref name="path"/> may be created or overwritten under the policy above.</summary>
    /// <param name="path">Relative or absolute output path.</param>
    /// <param name="workspaceRoot">Directory that bounds the ancestor walk; normally the current directory.</param>
    public static bool IsSafe(string path, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parent) || IsLink(parent) || IsLink(fullPath))
            {
                return false;
            }

            var rootPrefix = NormalizeRootPrefix(workspaceRoot);
            if (rootPrefix is null)
            {
                return true;
            }

            // Ancestors strictly below the workspace root are repository-controlled: none may be a link.
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var candidate = Path.GetDirectoryName(parent);
            while (candidate is not null && candidate.StartsWith(rootPrefix, comparison))
            {
                if (IsLink(candidate))
                {
                    return false;
                }

                candidate = Path.GetDirectoryName(candidate);
            }

            return true;
        }
        catch (Exception)
        {
            // Any failure to resolve or inspect the path is treated as unsafe (fail closed).
            return false;
        }
    }

    /// <summary>Full workspace path with exactly one trailing separator, so <c>C:\ws</c> never matches <c>C:\ws2</c>.</summary>
    private static string? NormalizeRootPrefix(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        var full = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }

    private static bool IsLink(string candidate)
    {
        try
        {
            if (new FileInfo(candidate).LinkTarget != null || new DirectoryInfo(candidate).LinkTarget != null)
            {
                return true;
            }

            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                var attrs = File.GetAttributes(candidate);
                return attrs != (FileAttributes)(-1) && attrs.HasFlag(FileAttributes.ReparsePoint);
            }

            return false;
        }
        catch (Exception)
        {
            // Not inspectable (permissions, malformed segment): a non-existent candidate cannot be a link yet.
            return false;
        }
    }
}
