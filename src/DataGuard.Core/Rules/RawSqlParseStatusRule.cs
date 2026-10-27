using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using DataGuard.Core.Rules.TypeCompatibility;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule DG019: raw SQL must parse before semantic validation can be complete. Reports descriptors an acquisition source
/// already marked <see cref="RawSqlParseStatus.Invalid"/>, and raw SQL (not stored-procedure calls, which the DG101/DG002/DG003
/// call matcher handles) that the injected
/// dialect <see cref="ISqlStatementParser"/> rejects. Without a parser (the default, and every provider whose adapter
/// has no grammar) acquisition errors are reported and other raw SQL is marked unevaluated. Raw SQL whose connection
/// hint names another provider is unevaluated rather than parsed with this provider's grammar.
/// </summary>
public sealed class RawSqlParseStatusRule : ContractRuleBase
{
    private readonly ISqlStatementParser? _parser;

    /// <summary>Initializes a new instance of the <see cref="RawSqlParseStatusRule"/> class without a dialect parser.</summary>
    public RawSqlParseStatusRule()
        : this(null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RawSqlParseStatusRule"/> class.</summary>
    /// <param name="parser">Dialect parser for the configured provider; null reports only acquisition parse status.</param>
    public RawSqlParseStatusRule(ISqlStatementParser? parser)
    {
        _parser = parser is NoOpSqlStatementParser ? null : parser;
    }

    public override string RuleId => "DG019";
    public override string Name => "Raw SQL Parse Error";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    public override string Description => "Raw SQL must parse successfully before validation";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor rawSql)
        {
            return Task.CompletedTask;
        }

        string? error = null;
        var location = contract.Location;
        var context = "acquisition";
        if (rawSql.ParseStatus == RawSqlParseStatus.Invalid)
        {
            error = rawSql.ParseError ?? "unknown parse error";
        }
        else if (ShouldParse(rawSql))
        {
            var result = _parser!.Parse(rawSql.SqlText);
            if (!result.IsValid)
            {
                error = result.Error ?? "unknown parse error";
                context = $"{_parser.Provider} dialect; SQL-relative coordinates";
                if (result.ErrorOffset is { } offset)
                {
                    location = rawSql.SourceMap.Select(segment => segment.Resolve(offset)).FirstOrDefault(mapped => mapped is not null)
                        ?? location;
                }
            }
        }

        if (error is null && !ShouldParse(rawSql) && !rawSql.IsStoredProcedure
            && rawSql.ProcedureName is null && !string.IsNullOrWhiteSpace(rawSql.SqlText))
        {
            MarkUnevaluated(rawSql, _parser is null
                ? "No SQL grammar parser is available for the configured provider; raw SQL syntax was not validated."
                : "The connection provider hint differs from the configured SQL grammar; raw SQL syntax was not validated.");
        }

        if (error is not null)
        {
            violations.Add(CreateViolation(RuleId, $"Raw SQL could not be parsed ({context}): {error}", Severity, location));
        }

        return Task.CompletedTask;
    }

    private bool ShouldParse(RawSqlDescriptor rawSql) =>
        _parser is not null
        && !rawSql.IsStoredProcedure
        && rawSql.ProcedureName is null
        && !string.IsNullOrWhiteSpace(rawSql.SqlText)
        && (string.IsNullOrWhiteSpace(rawSql.ConnectionProviderHint)
            || string.Equals(
                TypeCompatibilityRegistry.NormalizeProvider(rawSql.ConnectionProviderHint),
                TypeCompatibilityRegistry.NormalizeProvider(_parser.Provider),
                StringComparison.Ordinal));
}
