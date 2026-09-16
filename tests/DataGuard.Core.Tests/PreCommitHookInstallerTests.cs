using DataGuard.Cli.Hooks;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class PreCommitHookInstallerTests
{
    [Fact]
    public async Task NativeInstall_WritesPortableManagedScriptAndUninstallPreservesForeignHook()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-hooks-{Guid.NewGuid():N}");
        var hookPath = Path.Combine(root, ".git", "hooks", "pre-commit");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);

            var installed = await PreCommitHookInstaller.InstallAsync(root, HookType.NativeGit);

            installed.Success.Should().BeTrue();
            var script = await File.ReadAllTextAsync(hookPath);
            script.Should().StartWith("#!/bin/sh");
            script.Should().Contain("command -v dataguard >/dev/null 2>&1");
            script.Should().Contain("dataguard validate --format text");
            script.Should().NotContain("--offline");

            var removed = await PreCommitHookInstaller.UninstallAsync(root);
            removed.Success.Should().BeTrue();
            File.Exists(hookPath).Should().BeFalse();

            await File.WriteAllTextAsync(hookPath, "#!/bin/sh\necho user-hook\n");
            var secondRemove = await PreCommitHookInstaller.UninstallAsync(root);
            secondRemove.Success.Should().BeTrue();
            File.Exists(hookPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HuskyInstall_WritesGeneratedContentAndDoesNotReplaceForeignHook()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-hooks-{Guid.NewGuid():N}");
        var hookPath = Path.Combine(root, ".husky", "pre-commit");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);

            var installed = await PreCommitHookInstaller.InstallAsync(root, HookType.Husky);
            installed.Success.Should().BeTrue();
            (await File.ReadAllTextAsync(hookPath)).Should().Contain("DataGuard pre-commit hook");
            var status = PreCommitHookInstaller.GetStatus(root);
            status.Husky.Should().BeTrue();
            status.DataGuardManaged.Should().BeTrue("a DataGuard-managed Husky hook must be reported as owned by DataGuard");

            await File.WriteAllTextAsync(hookPath, "#!/bin/sh\necho user-hook\n");
            var blocked = await PreCommitHookInstaller.InstallAsync(root, HookType.Husky, force: true);
            blocked.Success.Should().BeFalse();
            (await File.ReadAllTextAsync(hookPath)).Should().Contain("user-hook");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LefthookInstall_WritesSupportedValidateCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-lefthook-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));

            var installed = await PreCommitHookInstaller.InstallAsync(root, HookType.Lefthook);

            installed.Success.Should().BeTrue();
            var config = await File.ReadAllTextAsync(Path.Combine(root, "lefthook.yml"));
            config.Should().Contain("pre-commit:");
            config.Should().Contain("dotnet dataguard validate --format text");
            config.Should().NotContain("--offline");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NativeInstall_RejectsSymbolicLinkWithoutChangingItsTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-hooks-{Guid.NewGuid():N}");
        var target = Path.Combine(Path.GetTempPath(), $"dg-hook-target-{Guid.NewGuid():N}");
        var hookPath = Path.Combine(root, ".git", "hooks", "pre-commit");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
            await File.WriteAllTextAsync(target, "user-owned target\n");
            try
            {
                File.CreateSymbolicLink(hookPath, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var result = await PreCommitHookInstaller.InstallAsync(root, HookType.NativeGit);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("symbolic link");
            (await File.ReadAllTextAsync(target)).Should().Be("user-owned target\n");
            (await PreCommitHookInstaller.UninstallAsync(root)).Success.Should().BeTrue();
            File.Exists(hookPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    [Fact]
    public async Task NativeInstall_RejectsSymbolicLinkInHooksDirectoryWithoutChangingTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-hooks-{Guid.NewGuid():N}");
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"dg-hook-directory-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(targetDirectory);
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, ".git", "hooks"), targetDirectory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var result = await PreCommitHookInstaller.InstallAsync(root, HookType.NativeGit);

            result.Success.Should().BeFalse();
            File.Exists(Path.Combine(targetDirectory, "pre-commit")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LinkedWorktreeGitdir_InstallsReportsAndUninstallsNativeHook()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-worktree-{Guid.NewGuid():N}");
        var gitDirectory = Path.Combine(Path.GetTempPath(), $"dg-gitdir-{Guid.NewGuid():N}");
        var hookPath = Path.Combine(gitDirectory, "hooks", "pre-commit");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(gitDirectory);
            var commonDirectory = Path.Combine(gitDirectory, "common");
            Directory.CreateDirectory(commonDirectory);
            await File.WriteAllTextAsync(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main\n");
            await File.WriteAllTextAsync(Path.Combine(gitDirectory, "commondir"), "common\n");
            await File.WriteAllTextAsync(Path.Combine(root, ".git"), $"gitdir: {gitDirectory}\n");

            var installed = await PreCommitHookInstaller.InstallAsync(root, HookType.NativeGit);
            installed.Success.Should().BeTrue();
            File.Exists(hookPath).Should().BeTrue();

            var status = PreCommitHookInstaller.GetStatus(root);
            status.NativeGitHook.Should().BeTrue();
            status.DataGuardManaged.Should().BeTrue();

            var removed = await PreCommitHookInstaller.UninstallAsync(root);
            removed.Success.Should().BeTrue();
            File.Exists(hookPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(gitDirectory))
            {
                Directory.Delete(gitDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LinkedWorktreeGitdir_RejectsExternalDirectoryWithoutCommonDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dg-worktree-{Guid.NewGuid():N}");
        var gitDirectory = Path.Combine(Path.GetTempPath(), $"dg-gitdir-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(gitDirectory);
            await File.WriteAllTextAsync(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main\n");
            await File.WriteAllTextAsync(Path.Combine(root, ".git"), $"gitdir: {gitDirectory}\n");

            var result = await PreCommitHookInstaller.InstallAsync(root, HookType.NativeGit);

            result.Success.Should().BeFalse();
            File.Exists(Path.Combine(gitDirectory, "hooks", "pre-commit")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(gitDirectory))
            {
                Directory.Delete(gitDirectory, recursive: true);
            }
        }
    }
}
