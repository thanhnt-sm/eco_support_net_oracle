using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Npgsql;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// PostgreSQL live query schema provider using CommandBehavior.SchemaOnly and Npgsql column schema.
/// </summary>
public sealed class PostgreSqlLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
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
        var withoutLiterals = System.Text.RegularExpressions.Regex.Replace(trimmed, @"'(?:''|[^'])*'", "''");
        if (withoutLiterals.Contains(';'))
        {
            // Reject stacked queries
            return columns;
        }

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM (\n{trimmed}\n) AS _dg_subq WHERE 1=0";

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
        catch
        {
            // Graceful fallback if live connection or query describe fails
        }

        return columns;
    }
}