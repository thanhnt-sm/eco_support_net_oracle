using System;
using System.Collections.Generic;
using System.Linq;
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

        var result = await engine.ValidateAsync(contracts, rules);

        result.Should().HaveCount(5, "backpressure must cap the collected violations");
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
