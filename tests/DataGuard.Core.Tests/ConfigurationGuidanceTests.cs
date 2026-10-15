using DataGuard.Cli;
using DataGuard.Cli.Services;
using DataGuard.Core.Models;
using DataGuard.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class ConfigurationGuidanceTests
{
    // Sanitized settings reproduce the reported shape; no application code or credentials.
    internal const string ReportedSnapshotConfiguration = """
        GroundTruthMode: Snapshot
        SnapshotFilePath: .dataguard-snapshot.json
        BaselineFilePath: .dataguard-baseline.json
        NamingConvention: SnakeCaseToPascalCase
        EnableBaseline: true
        ExcludedProcedures: []
        ExcludedEntities: []
        Oracle: true
        SqlServer: false
        MaxDegreeOfParallelism: 0
        EnableConcurrentValidation: true
        ValidationTimeoutSeconds: 300
        MaxViolationQueueSize: 100000
        EnableCredentialRotationDetection: true
        CredentialRotationWarningDays: 30
        EncryptConnectionStringAtRest: false
        KeyVaultUri:
        AwsRegion:
        VaultAddress:
        EnableAuditLogging: true
        AuditLogPath:
        AllowPlaintextConfigFallback: false
        ManualAssemblyPath:
        AutoDetectProvider: true
        AutoDetectEFContext: true
        AutoDetectDapper: true
        EnableSmartDefaults: true
        DefaultSchema:
        DefaultPackage:
        EnableTelemetry: false
        DefaultProvider: sqlserver
        TelemetryFileDirectory:
        TelemetryServiceName: dataguard
        TelemetryServiceVersion: unknown
        IncludeTelemetryEventDetails: false
        """;

    [Fact]
    public void BooleanProviderBlocks_AreRejectedInsteadOfSilentlySelectingSqlServer()
    {
        var load = () => ConfigLoader.DeserializeConfig(ReportedSnapshotConfiguration);
        load.Should().Throw<FormatException>().WithMessage("*Oracle*mapping*DefaultProvider*");
    }

    [Fact]
    public void GeneratedOracleConfiguration_IsCommentedAndSelectsOracle()
    {
        var yaml = ConfigLoader.SerializeConfig(new DataGuardConfiguration { DefaultProvider = "oracle" });
        yaml.Should().Contain("DefaultProvider: oracle #").And.Contain("MaxDegreeOfParallelism: 0 #");
        yaml.Should().NotContain("Oracle: true").And.NotContain("SqlServer: false");
        var config = ConfigLoader.DeserializeConfig(yaml);
        CliConfigurationResolver.ResolveProvider(config, null).Should().Be("oracle");
        CliConfigurationResolver.ResolveProvider(config, "sqlserver").Should().Be("sqlserver");
    }

    [Theory]
    [InlineData("Oracle: true\nDefaultProvider: sqlserver\n", "Oracle")]
    [InlineData("DefaultProvider: unsupported\n", "Unsupported provider")]
    public void ConfigValidate_RejectsInvalidShapesAndProviders(string yaml, string error)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dg-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ".dataguard.yml");
            File.WriteAllText(path, yaml);
            var run = CliProcessTestRunner.Run(directory, null, null, "config", "validate", "--config", path);
            run.ExitCode.Should().Be(2);
            run.Stderr.Should().Contain(error);
            run.Stdout.Should().NotContain("Configuration is valid");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [Theory]
    [InlineData("oracle-snapshot.minimal.yml")]
    [InlineData("oracle-snapshot.full.yml")]
    public async Task DocumentedExamples_ParseAndSelectOracleWithoutClaimingGrammarValidation(string filename)
    {
        var yaml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ConfigurationExamples", filename));
        var config = ConfigLoader.DeserializeConfig(yaml);
        config.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        config.ConnectionString.Should().BeNullOrEmpty();
        config.AllowPlaintextConfigFallback.Should().BeFalse();
        var provider = CliConfigurationResolver.ResolveProvider(config, null);
        provider.Should().Be("oracle");
        var raw = new DataGuard.Core.Abstractions.RawSqlDescriptor("sample", "UPDATE SAMPLE_RECORD rec SET rec.AMOUNT = :amount WHERE rec.ID = :id", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
        var rule = (DataGuard.Core.Rules.RawSqlParseStatusRule)ProviderRuleCatalog.Get(provider).Single(r => r.Rule.RuleId == "DG019").Rule;
        (await rule.ValidateAsync(raw, Array.Empty<ContractDescriptor>())).Should().BeEmpty();
        rule.DrainUnevaluatedContracts().Should().ContainSingle();
    }

    [Fact]
    public void InitOracle_WritesCommentedConfigAndPreservesExistingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dg-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ".dataguard.yml");
            var run = CliProcessTestRunner.Run(directory, null, null, "init", "--provider", "oracle", "--output", path);
            run.ExitCode.Should().Be(0, run.Stderr);
            var yaml = File.ReadAllText(path);
            yaml.Should().Contain("DefaultProvider: oracle #");
            var documented = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ConfigurationExamples", "oracle-snapshot.full.yml"));
            yaml.Replace("\r\n", "\n").Should().Be(documented.Replace("\r\n", "\n"));
            ConfigLoader.DeserializeConfig(yaml).DefaultProvider.Should().Be("oracle");
            File.AppendAllText(path, "# Handwritten team guidance\n");
            var before = File.ReadAllText(path);
            var repeated = CliProcessTestRunner.Run(directory, null, null, "init", "--output", path);
            repeated.ExitCode.Should().Be(2);
            File.ReadAllText(path).Should().Be(before);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ValidConflictingProviderBlocks_AreReportedWithoutChangingSelection()
    {
        var config = ConfigLoader.DeserializeConfig("DefaultProvider: sqlserver\nAutoDetectProvider: true\nOracle: {}\n");
        config.Oracle.Should().NotBeNull();
        CliConfigurationResolver.ResolveProvider(config, null).Should().Be("sqlserver");
    }
    [Fact]
    public void ConfigValidate_ExplainsConflictingValidProviderBlocks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dg-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ".dataguard.yml");
            File.WriteAllText(path, "DefaultProvider: sqlserver\nOracle: {}\n");
            var run = CliProcessTestRunner.Run(directory, null, null, "config", "validate", "--config", path);
            run.ExitCode.Should().Be(0);
            run.Stderr.Should().Contain("provider-specific settings do not select the dialect");
            run.Stdout.Should().Contain("DefaultProvider (effective): sqlserver");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [Theory]
    [InlineData("Oracle", "true")]
    [InlineData("SqlServer", "false")]
    [InlineData("Plugins", "true")]
    public void MappingSettings_RejectBooleanScalars(string key, string value)
    {
        var load = () => ConfigLoader.DeserializeConfig(key + ": " + value + "\n");
        load.Should().Throw<FormatException>().WithMessage(key + "*mapping*");
    }

    [Theory]
    [InlineData("label: # literal")]
    [InlineData("first line\nsecond line")]
    [InlineData("\"quoted\": # label\nDefaultProvider: not-a-key")]
    public void CommentedWriter_RoundTripsQuotedAndMultilineScalarValues(string value)
    {
        var config = new DataGuardConfiguration { TelemetryServiceName = value };
        var loaded = ConfigLoader.DeserializeConfig(ConfigLoader.SerializeConfig(config));
        loaded.TelemetryServiceName.Should().Be(value);
        loaded.DefaultProvider.Should().BeNull();
    }
}
