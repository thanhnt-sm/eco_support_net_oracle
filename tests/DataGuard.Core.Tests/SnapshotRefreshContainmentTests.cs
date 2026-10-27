using DataGuard.Cli.Commands;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// <c>snapshot refresh</c> is a live command that IDE hosts run without <c>--ide-safe</c>; a repository
/// <c>.dataguard.yml</c> must not be able to redirect the snapshot write outside the working directory
/// (or the config directory), and the write must be announced on stderr so a host can verify it.
/// </summary>
public class SnapshotRefreshContainmentTests
{
    private const string NamedConnectionVariable = "DG_E2E_CONNECTION";

    private static Dictionary<string, string?> IsolatedEnvironment(string dir, params (string Name, string? Value)[] extra)
    {
        var home = Path.Combine(dir, "home");
        Directory.CreateDirectory(home);
        var environment = new Dictionary<string, string?>
        {
            ["HOME"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(home, ".config"),
            ["APPDATA"] = Path.Combine(home, "AppData"),
            ["DATAGUARD_AUDIT_KEY"] = null,
            ["DATAGUARD_DATABASECONNECTION"] = null,
            [NamedConnectionVariable] = null,
        };
        foreach (var (name, value) in extra)
        {
            environment[name] = value;
        }

        return environment;
    }

    [Fact]
    public void IsContained_AcceptsCwdAndConfigDirectory_RejectsTraversal()
    {
        var root = Directory.CreateTempSubdirectory("dg-snap-contain").FullName;
        try
        {
            var cwd = Path.Combine(root, "sln");
            var configDir = Path.Combine(root, "ci");
            Directory.CreateDirectory(cwd);
            Directory.CreateDirectory(configDir);

            SnapshotCommands.IsSnapshotPathContained(Path.Combine(cwd, ".dataguard-snapshot.json"), cwd, null).Should().BeTrue();
            SnapshotCommands.IsSnapshotPathContained(Path.Combine(cwd, "snapshots", "x.json"), cwd, null).Should().BeTrue();
            SnapshotCommands.IsSnapshotPathContained(Path.Combine(configDir, "snap.json"), cwd, Path.Combine(configDir, "dataguard.yml")).Should().BeTrue("a config-relative snapshot next to the config file is allowed");
            SnapshotCommands.IsSnapshotPathContained(Path.Combine(configDir, "snap.json"), cwd, null).Should().BeFalse();
            SnapshotCommands.IsSnapshotPathContained(Path.Combine(root, "outside.json"), cwd, Path.Combine(configDir, "dataguard.yml")).Should().BeFalse();
            SnapshotCommands.IsSnapshotPathContained(Path.Combine(cwd, "..", "outside.json"), cwd, null).Should().BeFalse();
            SnapshotCommands.IsSnapshotPathContained(cwd + "2" + Path.DirectorySeparatorChar + "x.json", cwd, null).Should().BeFalse("a sibling directory sharing the prefix is outside");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SnapshotRefresh_ConfigRedirectsSnapshotOutsideWorkspace_Exit4BeforeConnecting()
    {
        var dir = Directory.CreateTempSubdirectory("dg-snap-refuse").FullName;
        try
        {
            var workspace = Path.Combine(dir, "ws");
            Directory.CreateDirectory(workspace);
            var config = Path.Combine(workspace, ".dataguard.yml");
            File.WriteAllText(config, "GroundTruthMode: Snapshot\nSnapshotFilePath: ../outside.json\n");

            var run = CliProcessTestRunner.RunWithEnvironment(
                workspace, null, null, IsolatedEnvironment(dir, (NamedConnectionVariable, IdeSafeEndToEndSupport.EnvConnection)),
                "snapshot", "refresh", "--config", config, "--connection-env", NamedConnectionVariable);

            run.ExitCode.Should().Be(4, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("Refusing to write snapshot");
            run.Stderr.Should().NotContain("Snapshot refresh failed", "the refusal must happen before any connection attempt");
            File.Exists(Path.Combine(dir, "outside.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotRefresh_ConnectionEnvUnset_Exit2WithoutSecretStoreOrConnection()
    {
        var dir = Directory.CreateTempSubdirectory("dg-snap-unset").FullName;
        try
        {
            var run = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir),
                "snapshot", "refresh", "--connection-env", NamedConnectionVariable);

            run.ExitCode.Should().Be(2, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain($"--connection-env {NamedConnectionVariable}: the environment variable is not set or empty.");
            run.Stderr.Should().NotContainAny("Secret store", "secret store", "Snapshot refresh failed");
            File.Exists(Path.Combine(dir, ".dataguard-snapshot.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotRefresh_DefaultPathWithoutConfig_IsContained_AndFailsOnlyAtTheConnection()
    {
        var dir = Directory.CreateTempSubdirectory("dg-snap-default").FullName;
        try
        {
            var run = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir, (NamedConnectionVariable, IdeSafeEndToEndSupport.EnvConnection)),
                "snapshot", "refresh", "--connection-env", NamedConnectionVariable);

            run.Stderr.Should().NotContain("Refusing to write snapshot", "the default .dataguard-snapshot.json under the working directory is contained");
            run.ExitCode.Should().Be(1, "the unreachable server is the only failure: " + run.Stdout + run.Stderr);
            run.Stderr.Should().NotContain("snapshot: wrote", "nothing was written");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
