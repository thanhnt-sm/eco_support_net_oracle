namespace DataGuard.Cli.Services;

/// <summary>Write-path policy and atomic writes shared by every user-chosen output sink.</summary>
internal static class OutputSinks
{
    // Target and parent may never be links; repository-controlled ancestors (inside the current directory) are
    // walked too, host-chosen ones (a junctioned %TEMP%) are not. See SafeWritablePath.
    internal static bool IsSafeWritablePath(string path) => SafeWritablePath.IsSafe(path, Directory.GetCurrentDirectory());

    // Shared refusal for every user-chosen output sink (validate formats, oracle-check SARIF). Runs before any work or
    // connection; exit 4 is the operational tool-error code, the same one assess uses for its SARIF sink.
    internal static bool RefuseUnsafeOutput(string outputPath, string artifact)
    {
        if (IsSafeWritablePath(outputPath))
        {
            return false;
        }

        Console.Error.WriteLine($"Refusing to write {artifact} through a symbolic link or invalid path.");
        Environment.ExitCode = 4;
        return true;
    }

    internal static async Task WriteTextAtomicallyAsync(string outputPath, string content, CancellationToken cancellationToken)
    {
        if (!IsSafeWritablePath(outputPath))
        {
            throw new InvalidOperationException($"Refusing to write to unsafe path: {outputPath}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            for (var attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    File.Move(tempPath, outputPath, overwrite: true);
                    break;
                }
                catch (Exception ex) when (attempt < 8 && (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException))
                {
                    Directory.CreateDirectory(directory);
                    await Task.Delay(25 * (1 << Math.Min(attempt - 1, 6)), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    // Echoes a path relative to the workspace (or "." for the root itself); paths outside it are returned unchanged.
    internal static string RelativizeToWorkspace(string workspaceRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return path ?? string.Empty;
        }

        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(path));
            var escapesRoot = relative == ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("../", StringComparison.Ordinal)
                || Path.IsPathFullyQualified(relative);
            return escapesRoot ? path : relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return path;
        }
    }
}
