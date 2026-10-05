using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// DG015: raw SQL references (FROM/JOIN) a table that does not exist in the ground-truth catalog
/// (a common AI-hallucination failure mode). CTEs, derived tables, table-valued functions, <c>#temp</c>,
/// <c>@table</c> variables, cross-database names, <c>DUAL</c> and system catalogs are never reported.
/// Shares one <see cref="IPhantomReferenceAnalyzer"/> result per raw SQL contract with <see cref="PhantomColumnRule"/> (DG016);
/// the default is the tokenizer <see cref="PhantomSqlAnalyzer"/>, SQL Server passes its ScriptDOM analyzer. When the
/// analyzer cannot parse the SQL nothing is reported (DG019 reports parse errors).
/// </summary>
public sealed class PhantomTableRule : ContractRuleBase
{
    private readonly IPhantomReferenceAnalyzer _analyzer;

    /// <summary>Initializes a new instance of the <see cref="PhantomTableRule"/> class.</summary>
    /// <param name="analyzer">Phantom reference analyzer; null uses the tokenizer <see cref="PhantomSqlAnalyzer"/>.</param>
    public PhantomTableRule(IPhantomReferenceAnalyzer? analyzer = null)
    {
        _analyzer = analyzer ?? PhantomSqlAnalyzer.Instance;
    }

    /// <inheritdoc />
    public override string RuleId => "DG015";

    /// <inheritdoc />
    public override string Name => "Phantom Table Reference";

    /// <inheritdoc />
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    /// <inheritdoc />
    public override string Description => "Raw SQL references a table that does not exist in the database schema";

    /// <inheritdoc />
    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor rawSql || string.IsNullOrWhiteSpace(rawSql.SqlText))
        {
            return Task.CompletedTask;
        }

        var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
        if (schema is null || schema.Tables.Count == 0)
        {
            return Task.CompletedTask;
        }

        var analysis = PhantomAnalysisCache.Get(rawSql, schema, _analyzer);
        if (analysis.ParseFailed)
        {
            return Task.CompletedTask;
        }

        foreach (var finding in analysis.PhantomTables)
        {
            violations.Add(CreateViolation(
                RuleId,
                $"Table '{finding.Table}' does not exist in database",
                Severity,
                rawSql.Location,
                new Dictionary<string, object?> { ["table"] = finding.Table }));
        }

        return Task.CompletedTask;
    }
}
