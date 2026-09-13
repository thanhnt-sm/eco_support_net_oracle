using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataGuard.Cli.Hooks;

/// <summary>
/// Installs pre-commit hooks for Husky, lefthook, or native git hooks.
/// </summary>
public static class PreCommitHookInstaller
{
    /// <summary>
    /// Installs pre-commit hook for the current repository.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<InstallResult> InstallAsync(
        string? repoRoot = null,
        HookType hookType = HookType.Auto,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var root = repoRoot ?? FindGitRoot();
        if (string.IsNullOrEmpty(root))
        {
            return InstallResult.Failed("Not a git repository (no .git directory or gitdir file found)");
        }

        var gitDirectory = ResolveGitDirectory(root);
        if (gitDirectory is null)
        {
            return InstallResult.Failed("Git metadata directory could not be resolved.");
        }

        var detectedType = hookType == HookType.Auto ? DetectHookType(root) : hookType;
        var hookPath = Path.Combine(gitDirectory, "hooks", "pre-commit");
        var huskyDir = Path.Combine(root, ".husky");
        var lefthookPath = Path.Combine(root, "lefthook.yml");

        try
        {
            switch (detectedType)
            {
                case HookType.Husky:
                    return await InstallHuskyHookAsync(root, huskyDir, force, cancellationToken);
                case HookType.Lefthook:
                    return await InstallLefthookConfigAsync(root, lefthookPath, force, cancellationToken);
                case HookType.NativeGit:
                    return await InstallNativeGitHookAsync(root, hookPath, force, cancellationToken);
                default:
                    // Try all in order of preference
                    var huskyResult = await InstallHuskyHookAsync(root, huskyDir, force, cancellationToken);
                    if (huskyResult.Success)
                    {
                        return huskyResult;
                    }

                    var lefthookResult = await InstallLefthookConfigAsync(root, lefthookPath, force, cancellationToken);
                    if (lefthookResult.Success)
                    {
                        return lefthookResult;
                    }

                    return await InstallNativeGitHookAsync(root, hookPath, force, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return InstallResult.Failed($"Installation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Uninstalls pre-commit hooks.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<UninstallResult> UninstallAsync(
        string? repoRoot = null,
        CancellationToken cancellationToken = default)
    {
        var root = repoRoot ?? FindGitRoot();
        if (string.IsNullOrEmpty(root))
        {
            return UninstallResult.Failed("Not a git repository");
        }

        var gitDirectory = ResolveGitDirectory(root);
        if (gitDirectory is null)
        {
            return UninstallResult.Failed("Git metadata directory could not be resolved.");
        }

        var results = new List<string>();

        // Remove only hooks/configurations carrying our managed marker. User
        // hooks and existing lefthook configuration are never uninstall targets.
        var hookPath = Path.Combine(gitDirectory, "hooks", "pre-commit");
        if (!HasSymbolicLinkAtOrBelow(gitDirectory, hookPath) && IsManagedFile(hookPath))
        {
            File.Delete(hookPath);
            results.Add("Removed .git/hooks/pre-commit");
        }

        // Remove husky
        var huskyDir = Path.Combine(root, ".husky");
        if (Directory.Exists(huskyDir) && !HasSymbolicLinkAtOrBelow(root, huskyDir))
        {
            var huskyHook = Path.Combine(huskyDir, "pre-commit");
            if (!HasSymbolicLinkAtOrBelow(root, huskyHook) && IsManagedFile(huskyHook))
            {
                File.Delete(huskyHook);
                results.Add("Removed .husky/pre-commit");
            }
        }

        // Remove lefthook config
        var lefthookPath = Path.Combine(root, "lefthook.yml");
        if (!HasSymbolicLinkAtOrBelow(root, lefthookPath) && IsManagedFile(lefthookPath))
        {
            File.Delete(lefthookPath);
            results.Add("Removed lefthook.yml");
        }

        return UninstallResult.Succeeded($"Uninstalled: {string.Join(", ", results)}");
    }

    /// <summary>
    /// Gets status of installed hooks.
    /// </summary>
    /// <returns></returns>
    public static HookStatus GetStatus(string? repoRoot = null)
    {
        var root = repoRoot ?? FindGitRoot();
        if (string.IsNullOrEmpty(root))
        {
            return new HookStatus { IsGitRepo = false };
        }

        var gitDirectory = ResolveGitDirectory(root);
        if (gitDirectory is null)
        {
            return new HookStatus { IsGitRepo = true };
        }

        var hookPath = Path.Combine(gitDirectory, "hooks", "pre-commit");
        var huskyPath = Path.Combine(root, ".husky", "pre-commit");
        var lefthookPath = Path.Combine(root, "lefthook.yml");

        var nativeGitHook = !HasSymbolicLinkAtOrBelow(gitDirectory, hookPath) && File.Exists(hookPath);
        var husky = !HasSymbolicLinkAtOrBelow(root, huskyPath) && File.Exists(huskyPath);
        var lefthook = !HasSymbolicLinkAtOrBelow(root, lefthookPath) && File.Exists(lefthookPath);
        var dataGuardManaged = (nativeGitHook && IsManagedFile(hookPath))
            || (husky && IsManagedFile(huskyPath))
            || (lefthook && IsManagedFile(lefthookPath));

        return new HookStatus
        {
            IsGitRepo = true,
            NativeGitHook = nativeGitHook,
            Husky = husky,
            Lefthook = lefthook,
            AnyInstalled = nativeGitHook || husky || lefthook,
            DataGuardManaged = dataGuardManaged,
        };
    }

    private static string? FindGitRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            var marker = Path.Combine(current, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    private static string? ResolveGitDirectory(string repoRoot)
    {
        var marker = Path.Combine(repoRoot, ".git");
        if (IsSymbolicLink(marker))
        {
            return null;
        }

        if (Directory.Exists(marker))
        {
            return marker;
        }

        if (!File.Exists(marker))
        {
            return null;
        }

        var line = File.ReadLines(marker).FirstOrDefault();
        const string prefix = "gitdir:";
        if (line is null || !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var location = line.Substring(prefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var resolved = Path.IsPathRooted(location)
            ? Path.GetFullPath(location)
            : Path.GetFullPath(Path.Combine(repoRoot, location));
        var commonDirectoryFile = Path.Combine(resolved, "commondir");
        var commonDirectory = IsRegularFile(commonDirectoryFile)
            ? File.ReadLines(commonDirectoryFile).FirstOrDefault()?.Trim()
            : null;
        var commonDirectoryPath = string.IsNullOrWhiteSpace(commonDirectory)
            ? null
            : Path.IsPathRooted(commonDirectory)
                ? Path.GetFullPath(commonDirectory)
                : Path.GetFullPath(Path.Combine(resolved, commonDirectory));

        return Directory.Exists(resolved)
            && !IsSymbolicLink(resolved)
            && IsRegularFile(Path.Combine(resolved, "HEAD"))
            && commonDirectoryPath is not null
            && Directory.Exists(commonDirectoryPath)
            && !IsSymbolicLink(commonDirectoryPath)
            ? resolved
            : null;
    }

    private static HookType DetectHookType(string repoRoot)
    {
        var huskyDir = Path.Combine(repoRoot, ".husky");
        if (Directory.Exists(huskyDir))
        {
            return HookType.Husky;
        }

        var lefthookPath = Path.Combine(repoRoot, "lefthook.yml");
        if (File.Exists(lefthookPath))
        {
            return HookType.Lefthook;
        }

        return HookType.NativeGit;
    }

    private static async Task<InstallResult> InstallHuskyHookAsync(
        string repoRoot, string huskyDir, bool force, CancellationToken cancellationToken)
    {
        try
        {
            if (HasSymbolicLinkAtOrBelow(repoRoot, huskyDir))
            {
                return InstallResult.Failed("Husky hook directory contains a symbolic link and will be preserved.");
            }

            Directory.CreateDirectory(huskyDir);
            var hookPath = Path.Combine(huskyDir, "pre-commit");
            if (HasSymbolicLinkAtOrBelow(repoRoot, hookPath))
            {
                return InstallResult.Failed("Husky hook path contains a symbolic link and will be preserved.");
            }

            if (IsSymbolicLink(hookPath))
            {
                return InstallResult.Failed("Husky pre-commit hook is a symbolic link and will be preserved.");
            }

            if (File.Exists(hookPath) && !IsManagedFile(hookPath))
            {
                return InstallResult.Failed("Husky pre-commit hook is not DataGuard-managed and will be preserved.");
            }

            var hookContent = GenerateHuskyHook();
            await WriteAtomicallyNoFollowAsync(hookPath, repoRoot, hookContent, cancellationToken);
            SetExecutableOnUnix(hookPath);

            return InstallResult.Succeeded("Husky pre-commit hook installed at .husky/pre-commit");
        }
        catch (Exception ex)
        {
            return InstallResult.Failed($"Husky installation failed: {ex.Message}");
        }
    }

    private static async Task<InstallResult> InstallLefthookConfigAsync(
        string repoRoot, string lefthookPath, bool force, CancellationToken cancellationToken)
    {
        try
        {
            if (HasSymbolicLinkAtOrBelow(repoRoot, lefthookPath))
            {
                return InstallResult.Failed("lefthook.yml path contains a symbolic link and will be preserved.");
            }

            if (IsSymbolicLink(lefthookPath))
            {
                return InstallResult.Failed("lefthook.yml is a symbolic link and will be preserved.");
            }

            if (File.Exists(lefthookPath) && !IsManagedFile(lefthookPath))
            {
                return InstallResult.Failed("lefthook.yml is not DataGuard-managed and will be preserved.");
            }

            var config = GenerateLefthookConfig();
            await WriteAtomicallyNoFollowAsync(lefthookPath, repoRoot, config, cancellationToken);

            return InstallResult.Succeeded("Lefthook configuration installed at lefthook.yml");
        }
        catch (Exception ex)
        {
            return InstallResult.Failed($"Lefthook installation failed: {ex.Message}");
        }
    }

    private static async Task<InstallResult> InstallNativeGitHookAsync(
        string repoRoot, string hookPath, bool force, CancellationToken cancellationToken)
    {
        try
        {
            var hooksDir = Path.GetDirectoryName(hookPath)!;
            var gitDirectory = Path.GetDirectoryName(hooksDir)!;
            if (HasSymbolicLinkAtOrBelow(gitDirectory, hooksDir))
            {
                return InstallResult.Failed("Native git hook directory contains a symbolic link and will be preserved.");
            }

            Directory.CreateDirectory(hooksDir);
            if (HasSymbolicLinkAtOrBelow(gitDirectory, hookPath))
            {
                return InstallResult.Failed("Native git hook path contains a symbolic link and will be preserved.");
            }

            if (IsSymbolicLink(hookPath))
            {
                return InstallResult.Failed("Native git pre-commit hook is a symbolic link and will be preserved.");
            }

            if (File.Exists(hookPath) && !IsManagedFile(hookPath))
            {
                return InstallResult.Failed("Native git pre-commit hook is not DataGuard-managed and will be preserved.");
            }

            var hookContent = GenerateNativeGitHook();
            await WriteAtomicallyNoFollowAsync(hookPath, gitDirectory, hookContent, cancellationToken);

            SetExecutableOnUnix(hookPath);

            return InstallResult.Succeeded("Native git pre-commit hook installed at .git/hooks/pre-commit");
        }
        catch (Exception ex)
        {
            return InstallResult.Failed($"Native git hook installation failed: {ex.Message}");
        }
    }

    private static bool IsManagedFile(string path) =>
        !IsSymbolicLink(path)
        && File.Exists(path)
        && File.ReadAllText(path).Contains("DataGuard pre-commit hook", StringComparison.Ordinal);

    private static async Task WriteAtomicallyNoFollowAsync(string path, string trustedAnchor, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (HasSymbolicLinkAtOrBelow(trustedAnchor, directory))
        {
            throw new IOException("Target directory contains a symbolic link.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (HasSymbolicLinkAtOrBelow(trustedAnchor, directory))
            {
                throw new IOException("Target directory changed to a symbolic link.");
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsRegularFile(string path) => File.Exists(path) && !IsSymbolicLink(path);

    private static bool HasSymbolicLinkAtOrBelow(string anchor, string path)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(anchor));
            var target = Path.GetFullPath(path);
            if (!target.Equals(root, StringComparison.Ordinal) && !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }

            if (IsSymbolicLink(root))
            {
                return true;
            }

            var relative = Path.GetRelativePath(root, target);
            var current = root;
            foreach (var component in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrEmpty(component) || component == ".")
                {
                    continue;
                }

                current = Path.Combine(current, component);
                if (IsSymbolicLink(current))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
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
            return true;
        }
    }

    private static void SetExecutableOnUnix(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static string GenerateHuskyHook()
    {
        return @"#!/usr/bin/env sh
. ""$(dirname -- ""$0"")/../husky.sh""

# DataGuard pre-commit hook
echo ""🔍 Running DataGuard pre-commit validation...""

# Run DataGuard validation in offline mode (fast, no DB)
if command -v dataguard >/dev/null 2>&1; then
    dataguard validate --format text
    exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        echo ""❌ DataGuard validation failed. Fix issues before committing.""
        echo ""💡 Run 'dataguard validate' to see details.""
        exit 1
    fi
    echo ""✅ DataGuard validation passed.""
else
    echo ""⚠ DataGuard CLI not found. Skipping validation.""
    echo ""   Install with: dotnet tool install -g DataGuard.CLI""
fi

exit 0
";
    }

    private static string GenerateNativeGitHook()
    {
        return @"#!/bin/sh
# DataGuard pre-commit hook (native git)
# Generated by DataGuard CLI

echo ""🔍 Running DataGuard pre-commit validation...""

# Check if dataguard is available
if command -v dataguard >/dev/null 2>&1; then
    dataguard validate --format text
    exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        echo ""❌ DataGuard validation failed. Fix issues before committing.""
        echo ""💡 Run 'dataguard validate' to see details.""
        exit 1
    fi
    echo ""✅ DataGuard validation passed.""
else
    echo ""⚠ DataGuard CLI not found. Skipping validation.""
    echo ""   Install with: dotnet tool install -g DataGuard.CLI""
fi

exit 0
";
    }

    private static string GenerateLefthookConfig()
    {
        return @"# lefthook.yml - DataGuard configuration
# Generated by DataGuard CLI
# Install: npm install -g lefthook && lefthook install

pre-commit:
  parallel: true
  commands:
    dataguard-validate:
      tags: dotnet
      run: dotnet dataguard validate --format text
      glob: ""*.cs""
      exclude:
        - ""**/bin/**""
        - ""**/obj/**""
        - ""**/Migrations/**""
      stage_fixed: true
";
    }
}

/// <summary>
/// Result of hook installation.
/// </summary>
public sealed record InstallResult(
    bool Success,
    string Message,
    HookType InstalledType = HookType.None)
{
    public static InstallResult Succeeded(string message, HookType type = HookType.NativeGit)
        => new(true, message, type);

    public static InstallResult Failed(string message)
        => new(false, message, HookType.None);
}

/// <summary>
/// Result of hook uninstallation.
/// </summary>
public sealed record UninstallResult(
    bool Success,
    string Message)
{
    public static UninstallResult Succeeded(string message)
        => new(true, message);

    public static UninstallResult Failed(string message)
        => new(false, message);
}

/// <summary>
/// Status of installed hooks.
/// </summary>
public sealed record HookStatus(
    bool IsGitRepo = false,
    bool NativeGitHook = false,
    bool Husky = false,
    bool Lefthook = false,
    bool AnyInstalled = false,
    bool DataGuardManaged = false)
{
    public override string ToString()
    {
        if (!IsGitRepo)
        {
            return "Not a git repository";
        }

        var parts = new List<string>();
        if (NativeGitHook)
        {
            parts.Add("Native Git");
        }

        if (Husky)
        {
            parts.Add("Husky");
        }

        if (Lefthook)
        {
            parts.Add("Lefthook");
        }

        var managed = DataGuardManaged ? " (DataGuard-managed)" : "";
        return parts.Count > 0
            ? $"Installed: {string.Join(", ", parts)}{managed}"
            : "No pre-commit hooks installed";
    }
}

/// <summary>
/// Type of pre-commit hook framework.
/// </summary>
public enum HookType
{
    Auto,
    NativeGit,
    Husky,
    Lefthook,
    None,
}
