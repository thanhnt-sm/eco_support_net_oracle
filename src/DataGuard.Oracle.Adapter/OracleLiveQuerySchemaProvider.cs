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
        var withoutLiterals = System.Text.RegularExpressions.Regex.Replace(trimmed, @"'(?:''|[^'])*'", "''");
        if (withoutLiterals.Contains(';'))
        {
            // Reject stacked queries
            return columns;
        }

        try
        {
            using var connection = new OracleConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM (\n{trimmed}\n) WHERE 1=0";

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
                    var columnSize = row["ColumnSize"] is not DBNull ? Convert.ToInt32(row["ColumnSize"]) : (int?)null;
                    var precision = row["NumericPrecision"] is not DBNull ? Convert.ToInt32(row["NumericPrecision"]) : (int?)null;
                    var scale = row["NumericScale"] is not DBNull ? Convert.ToInt32(row["NumericScale"]) : (int?)null;

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
        catch
        {
            // Graceful fallback if live connection or query describe fails
        }

        return columns;
    }
}
