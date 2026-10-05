using System.Diagnostics;
using FluentAssertions;
using Xunit;
using static DataGuard.Core.Tests.IdeSafeEndToEndSupport;

namespace DataGuard.Core.Tests;

/// <summary>
/// Process-level tests for the IDE-safe handshake contract (red-team findings 1, 5, 10, 14): the
/// <c>ide-safe: active</c> line is the first stderr line, <c>--allow-env-connection</c> keeps only the environment
/// credential, and <c>assess</c> echoes workspace-relative tool-error paths.
/// </summary>
public class IdeSafeHandshakeEndToEndTests
{
    [Fact]
    public void Validate_IdeSafe_WritesActiveLineFirstAndNeverConnects()
    {
        var (dir, config) = CreateHostileProjectFixture();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var (exitCode, _, stderr) = RunCli(
                EnvConnection, dir, "validate", "--ide-safe", "--project", dir, "--config", config, "--progress");
            stopwatch.Stop();

            var lines = Lines(stderr);
            lines.Should().NotBeEmpty();
            lines[0].Should().Be("ide-safe: active");
            lines.Count(l => l == "ide-safe: active").Should().Be(1);
            stderr.Should().Contain("ide-safe: suppressed").And.Contain("DATAGUARD_CONNECTION_STRING");
            stderr.Should().NotContain("kept environment connection");
            exitCode.Should().Be(0, "no adapter connection must be attempted; stderr: " + stderr);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_IdeSafeAllowEnvConnection_ReportsKeptEnvironmentConnection()
    {
        var (dir, config) = CreateHostileProjectFixture();
        try
        {
            var (exitCode, _, stderr) = RunCli(
                EnvConnection, dir, "validate", "--ide-safe", "--allow-env-connection", "--project", dir, "--config", config);

            var lines = Lines(stderr);
            lines[0].Should().Be("ide-safe: active");
            stderr.Should().Contain("ide-safe: kept environment connection (--allow-env-connection)");
            stderr.Should().Contain("ManualAssemblyPath");
            stderr.Should().Contain("live SQL shape rule disabled (use verify-shape)", "H1: validate never describes repo SQL against the kept credential");
            stderr.Should().NotContain("from-config", "the config-file connection value must never be used or echoed");
            exitCode.Should().NotBe(2, "--allow-env-connection is a valid validate option under --ide-safe");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_AllowEnvConnectionWithoutIdeSafe_IsNoOp()
    {
        var dir = Directory.CreateTempSubdirectory("dg-ide-safe").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT 1 FROM Dual\"; } }");

            // --allow-syntactic-only: a project-only run without ground truth is otherwise exit 3 (red-team C4 gate).
            var (exitCode, _, stderr) = RunCli(null, dir, "validate", "--allow-env-connection", "--project", dir, "--allow-syntactic-only");

            exitCode.Should().Be(0, stderr);
            stderr.Should().NotContain("ide-safe");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_IdeSafe_MissingConventionalConfigWarns_AndAcceptsFailOnUnavailable()
    {
        var dir = Directory.CreateTempSubdirectory("dg-ide-safe-gates").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), RepoSource);
            var missingConfig = Path.Combine(dir, ".dataguard.yml");

            // Hosts always pass the workspace config path; under --ide-safe a missing file is a warning, the run stays
            // lint-only without exit 3, and --fail-on-unavailable is an accepted option (PG004 is then blocking).
            var (lenientExit, _, lenientStderr) = RunCli(null, dir, "validate", "--ide-safe", "--config", missingConfig, "--project", dir, "--provider", "postgresql");
            Lines(lenientStderr)[0].Should().Be("ide-safe: active");
            lenientStderr.Should().Contain("Warning: configuration file not found").And.Contain("Rule PG004 not evaluated");
            lenientExit.Should().Be(0, lenientStderr);

            var (strictExit, _, strictStderr) = RunCli(null, dir, "validate", "--ide-safe", "--config", missingConfig, "--project", dir, "--provider", "postgresql", "--fail-on-unavailable");
            Lines(strictStderr)[0].Should().Be("ide-safe: active");
            strictStderr.Should().NotContain("is not allowed with --ide-safe");
            strictExit.Should().Be(3, strictStderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_IdeSafeRejectedOption_StillWritesActiveLineFirst()
    {
        var (exitCode, _, stderr) = RunCli(null, Path.GetTempPath(), "validate", "--ide-safe", "--offline");

        exitCode.Should().Be(2);
        var lines = Lines(stderr);
        lines[0].Should().Be("ide-safe: active");
        lines[1].Should().StartWith("--offline is not allowed with --ide-safe");
    }

    [Fact]
    public void Assess_IdeSafe_WritesActiveLineFirst_AndRelativisesToolErrorPath()
    {
        var workspace = Directory.CreateTempSubdirectory("dg-ide-safe-assess").FullName;
        try
        {
            var (exitCode, _, stderr) = RunCli(null, workspace, "assess", "--ide-safe", "--workspace", workspace);

            exitCode.Should().Be(4, "an empty workspace is an operational tool error");
            var lines = Lines(stderr);
            lines[0].Should().Be("ide-safe: active");
            stderr.Should().Contain("[DG1005] .:");
            stderr.Should().NotContain(workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void Assess_WithoutIdeSafe_PrintsNoHandshake()
    {
        var workspace = Directory.CreateTempSubdirectory("dg-assess-plain").FullName;
        try
        {
            var (_, _, stderr) = RunCli(null, workspace, "assess", "--workspace", workspace);
            stderr.Should().NotContain("ide-safe");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
