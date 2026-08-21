using DataGuard.Core.Abstractions;
using Npgsql;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// Reads PostgreSQL stored procedures (information_schema.routines + parameters).
/// </summary>
public sealed class PostgreSqlStoredProcedureParser : IContractSource
{
    public string SourceId => "postgresql-sp";

    public string DisplayName => "PostgreSQL Stored Procedures";

    private readonly string _connectionString;
    private readonly string _schema;

    public PostgreSqlStoredProcedureParser(string connectionString, string schema = "public")
    {
        _connectionString = connectionString;
        _schema = schema;
    }

    public async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ContractDescriptor>();

        const string sql = @"
            SELECT r.routine_name, p.parameter_name, p.data_type, p.parameter_mode,
                   p.ordinal_position, p.character_maximum_length, p.numeric_precision, p.numeric_scale,
                   r.specific_name
            FROM information_schema.routines r
            LEFT JOIN information_schema.parameters p
              ON r.specific_schema = p.specific_schema AND r.specific_name = p.specific_name
            WHERE r.routine_type = 'PROCEDURE' AND r.routine_schema = @schema
            ORDER BY r.routine_name, p.ordinal_position";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", _schema);

        var procedures = new Dictionary<string, (string Name, List<ParameterDescriptor> Parameters)>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (reader.IsDBNull(4))
            {
                continue; // LEFT JOIN filler row for a procedure without parameters - skip (ordinal_position IS NULL).
            }

            var paramName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var mode = reader.IsDBNull(3) ? "IN" : reader.GetString(3);
            var ordinal = reader.GetInt32(4);
            var maxLength = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var precision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var scale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7);
            var specificName = reader.IsDBNull(8) ? "" : reader.GetString(8);

            // Key by specific_name (unique per overload) so same-named overloads do not merge.
            var key = string.IsNullOrEmpty(specificName) ? name : specificName;
            if (!procedures.TryGetValue(key, out var entry))
            {
                entry = (name, new List<ParameterDescriptor>());
                procedures[key] = entry;
            }

            entry.Parameters.Add(new ParameterDescriptor(
                Name: paramName,
                DataType: dataType,
                Direction: MapDirection(mode),
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: false,
                OrdinalPosition: ordinal));
        }

        foreach (var (key, entry) in procedures)
        {
            result.Add(new StoredProcedureDescriptor(
                Id: $"postgres:{_schema}.{key}",
                Name: entry.Name,
                Schema: _schema,
                PackageName: "",
                Parameters: entry.Parameters,
                ResultColumns: new List<ColumnDescriptor>(),
                ReturnsRefCursor: false));
        }

        return result;
    }

    private static ParameterDirection MapDirection(string mode) => mode.ToUpperInvariant() switch
    {
        "IN" => ParameterDirection.Input,
        "OUT" => ParameterDirection.Output,
        "INOUT" => ParameterDirection.InputOutput,
        _ => ParameterDirection.Input
    };
}
