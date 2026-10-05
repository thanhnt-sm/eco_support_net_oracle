using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Oracle.ManagedDataAccess.Client;

namespace DataGuard.Oracle.Adapter;

/// <summary>
/// Oracle live query schema provider using CommandBehavior.SchemaOnly on a non-executing wrapper query.
/// Database errors are reported as <see cref="LiveSchemaStatus.Failed"/> and refused statements as
/// <see cref="LiveSchemaStatus.Unsupported"/>; columns are never fabricated as a described result (red-team H1).
/// </summary>
public sealed class OracleLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private static readonly System.Text.RegularExpressions.Regex DisallowedLiveCommandsRegex =
        new(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|CALL|CREATE|EXEC|EXECUTE|BEGIN|DO|GRANT|REVOKE|COMMIT|ROLLBACK|SAVEPOINT|LOCK|EXPLAIN|DECLARE|PRAGMA)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly bool _allowSyntacticFallback;

    /// <summary>Initializes a new instance of the <see cref="OracleLiveQuerySchemaProvider"/> class.</summary>
    /// <param name="connectionString">Oracle connection string.</param>
    /// <param name="allowSyntacticFallback">
    /// Non-live, opt-in: when true, a failed or unsupported describe also carries column names extracted from the SQL text
    /// with placeholder <c>VARCHAR2</c> types. The status stays <see cref="LiveSchemaStatus.Failed"/> or
    /// <see cref="LiveSchemaStatus.Unsupported"/>, so these columns are never treated as database ground truth. Default false.
    /// </param>
    public OracleLiveQuerySchemaProvider(string connectionString, bool allowSyntacticFallback = false)
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
            using var connection = new OracleConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.BindByName = true;
            command.CommandText = $"SELECT * FROM (\n{trimmed}\n) WHERE 1=0";

            // Bind dummy parameters to prevent ORA-01008 (not all variables bound) during SchemaOnly query compilation
            var paramMatches = System.Text.RegularExpressions.Regex.Matches(sanitizedSql, @"(?<!:):([A-Za-z0-9_]+)");
            var boundParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in paramMatches)
            {
                var paramName = match.Groups[1].Value;
                if (boundParams.Add(paramName))
                {
                    command.Parameters.Add(new OracleParameter(paramName, DBNull.Value));
                }
            }

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable != null)
            {
                var ordinal = 0;
                foreach (DataRow row in schemaTable.Rows)
                {
                    var columnName = row["ColumnName"]?.ToString();
                    if (string.IsNullOrEmpty(columnName))
                    {
                        continue;
                    }

                    var dataTypeName = row["DataTypeName"]?.ToString() ?? row["DataType"]?.ToString() ?? "VARCHAR2";
                    var isNullable = row["AllowDBNull"] is not DBNull && Convert.ToBoolean(row["AllowDBNull"]);
                    var columnSize = row["ColumnSize"] is not DBNull && int.TryParse(row["ColumnSize"]?.ToString(), out var cs) ? cs : (int?)null;
                    var precision = row["NumericPrecision"] is not DBNull && int.TryParse(row["NumericPrecision"]?.ToString(), out var pr) ? pr : (int?)null;
                    var scale = row["NumericScale"] is not DBNull && int.TryParse(row["NumericScale"]?.ToString(), out var sc) ? sc : (int?)null;

                    columns.Add(new ColumnDescriptor(
                        Name: columnName,
                        DataType: dataTypeName,
                        MaxLength: columnSize,
                        Precision: precision,
                        Scale: scale,
                        IsNullable: isNullable,
                        CharUsed: "C",
                        CharLength: columnSize,
                        DataDefault: null,
                        ColumnId: ordinal++));
                }
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
                DataType: "VARCHAR2",
                MaxLength: null,
                Precision: null,
                Scale: null,
                IsNullable: true,
                CharUsed: "C",
                CharLength: null,
                DataDefault: null,
                ColumnId: ordinal++));
        }
        return columns;
    }
}
