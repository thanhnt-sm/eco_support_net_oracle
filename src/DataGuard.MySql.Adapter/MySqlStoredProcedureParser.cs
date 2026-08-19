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
                   p.ORDINAL_POSITION, p.CHARACTER_MAXIMUM_LENGTH, p.NUMERIC_PRECISION, p.NUMERIC_SCALE
            FROM information_schema.ROUTINES r
            LEFT JOIN information_schema.PARAMETERS p
              ON r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA AND r.SPECIFIC_NAME = p.SPECIFIC_NAME
            WHERE r.ROUTINE_TYPE = 'PROCEDURE' AND (@schema = '' OR r.ROUTINE_SCHEMA = @schema)
            ORDER BY r.ROUTINE_NAME, p.ORDINAL_POSITION";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", _schema);

        var procedures = new Dictionary<string, List<ParameterDescriptor>>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var paramName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var mode = reader.IsDBNull(3) ? "IN" : reader.GetString(3);
            var ordinal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            var maxLength = reader.IsDBNull(5) ? null : NormalizeLength(reader.GetInt64(5));
            var precision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var scale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7);

            if (!procedures.TryGetValue(name, out var list))
            {
                list = new List<ParameterDescriptor>();
                procedures[name] = list;
            }
            list.Add(new ParameterDescriptor(
                Name: paramName,
                DataType: dataType,
                Direction: MapDirection(mode),
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: false,
                OrdinalPosition: ordinal));
        }

        foreach (var (name, parameters) in procedures)
        {
            result.Add(new StoredProcedureDescriptor(
                Id: $"mysql:{name}",
                Name: name,
                Schema: _schema,
                PackageName: "",
                Parameters: parameters,
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
