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
                .WithRules(new NamingConventionRule());

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
    public async Task ValidationPipeline_WithRules_AcceptsCustomRules()
    {
        using var pipeline = DataGuardApi.CreatePipeline();
        pipeline.WithRules(new ParameterCountRule());

        var entity = new EntityDescriptor("e1", "C", "C", "dbo",
            new List<PropertyDescriptor>
            {
                new PropertyDescriptor("Id", "int", "Id", "int", false, null, false, false),
            });
        var result = await pipeline.ValidateAsync(new[] { entity });

        result.Should().NotBeNull();
        result.ContractsValidated.Should().Be(1);
    }

    [Fact]
    public void ValidationPipeline_WithPlugins_NonExistentDir_DoesNotThrow()
    {
        using var pipeline = DataGuardApi.CreatePipeline();
        var act = () => pipeline.WithPlugins(Path.Combine(Path.GetTempPath(), $"dg-nope-{Guid.NewGuid():N}"));
        act.Should().NotThrow();
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
}
