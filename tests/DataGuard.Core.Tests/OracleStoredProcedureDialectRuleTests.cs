using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// H5: stored-procedure call descriptors carry SQL text synthesized by the extractor (<c>EXEC PKG.PROC</c>), so the
/// Oracle dialect wrappers DG010/DG011/DG013 must not report it; the same text written by the user is still reported.
/// </summary>
public class OracleStoredProcedureDialectRuleTests
{
    private static RawSqlDescriptor Raw(string sql, bool isStoredProcedure, string? procedureName = null) =>
        new("raw:sp", sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>())
        {
            IsStoredProcedure = isStoredProcedure,
            ProcedureName = procedureName,
        };

    [Fact]
    public async Task SqlServerSyntaxLeakRule_SynthesizedOraclePackageCall_NotFlagged()
    {
        var descriptor = Raw("EXEC PKG.PROC", isStoredProcedure: true, procedureName: "PKG.PROC");

        var violations = await new SqlServerSyntaxLeakRule().ValidateAsync(descriptor, new ContractDescriptor[] { descriptor });

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task SqlServerSyntaxLeakRule_UserWrittenExecInOracleSql_StillFlagged()
    {
        var descriptor = Raw("EXEC PKG.PROC", isStoredProcedure: false);
        var rule = new SqlServerSyntaxLeakRule();

        var violations = await rule.ValidateAsync(descriptor, new ContractDescriptor[] { descriptor });

        violations.Should().ContainSingle().Which.RuleId.Should().Be(rule.RuleId);
    }

    [Fact]
    public async Task OracleDialectWrappers_SkipStoredProcedureDescriptors()
    {
        var sp = Raw("SELECT NVL(a, 0), ISNULL(b, 0), GETDATE() FROM t WHERE ROWNUM < 2", isStoredProcedure: true, procedureName: "P");
        var plain = sp with { IsStoredProcedure = false, ProcedureName = null };
        var rules = new ContractRuleBase[] { new OracleSyntaxInNonOracleContextRule(), new NonOracleFunctionInOracleContextRule() };

        foreach (var rule in rules)
        {
            (await rule.ValidateAsync(sp, new ContractDescriptor[] { sp })).Should().BeEmpty(rule.RuleId);
            (await rule.ValidateAsync(plain, new ContractDescriptor[] { plain })).Should().NotBeEmpty(rule.RuleId)
                .And.OnlyContain(v => v.RuleId == rule.RuleId);
        }
    }
}
