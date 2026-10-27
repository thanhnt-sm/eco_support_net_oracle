using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.StoredProcedures;
using DataGuard.Core.Validation;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class ProcedureAmbiguityEvaluationTests
{
    [Theory]
    [InlineData("sqlserver", true, false)]
    [InlineData("sqlserver", false, false)]
    [InlineData("sqlserver", true, true)]
    [InlineData("sqlserver", false, true)]
    [InlineData("oracle", true, false)]
    [InlineData("oracle", false, false)]
    [InlineData("oracle", true, true)]
    [InlineData("oracle", false, true)]
    public async Task Ambiguity_IsUnevaluatedWithoutFabricatedViolation(string provider, bool argumentsKnown, bool strict)
    {
        var call = AmbiguousCall("ambiguous", provider, argumentsKnown);
        var all = Catalog(provider).Prepend<ContractDescriptor>(call).ToArray();
        var resolution = StoredProcedureCallResolver.Resolve(call, all, new StoredProcedureMatchOptions(provider));
        resolution.Status.Should().Be(StoredProcedureResolutionStatus.Ambiguous);
        var rule = new ParameterCountRule(provider, strictProcedureContracts: strict);

        (await rule.ValidateAsync(call, all)).Should().BeEmpty();
        var entry = rule.DrainUnevaluatedContracts().Should().ContainSingle().Subject;
        entry.RuleId.Should().Be("DG101");
        entry.ContractId.Should().Be(call.Id);
        entry.Reason.Should().Be(resolution.Note);
        entry.Reason.Should().NotBeNullOrWhiteSpace();
        rule.DrainUnevaluatedContracts().Should().BeEmpty();

        foreach (var sibling in new ContractRuleBase[] { new ParameterTypeMatchRule(provider), new ParameterDirectionRule(provider) })
        {
            (await sibling.ValidateAsync(call, all)).Should().BeEmpty();
            sibling.DrainUnevaluatedContracts().Should().BeEmpty("DG002 and DG003 behavior is unchanged");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DefaultSchemaAndOutOfScopeControls_DoNotBecomeUnevaluated(bool argumentsKnown)
    {
        var call = AmbiguousCall("sales", "sqlserver", argumentsKnown);
        var all = Catalog("sqlserver").Prepend<ContractDescriptor>(call).ToArray();
        var sales = new ParameterCountRule("sqlserver", defaultSchema: "sales");
        (await sales.ValidateAsync(call, all)).Should().BeEmpty();
        sales.DrainUnevaluatedContracts().Should().BeEmpty();
        StoredProcedureCallResolver.Resolve(call, all, new StoredProcedureMatchOptions("sqlserver", DefaultSchema: "sales"))
            .Procedure!.Schema.Should().Be("sales");

        foreach (var text in new[] { "EXEC audit.usp_Report 2026", "EXEC OtherDb.dbo.usp_Report 2026", "EXEC sp_executesql N'SELECT 1'", "SELECT 1" })
        {
            var outside = new RawSqlDescriptor("outside", text, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
            var rule = new ParameterCountRule("sqlserver");
            (await rule.ValidateAsync(outside, all)).Should().BeEmpty();
            rule.DrainUnevaluatedContracts().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicPipeline_CompleteButNotClean_AndBaselineCannotHideStatus(bool concurrent, bool baseline)
    {
        var directory = Directory.CreateTempSubdirectory("dg101-pipeline-");
        try
        {
            var path = Path.Combine(directory.FullName, "baseline.json");
            if (baseline)
            {
                await new BaselineManager(path).CreateBaselineAsync(
                    new[] { new ContractViolation("DG101", "ambiguous procedure", DiagnosticSeverity.Warning) }, "1.0", "Snapshot");
            }

            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration
            {
                EnableConcurrentValidation = concurrent,
                EnableSmartDefaults = false,
                EnableTelemetry = false,
                EnableAuditLogging = false,
                EnableBaseline = baseline,
                BaselineFilePath = path,
            }).WithProviderRules(new IContractRule[] { new ParameterCountRule("sqlserver"), new ParameterTypeMatchRule("sqlserver"), new ParameterDirectionRule("sqlserver") });
            var calls = new ContractDescriptor[]
            {
                AmbiguousCall("a", "sqlserver", true),
                AmbiguousCall("b", "sqlserver", false),
                ResolvedCall("resolved"),
            }.Concat(Catalog("sqlserver")).ToArray();
            var result = await pipeline.ValidateAsync(calls);

            result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Complete);
            result.TotalViolations.Should().Be(0);
            result.IsClean.Should().BeFalse();
            result.UnevaluatedContracts.Select(e => (e.RuleId, e.ContractId)).Should().BeEquivalentTo(new[] { ("DG101", "a"), ("DG101", "b") });
            var rerun = await pipeline.ValidateAsync(new ContractDescriptor[] { ResolvedCall("next") }.Concat(Catalog("sqlserver")).ToArray());
            rerun.IsClean.Should().BeTrue();
            rerun.UnevaluatedContracts.Should().BeEmpty();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OverlappingInvocations_SharingRule_KeepTheirOwnCallStatuses(bool concurrent, bool finishFirstRunFirst)
    {
        var rule = new GatedCountRule();
        var graph = BuiltInRuleDependencies.Create(new IContractRule[] { rule, new ParameterTypeMatchRule("sqlserver"), new ParameterDirectionRule("sqlserver") });
        ContractDescriptor[] Contracts(string prefix) => new ContractDescriptor[]
        {
            AmbiguousCall(prefix + ":a", "sqlserver", true),
            AmbiguousCall(prefix + ":b", "sqlserver", false),
            ResolvedCall(prefix + ":resolved"),
        }.Concat(Catalog("sqlserver")).ToArray();
        var first = GraphValidationExecutor.ValidateAsync(graph, Contracts("first"), concurrent, 2, 1000);
        Task<ValidationExecutionResult>? second = null;
        try
        {
            await rule.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = GraphValidationExecutor.ValidateAsync(graph, Contracts("second"), concurrent, 2, 1000);
            await rule.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (finishFirstRunFirst)
            {
                rule.ReleaseFirst.TrySetResult();
                AssertStatuses(await first, "first");
                rule.ReleaseSecond.TrySetResult();
                AssertStatuses(await second, "second");
            }
            else
            {
                rule.ReleaseSecond.TrySetResult();
                AssertStatuses(await second, "second");
                rule.ReleaseFirst.TrySetResult();
                AssertStatuses(await first, "first");
            }

            rule.DrainUnevaluatedContracts().Should().BeEmpty();
        }
        finally
        {
            rule.ReleaseFirst.TrySetResult();
            rule.ReleaseSecond.TrySetResult();
            await first;
            if (second is not null)
            {
                await second;
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cli_AmbiguityExitsThree_AllowUnevaluatedExitsZero(bool strict, bool baseline)
    {
        var directory = Directory.CreateTempSubdirectory("dg101-cli-");
        try
        {
            var dir = directory.FullName;
            var procedures = SnapshotConversion.FromProcedures(Catalog("sqlserver"));
            await new BaselineManager(Path.Combine(dir, ".dataguard-snapshot.json")).CreateSnapshotAsync(
                Array.Empty<ContractViolation>(), "1.0", "16.0",
                new[] { new SnapshotTable("Orders", new[] { new SnapshotColumn("Id", "int", null, null, 10, 0, false, null) }, "dbo") },
                procedures, "sqlserver", null, "CHAR", null);
            var config = Path.Combine(dir, "dataguard.yml");
            await File.WriteAllTextAsync(config, $"EnableSmartDefaults: false\nEnableBaseline: {baseline.ToString().ToLowerInvariant()}\nStrictProcedureContracts: {strict.ToString().ToLowerInvariant()}\nBaselineFilePath: .dataguard-baseline.json\nEnableTelemetry: false\nEnableAuditLogging: false\n");
            if (baseline)
            {
                await new BaselineManager(Path.Combine(dir, ".dataguard-baseline.json")).CreateBaselineAsync(
                    new[] { new ContractViolation("DG101", "ambiguous procedure", DiagnosticSeverity.Warning) }, "1.0", "Snapshot");
            }

            var source = Path.Combine(dir, "Repository.cs");
            await File.WriteAllTextAsync(source, """
                using System.Data;
                using Dapper;
                public class Repository {
                    public void Load(IDbConnection connection) {
                        connection.Execute("usp_Report", new { Year = 2026 }, commandType: CommandType.StoredProcedure);
                    }
                }
                """);
            var gated = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", source, "--config", config);
            gated.ExitCode.Should().Be(3, gated.Stdout + gated.Stderr);
            gated.Stderr.Should().Contain("UNEVALUATED").And.Contain("DG101").And.Contain("ambiguous");
            gated.Stdout.Should().NotContain("[WARNING] DG101:").And.NotContain("[ERROR] DG101:");
            var allowed = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", source, "--config", config, "--allow-unevaluated");
            allowed.ExitCode.Should().Be(0, allowed.Stdout + allowed.Stderr);
            allowed.Stderr.Should().Contain("UNEVALUATED").And.Contain("DG101");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void AssertStatuses(ValidationExecutionResult result, string prefix)
    {
        result.IsIncomplete.Should().BeFalse();
        result.Violations.Should().BeEmpty();
        result.UnevaluatedContracts.Select(e => (e.RuleId, e.ContractId)).Should().BeEquivalentTo(new[] { ("DG101", prefix + ":a"), ("DG101", prefix + ":b") });
    }

    private static RawSqlDescriptor AmbiguousCall(string id, string provider, bool argumentsKnown) =>
        new(id, provider == "oracle" ? "BEGIN HR.PKG.P(:value); END;" : "EXEC usp_Report @Year = 2026", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>())
        {
            ArgumentsKnown = argumentsKnown,
        };

    private static RawSqlDescriptor ResolvedCall(string id) =>
        new(id, "EXEC sales.usp_Report @Year = 2026", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());

    private static StoredProcedureDescriptor[] Catalog(string provider)
    {
        var parameter = new ParameterDescriptor(provider == "oracle" ? "P_VALUE" : "@Year", provider == "oracle" ? "NUMBER" : "int", ParameterDirection.Input, null, null, null, false, 1);
        return provider == "oracle"
            ? new[]
            {
                new StoredProcedureDescriptor("HR.PKG.P#1", "P", "HR", "PKG", new[] { parameter }, Array.Empty<ColumnDescriptor>(), false),
                new StoredProcedureDescriptor("HR.PKG.P#2", "P", "HR", "PKG", new[] { parameter }, Array.Empty<ColumnDescriptor>(), false),
            }
            : new[]
            {
                new StoredProcedureDescriptor("dbo.usp_Report", "usp_Report", "dbo", string.Empty, new[] { parameter }, Array.Empty<ColumnDescriptor>(), false),
                new StoredProcedureDescriptor("sales.usp_Report", "usp_Report", "sales", string.Empty, new[] { parameter }, Array.Empty<ColumnDescriptor>(), false),
            };
    }

    private sealed class GatedCountRule : ParameterCountRule
    {
        internal TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal GatedCountRule()
            : base("sqlserver")
        {
        }

        protected override async Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            await base.ValidateCoreAsync(contract, allContracts, violations, cancellationToken);
            if (contract.Id == "first:a")
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
            else if (contract.Id == "second:a")
            {
                SecondStarted.TrySetResult();
                await ReleaseSecond.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
