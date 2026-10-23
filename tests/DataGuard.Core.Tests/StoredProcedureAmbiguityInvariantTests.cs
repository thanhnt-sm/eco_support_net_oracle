using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.StoredProcedures;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class StoredProcedureAmbiguityInvariantTests
{
    [Theory]
    [InlineData("sqlserver", true, "qualify the call or set DefaultSchema/DefaultPackage")]
    [InlineData("sqlserver", false, "qualify the call or set DefaultSchema/DefaultPackage")]
    [InlineData("oracle", true, "accept the call equally well")]
    [InlineData("oracle", false, "call-site arguments not observed; 2 overloads")]
    public async Task EveryAmbiguityProducer_SuppliesTheReasonDg101Preserves(string provider, bool argumentsKnown, string expectedReason)
    {
        var oracle = provider == "oracle";
        var parameter = new ParameterDescriptor(oracle ? "P_VALUE" : "@Year", oracle ? "NUMBER" : "int", ParameterDirection.Input, null, null, null, false, 1);
        var first = new StoredProcedureDescriptor(oracle ? "HR.PKG.P#1" : "dbo.usp_Report", oracle ? "P" : "usp_Report", oracle ? "HR" : "dbo", oracle ? "PKG" : string.Empty, new[] { parameter }, Array.Empty<ColumnDescriptor>(), false);
        var second = first with { Id = oracle ? "HR.PKG.P#2" : "sales.usp_Report", Schema = oracle ? "HR" : "sales" };
        var call = new RawSqlDescriptor("call:ambiguous", oracle ? "BEGIN HR.PKG.P(:value); END;" : "EXEC usp_Report @Year = 2026", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>())
        {
            ArgumentsKnown = argumentsKnown,
        };
        var contracts = new ContractDescriptor[] { call, first, second };

        var resolution = StoredProcedureCallResolver.Resolve(call, contracts, new StoredProcedureMatchOptions(provider));

        resolution.Status.Should().Be(StoredProcedureResolutionStatus.Ambiguous);
        resolution.Procedure.Should().BeNull();
        resolution.Candidates.Should().BeEquivalentTo(new[] { first, second });
        resolution.Note.Should().NotBeNullOrWhiteSpace().And.Contain(expectedReason);
        foreach (var strict in new[] { false, true })
        {
            var rule = new ParameterCountRule(provider, strictProcedureContracts: strict);
            (await rule.ValidateAsync(call, contracts)).Should().BeEmpty();
            var unevaluated = rule.DrainUnevaluatedContracts().Should().ContainSingle().Subject;
            unevaluated.Reason.Should().Be(resolution.Note);
        }
    }
}
