using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// DG016: raw SQL references a column that does not exist in the catalog table it resolves to.
/// <c>alias.column</c> resolves the alias to the nearest table reference in scope; an unqualified SELECT-list
/// column is checked against the union of every table that SELECT references. Output aliases, expressions,
/// and columns of unknown sources (CTE, derived table, TVF, temp table) are never reported.
/// Shares <see cref="PhantomSqlAnalyzer"/> with <see cref="PhantomTableRule"/> (DG015).
/// </summary>
public sealed class PhantomColumnRule : ContractRuleBase
{
    /// <inheritdoc />
    public override string RuleId => "DG016";

    /// <inheritdoc />
    public override string Name => "Phantom Column Reference";

    /// <inheritdoc />
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    /// <inheritdoc />
    public override string Description => "Raw SQL references a column that does not exist in the referenced table";

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

        foreach (var finding in PhantomSqlAnalyzer.Analyze(rawSql, schema).Columns)
        {
            violations.Add(CreateViolation(
                RuleId,
                $"Column '{finding.Column}' does not exist in table '{finding.Table}'",
                Severity,
                rawSql.Location,
                new Dictionary<string, object?> { ["column"] = finding.Column, ["table"] = finding.Table }));
        }

        return Task.CompletedTask;
    }
}
