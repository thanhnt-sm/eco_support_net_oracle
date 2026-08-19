using DataGuard.Core.Abstractions;
using MySqlConnector;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// Reads MySQL stored procedures (information_schema.ROUTINES + PARAMETERS).
/// </summary>
public sealed class MySqlStoredProcedureParser : IContractSource
{
    public string SourceId => "mysql-sp";
    public string DisplayName => "MySQL Stored Procedures";
    private readonly string _connectionString;
    private readonly string _schema;

    public MySqlStoredProcedureParser(string connectionString, string schema = "")
    {
        _connectionString = connectionString;
        _schema = schema;
    }

    public async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ContractDescriptor>();

        const string sql = @"
            SELECT r.ROUTINE_NAME, p.PARAMETER_NAME, p.DATA_TYPE, p.PARAMETER_MODE,
                   p.ORDINAL_POSITION, p.CHARACTER_MAXIMUM_LENGTH, p.NUMERIC_PRECISION, p.NUMERIC_SCALE,
                   r.ROUTINE_SCHEMA
            FROM information_schema.ROUTINES r
            LEFT JOIN information_schema.PARAMETERS p
              ON r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA AND r.SPECIFIC_NAME = p.SPECIFIC_NAME
            WHERE r.ROUTINE_TYPE = 'PROCEDURE' AND (@schema = '' OR r.ROUTINE_SCHEMA = @schema)
            ORDER BY r.ROUTINE_SCHEMA, r.ROUTINE_NAME, p.ORDINAL_POSITION";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", _schema);

        var procedures = new Dictionary<string, (string Name, string Schema, List<ParameterDescriptor> Parameters)>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (reader.IsDBNull(1))
                continue; // LEFT JOIN filler row for a procedure without parameters - skip.

            var paramName = reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var mode = reader.IsDBNull(3) ? "IN" : reader.GetString(3);
            var ordinal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            var maxLength = reader.IsDBNull(5) ? null : NormalizeLength(reader.GetInt64(5));
            var precision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var scale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7);
            var routineSchema = reader.IsDBNull(8) ? "" : reader.GetString(8);

            var key = string.IsNullOrEmpty(routineSchema) ? name : $"{routineSchema}.{name}";
            if (!procedures.TryGetValue(key, out var entry))
            {
                entry = (name, routineSchema, new List<ParameterDescriptor>());
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
                Id: $"mysql:{key}",
                Name: entry.Name,
                Schema: string.IsNullOrEmpty(entry.Schema) ? _schema : entry.Schema,
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

    private static int? NormalizeLength(long value) => value > int.MaxValue ? null : (int)value;
}
