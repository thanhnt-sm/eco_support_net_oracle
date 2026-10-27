using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DataGuard.SqlServer.Adapter;

/// <summary>
/// T-SQL grammar check on the ScriptDOM parser (<see cref="TSql160Parser"/>), injected into DG019
/// (<c>RawSqlParseStatusRule</c>) by <c>ProviderRuleCatalog</c> for the <c>sqlserver</c> provider. Client-side
/// placeholders (<c>:name</c>, <c>?</c>, <c>{0}</c>) are rewritten to <c>@</c> variables first, exactly as
/// <see cref="TSqlPhantomAnalyzer"/> does, so ADO.NET/Dapper placeholder SQL is not reported as malformed.
/// </summary>
public sealed class TSqlStatementParser : ISqlStatementParser
{
    /// <summary>Gets the shared stateless instance.</summary>
    public static TSqlStatementParser Instance { get; } = new();

    /// <inheritdoc />
    public string Provider => "sqlserver";

    /// <inheritdoc />
    public SqlStatementParseResult Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlStatementParseResult.Unchecked;
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment? fragment;
        IList<ParseError> errors;
        var sourceOffsets = new List<int>();
        using (var reader = new StringReader(TSqlPhantomAnalyzer.NormalizePlaceholders(sql, sourceOffsets)))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors.Count > 0 || fragment is null)
        {
            var first = errors.Count > 0 ? errors[0] : null;
            if (first is null)
            {
                return SqlStatementParseResult.Invalid("T-SQL parser returned no fragment");
            }

            var offset = sourceOffsets[Math.Clamp(first.Offset, 0, sourceOffsets.Count - 1)];
            var position = SourceText.From(sql).Lines.GetLinePosition(offset);
            return SqlStatementParseResult.Invalid($"Line {position.Line + 1}, column {position.Character + 1}: {first.Message}") with
            {
                ErrorOffset = offset,
            };
        }

        return new SqlStatementParseResult(true, null, SqlParameterVisitor.Extract(fragment), SelectColumns(fragment));
    }

    /// <summary>
    /// Names of the select-list columns of the first top-level <c>SELECT</c>: aliases, or the last part of a column
    /// reference. <c>*</c> and unnamed expressions are skipped. Types are unknown (syntax only).
    /// </summary>
    private static IReadOnlyList<ColumnDescriptor> SelectColumns(TSqlFragment fragment)
    {
        var select = (fragment as TSqlScript)?.Batches
            .SelectMany(batch => batch.Statements)
            .OfType<SelectStatement>()
            .FirstOrDefault();
        if (select?.QueryExpression is not QuerySpecification query)
        {
            return Array.Empty<ColumnDescriptor>();
        }

        var columns = new List<ColumnDescriptor>();
        foreach (var element in query.SelectElements.OfType<SelectScalarExpression>())
        {
            var name = element.ColumnName?.Value
                ?? (element.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                columns.Add(new ColumnDescriptor(name, "unknown", null, null, null, true, null, ColumnId: columns.Count + 1));
            }
        }

        return columns;
    }
}
