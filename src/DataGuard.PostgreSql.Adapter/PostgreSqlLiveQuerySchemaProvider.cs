using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Npgsql;
using NpgsqlTypes;
namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// PostgreSQL live query schema provider using CommandBehavior.SchemaOnly and Npgsql column schema.
/// Database errors are reported as <see cref="LiveSchemaStatus.Failed"/> and refused statements as
/// <see cref="LiveSchemaStatus.Unsupported"/>; columns are never fabricated as a described result (red-team H1).
/// </summary>
public sealed class PostgreSqlLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private static readonly System.Text.RegularExpressions.Regex DisallowedLiveCommandsRegex =
        new(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|CALL|CREATE|EXEC|EXECUTE|BEGIN|DO|GRANT|REVOKE|COPY|LOCK|VACUUM|REINDEX|COMMIT|ROLLBACK|SAVEPOINT|SET|RESET|DISCARD|EXPLAIN)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly bool _allowSyntacticFallback;

    /// <summary>Initializes a new instance of the <see cref="PostgreSqlLiveQuerySchemaProvider"/> class.</summary>
    /// <param name="connectionString">Npgsql connection string.</param>
    /// <param name="allowSyntacticFallback">
    /// Non-live, opt-in: when true, a failed or unsupported describe also carries column names extracted from the SQL text
    /// with placeholder <c>text</c> types. The status stays <see cref="LiveSchemaStatus.Failed"/> or
    /// <see cref="LiveSchemaStatus.Unsupported"/>, so these columns are never treated as database ground truth. Default false.
    /// </param>
    public PostgreSqlLiveQuerySchemaProvider(string connectionString, bool allowSyntacticFallback = false)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _allowSyntacticFallback = allowSyntacticFallback;
    }

    public async Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return LiveSchemaResult.NotSupported("Empty SQL text cannot be described.");
        }

        var trimmed = sqlText.Trim().TrimEnd(';');
        var sanitizedSql = DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals(trimmed);
        if (sanitizedSql.Contains(';'))
        {
            // Reject stacked queries; no syntactic hint either, the statement boundary is unknown.
            return LiveSchemaResult.NotSupported("Stacked statements are not described live.");
        }

        if (DataGuard.Core.Rules.ColumnShapeMatchRule.HasUnclosedBlockComment(trimmed))
        {
            // Reject unclosed block comment breakout attempts
            return NotDescribed(LiveSchemaStatus.Unsupported, "Unclosed block comment; statement not described live.", sqlText);
        }

        var parenDepth = 0;
        foreach (var ch in sanitizedSql)
        {
            if (ch == '(')
            {
                parenDepth++;
            }
            else if (ch == ')')
            {
                parenDepth--;
                if (parenDepth < 0)
                {
                    // Premature closing parenthesis breakout attempt
                    return NotDescribed(LiveSchemaStatus.Unsupported, "Unbalanced closing parenthesis; statement not described live.", sqlText);
                }
            }
        }

        if (parenDepth != 0)
        {
            return NotDescribed(LiveSchemaStatus.Unsupported, "Unbalanced parentheses; statement not described live.", sqlText);
        }

        if (DisallowedLiveCommandsRegex.IsMatch(sanitizedSql))
        {
            // Never execute data-modifying or procedural statements live.
            return NotDescribed(LiveSchemaStatus.Unsupported, "Statement is not a read-only query; not described live.", sqlText);
        }

        var columns = new List<ColumnDescriptor>();
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandTimeout = 5;

            // codeql[dataguard/sql-injection-pattern]: by design. The wrapped text is the scanned repository's own SQL,
            // compiled schema-only (WHERE 1=0, every parameter bound to NULL, 5 s timeout) against the developer's database.
            command.CommandText = $"SELECT * FROM (\n{trimmed}\n) AS _dg_subq WHERE 1=0";

            // Bind dummy parameters to prevent 42P02 / unbound parameter exceptions during SchemaOnly query compilation
            var boundParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var atMatches = System.Text.RegularExpressions.Regex.Matches(sanitizedSql, @"(?<!@)@([A-Za-z0-9_]+)");
            foreach (System.Text.RegularExpressions.Match match in atMatches)
            {
                var paramName = match.Groups[1].Value;
                if (boundParams.Add(paramName))
                {
                    command.Parameters.Add(new NpgsqlParameter { ParameterName = paramName, Value = DBNull.Value, NpgsqlDbType = NpgsqlDbType.Unknown });
                }
            }

            var dollarMatches = System.Text.RegularExpressions.Regex.Matches(sanitizedSql, @"\$([0-9]+)");
            var maxDollar = 0;
            foreach (System.Text.RegularExpressions.Match match in dollarMatches)
            {
                if (int.TryParse(match.Groups[1].Value, out var idx) && idx > maxDollar && idx <= 1000)
                {
                    maxDollar = idx;
                }
            }

            for (var i = 1; i <= maxDollar; i++)
            {
                var paramName = i.ToString();
                if (boundParams.Add(paramName))
                {
                    command.Parameters.Add(new NpgsqlParameter { ParameterName = paramName, Value = DBNull.Value, NpgsqlDbType = NpgsqlDbType.Unknown });
                }
            }

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
            var columnSchema = await reader.GetColumnSchemaAsync(cancellationToken).ConfigureAwait(false);
            var ordinal = 0;
            foreach (var col in columnSchema)
            {
                if (string.IsNullOrEmpty(col.ColumnName))
                {
                    continue;
                }

                var dataTypeName = col.DataTypeName ?? "text";
                var isNullable = col.AllowDBNull ?? true;
                var maxLength = col.ColumnSize;
                var precision = col.NumericPrecision;
                var scale = col.NumericScale;

                columns.Add(new ColumnDescriptor(
                    Name: col.ColumnName,
                    DataType: dataTypeName,
                    MaxLength: maxLength,
                    Precision: precision,
                    Scale: scale,
                    IsNullable: isNullable,
                    CharUsed: null,
                    CharLength: maxLength,
                    DataDefault: null,
                    ColumnId: ordinal++));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Connection, permission or compile error: the shape is unknown, not "whatever the SQL text suggests".
            return NotDescribed(LiveSchemaStatus.Failed, ex.Message, sqlText);
        }

        return columns.Count == 0
            ? NotDescribed(LiveSchemaStatus.Failed, "The database returned no result-set columns for the statement.", sqlText)
            : LiveSchemaResult.FromColumns(columns);
    }

    private LiveSchemaResult NotDescribed(LiveSchemaStatus status, string reason, string sqlText)
    {
        var hint = _allowSyntacticFallback ? ExtractSyntacticColumns(sqlText) : null;
        return status == LiveSchemaStatus.Failed
            ? LiveSchemaResult.Fail(reason, hint)
            : LiveSchemaResult.NotSupported(reason, hint);
    }

    /// <summary>Non-live hint: column names from the SQL text with placeholder types. Never ground truth.</summary>
    private static IReadOnlyList<ColumnDescriptor> ExtractSyntacticColumns(string sqlText)
    {
        var columns = new List<ColumnDescriptor>();
        var syntacticColumns = ColumnShapeMatchRule.ExtractColumnNamesFromSql(sqlText);
        var ordinal = 0;
        foreach (var colName in syntacticColumns)
        {
            columns.Add(new ColumnDescriptor(
                Name: colName,
                DataType: "text",
                MaxLength: null,
                Precision: null,
                Scale: null,
                IsNullable: true,
                CharUsed: null,
                CharLength: null,
                DataDefault: null,
                ColumnId: ordinal++));
        }
        return columns;
    }
}
