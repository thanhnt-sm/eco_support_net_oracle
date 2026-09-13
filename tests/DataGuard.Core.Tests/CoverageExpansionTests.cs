using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Contracts;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using DataGuard.Core.Validation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.CodeAnalysis;
using Xunit;
using CoreDirection = DataGuard.Core.Abstractions.ParameterDirection;

namespace DataGuard.Core.Tests;

public class SqlKeywordMatcherTests
{
    [Fact]
    public void ContainsAny_MatchIsCaseInsensitive()
    {
        SqlKeywordMatcher.ContainsAny("select * from dual", new[] { "SELECT" }).Should().BeTrue();
        SqlKeywordMatcher.ContainsAny("no sql here", new[] { "SELECT" }).Should().BeFalse();
        SqlKeywordMatcher.ContainsAny("x", Array.Empty<string>()).Should().BeFalse();
    }
}

public class ConcurrentValidationEngineBackpressureTests
{
    [Fact]
    public async Task ValidateAsync_EmptyInputs_ReturnsEmpty()
    {
        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 2);

        var result = await engine.ValidateAsync(
            Array.Empty<ContractDescriptor>(),
            Array.Empty<IContractRule>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_BackpressureCapsViolationCount()
    {
        var contracts = Enumerable.Range(0, 20)
            .Select(i => (ContractDescriptor)new RawSqlDescriptor($"raw:{i}", "EXEC p", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>()))
            .ToList();
        var rules = new IContractRule[] { new AlwaysViolateRule() };
        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 4, maxViolationQueueSize: 5);

        var result = await engine.ValidateDetailedAsync(contracts, rules);

        result.Violations.Should().HaveCount(5, "backpressure must cap the collected violations");
        result.IsIncomplete.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateDetailedAsync_Overflow_IsIncompleteAndLegacyFailsClosed()
    {
        var contracts = Enumerable.Range(0, 2)
            .Select(i => (ContractDescriptor)new RawSqlDescriptor($"raw:{i}", "EXEC p", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>()))
            .ToList();
        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 1, maxViolationQueueSize: 1);

        var detailed = await engine.ValidateDetailedAsync(contracts, new IContractRule[] { new AlwaysViolateRule() });

        detailed.Violations.Should().HaveCount(1);
        detailed.IsIncomplete.Should().BeTrue();
        detailed.DroppedViolationCount.Should().Be(1);
        var legacy = async () => await engine.ValidateAsync(contracts, new IContractRule[] { new AlwaysViolateRule() });
        await legacy.Should().ThrowAsync<ValidationIncompleteException>();
    }

    [Fact]
    public async Task ValidateDetailedAsync_Overflow_UsesStableJobOrder()
    {
        var contracts = Enumerable.Range(0, 5)
            .Select(i => (ContractDescriptor)new RawSqlDescriptor($"raw:{i}", "EXEC p", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>()))
            .ToList();
        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 5, maxViolationQueueSize: 2);

        for (var run = 0; run < 5; run++)
        {
            var result = await engine.ValidateDetailedAsync(contracts, new IContractRule[] { new OrderedViolationRule() });
            result.Violations.Select(v => v.Message).Should().Equal("raw:0", "raw:1");
        }
    }

    [Fact]
    public async Task ValidateDetailedAsync_ZeroCap_IsCompleteForNoViolationsAndCountsDrops()
    {
        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 1, maxViolationQueueSize: 0);
        var empty = await engine.ValidateDetailedAsync(Array.Empty<ContractDescriptor>(), new IContractRule[] { new AlwaysViolateRule() });
        empty.IsIncomplete.Should().BeFalse();
        empty.DroppedViolationCount.Should().Be(0);

        var contract = new RawSqlDescriptor("raw:0", "EXEC p", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
        var overflow = await engine.ValidateDetailedAsync(new ContractDescriptor[] { contract }, new IContractRule[] { new AlwaysViolateRule() });
        overflow.IsIncomplete.Should().BeTrue();
        overflow.DroppedViolationCount.Should().Be(1);
    }

    private sealed class AlwaysViolateRule : IContractRule
    {
        public string RuleId => "DGTEST";
        public string Name => "Always violate";
        public string Description => "Test rule";
        public DiagnosticSeverity Severity => DiagnosticSeverity.Error;

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
            ContractDescriptor contract,
            IReadOnlyList<ContractDescriptor> allContracts,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ContractViolation> list =
            [
                new ContractViolation("DGTEST", "always", DiagnosticSeverity.Error, null, null),
            ];
            return Task.FromResult(list);
        }
    }

    private sealed class OrderedViolationRule : IContractRule
    {
        public string RuleId => "DGORDER";
        public string Name => "Ordered";
        public string Description => "Ordered";
        public DiagnosticSeverity Severity => DiagnosticSeverity.Error;

        public async Task<IReadOnlyList<ContractViolation>> ValidateAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, CancellationToken cancellationToken = default)
        {
            await Task.Delay(contract.Id == "raw:0" ? 20 : 1, cancellationToken);
            return new[] { new ContractViolation(RuleId, contract.Id, Severity, null, null) };
        }
    }
}

public class ManualContractSourceTests
{
    [Fact]
    public async Task ExtractContractsAsync_ReadsExpectedColumnAndSpParameter()
    {
        var source = new ManualContractSource(typeof(ManualSample).Assembly.Location);

        var contracts = await source.ExtractContractsAsync();

        var entity = contracts.OfType<EntityDescriptor>().Should().ContainSingle(e => e.Name == nameof(ManualSample)).Subject;
        entity.Properties.Should().ContainSingle(p => p.ColumnName == "customer_id" && p.ClrTypeName == "int");

        var sp = contracts.OfType<StoredProcedureDescriptor>().Should().ContainSingle(s => s.Name == nameof(ManualSample.GetCustomer)).Subject;
        sp.Parameters.Should().ContainSingle(p => p.Name == "p_id" && p.ClrType == "int" && p.Direction == CoreDirection.Input);
        sp.Parameters.Should().ContainSingle(p => p.Name == "p_out" && p.Direction == CoreDirection.Output && p.ClrType == "string");
    }

    [Fact]
    public async Task ExtractContractsAsync_ReadsCompatibilityAttributesWithoutDatabaseAccess()
    {
        var source = new ManualContractSource(typeof(CompatibilityManualSample).Assembly.Location);

        var contracts = await source.ExtractContractsAsync();

        var entity = contracts.OfType<EntityDescriptor>()
            .Should().ContainSingle(value => value.Name == nameof(CompatibilityManualSample)).Subject;
        entity.TableName.Should().Be("CUSTOMERS");
        entity.Properties.Should().ContainSingle(value => value.Name == nameof(CompatibilityManualSample.Id) && value.ColumnName == nameof(CompatibilityManualSample.Id));

        var sp = contracts.OfType<StoredProcedureDescriptor>()
            .Should().ContainSingle(value => value.Name == nameof(CompatibilityManualSample.Find)).Subject;
        sp.Parameters.Should().ContainSingle(value => value.Name == "p_id" && value.DataType == "NUMBER" && value.Direction == CoreDirection.Input);
        sp.ResultColumns.Should().ContainSingle(value => value.Name == "CUSTOMER_NAME" && value.DataType == "string" && value.MaxLength == 100);
    }

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        var act = () => new ManualContractSource(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

public class ManualSample
{
    [ExpectedColumn("customer_id", "int", IsNullable = false)]
    public int Id { get; set; }

    [ExpectedSpParameter("p_id", "int", "Input", ClrType = "int")]
    [ExpectedSpParameter("p_out", "varchar2", "Output", MaxLength = 200, ClrType = "string")]
    public string GetCustomer() => "";
}

[global::DataGuard.Contracts.DataContract("CUSTOMERS", Schema = "dbo")]
public class CompatibilityManualSample
{
    public int Id { get; set; }

    [global::DataGuard.Contracts.ResultSet("CUSTOMER_NAME", "string", MaxLength = 100)]
    public string Find([global::DataGuard.Contracts.SqlParameter("p_id", "NUMBER")] int id) => string.Empty;
}

public class EfModelSourceLiveTests
{
    [Fact]
    public async Task ExtractContractsAsync_InMemoryContext_MapsEntityAndColumn()
    {
        var options = new DbContextOptionsBuilder<CoverageDbContext>()
            .UseInMemoryDatabase($"dg-ef-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var context = new CoverageDbContext(options);
        var source = new EfModelSource(context, new DataGuardConfiguration());

        var contracts = await source.ExtractContractsAsync();

        var entity = contracts.OfType<EntityDescriptor>().Should().ContainSingle(e => e.Name == nameof(CoverageCustomer)).Subject;
        entity.TableName.Should().Be("CoverageCustomer");
        entity.Properties.Should().Contain(p => p.Name == nameof(CoverageCustomer.FullName));
    }

    [Fact]
    public async Task ExtractContractsAsync_ExcludedEntity_IsSkipped()
    {
        var options = new DbContextOptionsBuilder<CoverageDbContext>()
            .UseInMemoryDatabase($"dg-ef-ex-{Guid.NewGuid():N}")
            .Options;
        await using var context = new CoverageDbContext(options);
        var source = new EfModelSource(context, new DataGuardConfiguration
        {
            ExcludedEntities = new[] { typeof(CoverageCustomer).FullName! },
        });

        var contracts = await source.ExtractContractsAsync();

        contracts.OfType<EntityDescriptor>().Should().NotContain(e => e.Name == nameof(CoverageCustomer));
    }
}

public class CoverageCustomer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
}

public class CoverageDbContext : DbContext
{
    public CoverageDbContext(DbContextOptions<CoverageDbContext> options)
        : base(options)
    {
    }

    public DbSet<CoverageCustomer> CoverageCustomers => Set<CoverageCustomer>();
}
