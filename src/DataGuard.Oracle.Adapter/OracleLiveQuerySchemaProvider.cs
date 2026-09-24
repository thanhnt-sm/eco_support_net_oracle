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
/// </summary>
public sealed class OracleLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private static readonly System.Text.RegularExpressions.Regex DisallowedLiveCommandsRegex =
        new(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE|CALL|CREATE|EXEC|EXECUTE|BEGIN|DO|GRANT|REVOKE|COMMIT|ROLLBACK|SAVEPOINT|LOCK|EXPLAIN|DECLARE|PRAGMA)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly string _connectionString;

    public OracleLiveQuerySchemaProvider(string connectionString)
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
