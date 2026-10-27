using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DataGuard.Core.Tests")]

namespace DataGuard.SqlServer.Adapter;

/// <summary>
/// SQL Server live query schema provider using sys.sp_describe_first_result_set (DG018/DG020 for <c>sqlserver</c>).
/// </summary>
public sealed class SqlServerLiveQuerySchemaProvider : ILiveQuerySchemaProvider
{
    private readonly string _connectionString;

    public SqlServerLiveQuerySchemaProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return LiveSchemaResult.NotSupported("Empty SQL text cannot be described.");
        }

        try
        {
            return LiveSchemaResult.FromColumns(await DescribeCoreAsync(sqlText, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // sp_describe_first_result_set rejects temp tables and dynamic SQL; connection errors land here too.
            return LiveSchemaResult.Fail(ex.Message);
        }
    }

    private async Task<IReadOnlyList<ColumnDescriptor>> DescribeCoreAsync(string sqlText, CancellationToken cancellationToken)
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = CreateDescribeCommand(connection, sqlText);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadColumnsAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<ColumnDescriptor>> ReadColumnsAsync(
        System.Data.Common.DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDescriptor>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader["name"] is DBNull ? null : reader["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var typeName = reader["system_type_name"] is DBNull ? "unknown" : reader["system_type_name"]?.ToString() ?? "unknown";
            var isNullable = reader["is_nullable"] is not DBNull && Convert.ToBoolean(reader["is_nullable"]);
            var maxLength = reader["max_length"] is DBNull ? (int?)null : Convert.ToInt32(reader["max_length"]);
            var systemTypeId = reader["system_type_id"] is DBNull ? 0 : Convert.ToInt32(reader["system_type_id"]);

            // Describe reports bytes; descriptors use UTF-16 units for nchar/nvarchar, as the catalog parser does.
            var descriptorLength = SqlServerStoredProcedureParser.NormalizeMaxLength(maxLength, systemTypeId);
            var precision = reader["precision"] is DBNull ? (int?)null : Convert.ToInt32(reader["precision"]);
            var scale = reader["scale"] is DBNull ? (int?)null : Convert.ToInt32(reader["scale"]);
            var ordinal = reader["column_ordinal"] is DBNull ? 0 : Convert.ToInt32(reader["column_ordinal"]);

            // Extract base type name before length/precision suffix, e.g. "nvarchar(50)" -> "nvarchar"
            var baseType = typeName.Split('(')[0].Trim();

            columns.Add(new ColumnDescriptor(
                Name: name,
                DataType: baseType,
                MaxLength: descriptorLength,
                Precision: precision,
                Scale: scale,
                IsNullable: isNullable,
                CharUsed: null,
                CharLength: descriptorLength,
                DataDefault: null,
                ColumnId: ordinal));
        }

        return columns;
    }

    internal static Microsoft.Data.SqlClient.SqlCommand CreateDescribeCommand(
        Microsoft.Data.SqlClient.SqlConnection connection,
        string sqlText)
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = "EXEC sys.sp_describe_first_result_set @tsql = @tsql, @params = NULL, @browse_information_mode = 0";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@tsql", System.Data.SqlDbType.NVarChar, -1) { Value = sqlText });
        return command;
    }
}
