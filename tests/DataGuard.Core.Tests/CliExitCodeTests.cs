using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Integration tests invoking the real CLI binary (snapshot diff) to pin the
/// documented exit codes: 0 = no drift / drift without --fail-on-drift,
/// 1 = drift with --fail-on-drift, and 3 = unevaluated when no fresh
/// acquisition exists. Legacy (v1) snapshots are fail-closed by default.
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

    private static string TestConfiguration =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";

    private static string CliDllPath => Path.Combine(
        FindRepoRoot(), "src", "DataGuard.Cli", "bin", TestConfiguration, "net9.0", "DataGuard.Cli.dll");

    private static (int ExitCode, string Output) RunCli(params string[] args)
        => RunCliInDirectory(null, null, args);

    private static (int ExitCode, string Output) RunCliInDirectory(
        string? workingDirectory,
        string? standardInput,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        // These fixtures assert offline snapshot behavior. Do not inherit an
        // operator/CI credential that the CLI correctly gives precedence to.
        psi.Environment.Remove("DATAGUARD_CONNECTION_STRING");
        psi.ArgumentList.Add(CliDllPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

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
    public void SnapshotDiff_WithoutFreshAcquisition_Exit3EvenWithFailOnDrift()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            // A legacy snapshot without a live acquisition is unevaluated; it must
            // never be treated as either drift or a clean comparison.
            var snapshot = WriteLegacySnapshot(dir, 1);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"""
                GroundTruthMode: Snapshot
                SnapshotFilePath: {snapshot}
                """);

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config, "--fail-on-drift");

            exitCode.Should().Be(3);
            output.Should().Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotDiff_WithoutFreshAcquisition_Exit3WithoutFailOnDrift()
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

            exitCode.Should().Be(3);
            output.Should().Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotDiff_NoFreshAcquisitionNeverReportsClean()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            // An empty offline validation is not a fresh schema acquisition.
            var snapshot = WriteLegacySnapshot(dir, 0);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"""
                GroundTruthMode: Snapshot
                SnapshotFilePath: {snapshot}
                """);

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config);

            exitCode.Should().Be(3);
            output.Should().Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotDiff_SchemaBearingSnapshotWithoutConnection_Exit3()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "schema-snapshot.json");
            File.WriteAllText(snapshot, """
                {
                  "Version": 2,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "DatabaseVersion": "19.0",
                  "SchemaHash": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "Violations": [],
                  "Schema": [{ "Name": "CUSTOMERS", "Columns": [{ "Name": "ID", "DataType": "NUMBER", "MaxLength": null, "CharLength": null, "Precision": 22, "Scale": 0, "IsNullable": false, "CharUsed": null }] }]
                }
                """);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"GroundTruthMode: Snapshot\nSnapshotFilePath: {snapshot}\n");

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config);

            exitCode.Should().Be(3);
            output.Should().Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotDiff_V3ProviderMismatch_IsUnevaluatedBeforeConnection()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-exit").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "schema-v3.json");
            File.WriteAllText(snapshot, """
                {
                  "Version": 3,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "DatabaseVersion": "16.0",
                  "SchemaHash": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                  "Violations": [],
                  "SchemaHashKind": "canonical-schema-v1",
                  "Provider": "sqlserver",
                  "SchemaScope": "dbo",
                  "SchemaCanonicalizationVersion": "v1",
                  "Schema": [{ "Name": "dbo.CUSTOMERS", "Columns": [{ "Name": "ID", "DataType": "int", "IsNullable": false }] }]
                }
                """);
            var config = WriteConfig(dir, snapshot);
            File.WriteAllText(config, $"GroundTruthMode: Snapshot\nSnapshotFilePath: {snapshot}\n");

            var (exitCode, output) = RunCli("snapshot", "diff", "--config", config, "--provider", "postgresql");

            exitCode.Should().Be(3);
            output.Should().Contain("provider");
            output.Should().Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SnapshotRefresh_WithoutConnection_Exit3()
    {
        var (exitCode, output) = RunCli("snapshot", "refresh");

        exitCode.Should().Be(3);
        output.Should().Contain("UNEVALUATED");
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
    public void Assess_MissingWorkspace_Exit4ForOperationalToolError()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dataguard-missing-" + Guid.NewGuid().ToString("N"));
        var (exitCode, output) = RunCli("assess", "--workspace", missing);

        exitCode.Should().Be(4);
        output.Should().Contain("DG1000");
    }

    [Fact]
    public void Validate_UnavailableProviderRule_Exit3AndSuppressesSuccessPayload()
    {
        var (exitCode, output) = RunCli("validate", "--provider", "postgresql");

        exitCode.Should().Be(3);
        output.Should().Contain("PG004 unavailable");
        output.Should().NotContain("Validation complete");
    }

    [Fact]
    public void Validate_EfSnapshot_ExportsSourceOnlyContract()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-snapshot").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "AppSnapshot.cs");
            var outputPath = Path.Combine(dir, "contracts.json");
            File.WriteAllText(snapshot, """
                class Snapshot { void Build(ModelBuilder modelBuilder) {
                    modelBuilder.Entity<Customer>(entity => { entity.ToTable("CUSTOMERS"); entity.Property(item => item.Name).HasColumnName("FULL_NAME").HasMaxLength(120).IsRequired(); });
                }}
                """);

            var (exitCode, output) = RunCli("validate", "--ef-snapshot", snapshot, "--format", "contracts", "--output", outputPath);

            exitCode.Should().Be(0);
            output.Should().Contain("Contracts exported");
            File.ReadAllText(outputPath).Should().Contain("CUSTOMERS").And.Contain("FULL_NAME");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_SkipRules_ExcludesSpecifiedRules()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-skip-rules").FullName;
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

            var (exitCodeWithRule, outputWithRule) = RunCli("validate", "--ef-snapshot", snapshot, "--format", "text");
            outputWithRule.Should().Contain("DG006");

            var (exitCodeSkipped, outputSkipped) = RunCli("validate", "--ef-snapshot", snapshot, "--format", "text", "--skip-rules", "DG006");
            outputSkipped.Should().NotContain("DG006");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_EfProject_SelectsSourceSnapshotWithoutBuildingOrLoadingAssembly()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-ef-project").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "AppDbContextModelSnapshot.cs");
            var ignored = Path.Combine(dir, "obj", "IgnoredModelSnapshot.cs");
            var outputPath = Path.Combine(dir, "contracts.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ignored)!);
            File.WriteAllText(snapshot, """
                class Snapshot { void Build(ModelBuilder modelBuilder) {
                    modelBuilder.Entity<Customer>(entity => { entity.ToTable("CUSTOMERS"); });
                }}
                """);
            File.WriteAllText(ignored, "class Ignored { }");

            var (exitCode, output) = RunCli(
                "validate", "--ef-project", dir, "--ef-context", "AppDbContext",
                "--format", "contracts", "--output", outputPath);

            exitCode.Should().Be(0);
            output.Should().Contain("Contracts exported");
            File.ReadAllText(outputPath).Should().Contain("CUSTOMERS");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_EfProject_RejectsAmbiguousOrConflictingSourceSelection()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-ef-project-errors").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "FirstModelSnapshot.cs"), "class First { }");
            File.WriteAllText(Path.Combine(dir, "SecondModelSnapshot.cs"), "class Second { }");
            var explicitSnapshot = Path.Combine(dir, "FirstModelSnapshot.cs");
            var outputPath = Path.Combine(dir, "contracts.json");

            var (ambiguousExitCode, ambiguousOutput) = RunCli(
                "validate", "--ef-project", dir, "--format", "contracts", "--output", outputPath);
            ambiguousExitCode.Should().Be(2);
            ambiguousOutput.Should().Contain("multiple ModelSnapshot");

            var (conflictExitCode, conflictOutput) = RunCli(
                "validate", "--ef-project", dir, "--ef-snapshot", explicitSnapshot,
                "--format", "contracts", "--output", outputPath);
            conflictExitCode.Should().Be(2);
            conflictOutput.Should().Contain("either --ef-snapshot or --ef-project");

            var (invalidFileExitCode, invalidFileOutput) = RunCli(
                "validate", "--ef-project", explicitSnapshot, "--format", "contracts", "--output", outputPath);
            invalidFileExitCode.Should().Be(2);
            invalidFileOutput.Should().Contain("directory or a .csproj");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Version_Exit0()
    {
        var (exitCode, output) = RunCli("version");

        exitCode.Should().Be(0);
        output.Should().Contain("DataGuard CLI version");
    }

    [Fact]
    public void HookCommands_InstallStatusAndUninstallOnlyManagedHook()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-hooks").FullName;
        var hookPath = Path.Combine(dir, ".git", "hooks", "pre-commit");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);

            var (installExitCode, installOutput) = RunCliInDirectory(dir, null, "hook", "install", "--type", "native");
            installExitCode.Should().Be(0);
            installOutput.Should().Contain("installed");
            File.ReadAllText(hookPath).Should().Contain("DataGuard pre-commit hook");

            var (statusExitCode, statusOutput) = RunCliInDirectory(dir, null, "hook", "status");
            statusExitCode.Should().Be(0);
            statusOutput.Should().Contain("DataGuard-managed");

            var (uninstallExitCode, uninstallOutput) = RunCliInDirectory(dir, null, "hook", "uninstall");
            uninstallExitCode.Should().Be(0);
            uninstallOutput.Should().Contain("Removed");
            File.Exists(hookPath).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void InitWizard_WritesToExplicitOutputPath()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-wizard").FullName;
        var configPath = Path.Combine(dir, "wizard.yml");
        try
        {
            var (exitCode, output) = RunCliInDirectory(
                dir,
                "\n1\n1\n",
                "init", "--wizard", "--output", configPath);

            exitCode.Should().Be(0);
            output.Should().Contain("Interactive Setup Wizard");
            File.Exists(configPath).Should().BeTrue();
            File.Exists(Path.Combine(dir, ".dataguard.yml")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
