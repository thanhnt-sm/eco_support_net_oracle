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
}
