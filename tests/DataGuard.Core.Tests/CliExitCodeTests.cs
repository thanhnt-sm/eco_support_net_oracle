using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
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
    private static (int ExitCode, string Output) RunCli(params string[] args)
        => RunCliInDirectory(null, null, args);

    /// <summary>Stdout and stderr are joined: these assertions care that a message appeared, not on which stream.</summary>
    private static (int ExitCode, string Output) RunCliInDirectory(
        string? workingDirectory,
        string? standardInput,
        params string[] args)
    {
        // These fixtures assert offline snapshot behavior. Do not inherit an
        // operator/CI credential that the CLI correctly gives precedence to.
        var run = CliProcessTestRunner.Run(workingDirectory, standardInput, environmentConnection: null, args);
        return (run.ExitCode, run.Stdout + run.Stderr);
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
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("oracle")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    public void SnapshotRefresh_WithoutConnection_Exit3_ForEveryProvider(string provider)
    {
        var (exitCode, output) = RunCli("snapshot", "refresh", "--provider", provider);

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
    public void Validate_NoSource_Exit3AndSuppressesSuccessPayload()
    {
        // Was "UnavailableProviderRule_Exit3": PG004 no longer blocks (red-team C2); with no source at all the run is
        // still unevaluated, now because acquisition found nothing.
        var dir = Directory.CreateTempSubdirectory("dg-cli-nosource").FullName;
        try
        {
            var (exitCode, output) = RunCliInDirectory(dir, null, "validate", "--provider", "postgresql");

            exitCode.Should().Be(3);
            output.Should().Contain("UNEVALUATED");
            output.Should().NotContain("Validation complete");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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

    [Fact]
    public void ScanCommand_WithJsonFormat_ReturnsZeroAndValidJson()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-scan").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT 1 FROM Dual\"; } }");
            var (exitCode, output) = RunCliInDirectory(dir, null, "scan", "--project", dir, "--format", "json");
            exitCode.Should().Be(0);
            output.Should().Contain("\"filesScanned\":");
            output.Should().Contain("\"queries\":");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyShapeCommand_WithoutDatabase_ReturnsHandledOutput()
    {
        var dir = Directory.CreateTempSubdirectory("dg-cli-shape").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");
            var (exitCode, output) = RunCliInDirectory(dir, null, "verify-shape", "--project", dir, "--format", "json");
            exitCode.Should().Be(2);
            output.Should().Contain("verify-shape requires --connection");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Empty-pass gate (red-team C4) and unavailable rules (red-team C2) ----

    /// <summary>Writes a schema-bearing <c>.dataguard-snapshot.json</c> (v3) through the production BaselineManager API.</summary>
    private static async Task<string> WriteDefaultSnapshotAsync(string dir, string provider)
    {
        var path = Path.Combine(dir, ".dataguard-snapshot.json");
        var schema = new List<SnapshotTable>
        {
            new("CUSTOMERS", new List<SnapshotColumn>
            {
                new("ID", "NUMBER", null, null, 22, 0, false, null),
                new("NAME", "VARCHAR2", 100, 100, null, null, true, "C"),
            }),
        };
        await new BaselineManager(path).CreateBaselineAsync(
            Array.Empty<ContractViolation>(), "1.0", "Snapshot", "19.0", schema: schema, provider: provider);
        return path;
    }

    private static string NewTempDirectory(string prefix) => Directory.CreateTempSubdirectory(prefix).FullName;

    [Theory]
    [InlineData("validate")]
    [InlineData("scan", "--project", ".")]
    [InlineData("baseline")]
    [InlineData("snapshot", "refresh")]
    [InlineData("snapshot", "diff")]
    [InlineData("verify-shape", "--project", ".", "--connection", "Server=127.0.0.1,1")]
    [InlineData("preflight", "--target", "t", "--output", "manifest.json", "--connection", "Server=127.0.0.1,1")]
    [InlineData("init", "--output", "init.yml")]
    public void UnknownProvider_Exit2WithAllowedValues_ForEveryCommand(params string[] command)
    {
        var dir = NewTempDirectory("dg-cli-provider");
        try
        {
            var run = CliProcessTestRunner.Run(dir, null, null, command.Concat(new[] { "--provider", "orcl" }).ToArray());

            run.ExitCode.Should().Be(2, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("Unsupported provider 'orcl'")
                .And.Contain("sqlserver, oracle, mysql, postgresql, postgres");
            File.Exists(Path.Combine(dir, "init.yml")).Should().BeFalse("nothing is written for a rejected provider");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConfigDefaultProvider_IsWhitelistedToo()
    {
        var dir = NewTempDirectory("dg-cli-provider-config");
        try
        {
            var config = Path.Combine(dir, "dataguard.yml");
            File.WriteAllText(config, "DefaultProvider: oracel\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(2);
            run.Stderr.Should().Contain("Unsupported provider 'oracel' (config DefaultProvider)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("baseline")]
    [InlineData("snapshot", "refresh")]
    [InlineData("snapshot", "show")]
    [InlineData("snapshot", "diff")]
    [InlineData("config", "show")]
    [InlineData("config", "validate")]
    [InlineData("oracle-check")]
    public void MissingConfigFile_Exit2_ForEveryCommand(params string[] command)
    {
        var dir = NewTempDirectory("dg-cli-missing-config");
        try
        {
            var missing = Path.Combine(dir, "missing.yml");

            var run = CliProcessTestRunner.Run(dir, null, null, command.Concat(new[] { "--config", missing }).ToArray());

            run.ExitCode.Should().Be(2, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("Configuration file not found");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void InvalidConfigValue_Exit2()
    {
        var dir = NewTempDirectory("dg-cli-invalid-config");
        try
        {
            var config = Path.Combine(dir, "dataguard.yml");
            File.WriteAllText(config, "EnableBaseline: maybe\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(2);
            run.Stderr.Should().Contain("Configuration invalid");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StrictConfig_UnknownKeys_Exit2()
    {
        var dir = NewTempDirectory("dg-cli-strict");
        try
        {
            var config = Path.Combine(dir, "dataguard.yml");
            File.WriteAllText(config, "StrictConfig: true\nSnapshotPath: x.json\nProvidr: oracle\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(2);
            run.Stderr.Should().Contain("unknown configuration keys: SnapshotPath, Providr");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task NonStrictConfig_UnknownKeys_WarnsAndProceeds()
    {
        var dir = NewTempDirectory("dg-cli-nonstrict");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "sqlserver");
            var config = Path.Combine(dir, "dataguard.yml");
            File.WriteAllText(config, "SnapshotPath: x.json\nProvidr: oracle\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("Warning: unknown configuration keys: SnapshotPath, Providr");
            run.Stdout.Should().Contain("Using snapshot");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_ProjectOnlyWithoutGroundTruth_Exit3Unevaluated()
    {
        var dir = NewTempDirectory("dg-cli-syntactic");
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", dir);

            run.ExitCode.Should().Be(3);
            run.Stderr.Should().Contain("UNEVALUATED: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_ProjectOnlyWithAllowSyntacticOnly_WarnsAndExitsByViolations()
    {
        var dir = NewTempDirectory("dg-cli-syntactic-ok");
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id, Name FROM Users\"; } }");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", dir, "--allow-syntactic-only");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("Warning: no ground truth").And.NotContain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_ContractsExportWithoutGroundTruth_IsNotGated()
    {
        var dir = NewTempDirectory("dg-cli-export");
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), "public class Repo { public void F() { var s = \"SELECT Id FROM Users\"; } }");
            var output = Path.Combine(dir, "contracts.json");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", dir, "--format", "contracts", "--output", output);

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            File.Exists(output).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_DefaultSnapshotDiscovered_Exit0()
    {
        var dir = NewTempDirectory("dg-cli-default-snapshot");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "sqlserver");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stdout.Should().Contain("Using snapshot .dataguard-snapshot.json");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_DefaultSnapshotNextToConfig_IsUsed()
    {
        var cwd = NewTempDirectory("dg-cli-snapshot-cwd");
        var configDir = NewTempDirectory("dg-cli-snapshot-config");
        try
        {
            var snapshot = await WriteDefaultSnapshotAsync(configDir, "sqlserver");
            var config = Path.Combine(configDir, ".dataguard.yml");
            File.WriteAllText(config, "GroundTruthMode: Snapshot\n");

            var run = CliProcessTestRunner.Run(cwd, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stdout.Should().Contain("Using snapshot " + snapshot);
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
            Directory.Delete(configDir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_BareOffline_UsesSnapshotAndNeverConnects()
    {
        var dir = NewTempDirectory("dg-cli-offline");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "sqlserver");

            // An unroutable credential: bare --offline must drop it, use the snapshot, and finish quickly.
            var run = CliProcessTestRunner.Run(dir, null, "Server=127.0.0.1,1;Connect Timeout=1", "validate", "--offline");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stdout.Should().Contain("Using snapshot");
            run.Stderr.Should().NotContain("Manual mode requires --assembly");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_BareOfflineWithoutSnapshot_Exit3()
    {
        var dir = NewTempDirectory("dg-cli-offline-nosnap");
        try
        {
            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--offline");

            run.ExitCode.Should().Be(3, "bare --offline means Snapshot mode; no snapshot is unevaluated, not a Manual-mode usage error");
            run.Stderr.Should().Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("oracle", "DG012")]
    [InlineData("postgresql", "PG004")]
    [InlineData("postgres", "PG004")]
    public async Task Validate_UnavailableRule_ReportedButDoesNotBlock(string provider, string ruleId)
    {
        var dir = NewTempDirectory("dg-cli-unavailable");
        try
        {
            await WriteDefaultSnapshotAsync(dir, provider);

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", provider);

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain($"Rule {ruleId} not evaluated: ");
            run.Stderr.Split('\n').Count(line => line.Contains($"{ruleId} not evaluated", StringComparison.Ordinal)).Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_FailOnUnavailable_Exit3()
    {
        var dir = NewTempDirectory("dg-cli-fail-unavailable");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "oracle");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", "oracle", "--fail-on-unavailable");

            run.ExitCode.Should().Be(3);
            run.Stderr.Should().Contain("DG012 not evaluated").And.Contain("UNEVALUATED");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_FailOnUnavailableRulesConfigKey_Exit3()
    {
        var dir = NewTempDirectory("dg-cli-fail-unavailable-config");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "oracle");
            var config = Path.Combine(dir, ".dataguard.yml");
            File.WriteAllText(config, "DefaultProvider: oracle\nFailOnUnavailableRules: true\nStrictConfig: true\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("DG012 not evaluated");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_FailOnUnavailableWithSkippedRule_Exit0()
    {
        var dir = NewTempDirectory("dg-cli-skip-unavailable");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "oracle");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", "oracle", "--fail-on-unavailable", "--skip-rules", "DG012");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().NotContain("DG012");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Snapshot v4 checks on validate (red-team H4) ----

    /// <summary>Writes a version 4 <c>.dataguard-snapshot.json</c> (tables plus one stored procedure) through the production API.</summary>
    private static async Task<string> WriteV4SnapshotAsync(string dir, string provider)
    {
        var path = Path.Combine(dir, ".dataguard-snapshot.json");
        var tables = new List<SnapshotTable>
        {
            new("CUSTOMERS", new List<SnapshotColumn>
            {
                new("ID", "NUMBER", null, null, 22, 0, false, null),
                new("NAME", "VARCHAR2", 100, 100, null, null, true, "C"),
            }),
        };
        var procedures = SnapshotConversion.FromProcedures(new ContractDescriptor[]
        {
            new StoredProcedureDescriptor(
                "proc:GET_CUSTOMER",
                "GET_CUSTOMER",
                "APP",
                string.Empty,
                new[] { new ParameterDescriptor("P_ID", "NUMBER", ParameterDirection.Input, null, 22, 0, false, 1) },
                Array.Empty<ColumnDescriptor>(),
                false),
        });
        await new BaselineManager(path).CreateSnapshotAsync(
            Array.Empty<ContractViolation>(), "1.0", "19.0", tables, procedures, provider, null, "CHAR", null);
        return path;
    }

    private static void RewriteSnapshot(string path, Action<System.Text.Json.Nodes.JsonNode> edit)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        File.WriteAllText(path, node.ToJsonString());
    }

    [Fact]
    public async Task Validate_V4Snapshot_Exit0_WithoutLegacyWarnings()
    {
        var dir = NewTempDirectory("dg-cli-v4");
        try
        {
            await WriteV4SnapshotAsync(dir, "sqlserver");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().NotContain("snapshot has no stored procedures").And.NotContain("days old").And.NotContain("integrity");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_TablesOnlySnapshot_WarnsThatProcedureChecksNeedARefresh()
    {
        var dir = NewTempDirectory("dg-cli-v3-warn");
        try
        {
            await WriteDefaultSnapshotAsync(dir, "sqlserver");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("snapshot has no stored procedures; run 'dataguard snapshot refresh' to enable procedure checks");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validate_TamperedSnapshot_Exit3IntegrityCheckFailed(bool v4)
    {
        var dir = NewTempDirectory("dg-cli-tampered");
        try
        {
            var path = v4 ? await WriteV4SnapshotAsync(dir, "sqlserver") : await WriteDefaultSnapshotAsync(dir, "sqlserver");

            // Same PR edits the committed snapshot to make a column look wider: the stored hash no longer matches.
            RewriteSnapshot(path, node => node["Schema"]![0]!["Columns"]![1]!["MaxLength"] = 4000);
            var run = CliProcessTestRunner.Run(dir, null, null, "validate");

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("UNEVALUATED: snapshot integrity check failed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_TamperedProcedureParameter_Exit3()
    {
        var dir = NewTempDirectory("dg-cli-tampered-proc");
        try
        {
            var path = await WriteV4SnapshotAsync(dir, "sqlserver");
            RewriteSnapshot(path, node => node["StoredProcedures"]![0]!["Parameters"]![0]!["DataType"] = "VARCHAR2");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate");

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("snapshot integrity check failed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Validate_SnapshotProviderMismatch_Exit3(bool v4)
    {
        var dir = NewTempDirectory("dg-cli-provider-mismatch");
        try
        {
            _ = v4 ? await WriteV4SnapshotAsync(dir, "oracle") : await WriteDefaultSnapshotAsync(dir, "oracle");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", "sqlserver");

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("UNEVALUATED: snapshot provider 'oracle' does not match 'sqlserver'");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_SnapshotProviderAlias_PostgresMatchesPostgresql()
    {
        var dir = NewTempDirectory("dg-cli-provider-alias");
        try
        {
            await WriteV4SnapshotAsync(dir, "postgresql");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", "postgres");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().NotContain("does not match");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_OldSnapshot_WarnsWithItsAge()
    {
        var dir = NewTempDirectory("dg-cli-old-snapshot");
        try
        {
            var path = await WriteV4SnapshotAsync(dir, "sqlserver");

            // CreatedAt is metadata, not hashed content: an old but untouched snapshot stays valid and only warns.
            RewriteSnapshot(path, node => node["CreatedAt"] = DateTimeOffset.UtcNow.AddDays(-200).AddHours(-1).ToString("O"));
            var run = CliProcessTestRunner.Run(dir, null, null, "validate");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("Warning: snapshot is 200 days old (SnapshotMaxAgeDays: 90)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(30, false)]
    [InlineData(0, false)]
    public async Task Validate_SnapshotMaxAgeDaysConfigKey_ControlsTheAgeWarning(int maxAgeDays, bool warns)
    {
        var dir = NewTempDirectory("dg-cli-snapshot-age");
        try
        {
            var path = await WriteV4SnapshotAsync(dir, "sqlserver");
            RewriteSnapshot(path, node => node["CreatedAt"] = DateTimeOffset.UtcNow.AddDays(-20).AddHours(-1).ToString("O"));
            var config = Path.Combine(dir, ".dataguard.yml");
            File.WriteAllText(config, $"StrictConfig: true\nSnapshotMaxAgeDays: {maxAgeDays}\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config);

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            if (warns)
            {
                run.Stderr.Should().Contain($"snapshot is 20 days old (SnapshotMaxAgeDays: {maxAgeDays})");
            }
            else
            {
                run.Stderr.Should().NotContain("days old");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotShow_V4_PrintsTableAndProcedureCountsAndIntegrity()
    {
        var dir = NewTempDirectory("dg-cli-show-v4");
        try
        {
            await WriteV4SnapshotAsync(dir, "sqlserver");

            var run = CliProcessTestRunner.Run(dir, null, null, "snapshot", "show");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stdout.Should().Contain("Version: 4")
                .And.Contain("Tables: 1 (columns: 2)")
                .And.Contain("Stored Procedures: 1 (parameters: 1)")
                .And.Contain("Length Semantics: CHAR")
                .And.Contain("Integrity: Verified");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotDiff_TamperedV4Snapshot_Exit3BeforeConnecting()
    {
        var dir = NewTempDirectory("dg-cli-diff-tampered");
        try
        {
            var path = await WriteV4SnapshotAsync(dir, "sqlserver");
            RewriteSnapshot(path, node => node["StoredProcedures"]![0]!["Parameters"]![0]!["Direction"] = "Output");

            var run = CliProcessTestRunner.Run(dir, null, null, "snapshot", "diff");

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("UNEVALUATED: snapshot integrity check failed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private const string UnreachableOracleConnection = "Data Source=127.0.0.1:1/XEPDB1;User Id=u;Password=oracle-secret;Connection Timeout=2";

    /// <summary>
    /// A typed Dapper query plus a source-only EF snapshot (ground truth) for the Oracle provider. With no DefaultSchema the
    /// Oracle catalog is not read, so only the live shape rule touches the (unreachable) database.
    /// </summary>
    private static string WriteUnevaluatedFixture(string dir, bool partialSnapshot = false)
    {
        File.WriteAllText(Path.Combine(dir, "Repo.cs"), """
            using Dapper;
            public class Customer { public int Id { get; set; } public string Name { get; set; } }
            public class Repo { public void F(System.Data.IDbConnection c) { var rows = c.Query<Customer>("SELECT Id, Name FROM Customers"); } }
            """);
        var snapshotDir = Directory.CreateDirectory(Path.Combine(dir, "Migrations")).FullName;
        var snapshot = Path.Combine(snapshotDir, "AppModelSnapshot.cs");
        var extraEntity = partialSnapshot ? "\n  modelBuilder.Entity(\"Shop.Order\", b => { b.ToTable(\"ORDERS\"); });" : string.Empty;
        File.WriteAllText(snapshot, "class Snapshot { void Build(ModelBuilder modelBuilder) {\n  modelBuilder.Entity<Customer>(b => { b.ToTable(\"Customers\"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); });" + extraEntity + "\n}}\n");
        return snapshot;
    }

    [Fact]
    public void Validate_LiveDescribeFails_ListsUnevaluatedContractAndExits3()
    {
        var dir = NewTempDirectory("dg-cli-unevaluated");
        try
        {
            var snapshot = WriteUnevaluatedFixture(dir);

            var run = CliProcessTestRunner.Run(dir, null, null,
                "validate", "--provider", "oracle", "--connection", UnreachableOracleConnection,
                "--project", Path.Combine(dir, "Repo.cs"), "--ef-snapshot", snapshot);

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("UNEVALUATED: 1 contract(s) could not be evaluated:");
            run.Stderr.Should().MatchRegex(@"DG020 project-sql:[^\s]*Repo\.cs:\d+(?::[0-9a-f]{8})?: Cannot determine result set shape for query \(describe failed\)");
            (run.Stdout + run.Stderr).Should().NotContain("[WARNING] DG020", "an undescribed shape is no longer a DG020 warning");
            (run.Stdout + run.Stderr).Should().NotContain("oracle-secret");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_LiveDescribeFailsWithAllowUnevaluated_StillListsButExitsByViolations()
    {
        var dir = NewTempDirectory("dg-cli-allow-unevaluated");
        try
        {
            var snapshot = WriteUnevaluatedFixture(dir);

            var run = CliProcessTestRunner.Run(dir, null, null,
                "validate", "--provider", "oracle", "--connection", UnreachableOracleConnection,
                "--project", Path.Combine(dir, "Repo.cs"), "--ef-snapshot", snapshot, "--allow-unevaluated");

            run.Stderr.Should().Contain("UNEVALUATED: 1 contract(s) could not be evaluated:");
            run.Stderr.Should().NotContain("pass --allow-unevaluated");
            var hasErrors = (run.Stdout + run.Stderr).Contains("[ERROR]", StringComparison.Ordinal);
            run.ExitCode.Should().Be(hasErrors ? 1 : 0, run.Stdout + run.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_PartialEfSnapshot_PrintsAcquisitionDiagnosticAndExits3UnlessAllowed()
    {
        var dir = NewTempDirectory("dg-cli-acquisition");
        try
        {
            var snapshot = WriteUnevaluatedFixture(dir, partialSnapshot: true);

            var gated = CliProcessTestRunner.Run(dir, null, null, "validate", "--ef-snapshot", snapshot);
            var allowed = CliProcessTestRunner.Run(dir, null, null, "validate", "--ef-snapshot", snapshot, "--allow-unevaluated");

            gated.ExitCode.Should().Be(3, gated.Stdout + gated.Stderr);
            gated.Stderr.Should().Contain($"ACQUISITION: {snapshot}: DG1304 (line 3)");
            allowed.Stderr.Should().Contain("ACQUISITION: ");
            allowed.ExitCode.Should().BeOneOf(new[] { 0, 1 }, allowed.Stdout + allowed.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_IdeSafe_AcceptsAllowUnevaluated()
    {
        var dir = NewTempDirectory("dg-cli-ide-safe-unevaluated");
        try
        {
            WriteUnevaluatedFixture(dir);

            // --ide-safe already implies --allow-unevaluated (like --allow-syntactic-only); passing it explicitly is allowed.
            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--ide-safe", "--allow-unevaluated", "--project", Path.Combine(dir, "Repo.cs"));

            run.Stderr.Should().StartWith("ide-safe: active");
            run.ExitCode.Should().BeOneOf(new[] { 0, 1 }, run.Stdout + run.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- Credentials and Manual-mode assembly loading (red-team D1, Medium: Assembly.LoadFrom) ----
    private const string NamedConnectionVariable = "DG_E2E_CONNECTION";

    /// <summary>Child environment with a private home (no credential file, audit log under the temp dir) plus extras.</summary>
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

    private static string WriteRepoSource(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "Repo.cs"), IdeSafeEndToEndSupport.RepoSource);
        return dir;
    }

    [Fact]
    public void VerifyShape_ConnectionEnv_UsesTheNamedVariableWithoutTheArgvWarning()
    {
        var dir = NewTempDirectory("dg-cli-connection-env");
        try
        {
            var run = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir, (NamedConnectionVariable, IdeSafeEndToEndSupport.EnvConnection)),
                "verify-shape", "--project", WriteRepoSource(dir), "--connection-env", NamedConnectionVariable);

            run.Stderr.Should().NotContain("verify-shape requires --connection", "the named variable supplied the connection");
            run.Stderr.Should().NotContain(CliConfigurationResolver.CommandLineConnectionWarning);
            run.ExitCode.Should().NotBe(2, run.Stdout + run.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyShape_ConnectionEnvNamingAnUnsetVariable_Exit2()
    {
        var dir = NewTempDirectory("dg-cli-connection-env-unset");
        try
        {
            var run = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir),
                "verify-shape", "--project", WriteRepoSource(dir), "--connection-env", NamedConnectionVariable);

            run.ExitCode.Should().Be(2, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain($"--connection-env {NamedConnectionVariable}: the environment variable is not set or empty.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyShape_CommandLineConnection_PrintsTheDeprecationWarningOnce()
    {
        var dir = NewTempDirectory("dg-cli-connection-argv");
        try
        {
            var run = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir),
                "verify-shape", "--project", WriteRepoSource(dir), "--connection", IdeSafeEndToEndSupport.EnvConnection);

            var warnings = IdeSafeEndToEndSupport.Lines(run.Stderr).Count(line => line == CliConfigurationResolver.CommandLineConnectionWarning);
            warnings.Should().Be(1, run.Stderr);
            run.Stderr.Should().Contain("warning: a connection string on the command line is visible to process listings; prefer --connection-env");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyShape_PlaintextYamlConnection_IgnoredByDefault_HonoredWithAllowPlaintextConfigFallback()
    {
        var dir = NewTempDirectory("dg-cli-plaintext");
        try
        {
            WriteRepoSource(dir);
            var ignoredConfig = Path.Combine(dir, "ignored.yml");
            File.WriteAllText(ignoredConfig, $"ConnectionString: \"{IdeSafeEndToEndSupport.EnvConnection}\"\n");
            var allowedConfig = Path.Combine(dir, "allowed.yml");
            File.WriteAllText(allowedConfig, $"ConnectionString: \"{IdeSafeEndToEndSupport.EnvConnection}\"\nAllowPlaintextConfigFallback: true\n");

            var ignored = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir), "verify-shape", "--project", dir, "--config", ignoredConfig);
            var allowed = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir), "verify-shape", "--project", dir, "--config", allowedConfig);

            ignored.ExitCode.Should().Be(2, ignored.Stdout + ignored.Stderr);
            ignored.Stderr.Should().Contain(CliConfigurationResolver.IgnoredPlaintextConnectionWarning)
                .And.Contain("verify-shape requires --connection");
            ignored.Stderr.Should().NotContain("127.0.0.1", "the ignored value is never echoed");

            allowed.Stderr.Should().Contain(CliConfigurationResolver.PlaintextConnectionInUseWarning);
            allowed.Stderr.Should().NotContain("verify-shape requires --connection");
            allowed.ExitCode.Should().NotBe(2, allowed.Stdout + allowed.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_IdeSafeConnectionEnv_RequiresAllowEnvConnection()
    {
        var dir = NewTempDirectory("dg-cli-ide-safe-connection-env");
        try
        {
            WriteRepoSource(dir);
            var environment = IsolatedEnvironment(dir, (NamedConnectionVariable, IdeSafeEndToEndSupport.EnvConnection));

            var rejected = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, environment, "validate", "--ide-safe", "--project", dir, "--connection-env", NamedConnectionVariable);
            var kept = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, environment, "validate", "--ide-safe", "--allow-env-connection", "--project", dir, "--connection-env", NamedConnectionVariable);

            rejected.ExitCode.Should().Be(2, rejected.Stderr);
            var lines = IdeSafeEndToEndSupport.Lines(rejected.Stderr);
            lines[0].Should().Be("ide-safe: active");
            lines[1].Should().StartWith("--connection-env is not allowed with --ide-safe unless --allow-env-connection is also given");

            IdeSafeEndToEndSupport.Lines(kept.Stderr)[0].Should().Be("ide-safe: active");
            kept.Stderr.Should().Contain("ide-safe: kept environment connection (--allow-env-connection)");
            kept.ExitCode.Should().NotBe(2, kept.Stdout + kept.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ManualAssemblyPathFromConfig_RequiresAllowAssemblyFromConfig()
    {
        var dir = NewTempDirectory("dg-cli-manual-config");
        try
        {
            var assembly = typeof(global::DataGuard.Contracts.ExpectedColumnAttribute).Assembly.Location;
            var config = Path.Combine(dir, "manual.yml");
            File.WriteAllText(config, $"GroundTruthMode: Manual\nManualAssemblyPath: \"{assembly}\"\n");

            var validate = CliProcessTestRunner.RunWithEnvironment(dir, null, null, IsolatedEnvironment(dir), "validate", "--config", config);
            var baseline = CliProcessTestRunner.RunWithEnvironment(dir, null, null, IsolatedEnvironment(dir), "baseline", "--config", config, "--output", Path.Combine(dir, "baseline.json"));
            var allowed = CliProcessTestRunner.RunWithEnvironment(dir, null, null, IsolatedEnvironment(dir), "validate", "--config", config, "--allow-assembly-from-config");
            var commandLine = CliProcessTestRunner.RunWithEnvironment(dir, null, null, IsolatedEnvironment(dir), "validate", "--offline", "--assembly", assembly);

            validate.ExitCode.Should().Be(2, validate.Stderr);
            validate.Stderr.Should().Contain("ManualAssemblyPath is set in the configuration file; pass --allow-assembly-from-config");
            baseline.ExitCode.Should().Be(2, baseline.Stderr);
            baseline.Stderr.Should().Contain("--allow-assembly-from-config");
            File.Exists(Path.Combine(dir, "baseline.json")).Should().BeFalse();

            allowed.Stderr.Should().NotContain("ManualAssemblyPath is set in the configuration file");
            allowed.ExitCode.Should().NotBe(2, allowed.Stdout + allowed.Stderr);
            commandLine.Stderr.Should().NotContain("ManualAssemblyPath is set in the configuration file", "--assembly on the command line needs no flag");
            commandLine.ExitCode.Should().NotBe(2, commandLine.Stdout + commandLine.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_IdeSafeRejectsAllowAssemblyFromConfig()
    {
        var dir = NewTempDirectory("dg-cli-ide-safe-assembly");
        try
        {
            var run = CliProcessTestRunner.RunWithEnvironment(
                dir, null, null, IsolatedEnvironment(dir), "validate", "--ide-safe", "--allow-assembly-from-config", "--project", WriteRepoSource(dir));

            run.ExitCode.Should().Be(2);
            IdeSafeEndToEndSupport.Lines(run.Stderr)[1].Should().StartWith("--allow-assembly-from-config is not allowed with --ide-safe");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- One pipeline: config-driven procedure rules and plugins (red-team B4/D3) ----

    /// <summary>
    /// SP_Contract fixture (golden case SP_001): a v4 snapshot with <c>dbo.usp_GetOrders(@CustomerId, @Status, @Top = default)</c>
    /// written through <see cref="BaselineManager.CreateSnapshotAsync"/>, and a Dapper call that omits the required <c>@Status</c>.
    /// </summary>
    private static async Task WriteMissingParameterFixtureAsync(string dir)
    {
        var procedures = SnapshotConversion.FromProcedures(new ContractDescriptor[]
        {
            new StoredProcedureDescriptor(
                "sqlserver:dbo.usp_GetOrders",
                "usp_GetOrders",
                "dbo",
                string.Empty,
                new[]
                {
                    new ParameterDescriptor("@CustomerId", "int", ParameterDirection.Input, null, 10, 0, false, 1),
                    new ParameterDescriptor("@Status", "nvarchar", ParameterDirection.Input, 20, null, null, true, 2),
                    new ParameterDescriptor("@Top", "int", ParameterDirection.Input, null, 10, 0, true, 3, HasDefault: true),
                },
                Array.Empty<ColumnDescriptor>(),
                false),
        });
        var tables = new List<SnapshotTable>
        {
            new("Orders", new List<SnapshotColumn> { new("Id", "int", null, null, 10, 0, false, null) }, "dbo"),
        };
        await new BaselineManager(Path.Combine(dir, ".dataguard-snapshot.json")).CreateSnapshotAsync(
            Array.Empty<ContractViolation>(), "1.0", "16.0", tables, procedures, "sqlserver", null, "CHAR", null);
        File.WriteAllText(Path.Combine(dir, "OrderRepository.cs"), """
            using System.Data;
            using Dapper;

            public class OrderRepository
            {
                public void Load(IDbConnection conn, int customerId)
                {
                    conn.Execute("dbo.usp_GetOrders", new { CustomerId = customerId }, commandType: CommandType.StoredProcedure);
                }
            }
            """);
    }

    [Fact]
    public async Task Validate_StrictProcedureContractsConfig_TurnsDg101WarningIntoError()
    {
        var dir = NewTempDirectory("dg-cli-strict-sp");
        try
        {
            await WriteMissingParameterFixtureAsync(dir);
            var repo = Path.Combine(dir, "OrderRepository.cs");
            var strictConfig = Path.Combine(dir, "strict.yml");
            File.WriteAllText(strictConfig, "StrictProcedureContracts: true\n");

            var lenient = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", repo);
            var strict = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", repo, "--config", strictConfig);

            lenient.Stdout.Should().Contain("[WARNING] DG101:", lenient.Stdout + lenient.Stderr)
                .And.Contain("@Status");
            lenient.ExitCode.Should().Be(0, lenient.Stdout + lenient.Stderr);
            strict.Stdout.Should().Contain("[ERROR] DG101:", strict.Stdout + strict.Stderr)
                .And.Contain("@Status");
            strict.ExitCode.Should().Be(1, strict.Stdout + strict.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_PluginsDir_WithAllowUnsignedLocal_RunsThePluginRule()
    {
        var dir = NewTempDirectory("dg-cli-plugins");
        try
        {
            await WriteV4SnapshotAsync(dir, "sqlserver");
            var plugins = Directory.CreateDirectory(Path.Combine(dir, "plugins")).FullName;
            TestPluginBuilder.WritePlugin(plugins, "PLUG001");
            var config = Path.Combine(dir, "dataguard.yml");
            File.WriteAllText(config, "Plugins:\n  AllowUnsignedLocal: true\nStrictConfig: true\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--config", config, "--plugins-dir", plugins);

            run.Stdout.Should().Contain("[WARNING] PLUG001: PLUG001 saw", run.Stdout + run.Stderr);
            run.Stderr.Should().NotContain("not loaded");
            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_PluginsDir_DefaultTrustPolicyRejectsUnsignedPlugin()
    {
        var dir = NewTempDirectory("dg-cli-plugins-unsigned");
        try
        {
            await WriteV4SnapshotAsync(dir, "sqlserver");
            var plugins = Directory.CreateDirectory(Path.Combine(dir, "plugins")).FullName;
            TestPluginBuilder.WritePlugin(plugins, "PLUG001");

            var reported = CliProcessTestRunner.Run(dir, null, null, "validate", "--plugins-dir", plugins);
            var gated = CliProcessTestRunner.Run(dir, null, null, "validate", "--plugins-dir", plugins, "--fail-on-unavailable");

            reported.Stderr.Should().Contain("Plugin DataGuard.TestPlugins.PLUG001.dll not loaded: Signed provenance verifier is required.");
            reported.Stdout.Should().NotContain("PLUG001 saw");
            reported.ExitCode.Should().Be(0, reported.Stdout + reported.Stderr);
            gated.ExitCode.Should().Be(3, gated.Stdout + gated.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_PluginsDir_MissingDirectory_Exit2()
    {
        var dir = NewTempDirectory("dg-cli-plugins-missing");
        try
        {
            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--plugins-dir", Path.Combine(dir, "nope"));

            run.ExitCode.Should().Be(2, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("--plugins-dir directory not found");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_PluginsDir_RejectedUnderIdeSafe_Exit2()
    {
        var dir = NewTempDirectory("dg-cli-plugins-ide-safe");
        try
        {
            var plugins = Directory.CreateDirectory(Path.Combine(dir, "plugins")).FullName;

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--ide-safe", "--plugins-dir", plugins);

            run.ExitCode.Should().Be(2, run.Stdout + run.Stderr);
            run.Stderr.Should().StartWith("ide-safe: active");
            run.Stderr.Should().Contain("--plugins-dir is not allowed with --ide-safe");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
