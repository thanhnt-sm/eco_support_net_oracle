using System.Diagnostics;
using FluentAssertions;
using Xunit;
using static DataGuard.Core.Tests.IdeSafeEndToEndSupport;

namespace DataGuard.Core.Tests;

/// <summary>
/// Process-level tests for the write-path policy through directory links (review M3 / post-review gate g):
/// a junction committed inside the workspace must never receive validate, evidence, oracle-check or verify-shape
/// output, whether it is the direct parent (g1) or an ancestor (g2); a host-chosen junction outside the
/// workspace (g3) stays writable.
/// </summary>
public class IdeSafeWritePathEndToEndTests
{
    [WindowsFact]
    public void Assess_OutputUnderJunction_WritesSarif()
    {
        using var fixture = new WorkspaceWithJunction("dg-junction", junctionInsideWorkspace: false);
        fixture.WriteWorkspaceFile(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        var output = Path.Combine(fixture.Junction, "sub", "assess.sarif");

        var (exitCode, _, stderr) = RunCli(
            null, fixture.Workspace, "assess", "--ide-safe", "--workspace", fixture.Workspace, "--format", "sarif", "--output", output);

        exitCode.Should().NotBe(4, stderr);
        File.Exists(output).Should().BeTrue("SARIF must be written under a junctioned output directory");
    }

    [WindowsTheory]
    [InlineData("sarif", "x.sarif", false, "Refusing to write SARIF through a symbolic link or invalid path.")]
    [InlineData("sarif", "x.sarif", true, "Refusing to write SARIF through a symbolic link or invalid path.")]
    [InlineData("evidence", "x.md", true, "Refusing to write evidence output through a symbolic link or invalid path.")]
    public void Validate_OutputThroughJunctionInsideWorkspace_IsRejected(string format, string fileName, bool nested, string expectedMessage)
    {
        using var fixture = new WorkspaceWithJunction("dg-ws-junction", junctionInsideWorkspace: true, targetSubdirectory: "sub");
        fixture.WriteWorkspaceFile("Repo.cs", RepoSource);
        var output = nested ? Path.Combine(fixture.Junction, "sub", fileName) : Path.Combine(fixture.Junction, fileName);
        var landed = nested ? Path.Combine(fixture.Target, "sub", fileName) : Path.Combine(fixture.Target, fileName);

        var (exitCode, _, stderr) = RunCli(
            null, fixture.Workspace, "validate", "--ide-safe", "--project", fixture.Workspace, "--format", format, "--output", output);

        exitCode.Should().Be(4, stderr);
        stderr.Should().Contain(expectedMessage);
        File.Exists(landed).Should().BeFalse("nothing may be written through an in-workspace junction");
        Directory.GetFiles(fixture.Target, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [WindowsFact]
    public void Validate_OutputUnderSiblingJunctionOutsideWorkspace_IsWritten()
    {
        using var fixture = new WorkspaceWithJunction("dg-sibling-junction", junctionInsideWorkspace: false, targetSubdirectory: "out");
        fixture.WriteWorkspaceFile("Repo.cs", RepoSource);
        var output = Path.Combine(fixture.Junction, "out", "x.sarif");

        var (exitCode, _, stderr) = RunCli(
            null, fixture.Workspace, "validate", "--ide-safe", "--project", fixture.Workspace, "--format", "sarif", "--output", output);

        exitCode.Should().Be(0, stderr);
        File.Exists(Path.Combine(fixture.Target, "out", "x.sarif")).Should().BeTrue("a host-chosen junction outside the workspace is a normal layout");
        File.Exists(Path.Combine(fixture.Target, "out", "summary.json")).Should().BeTrue();
    }

    [WindowsFact]
    public void OracleCheck_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting()
    {
        using var fixture = new WorkspaceWithJunction("dg-oracle-junction", junctionInsideWorkspace: true);
        var output = Path.Combine(fixture.Junction, "x.sarif");

        var (exitCode, _, stderr) = RunCli(null, fixture.Workspace, "oracle-check", "--format", "sarif", "--output", output);

        exitCode.Should().Be(4, stderr);
        stderr.Should().Contain("Refusing to write SARIF through a symbolic link or invalid path.");
        stderr.Should().NotContain("requires --connection", "the write-path check must run before any connection is required");
        Directory.GetFiles(fixture.Target).Should().BeEmpty();
    }

    [WindowsFact]
    public void VerifyShape_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting()
    {
        using var fixture = new WorkspaceWithJunction("dg-verify-junction", junctionInsideWorkspace: true);
        fixture.WriteWorkspaceFile("Repo.cs", RepoSource);
        var output = Path.Combine(fixture.Junction, "shape.json");

        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, stderr) = RunCli(
            null, fixture.Workspace, "verify-shape", "--connection", EnvConnection, "--project", fixture.Workspace, "--format", "json", "--output", output);
        stopwatch.Stop();

        exitCode.Should().Be(4, stderr);
        stderr.Should().Contain("Refusing to write verify-shape output through a symbolic link or invalid path.");
        stderr.Should().NotContain("verify-shape failed", "the write-path check must run before the live pass, not after it");
        Directory.GetFiles(fixture.Target).Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }
}
