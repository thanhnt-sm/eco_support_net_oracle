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
            return InstallResult.Failed("Not a git repository (no .git directory found)");
        }

        var detectedType = hookType == HookType.Auto ? DetectHookType(root) : hookType;
        var hookPath = Path.Combine(root, ".git", "hooks", "pre-commit");
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

        var results = new List<string>();

        // Remove native git hook
        var hookPath = Path.Combine(root, ".git", "hooks", "pre-commit");
        if (File.Exists(hookPath))
        {
            File.Delete(hookPath);
            results.Add("Removed .git/hooks/pre-commit");
        }

        // Remove husky
        var huskyDir = Path.Combine(root, ".husky");
        if (Directory.Exists(huskyDir))
        {
            var huskyHook = Path.Combine(huskyDir, "pre-commit");
            if (File.Exists(huskyHook))
            {
                File.Delete(huskyHook);
                results.Add("Removed .husky/pre-commit");
            }
        }

        // Remove lefthook config
        var lefthookPath = Path.Combine(root, "lefthook.yml");
        if (File.Exists(lefthookPath))
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

        var hookPath = Path.Combine(root, ".git", "hooks", "pre-commit");
        var huskyPath = Path.Combine(root, ".husky", "pre-commit");
        var lefthookPath = Path.Combine(root, "lefthook.yml");

        var nativeGitHook = File.Exists(hookPath);
        var husky = File.Exists(huskyPath);
        var lefthook = File.Exists(lefthookPath);
        var dataGuardManaged = nativeGitHook && File.ReadAllText(hookPath).Contains("DataGuard");

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
            if (Directory.Exists(Path.Combine(current, ".git")))
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
            Directory.CreateDirectory(huskyDir);
            var hookPath = Path.Combine(huskyDir, "pre-commit");

            if (File.Exists(hookPath) && !force)
            {
                return InstallResult.Failed("Husky pre-commit hook already exists. Use --force to overwrite.");
            }

            var hookContent = GenerateHuskyHook();
            await File.WriteAllTextAsync(hookPath, hookPath, cancellationToken);

            // Make executable
            File.SetAttributes(hookPath, File.GetAttributes(hookPath) | FileAttributes.ReadOnly);

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
            if (File.Exists(lefthookPath) && !force)
            {
                return InstallResult.Failed("lefthook.yml already exists. Use --force to overwrite.");
            }

            var config = GenerateLefthookConfig();
            await File.WriteAllTextAsync(lefthookPath, config, cancellationToken);

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
            Directory.CreateDirectory(hooksDir);

            if (File.Exists(hookPath) && !force)
            {
                return InstallResult.Failed("Native git pre-commit hook already exists. Use --force to overwrite.");
            }

            var hookContent = GenerateNativeGitHook();
            await File.WriteAllTextAsync(hookPath, hookContent, cancellationToken);

            // Make executable
            var fileInfo = new FileInfo(hookPath);
            fileInfo.IsReadOnly = false;
            fileInfo.Attributes |= FileAttributes.ReadOnly;

            return InstallResult.Succeeded("Native git pre-commit hook installed at .git/hooks/pre-commit");
        }
        catch (Exception ex)
        {
            return InstallResult.Failed($"Native git hook installation failed: {ex.Message}");
        }
    }

    private static string GenerateHuskyHook()
    {
        return @"#!/usr/bin/env sh
. ""$(dirname -- ""$0"")/../husky.sh""

# DataGuard pre-commit hook
echo ""🔍 Running DataGuard pre-commit validation...""

# Run DataGuard validation in offline mode (fast, no DB)
if command -v dataguard &> /dev/null; then
    dataguard validate --offline --format text
    exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        echo ""❌ DataGuard validation failed. Fix issues before committing.""
        echo ""💡 Run 'dataguard validate --offline' to see details.""
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
if command -v dataguard &> /dev/null; then
    dataguard validate --offline --format text
    exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        echo ""❌ DataGuard validation failed. Fix issues before committing.""
        echo ""💡 Run 'dataguard validate --offline' to see details.""
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
      run: dotnet dataguard validate --offline --format text
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