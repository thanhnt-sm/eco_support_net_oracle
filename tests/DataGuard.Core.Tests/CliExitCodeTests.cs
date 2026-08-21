using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Integration tests invoking the real CLI binary (snapshot diff) to pin the
/// documented exit codes: 0 = no drift / drift without --fail-on-drift,
/// 1 = drift with --fail-on-drift. Uses legacy (v1) snapshots to also pin the
/// backward-compat path: no crash, warning, drift decided by violation hash.
/// </summary>
public class CliExitCodeTests
{
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
        FindRepoRoot(), "src", "DataGuard.Cli", "bin", "Debug", "net9.0", "DataGuard.Cli.dll");

    private static (int ExitCode, string Output) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(CliDllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(60_000).Should().BeTrue("CLI must exit within 60s");
        return (process.ExitCode, output);
    }

    private static string WriteLegacySnapshot(string dir, int violationCount)
    {
        var path = Path.Combine(dir, $"snapshot-v1-{violationCount}.json");
        var violations = violationCount == 0
            ? "[]"
            : """[{ "ruleId": "DG001", "message": "old baseline violation", "severity": "Error", "location": null, "properties": null }]""";
        File.WriteAllText(path, $$"""
            {
              "Version": 1,
              "CreatedAt": "2026-01-01T00:00:00Z",
              "SchemaVersion": "1.0",
              "GroundTruthMode": "Snapshot",
              "Violations": {{violations}}
            }
            """);
        return path;
    }

    private static string WriteConfig(string dir, string snapshotPath) =>
        Path.Combine(dir, $"config-{Path.GetFileName(snapshotPath)}.yml");

    [Fact]
    public void SnapshotDiff_DriftWithFailOnDrift_Exit1()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            // Legacy snapshot with 1 violation; offline diff produces 0 violations
            // -> hashes differ -> drift.
            var snapshot = WriteLegacySnapshot(dir, 1);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"""
                GroundTruthMode: Snapshot
                SnapshotFilePath: {snapshot}
                """);

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config, "--fail-on-drift");

            exitCode.Should().Be(1);
            output.Should().Contain("snapshot format v1", "legacy snapshots must hit the backward-compat path with a warning");
            output.Should().Contain("Schema differences detected");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotDiff_DriftWithoutFailOnDrift_Exit0()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            var snapshot = WriteLegacySnapshot(dir, 1);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"""
                GroundTruthMode: Snapshot
                SnapshotFilePath: {snapshot}
                """);

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config);

            exitCode.Should().Be(0, "without --fail-on-drift drift must not fail the run");
            output.Should().Contain("snapshot format v1");
            output.Should().Contain("Schema differences detected");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotDiff_NoDrift_Exit0()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            // Legacy snapshot with 0 violations hashes the same as an empty
            // offline validation -> no drift.
            var snapshot = WriteLegacySnapshot(dir, 0);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"""
                GroundTruthMode: Snapshot
                SnapshotFilePath: {snapshot}
                """);

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config);

            exitCode.Should().Be(0);
            output.Should().Contain("snapshot format v1");
            output.Should().Contain("No differences detected");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_MachineReadableFormatWithoutOutput_Exit2()
    {
        var (exitCode, output) = RunCli("validate", "--format", "sarif");

        exitCode.Should().Be(2, "machine-readable formats require --output");
        output.Should().Contain("requires --output");
    }

    [Fact]
    public void Validate_UnsupportedFormat_Exit2()
    {
        var (exitCode, output) = RunCli("validate", "--format", "pdf", "--output", "/tmp/dg-out.pdf");

        exitCode.Should().Be(2);
        output.Should().Contain("Unsupported --format");
    }

    [Fact]
    public void Version_Exit0()
    {
        var (exitCode, output) = RunCli("version");

        exitCode.Should().Be(0);
        output.Should().Contain("DataGuard CLI version");
    }
}
