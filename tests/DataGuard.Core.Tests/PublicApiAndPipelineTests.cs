using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataGuard;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Plugins;
using DataGuard.Core.Rules;
using DataGuard.Core.Security;
using DataGuard.Core.Telemetry;
using DataGuard.Core.Validation;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class PublicApiAndPipelineTests
{
    [Fact]
    public async Task ValidationPipeline_BasicExecution_ReturnsValidationResult()
    {
        using var pipeline = DataGuardApi.CreatePipeline();

        var entity = new EntityDescriptor(
            "e1", "Customer", "Customer", "CUSTOMERS",
            new List<PropertyDescriptor>
            {
                new PropertyDescriptor("FullName", "string", "FULL_NAME", "VARCHAR2(100)", false, 200, false, false),
            });

        var result = await pipeline.ValidateAsync(new[] { entity });

        result.Should().NotBeNull();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.ContractsValidated.Should().Be(1);
    }

    [Fact]
    public async Task ValidationPipeline_TelemetryWritesLocalDailyArchive()
    {
        var directory = Directory.CreateTempSubdirectory("dg-pipeline-observability-");
        try
        {
            var config = new DataGuardConfiguration
            {
                EnableTelemetry = true,
                EnableAuditLogging = false,
                TelemetryFileDirectory = directory.FullName,
                TelemetryServiceName = "dataguard-pipeline-tests",
            };

            using (var pipeline = DataGuardApi.CreatePipeline(config))
            {
                var result = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
                result.ContractsValidated.Should().Be(0);
            }

            var archiveFiles = Directory.GetFiles(directory.FullName, "*.ndjson", SearchOption.AllDirectories);
            archiveFiles.Should().ContainSingle();
            (await File.ReadAllTextAsync(archiveFiles[0])).Should().Contain("dataguard.validation");
        }
        finally
        {
            if (directory.Exists)
            {
                directory.Delete(recursive: true);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_FluentConfiguration_ChainsCorrectly()
    {
        var tempBaseline = Path.Combine(Path.GetTempPath(), $"dg-base-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempBaseline, """
                {
                  "Version": 2,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "Violations": []
                }
                """);

            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration())
                .WithBaselineFile(tempBaseline)
                .WithTelemetry(new Telemetry.TelemetryConfig(Enabled: false))
                .WithRules(new PipelineUnifiedTests.FixedRule("CUSTOM900"));

            var entity = new EntityDescriptor(
                "e1", "Customer", "Customer", "CUSTOMERS",
                new List<PropertyDescriptor>
                {
                    new PropertyDescriptor("FullName", "string", "FULL_NAME", "VARCHAR2(100)", false, 200, false, false),
                });

            var result = await pipeline.ValidateAsync(new[] { entity });
            result.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(tempBaseline))
            {
                File.Delete(tempBaseline);
            }
        }
    }

    [Fact]
    public void DataGuardFactory_CreatesComponents()
    {
        var graph = DataGuardFactory.CreateRuleGraph();
        graph.Should().NotBeNull();
        graph.GetExecutionOrder().Should().NotBeEmpty();

        var cred = DataGuardFactory.CreateCredentialManager(new DataGuardConfiguration());
        cred.Should().NotBeNull();
    }
    [Fact]
    public async Task ValidationPipeline_CheckDriftWithoutBaseline_ReportsSetupRequirement()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"dg-baseline-{Guid.NewGuid():N}.json");
        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath });

        var report = await pipeline.CheckDriftAsync(Array.Empty<ContractViolation>());

        report.HasBaseline.Should().BeFalse();
        report.DriftDetected.Should().BeFalse();
        report.NewViolations.Should().BeEmpty();
        report.Message.Should().Contain("CreateBaseline");
        report.Status.Should().Be(DriftEvaluationStatus.Missing);
    }

    [Fact]
    public async Task ValidationPipeline_CheckDriftWithEmptyBaseline_ReportsNoDrift()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"dg-baseline-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(baselinePath, """
                {
                  "Version": 2,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "Violations": []
                }
                """);

            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath, EnableSmartDefaults = false });
            var report = await pipeline.CheckDriftAsync(Array.Empty<ContractViolation>());

            report.HasBaseline.Should().BeTrue();
            report.DriftDetected.Should().BeFalse();
            report.NewViolations.Should().BeEmpty();
            report.Status.Should().Be(DriftEvaluationStatus.Complete);
        }
        finally
        {
            if (File.Exists(baselinePath))
            {
                File.Delete(baselinePath);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_ReportsTypedStatusAndStructuralChange()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"dg-schema-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(baselinePath, """
                {
                  "Version": 2,
                  "CreatedAt": "2026-01-01T00:00:00Z",
                  "SchemaVersion": "1.0",
                  "GroundTruthMode": "Snapshot",
                  "DatabaseVersion": "1.0",
                  "SchemaHash": "",
                  "Violations": [],
                  "Schema": [{ "Name": "CUSTOMERS", "Columns": [{ "Name": "ID", "DataType": "NUMBER", "MaxLength": null, "CharLength": null, "Precision": 22, "Scale": 0, "IsNullable": false, "CharUsed": null }] }]
                }
                """);

            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath, EnableSmartDefaults = false });
            var currentSchema = new DatabaseSchemaDescriptor(
                "current",
                new[] { new DatabaseTableDescriptor("CUSTOMERS", new[] { new ColumnDescriptor("ID", "NUMBER", null, 22, 0, false, null, null), new ColumnDescriptor("NAME", "VARCHAR2", 120, null, null, true, "CHAR", 120) }) },
                "CHAR");

            var report = await pipeline.CheckDriftAsync(currentSchema);

            report.Status.Should().Be(DriftEvaluationStatus.Complete);
            report.HasBaseline.Should().BeTrue();
            report.DriftDetected.Should().BeTrue();
            report.BaselineHash.Should().NotBe(report.CurrentHash);
        }
        finally
        {
            if (File.Exists(baselinePath))
            {
                File.Delete(baselinePath);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_DistinguishesCorruptAndUnsupportedBaselines()
    {
        var corruptPath = Path.Combine(Path.GetTempPath(), $"dg-corrupt-{Guid.NewGuid():N}.json");
        var unsupportedPath = Path.Combine(Path.GetTempPath(), $"dg-unsupported-{Guid.NewGuid():N}.json");
        var schema = new DatabaseSchemaDescriptor("current", Array.Empty<DatabaseTableDescriptor>(), "CHAR");
        try
        {
            await File.WriteAllTextAsync(corruptPath, "{ this is not json");
            using (var corruptPipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = corruptPath }))
            {
                var corrupt = await corruptPipeline.CheckDriftAsync(schema);
                corrupt.Status.Should().Be(DriftEvaluationStatus.Corrupt);
                corrupt.DriftDetected.Should().BeFalse();
            }

            await File.WriteAllTextAsync(unsupportedPath, """
                { "Version": 99, "CreatedAt": "2026-01-01T00:00:00Z", "SchemaVersion": "1.0", "GroundTruthMode": "Snapshot", "DatabaseVersion": "1.0", "SchemaHash": "", "Violations": [], "Schema": [] }
                """);
            using (var unsupportedPipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = unsupportedPath }))
            {
                var unsupported = await unsupportedPipeline.CheckDriftAsync(schema);
                unsupported.Status.Should().Be(DriftEvaluationStatus.UnsupportedVersion);
                unsupported.DriftDetected.Should().BeFalse();
            }
        }
        finally
        {
            if (File.Exists(corruptPath))
            {
                File.Delete(corruptPath);
            }
            if (File.Exists(unsupportedPath))
            {
                File.Delete(unsupportedPath);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_WithoutPersistedSchemaIsUnevaluated()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"dg-no-schema-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(baselinePath, """
                { "Version": 2, "CreatedAt": "2026-01-01T00:00:00Z", "SchemaVersion": "1.0", "GroundTruthMode": "Snapshot", "DatabaseVersion": "1.0", "SchemaHash": "", "Violations": [] }
                """);
            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath });
            var report = await pipeline.CheckDriftAsync(new DatabaseSchemaDescriptor("current", Array.Empty<DatabaseTableDescriptor>(), "CHAR"));
            report.Status.Should().Be(DriftEvaluationStatus.Unevaluated);
            report.DriftDetected.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(baselinePath))
            {
                File.Delete(baselinePath);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_EmptyEvaluatedSchemaIsComplete()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"dg-empty-schema-{Guid.NewGuid():N}.json");
        try
        {
            var emptySchema = Array.Empty<SnapshotTable>();
            await new BaselineManager(baselinePath).CreateBaselineAsync(
                Array.Empty<ContractViolation>(), "1.0", "Snapshot",
                schemaHash: BaselineManager.ComputeSchemaHash(emptySchema, null, null, "v1"),
                schema: emptySchema);
            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath, EnableSmartDefaults = false });
            var report = await pipeline.CheckDriftAsync(new DatabaseSchemaDescriptor("current", Array.Empty<DatabaseTableDescriptor>(), "CHAR"));
            report.Status.Should().Be(DriftEvaluationStatus.Complete);
            report.BaselineHash.Should().Be(report.CurrentHash);
            report.DriftDetected.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(baselinePath))
            {
                File.Delete(baselinePath);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_RejectsUnknownCanonicalizationVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-canon-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                { "Version": 3, "CreatedAt": "2026-01-01T00:00:00Z", "SchemaVersion": "1.0", "GroundTruthMode": "Snapshot", "DatabaseVersion": "1.0", "SchemaHash": "", "SchemaHashKind": "canonical-schema-v1", "SchemaCanonicalizationVersion": "v2", "Violations": [], "Schema": [] }
                """);
            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = path, EnableSmartDefaults = false });
            var report = await pipeline.CheckDriftAsync(new DatabaseSchemaDescriptor("current", Array.Empty<DatabaseTableDescriptor>(), "CHAR"));
            report.Status.Should().Be(DriftEvaluationStatus.UnsupportedVersion);
            report.DriftDetected.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_RejectsConfiguredProviderMismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-provider-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                { "Version": 3, "CreatedAt": "2026-01-01T00:00:00Z", "SchemaVersion": "1.0", "GroundTruthMode": "Snapshot", "DatabaseVersion": "1.0", "SchemaHash": "", "SchemaHashKind": "canonical-schema-v1", "Provider": "sqlserver", "SchemaScope": "dbo", "SchemaCanonicalizationVersion": "v1", "Violations": [], "Schema": [] }
                """);
            var config = new DataGuardConfiguration { BaselineFilePath = path, EnableSmartDefaults = false, DefaultProvider = "postgresql" };
            using var pipeline = DataGuardApi.CreatePipeline(config);
            var report = await pipeline.CheckDriftAsync(new DatabaseSchemaDescriptor("current", Array.Empty<DatabaseTableDescriptor>(), "CHAR"));
            report.Status.Should().Be(DriftEvaluationStatus.Unevaluated);
            report.DriftDetected.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ValidationPipeline_CheckSchemaDrift_RequiresProviderContextForV3()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-provider-missing-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                { "Version": 3, "CreatedAt": "2026-01-01T00:00:00Z", "SchemaVersion": "1.0", "GroundTruthMode": "Snapshot", "DatabaseVersion": "1.0", "SchemaHash": "", "SchemaHashKind": "canonical-schema-v1", "Provider": "sqlserver", "SchemaScope": "dbo", "SchemaCanonicalizationVersion": "v1", "Violations": [], "Schema": [] }
                """);
            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = path, EnableSmartDefaults = false });
            var report = await pipeline.CheckDriftAsync(new DatabaseSchemaDescriptor("current", Array.Empty<DatabaseTableDescriptor>(), "CHAR"));
            report.Status.Should().Be(DriftEvaluationStatus.Unevaluated);
            report.DriftDetected.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

public class CredentialManagerTests
{
    [Fact]
    public void CredentialManager_ResolvesConfigCredentials()
    {
        var config = new DataGuardConfiguration
        {
            ConnectionString = "Server=myServer;Database=myDb;",
        };

        var manager = new CredentialManager(config);
        manager.Should().NotBeNull();
    }
}

public class RulePluginManagerTests
{
    [Fact]
    public void RulePluginManager_NonExistentDirectory_ReturnsEmpty()
    {
        var manager = new RulePluginManager(Path.Combine(Path.GetTempPath(), $"dg-plugins-{Guid.NewGuid():N}"));
        var rules = manager.GetAllRules(System.Collections.Immutable.ImmutableArray<IContractRule>.Empty);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void RulePluginManager_NullDirectory_ReturnsEmpty()
    {
        using var manager = new RulePluginManager(pluginDirectory: null);
        var rules = manager.GetAllRules(System.Collections.Immutable.ImmutableArray<IContractRule>.Empty);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void RulePluginManager_GetAllRules_MergesBuiltInRules()
    {
        using var manager = new RulePluginManager(pluginDirectory: null);
        var builtIn = System.Collections.Immutable.ImmutableArray.Create<IContractRule>(
            new DataGuard.Core.Rules.ParameterCountRule(),
            new DataGuard.Core.Rules.ParameterDirectionRule());

        var all = manager.GetAllRules(builtIn);

        all.Length.Should().Be(2);
        all.Should().Contain(r => r.RuleId == "DG101");
        all.Should().Contain(r => r.RuleId == "DG003");
    }

    [Fact]
    public void RulePluginManager_GetRuleById_FindsBuiltIn()
    {
        using var manager = new RulePluginManager(pluginDirectory: null);
        var builtIn = System.Collections.Immutable.ImmutableArray.Create<IContractRule>(
            new DataGuard.Core.Rules.ParameterCountRule());

        var rule = manager.GetRuleById("DG101", builtIn);

        rule.Should().NotBeNull();
        rule!.RuleId.Should().Be("DG101");
    }

    [Fact]
    public void RulePluginManager_GetRuleById_ReturnsNullForUnknown()
    {
        using var manager = new RulePluginManager(pluginDirectory: null);
        var rule = manager.GetRuleById("NONEXISTENT", System.Collections.Immutable.ImmutableArray<IContractRule>.Empty);
        rule.Should().BeNull();
    }

    [Fact]
    public void RulePluginManager_GetRuleMetadata_ReturnsEmpty()
    {
        using var manager = new RulePluginManager(pluginDirectory: null);
        var metadata = manager.GetRuleMetadata();
        metadata.Should().BeEmpty();
    }

    [Fact]
    public void RulePluginManager_Dispose_DoesNotThrow()
    {
        var manager = new RulePluginManager(pluginDirectory: null);
        var act = () => manager.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void RulePluginManager_EmptyDirectory_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dg-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var manager = new RulePluginManager(tempDir);
            var rules = manager.GetAllRules(System.Collections.Immutable.ImmutableArray<IContractRule>.Empty);
            rules.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }
    [Fact]
    public void RulePluginMetadata_ReadsCompleteMetadata()
    {
        var metadata = new RulePluginMetadata(new Dictionary<string, object>
        {
            ["RuleId"] = "DG900",
            ["Name"] = "Plugin rule",
            ["Description"] = "Plugin rule description",
            ["Category"] = "Custom",
            ["DefaultSeverity"] = "Error",
            ["MinDataGuardVersion"] = "2.0.0",
            ["Author"] = "DataGuard",
            ["Tags"] = new[] { "plugin", "test" },
        });

        metadata.RuleId.Should().Be("DG900");
        metadata.Name.Should().Be("Plugin rule");
        metadata.Description.Should().Be("Plugin rule description");
        metadata.Category.Should().Be("Custom");
        metadata.DefaultSeverity.Should().Be("Error");
        metadata.MinDataGuardVersion.Should().Be("2.0.0");
        metadata.Author.Should().Be("DataGuard");
        metadata.Tags.Should().Equal("plugin", "test");
    }
}

public class DataGuardApiSurfaceTests
{
    [Fact]
    public void DataGuardApi_Version_IsSemanticVersion()
    {
        DataGuardApi.Version.Should().Be("1.0.0");
        Version.Parse(DataGuardApi.Version).Should().NotBeNull();
    }

    [Fact]
    public void DataGuardApi_CreatePipeline_ReturnsInstance()
    {
        using var pipeline = DataGuardApi.CreatePipeline();
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void DataGuardApi_CreatePipeline_WithConfig_ReturnsInstance()
    {
        var config = new DataGuardConfiguration { EnableTelemetry = false };
        using var pipeline = DataGuardApi.CreatePipeline(config);
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void DataGuardApi_CreatePipeline_RespectsSmartDefaultOptOut()
    {
        var config = new DataGuardConfiguration { EnableSmartDefaults = false, DefaultSchema = null };

        using var pipeline = DataGuardApi.CreatePipeline(config);

        config.DefaultSchema.Should().BeNull();
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidationPipeline_WithRules_AcceptsCustomRules()
    {
        using var pipeline = DataGuardApi.CreatePipeline();
        pipeline.WithRules(new PipelineUnifiedTests.FixedRule("CUSTOM901", "custom finding"));

        var entity = new EntityDescriptor("e1", "C", "C", "dbo",
            new List<PropertyDescriptor>
            {
                new PropertyDescriptor("Id", "int", "Id", "int", false, null, false, false),
            });
        var result = await pipeline.ValidateAsync(new[] { entity });

        result.Should().NotBeNull();
        result.ContractsValidated.Should().Be(1);
        result.Violations.Should().Contain(violation => violation.RuleId == "CUSTOM901");
    }

    [Fact]
    public void ValidationPipeline_WithRules_DuplicateBuiltInRuleIdThrows()
    {
        using var pipeline = DataGuardApi.CreatePipeline();

        // A second DG101 used to be dropped silently by the graph; now the conflict is reported.
        var act = () => pipeline.WithRules(new ParameterCountRule());

        act.Should().Throw<InvalidOperationException>().WithMessage("*DG101*");
    }

    [Fact]
    public void ValidationPipeline_WithPlugins_NonExistentDir_DoesNotThrow()
    {
        using var pipeline = DataGuardApi.CreatePipeline();
        var act = () => pipeline.WithPlugins(Path.Combine(Path.GetTempPath(), $"dg-nope-{Guid.NewGuid():N}"));
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidationPipeline_WithPlugins_RepeatedCallsAreOwnedAndReleasedOnDispose()
    {
        var pipeline = DataGuardApi.CreatePipeline();
        pipeline.WithPlugins(Path.Combine(Path.GetTempPath(), $"dg-nope-{Guid.NewGuid():N}"));
        pipeline.WithPlugins(Path.Combine(Path.GetTempPath(), $"dg-nope-{Guid.NewGuid():N}"));

        var field = typeof(ValidationPipeline).GetField(
            "_pluginManagers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var managers = (System.Collections.ICollection)field.GetValue(pipeline)!;
        managers.Count.Should().Be(2);

        pipeline.Dispose();
        ((System.Collections.ICollection)field.GetValue(pipeline)!).Count.Should().Be(0);
    }

    [Fact]
    public void ValidationPipeline_Dispose_MultipleTimes_DoesNotThrow()
    {
        var pipeline = DataGuardApi.CreatePipeline();
        pipeline.Dispose();
        var act = () => pipeline.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidationResult_IsClean_WhenNoViolations()
    {
        var result = new ValidationResult(1, 0, 0, 0, 0,
            System.Collections.Immutable.ImmutableArray<ContractViolation>.Empty,
            TimeSpan.FromMilliseconds(10), "1.0");

        result.IsClean.Should().BeTrue();
        result.HasErrors.Should().BeFalse();
        result.HasWarnings.Should().BeFalse();
        result.HasViolations.Should().BeFalse();
        result.ViolationsPerContract.Should().Be(0);
    }

    [Fact]
    public void ValidationResult_HasErrors_WhenErrorsPresent()
    {
        var violations = System.Collections.Immutable.ImmutableArray.Create(
            new ContractViolation("DG001", "test", Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var result = new ValidationResult(1, 1, 1, 0, 0, violations, TimeSpan.Zero, "1.0");

        result.HasErrors.Should().BeTrue();
        result.IsClean.Should().BeFalse();
        result.ViolationsPerContract.Should().Be(1.0);
    }

    [Fact]
    public void DriftReport_DefaultValues()
    {
        var report = new DriftReport(HasBaseline: false, DriftDetected: false,
            NewViolations: System.Collections.Immutable.ImmutableArray<ContractViolation>.Empty);

        report.HasDrift.Should().BeFalse();
        report.NewViolationCount.Should().Be(0);
        report.HasBaseline.Should().BeFalse();
        report.Status.Should().Be(DriftEvaluationStatus.Missing);
    }

    [Fact]
    public void DriftReport_WithDrift()
    {
        var violations = System.Collections.Immutable.ImmutableArray.Create(
            new ContractViolation("DG001", "drift", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning));
        var report = new DriftReport(HasBaseline: true, DriftDetected: true, NewViolations: violations);

        report.HasDrift.Should().BeTrue();
        report.NewViolationCount.Should().Be(1);
    }

    [Fact]
    public void DataGuardFactory_CreateCredentialManager_ReturnsInstance()
    {
        var config = new DataGuardConfiguration();
        var manager = DataGuardFactory.CreateCredentialManager(config);
        manager.Should().NotBeNull();
    }

    [Fact]
    public void DataGuardFactory_CreateAuditLogger_AuditDisabled_ReturnsNullLogger()
    {
        var config = new DataGuardConfiguration { EnableAuditLogging = false };
        var logger = DataGuardFactory.CreateAuditLogger(config);
        logger.Should().NotBeNull();
        logger.Should().BeOfType<NullAuditLogger>();
    }

    [Fact]
    public void DataGuardFactory_CreateTelemetryCollector_Disabled_ReturnsNull()
    {
        var config = new TelemetryConfig(Enabled: false);
        var collector = DataGuardFactory.CreateTelemetryCollector(config);
        collector.Should().BeNull();
    }

    [Fact]
    public void DataGuardFactory_CreateTelemetryCollector_Enabled_ReturnsInstance()
    {
        var config = new TelemetryConfig(Enabled: true);
        var collector = DataGuardFactory.CreateTelemetryCollector(config);
        collector.Should().NotBeNull();
        collector!.Dispose();
    }

    [Fact]
    public void DataGuardFactory_CreateRuleGraph_ReturnsGraph()
    {
        var graph = DataGuardFactory.CreateRuleGraph();
        graph.Should().NotBeNull();
        var order = graph.GetExecutionOrder();
        order.Should().NotBeEmpty();
    }

    [Fact]
    public void DataGuardFactory_CreateAuditLogger_AuditEnabled_ReturnsFileLogger()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"dg-audit-{Guid.NewGuid():N}.log");
        var config = new DataGuardConfiguration { EnableAuditLogging = true, AuditLogPath = logPath };
        var logger = DataGuardFactory.CreateAuditLogger(config);
        logger.Should().NotBeNull();
        logger.Should().BeOfType<FileAuditLogger>();
    }
}

public class ValidationMetricsTests
{
    [Fact]
    public void ValidationMetrics_Constants_AreDefined()
    {
        ValidationMetrics.ValidationsTotal.Should().Be("validations.total");
        ValidationMetrics.ViolationsTotal.Should().Be("violations.total");
        ValidationMetrics.ViolationsErrors.Should().Be("violations.errors");
        ValidationMetrics.ViolationsWarnings.Should().Be("violations.warnings");
        ValidationMetrics.ValidationContracts.Should().Be("validation.contracts");
        ValidationMetrics.ValidationDuration.Should().Be("validation.duration");
        ValidationMetrics.RuleExecutions.Should().Be("rule.executions");
        ValidationMetrics.RuleDuration.Should().Be("rule.duration");
    }
}

public class ValidationPipelineExtensionTests
{
    [Fact]
    public void WithBaseline_DelegatesToWithBaselineFile()
    {
        using var pipeline = DataGuardApi.CreatePipeline();
        var result = pipeline.WithBaseline("custom-baseline.json");
        result.Should().BeSameAs(pipeline);
    }
}

/// <summary>Red-team B4/D3: one rule composition and one executor for the CLI and the public API.</summary>
public class PipelineUnifiedTests
{
    private static DataGuardConfiguration Config(bool concurrent = true, string? baselinePath = null) => new()
    {
        EnableSmartDefaults = false,
        EnableAuditLogging = false,
        EnableConcurrentValidation = concurrent,
        BaselineFilePath = baselinePath,
    };

    private static EntityDescriptor Entity(string id) => new(
        id, "C", "C", "dbo",
        new List<PropertyDescriptor> { new("Id", "int", "Id", "int", false, null, false, false) });

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("oracle")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void CliAndApi_ComposeIdenticalRuleIdSets(string provider)
    {
        // What `dataguard validate` executes: the catalog's ready rules composed by ProviderRuleCatalog.Compose.
        var cliPlan = DataGuard.Cli.ProviderRuleCatalog.Compose(DataGuard.Cli.ProviderRuleCatalog.GetReadyRules(provider))
            .GetExecutionOrder().Select(rule => rule.RuleId).ToList();

        using var pipeline = DataGuard.Cli.ProviderRuleCatalog.CreatePipeline(provider, Config());
        var apiPlan = pipeline.Rules.Select(rule => rule.RuleId).ToList();

        apiPlan.Should().Equal(cliPlan, "CLI and API must run the same rules in the same order");
        apiPlan.Should().BeEquivalentTo(DataGuard.Cli.ProviderRuleCatalog.Get(provider)
            .Where(registration => registration.Availability == DataGuard.Cli.RuleAvailability.Ready)
            .Select(registration => registration.Rule.RuleId));
        apiPlan.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreatePipeline_PassesProcedureSettingsFromConfiguration()
    {
        var config = Config() with { StrictProcedureContracts = true, DefaultSchema = "APP", DefaultPackage = "PKG" };

        using var pipeline = DataGuard.Cli.ProviderRuleCatalog.CreatePipeline("oracle", config);

        var dg101 = pipeline.Rules.OfType<ParameterCountRule>().Single();
        dg101.StrictProcedureContracts.Should().BeTrue();
        dg101.DefaultSchema.Should().Be("APP");
        dg101.DefaultPackage.Should().Be("PKG");
    }

    [Fact]
    public void WithProviderRules_ReplacesDefaultsAndKeepsCustomRules()
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config())
            .WithRules(new FixedRule("CUSTOM902"))
            .WithProviderRules(new IContractRule[] { new FixedRule("P1"), new ParameterCountRule() });

        pipeline.Rules.Select(rule => rule.RuleId).Should().BeEquivalentTo(new[] { "P1", "DG101", "CUSTOM902" });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pipeline_SurfacesAndDrainsUnevaluatedContracts_OnBothExecutionPaths(bool concurrent)
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config(concurrent))
            .WithProviderRules(new IContractRule[] { new UnevaluatingRule(), new FixedRule("A1") });

        var first = await pipeline.ValidateAsync(new[] { Entity("c1"), Entity("c2") });
        var second = await pipeline.ValidateAsync(new[] { Entity("c3") });

        first.UnevaluatedContracts.Select(entry => (entry.RuleId, entry.ContractId)).Should().Equal(("UNEV1", "c1"), ("UNEV1", "c2"));
        first.UnevaluatedContracts.Should().OnlyContain(entry => entry.Reason == "no ground truth for this contract");
        first.IsClean.Should().BeFalse("an unevaluated contract is not a pass");
        first.ExecutionStatus.Should().Be(ValidationExecutionStatus.Complete);
        second.UnevaluatedContracts.Should().ContainSingle().Which.ContractId.Should().Be("c3", "entries are drained after each run");
    }

    [Fact]
    public async Task GraphExecutor_SequentialAndConcurrent_ProduceIdenticalResults()
    {
        IContractRule[] Rules() => new IContractRule[] { new FixedRule("B1", "z"), new FixedRule("A1", "y"), new UnevaluatingRule() };
        var contracts = new ContractDescriptor[] { Entity("e2"), Entity("e1") };

        var concurrent = await GraphValidationExecutor.ValidateAsync(BuiltInRuleDependencies.Create(Rules()), contracts, concurrent: true, 4, 1000);
        var sequential = await GraphValidationExecutor.ValidateAsync(BuiltInRuleDependencies.Create(Rules()), contracts, concurrent: false, 4, 1000);

        sequential.Violations.Select(v => (v.RuleId, v.Message)).Should().Equal(concurrent.Violations.Select(v => (v.RuleId, v.Message)));
        sequential.UnevaluatedContracts.Select(e => (e.RuleId, e.ContractId)).Should().Equal(concurrent.UnevaluatedContracts.Select(e => (e.RuleId, e.ContractId)));
        sequential.UnevaluatedContracts.Should().HaveCount(2);
        sequential.IsIncomplete.Should().BeFalse();
        concurrent.IsIncomplete.Should().BeFalse();
    }

    [Fact]
    public async Task GraphExecutor_Sequential_RecordsFailedRuleAsIncomplete()
    {
        var result = await GraphValidationExecutor.ValidateAsync(
            BuiltInRuleDependencies.Create(new IContractRule[] { new ThrowingRule(), new FixedRule("A1") }),
            new ContractDescriptor[] { Entity("e1") },
            concurrent: false,
            1,
            1000);

        result.IsIncomplete.Should().BeTrue();
        result.RuleOutcomes.Should().ContainSingle(outcome => outcome.RuleId == "THROW1" && outcome.State == RuleExecutionState.Failed);
        result.Violations.Should().ContainSingle(violation => violation.RuleId == "A1");
    }

    [Fact]
    public async Task StreamAsync_ReportsUnevaluatedContractsBeforeTheStreamCompletes()
    {
        var unevaluated = new List<UnevaluatedContract>();
        var violations = new List<ContractViolation>();
        var contracts = new ContractDescriptor[] { Entity("e1"), Entity("e2") };

        await foreach (var violation in new ConcurrentValidationEngine(2).StreamAsync(
            contracts, new IContractRule[] { new FixedRule("A1"), new UnevaluatingRule() }, unevaluated.Add))
        {
            violations.Add(violation);
        }

        violations.Should().HaveCount(2);
        unevaluated.Select(entry => entry.ContractId).Should().Equal("e1", "e2");
    }

    [Fact]
    public async Task CreateBaselineAsync_WithSchemaAndProcedures_WritesV4_AndDriftDetectsChanges()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-v4-api-{Guid.NewGuid():N}.json");
        try
        {
            var config = Config(baselinePath: path) with { DefaultProvider = "oracle", DefaultSchema = "APP" };
            using var pipeline = DataGuardApi.CreatePipeline(config);
            var schema = Schema(new ColumnDescriptor("ID", "NUMBER", null, 22, 0, false, null, null));
            var procedure = Procedure("NUMBER");

            var baseline = await pipeline.CreateBaselineAsync(Array.Empty<ContractViolation>(), schema, new[] { procedure });

            baseline.Version.Should().Be(SnapshotFormat.WithStoredProceduresVersion);
            baseline.SchemaHashKind.Should().Be(SnapshotFormat.CanonicalSchemaV2HashKind);
            baseline.StoredProcedures.Should().ContainSingle();

            var same = await pipeline.CheckDriftAsync(schema, new[] { procedure });
            same.Status.Should().Be(DriftEvaluationStatus.Complete, same.Message);
            same.DriftDetected.Should().BeFalse();
            same.CurrentHash.Should().Be(baseline.SchemaHash);

            var tablesOnly = await pipeline.CheckDriftAsync(schema);
            tablesOnly.Status.Should().Be(DriftEvaluationStatus.Complete);
            tablesOnly.DriftDetected.Should().BeFalse();
            tablesOnly.Message.Should().Contain("not compared");

            var procedureChanged = await pipeline.CheckDriftAsync(schema, new[] { Procedure("VARCHAR2") });
            procedureChanged.DriftDetected.Should().BeTrue();

            var widened = Schema(
                new ColumnDescriptor("ID", "NUMBER", null, 22, 0, false, null, null),
                new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "C", 100));
            (await pipeline.CheckDriftAsync(widened, new[] { procedure })).DriftDetected.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateBaselineAsync_TablesOnly_WritesV4WithEmptyProcedures()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-v4-tables-{Guid.NewGuid():N}.json");
        try
        {
            using var pipeline = DataGuardApi.CreatePipeline(Config(baselinePath: path) with { DefaultProvider = "sqlserver" });
            var schema = Schema(new ColumnDescriptor("ID", "int", null, 10, 0, false, null, null));

            var baseline = await pipeline.CreateBaselineAsync(Array.Empty<ContractViolation>(), schema, storedProcedures: null);
            var report = await pipeline.CheckDriftAsync(schema);

            baseline.Version.Should().Be(SnapshotFormat.WithStoredProceduresVersion);
            baseline.StoredProcedures.Should().BeEmpty();
            report.Status.Should().Be(DriftEvaluationStatus.Complete, report.Message);
            report.DriftDetected.Should().BeFalse();
            report.Message.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CheckDriftAsync_TamperedV4Snapshot_IsCorrupt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-v4-tampered-{Guid.NewGuid():N}.json");
        try
        {
            using var pipeline = DataGuardApi.CreatePipeline(Config(baselinePath: path) with { DefaultProvider = "sqlserver" });
            var schema = Schema(new ColumnDescriptor("ID", "int", null, 10, 0, false, null, null));
            await pipeline.CreateBaselineAsync(Array.Empty<ContractViolation>(), schema, storedProcedures: null);
            var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            node["Schema"]![0]!["Columns"]![0]!["DataType"] = "bigint";
            await File.WriteAllTextAsync(path, node.ToJsonString());

            var report = await pipeline.CheckDriftAsync(schema);

            report.Status.Should().Be(DriftEvaluationStatus.Corrupt);
            report.DriftDetected.Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DatabaseSchemaDescriptor Schema(params ColumnDescriptor[] columns) =>
        new("current", new[] { new DatabaseTableDescriptor("CUSTOMERS", columns) }, "CHAR");

    private static StoredProcedureDescriptor Procedure(string parameterType) => new(
        "proc:GET_CUSTOMER",
        "GET_CUSTOMER",
        "APP",
        string.Empty,
        new[] { new ParameterDescriptor("P_ID", parameterType, ParameterDirection.Input, null, 22, 0, false, 1) },
        Array.Empty<ColumnDescriptor>(),
        false);

    /// <summary>Emits one violation per contract with a fixed message.</summary>
    internal sealed class FixedRule : IContractRule
    {
        private readonly string? _message;

        public FixedRule(string ruleId, string? message = null)
        {
            RuleId = ruleId;
            _message = message;
        }

        public string RuleId { get; }

        public string Name => RuleId;

        public Microsoft.CodeAnalysis.DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;

        public string Description => "test rule";

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
            ContractDescriptor contract,
            IReadOnlyList<ContractDescriptor> allContracts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractViolation>>(new[]
            {
                new ContractViolation(RuleId, $"{_message ?? RuleId} {contract.Id}", Severity),
            });
    }

    /// <summary>Uses the base-class <c>MarkUnevaluated</c> for every contract.</summary>
    private sealed class UnevaluatingRule : ContractRuleBase
    {
        public override string RuleId => "UNEV1";

        public override string Name => "Unevaluating";

        public override Microsoft.CodeAnalysis.DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Error;

        public override string Description => "Marks every contract unevaluated";

        protected override Task ValidateCoreAsync(
            ContractDescriptor contract,
            IReadOnlyList<ContractDescriptor> allContracts,
            List<ContractViolation> violations,
            CancellationToken cancellationToken)
        {
            MarkUnevaluated(contract, "no ground truth\nfor this contract");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRule : IContractRule
    {
        public string RuleId => "THROW1";

        public string Name => RuleId;

        public Microsoft.CodeAnalysis.DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Error;

        public string Description => "throws";

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
            ContractDescriptor contract,
            IReadOnlyList<ContractDescriptor> allContracts,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
