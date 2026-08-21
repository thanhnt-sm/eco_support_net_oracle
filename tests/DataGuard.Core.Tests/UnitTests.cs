using System.IO;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Security;
using DataGuard.Core.Validation;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class ConfigurationTests
{
    [Fact]
    public void DataGuardConfiguration_Defaults_AreCorrect()
    {
        var config = new DataGuardConfiguration();

        config.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        config.NamingConvention.Should().Be(NamingConvention.SnakeCaseToPascalCase);
        config.EnableBaseline.Should().BeTrue();
    }

    [Fact]
    public void OracleConfiguration_Defaults_AreCorrect()
    {
        var config = new OracleConfiguration();

        config.UseRefCursorDescribe.Should().BeTrue();
        config.UseAllArguments.Should().BeTrue();
        config.UseAllTabColumns.Should().BeTrue();
    }

    [Fact]
    public void SqlServerConfiguration_Defaults_AreCorrect()
    {
        var config = new SqlServerConfiguration();

        config.Schema.Should().Be("dbo");
        config.UseFirstResultSet.Should().BeTrue();
    }
}

public class NamingConventionRuleTests
{
    [Theory]
    [InlineData("customer_id", "CustomerId")]
    [InlineData("order_date", "OrderDate")]
    [InlineData("total_amount", "TotalAmount")]
    [InlineData("single", "Single")]
    public void ToPascalCase_ConvertsSnakeCaseCorrectly(string snakeCase, string expectedPascalCase)
    {
        NamingConventionRule.ToPascalCase(snakeCase).Should().Be(expectedPascalCase);
    }

    [Theory]
    [InlineData("CustomerId", "customer_id")]
    [InlineData("OrderDate", "order_date")]
    [InlineData("TotalAmount", "total_amount")]
    [InlineData("Single", "single")]
    public void ToSnakeCase_ConvertsPascalCaseCorrectly(string pascalCase, string expectedSnakeCase)
    {
        NamingConventionRule.ToSnakeCase(pascalCase).Should().Be(expectedSnakeCase);
    }
}

public class ParameterTypeMatchRuleTests
{
    [Theory]
    [InlineData("int", "int", false, true)]
    [InlineData("long", "bigint", false, true)]
    [InlineData("string", "nvarchar(50)", false, true)]
    [InlineData("DateTime", "datetime2", false, true)]
    [InlineData("Guid", "uniqueidentifier", false, true)]
    [InlineData("int", "varchar(50)", false, false)]
    [InlineData("string", "int", false, false)]
    [InlineData("int", "POINT", false, false)]
    [InlineData("string", "CHART", false, false)]
    [InlineData("bool", "NUMBER(1)", true, true)]
    public void IsTypeCompatible_SqlServer_ReturnsExpected(string clrType, string dbType, bool isOracle, bool expected)
    {
        var result = ParameterTypeMatchRule.IsTypeCompatible(clrType, dbType, isOracle);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("int", "NUMBER(10)", true, true)]
    [InlineData("string", "NVARCHAR2(50)", true, true)]
    [InlineData("DateTime", "DATE", true, true)]
    [InlineData("Guid", "RAW(16)", true, true)]
    [InlineData("string", "NUMBER", true, false)]
    public void IsTypeCompatible_Oracle_ReturnsExpected(string clrType, string dbType, bool isOracle, bool expected)
    {
        var result = ParameterTypeMatchRule.IsTypeCompatible(clrType, dbType, isOracle);
        result.Should().Be(expected);
    }
}

public class BaselineManagerTests
{
    [Fact]
    public async Task CreateBaseline_SavesCorrectFormat()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var manager = new BaselineManager(tempFile);
            var violations = new[]
            {
                new ContractViolation("DG001", "Test violation", DiagnosticSeverity.Error),
                new ContractViolation("DG002", "Another violation", DiagnosticSeverity.Warning),
            };

            var baseline = await manager.CreateBaselineAsync(violations, "1.0", "Snapshot");

            baseline.Version.Should().Be(2);
            baseline.Violations.Should().HaveCount(2);
            baseline.SchemaVersion.Should().Be("1.0");
            baseline.GroundTruthMode.Should().Be("Snapshot");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task LoadBaseline_ReadsCorrectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var manager = new BaselineManager(tempFile);
            var violations = new[]
            {
                new ContractViolation("DG001", "Test violation", DiagnosticSeverity.Error),
            };

            await manager.CreateBaselineAsync(violations, "1.0", "Snapshot");
            var loaded = await manager.LoadAsync();

            loaded.Should().NotBeNull();
            loaded!.Violations.Should().HaveCount(1);
            loaded.Violations[0].RuleId.Should().Be("DG001");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task FilterNewViolations_ReturnsOnlyNewViolations()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var manager = new BaselineManager(tempFile);
            var baselineViolations = new[]
            {
                new ContractViolation("DG001", "Baseline violation", DiagnosticSeverity.Error),
            };

            var baseline = await manager.CreateBaselineAsync(baselineViolations, "1.0", "Snapshot");

            var newViolations = new[]
            {
                new ContractViolation("DG001", "Baseline violation", DiagnosticSeverity.Error), // same as baseline
                new ContractViolation("DG002", "New violation", DiagnosticSeverity.Error), // new
            };

            var filtered = manager.FilterNewViolations(newViolations, baseline);

            filtered.Should().HaveCount(1);
            filtered.First().RuleId.Should().Be("DG002");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

public class SchemaHashTests
{
    private static SnapshotTable Table(string name, params SnapshotColumn[] columns) => new (name, columns);

    private static SnapshotColumn Col(string name, string type = "NUMBER", bool nullable = false) =>
        new (name, type, 100, null, 22, 0, nullable, null);

    [Fact]
    public void SchemaHash_ChangesWhenColumnAdded()
    {
        var before = new[] { Table("CUSTOMERS", Col("ID"), Col("NAME")) };
        var after = new[] { Table("CUSTOMERS", Col("ID"), Col("NAME"), Col("EMAIL")) };

        BaselineManager.ComputeSchemaHash(before).Should().NotBe(BaselineManager.ComputeSchemaHash(after));
    }

    [Fact]
    public void SchemaHash_StableAcrossViolationOrdering()
    {
        // Same logical schema expressed in a different order must hash identically.
        var ordered = new[]
        {
            Table("CUSTOMERS", Col("ID"), Col("NAME")),
            Table("ORDERS", Col("ID")),
        };
        var reordered = new[]
        {
            Table("ORDERS", Col("ID")),
            Table("CUSTOMERS", Col("NAME"), Col("ID")),
        };

        BaselineManager.ComputeSchemaHash(ordered).Should().Be(BaselineManager.ComputeSchemaHash(reordered));
    }

    [Fact]
    public void SchemaHash_FullHexLength()
    {
        var hash = BaselineManager.ComputeSchemaHash(new[] { Table("T", Col("ID")) });
        hash.Should().HaveLength(64).And.MatchRegex("^[0-9A-F]{64}$");
    }
}

public class AuditLoggerTests
{
    [Fact]
    public async Task AuditLog_HashChain_VerifiesIntegrity()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.log");
        try
        {
            var logger = new FileAuditLogger(tempFile);
            await logger.LogCredentialAccessAsync("test", "provider", "hash1");
            await logger.LogCredentialAccessAsync("test", "provider", "hash2");

            (await logger.VerifyIntegrityAsync()).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task AuditLog_TamperedEntry_FailsVerification()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.log");
        try
        {
            var logger = new FileAuditLogger(tempFile);
            await logger.LogCredentialAccessAsync("test", "provider", "hash1");

            await File.AppendAllTextAsync(
                tempFile,
                "{\"Timestamp\":\"2020-01-01T00:00:00+00:00\",\"EventType\":\"Forged\",\"Hash\":\"deadbeef\",\"PreviousHash\":null}\n");

            (await logger.VerifyIntegrityAsync()).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task AuditLog_TailTruncation_FailsVerification()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.log");
        try
        {
            var logger = new FileAuditLogger(tempFile);
            for (var i = 0; i < 10; i++)
            {
                await logger.LogCredentialAccessAsync("test", "provider", $"hash{i}");
            }

            // Tail truncation attack: drop the last entry so the chain no longer
            // reaches the checkpoint written after each append.
            var lines = File.ReadAllLines(tempFile).ToList();
            lines.RemoveAt(lines.Count - 1);
            File.WriteAllLines(tempFile, lines);

            (await logger.VerifyIntegrityAsync()).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}

public class ConcurrentValidationEngineTests
{
    [Fact]
    public async Task ValidateAsync_MatchesSequentialResults()
    {
        var entity = new EntityDescriptor(
            "e1", "Customer", "Customer", "CUSTOMERS",
            new List<PropertyDescriptor>
            {
                new PropertyDescriptor("FullName", "string", "FULL_NAME", "VARCHAR2(100)", false, 200, false, false),
            });
        var contracts = new List<ContractDescriptor> { entity };
        var rules = new List<IContractRule> { new NamingConventionRule() };

        var engine = new ConcurrentValidationEngine();
        var concurrent = await engine.ValidateAsync(contracts, rules);

        var sequential = new List<ContractViolation>();
        foreach (var rule in rules)
        {
            sequential.AddRange(await rule.ValidateAsync(entity, contracts));
        }

        var concurrentSet = concurrent.Select(v => $"{v.RuleId}|{v.Message}").OrderBy(s => s, StringComparer.Ordinal).ToList();
        var sequentialSet = sequential.Select(v => $"{v.RuleId}|{v.Message}").OrderBy(s => s, StringComparer.Ordinal).ToList();
        concurrentSet.Should().Equal(sequentialSet);
    }
}