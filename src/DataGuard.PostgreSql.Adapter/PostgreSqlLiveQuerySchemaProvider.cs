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
/// </summary>
public sealed class PostgreSqlLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private static readonly System.Text.RegularExpressions.Regex DisallowedLiveCommandsRegex =
        new(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|CALL|CREATE|EXEC|EXECUTE|BEGIN|DO|GRANT|REVOKE|COPY|LOCK|VACUUM|REINDEX|COMMIT|ROLLBACK|SAVEPOINT|SET|RESET|DISCARD|EXPLAIN)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly string _connectionString;

    public PostgreSqlLiveQuerySchemaProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<IReadOnlyList<ColumnDescriptor>> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDescriptor>();
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return columns;
        }

        var trimmed = sqlText.Trim().TrimEnd(';');
        var sanitizedSql = DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals(trimmed);
        if (sanitizedSql.Contains(';'))
        {
            // Reject stacked queries
            return columns;
        }

        if (DataGuard.Core.Rules.ColumnShapeMatchRule.HasUnclosedBlockComment(trimmed))
        {
            // Reject unclosed block comment breakout attempts
            return ExtractSyntacticColumns(sqlText);
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
                    return ExtractSyntacticColumns(sqlText);
                }
            }
        }

        if (parenDepth != 0)
        {
            // Unbalanced parentheses
            return ExtractSyntacticColumns(sqlText);
        }

        if (DisallowedLiveCommandsRegex.IsMatch(sanitizedSql))
        {
            // Do not execute data-modifying queries live; fall back to syntactic extraction
            return ExtractSyntacticColumns(sqlText);
        }
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Graceful fallback if live connection or query describe fails
        }

        if (columns.Count == 0)
        {
            return ExtractSyntacticColumns(sqlText);
        }

        return columns;
    }

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