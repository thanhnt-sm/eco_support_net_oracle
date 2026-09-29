using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Process-level tests for the IDE-safe handshake contract (red-team findings 1, 5, 10, 14):
/// the <c>ide-safe: active</c> line is the first stderr line, <c>--allow-env-connection</c> keeps only the
/// environment credential, baseline suppression is visible, and <c>assess</c> writes under a junctioned
/// temp directory and echoes workspace-relative tool-error paths.
/// </summary>
public class IdeSafeEndToEndTests
{
    private const string EnvConnection = "Server=127.0.0.1,1;Connect Timeout=1";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DataGuard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("DataGuard.sln not found above " + AppContext.BaseDirectory);
    }

    private static string CliDllPath => Path.Combine(
        FindRepoRoot(), "src", "DataGuard.Cli", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug", "net9.0", "DataGuard.Cli.dll");

    /// <summary>Runs the built CLI; stdout and stderr are drained concurrently so neither pipe can stall the child.</summary>
    private static (int ExitCode, string Stdout, string Stderr) RunCli(string? envConnection, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        psi.Environment.Remove("DATAGUARD_CONNECTION_STRING");
        if (envConnection is not null)
        {
            psi.Environment["DATAGUARD_CONNECTION_STRING"] = envConnection;
        }

        psi.ArgumentList.Add(CliDllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit(60_000).Should().BeTrue("CLI must exit within 60s");
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static string[] Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    /// <summary>Hostile repo fixture: config requests assembly loading, a project with one inline SQL literal.</summary>
    private static (string Dir, string Config) CreateHostileProjectFixture()
    {
        var dir = Directory.CreateTempSubdirectory("dg-ide-safe").FullName;
        File.WriteAllText(
            Path.Combine(dir, "Repo.cs"),
            "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");
        var config = Path.Combine(dir, ".dataguard.yml");
        File.WriteAllText(config, "GroundTruthMode: Manual\nManualAssemblyPath: tools/evil.dll\nConnectionString: Data Source=from-config\n");
        return (dir, config);
    }

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
            var (exitCode, _, stderr) = RunCli(null, dir, "validate", "--allow-env-connection", "--project", dir);

            exitCode.Should().Be(0, stderr);
            stderr.Should().NotContain("ide-safe");
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

    /// <summary>NTFS junction (no privilege needed); fails the test loudly rather than skipping when mklink is unavailable.</summary>
    private static void CreateJunction(string junction, string target)
    {
        var mklink = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{junction}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var mklinkError = mklink.StandardError.ReadToEnd();
        mklink.WaitForExit();
        if (mklink.ExitCode != 0)
        {
            Assert.Fail("mklink /J failed on this machine; junction test cannot run: " + mklinkError);
        }
    }

    [WindowsFact]
    public void Assess_OutputUnderJunction_WritesSarif()
    {
        var root = Directory.CreateTempSubdirectory("dg-junction").FullName;
        var target = Path.Combine(root, "target");
        var junction = Path.Combine(root, "junc");
        Directory.CreateDirectory(target);
        CreateJunction(junction, target);

        try
        {
            var workspace = Path.Combine(root, "ws");
            Directory.CreateDirectory(workspace);
            File.WriteAllText(
                Path.Combine(workspace, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
            var output = Path.Combine(junction, "sub", "assess.sarif");

            var (exitCode, _, stderr) = RunCli(
                null, workspace, "assess", "--ide-safe", "--workspace", workspace, "--format", "sarif", "--output", output);

            exitCode.Should().NotBe(4, stderr);
            File.Exists(output).Should().BeTrue("SARIF must be written under a junctioned output directory");
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Review M3 / post-review gate (g): a junction committed inside the workspace must never receive validate,
    /// evidence or oracle-check output, whether it is the direct parent (g1) or an ancestor (g2). The sibling
    /// junction outside the workspace (g3) stays writable.
    /// </summary>
    [WindowsTheory]
    [InlineData("sarif", "x.sarif", false, "Refusing to write SARIF through a symbolic link or invalid path.")]
    [InlineData("sarif", "x.sarif", true, "Refusing to write SARIF through a symbolic link or invalid path.")]
    [InlineData("evidence", "x.md", true, "Refusing to write evidence output through a symbolic link or invalid path.")]
    public void Validate_OutputThroughJunctionInsideWorkspace_IsRejected(string format, string fileName, bool nested, string expectedMessage)
    {
        var root = Directory.CreateTempSubdirectory("dg-ws-junction").FullName;
        var workspace = Path.Combine(root, "ws");
        var target = Path.Combine(root, "outside-target");
        var junction = Path.Combine(workspace, "junction");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        File.WriteAllText(Path.Combine(workspace, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");
        CreateJunction(junction, target);
        var output = nested ? Path.Combine(junction, "sub", fileName) : Path.Combine(junction, fileName);
        var landed = nested ? Path.Combine(target, "sub", fileName) : Path.Combine(target, fileName);

        try
        {
            var (exitCode, _, stderr) = RunCli(
                null, workspace, "validate", "--ide-safe", "--project", workspace, "--format", format, "--output", output);

            exitCode.Should().Be(4, stderr);
            stderr.Should().Contain(expectedMessage);
            File.Exists(landed).Should().BeFalse("nothing may be written through an in-workspace junction");
            Directory.GetFiles(target, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void Validate_OutputUnderSiblingJunctionOutsideWorkspace_IsWritten()
    {
        var root = Directory.CreateTempSubdirectory("dg-sibling-junction").FullName;
        var workspace = Path.Combine(root, "ws");
        var target = Path.Combine(root, "sibling-target");
        var junction = Path.Combine(root, "sibling-junction");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(target, "out"));
        File.WriteAllText(Path.Combine(workspace, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");
        CreateJunction(junction, target);
        var output = Path.Combine(junction, "out", "x.sarif");

        try
        {
            var (exitCode, _, stderr) = RunCli(
                null, workspace, "validate", "--ide-safe", "--project", workspace, "--format", "sarif", "--output", output);

            exitCode.Should().Be(0, stderr);
            File.Exists(Path.Combine(target, "out", "x.sarif")).Should().BeTrue("a host-chosen junction outside the workspace is a normal layout");
            File.Exists(Path.Combine(target, "out", "summary.json")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void OracleCheck_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting()
    {
        var root = Directory.CreateTempSubdirectory("dg-oracle-junction").FullName;
        var workspace = Path.Combine(root, "ws");
        var target = Path.Combine(root, "outside-target");
        var junction = Path.Combine(workspace, "junction");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(target);
        CreateJunction(junction, target);
        var output = Path.Combine(junction, "x.sarif");

        try
        {
            var (exitCode, _, stderr) = RunCli(null, workspace, "oracle-check", "--format", "sarif", "--output", output);

            exitCode.Should().Be(4, stderr);
            stderr.Should().Contain("Refusing to write SARIF through a symbolic link or invalid path.");
            stderr.Should().NotContain("requires --connection", "the write-path check must run before any connection is required");
            Directory.GetFiles(target).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void VerifyShape_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting()
    {
        var root = Directory.CreateTempSubdirectory("dg-verify-junction").FullName;
        var workspace = Path.Combine(root, "ws");
        var target = Path.Combine(root, "outside-target");
        var junction = Path.Combine(workspace, "junction");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(workspace, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");
        CreateJunction(junction, target);
        var output = Path.Combine(junction, "shape.json");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var (exitCode, _, stderr) = RunCli(
                null, workspace, "verify-shape", "--connection", EnvConnection, "--project", workspace, "--format", "json", "--output", output);
            stopwatch.Stop();

            exitCode.Should().Be(4, stderr);
            stderr.Should().Contain("Refusing to write verify-shape output through a symbolic link or invalid path.");
            stderr.Should().NotContain("verify-shape failed", "the write-path check must run before the live pass, not after it");
            Directory.GetFiles(target).Should().BeEmpty();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        }
        finally
        {
            Directory.Delete(junction, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_BaselineSuppressesViolation_WarnsInTextAndProgressModes()
    {
        var dir = Directory.CreateTempSubdirectory("dg-baseline-warn").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "AppSnapshot.cs");
            File.WriteAllText(snapshot, """
                class Snapshot { void Build(ModelBuilder modelBuilder) {
                    modelBuilder.Entity<Customer>(entity => {
                        entity.ToTable("CUSTOMERS");
                        entity.Property(item => item.FirstName).HasColumnName("x_y_z_unmatched");
                    });
                }}
                """);

            var (_, firstStdout, _) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--format", "text");
            var match = Regex.Match(firstStdout, @"\[\w+\] DG006: (?<msg>.+?)(?: \(\d+:\d+\))?\r?$", RegexOptions.Multiline);
            match.Success.Should().BeTrue("fixture must produce one DG006; stdout: " + firstStdout);

            var baseline = Path.Combine(dir, "baseline.json");
            File.WriteAllText(baseline, System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = 2,
                CreatedAt = "2026-01-01T00:00:00Z",
                SchemaVersion = "1.0",
                GroundTruthMode = "Snapshot",
                DatabaseVersion = "unknown",
                SchemaHash = new string('A', 64),
                Violations = new[] { new { ruleId = "DG006", message = match.Groups["msg"].Value, severity = "Info", location = (object?)null, properties = (object?)null } },
            }));
            var config = Path.Combine(dir, "config.yml");
            File.WriteAllText(config, $"BaselineFilePath: {baseline}\nEnableBaseline: true\n");

            var (_, textStdout, textStderr) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--config", config, "--format", "text");
            textStdout.Should().NotContain("DG006");
            textStderr.Should().Contain("baseline: 1 violations suppressed by baseline.json");

            var (_, _, progressStderr) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--config", config, "--format", "text", "--progress");
            progressStderr.Should().Contain("\"Kind\":\"BaselineApplied\"")
                .And.Contain("\"Phase\":\"Validating rules\"")
                .And.Contain("\"Detail\":\"baseline.json\"")
                .And.Contain("\"SuppressedCount\":1");
            progressStderr.Should().NotContain("baseline: 1 violations suppressed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>Fact that is skipped (not silently passed) on non-Windows hosts, where NTFS junctions do not exist.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "NTFS junctions (mklink /J) require Windows.";
        }
    }
}

/// <summary>Theory counterpart of <see cref="WindowsFactAttribute"/>.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "NTFS junctions (mklink /J) require Windows.";
        }
    }
}
