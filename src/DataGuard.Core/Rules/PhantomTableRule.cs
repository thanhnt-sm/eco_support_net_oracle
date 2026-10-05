using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// DG015: raw SQL references (FROM/JOIN) a table that does not exist in the ground-truth catalog
/// (a common AI-hallucination failure mode). CTEs, derived tables, table-valued functions, <c>#temp</c>,
/// <c>@table</c> variables, cross-database names, <c>DUAL</c> and system catalogs are never reported.
/// Shares <see cref="PhantomSqlAnalyzer"/> with <see cref="PhantomColumnRule"/> (DG016).
/// </summary>
public sealed class PhantomTableRule : ContractRuleBase
{
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

        foreach (var finding in PhantomSqlAnalyzer.Analyze(rawSql, schema).Tables)
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
