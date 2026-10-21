using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class NamingConventionConfigurationTests
{
    [Theory]
    [InlineData(NamingConvention.ExactMatch, "customer_id", true)]
    [InlineData(NamingConvention.ExactMatch, "CustomerId", false)]
    [InlineData(NamingConvention.ExactMatch, "CUSTOMERID", false)]
    [InlineData(NamingConvention.SnakeCaseToPascalCase, "customer_id", false)]
    [InlineData(NamingConvention.PascalCaseToSnakeCase, "customer_id", false)]
    public async Task Rule_UsesConfiguredMatchingWithoutChangingConversionModes(NamingConvention convention, string column, bool expectedFinding)
    {
        var entity = Entity(column);
        var findings = await new NamingConventionRule(convention).ValidateAsync(entity, new[] { entity });
        findings.Any(v => v.RuleId == "DG006").Should().Be(expectedFinding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipeline_AppliesExactMatchConfiguration(bool providerRules)
    {
        var config = new DataGuardConfiguration(NamingConvention: NamingConvention.ExactMatch, EnableBaseline: false, EnableAuditLogging: false);
        using var pipeline = providerRules
            ? ProviderRuleCatalog.CreatePipeline("sqlserver", config)
            : DataGuard.DataGuardApi.CreatePipeline(config);
        var result = await pipeline.ValidateAsync(new[] { Entity("customer_id") });
        result.Violations.Should().Contain(v => v.RuleId == "DG006");
    }

    [Theory]
    [InlineData("ExactMatch", true)]
    [InlineData("SnakeCaseToPascalCase", false)]
    public void Cli_UsesNamingConfigurationInSarif(string convention, bool expectedFinding)
    {
        var dir = Directory.CreateTempSubdirectory("dg-naming-config-");
        try
        {
            var snapshot = Path.Combine(dir.FullName, "AppSnapshot.cs");
            var config = Path.Combine(dir.FullName, "config.yml");
            var output = Path.Combine(dir.FullName, "result.sarif");
            File.WriteAllText(snapshot, """
                class Snapshot { void Build(ModelBuilder modelBuilder) {
                    modelBuilder.Entity<Customer>(entity => {
                        entity.ToTable("Customers");
                        entity.Property(item => item.CustomerId).HasColumnName("customer_id");
                    });
                }}
                """);
            File.WriteAllText(config, $"NamingConvention: {convention}\nEnableBaseline: false\nEnableAuditLogging: false\n");
            var run = CliProcessTestRunner.Run(dir.FullName, null, null,
                "validate", "--config", config, "--ef-snapshot", snapshot, "--format", "sarif", "--output", output);
            File.Exists(output).Should().BeTrue("CLI must produce SARIF: {0}", run.Stderr);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(output));
            var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
            results.EnumerateArray().Any(r => r.GetProperty("ruleId").GetString() == "DG006").Should().Be(expectedFinding);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static EntityDescriptor Entity(string column) => new("entity:Customer", "Customer", "Customer", "Customers",
        new[] { new PropertyDescriptor("CustomerId", "string", column, "nvarchar", true, null, false, false, null) });
}
