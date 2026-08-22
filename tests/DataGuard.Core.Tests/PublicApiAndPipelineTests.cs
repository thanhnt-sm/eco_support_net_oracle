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
