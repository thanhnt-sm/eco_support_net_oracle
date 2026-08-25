using DataGuard.Core.Abstractions;
using MySqlConnector;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// Reads MySQL stored procedures from INFORMATION_SCHEMA.ROUTINES + PARAMETERS,
/// and table columns from INFORMATION_SCHEMA.COLUMNS.
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

        result.AddRange(await ExtractStoredProceduresAsync(cancellationToken));
        result.AddRange(await ExtractTableColumnsAsync(cancellationToken));

        return result;
    }

    /// <summary>
    /// Extracts stored procedure descriptors with their parameters from INFORMATION_SCHEMA.
    /// </summary>
    private async Task<IReadOnlyList<ContractDescriptor>> ExtractStoredProceduresAsync(CancellationToken cancellationToken)
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
            {
                continue; // LEFT JOIN filler row for a procedure without parameters - skip.
            }

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
                DataType: NormalizeMySqlType(dataType),
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

    /// <summary>
    /// Extracts table column descriptors from INFORMATION_SCHEMA.COLUMNS.
    /// Produces DatabaseSchemaDescriptor for length-mismatch and nullability rules.
    /// </summary>
    private async Task<IReadOnlyList<ContractDescriptor>> ExtractTableColumnsAsync(CancellationToken cancellationToken)
    {
        var result = new List<ContractDescriptor>();

        const string sql = @"
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                   NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_TYPE,
                   CHARACTER_SET_NAME, COLUMN_KEY, ORDINAL_POSITION
            FROM information_schema.COLUMNS
            WHERE (@schema = '' OR TABLE_SCHEMA = @schema)
            ORDER BY TABLE_NAME, ORDINAL_POSITION";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", _schema);

        var tables = new Dictionary<string, List<ColumnDescriptor>>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tableName = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var columnName = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var charMaxLength = reader.IsDBNull(3) ? null : NormalizeLength(reader.GetInt64(3));
            var numericPrecision = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4);
            var numericScale = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var isNullable = !reader.IsDBNull(6) && reader.GetString(6) == "YES";
            var columnType = reader.IsDBNull(7) ? "" : reader.GetString(7); // e.g. "varchar(255)", "int(11)"
            var charSetName = reader.IsDBNull(8) ? null : reader.GetString(8);
            var columnKey = reader.IsDBNull(9) ? "" : reader.GetString(9); // PRI, UNI, MUL
            var ordinalPosition = reader.IsDBNull(10) ? 0 : reader.GetInt32(10);

            // MySQL INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH is in characters,
            // not bytes. Store it as MaxLength (chars). Store the column type for
            // byte-capacity calculations in the length mismatch detector.
            var column = new ColumnDescriptor(
                Name: columnName,
                DataType: dataType.ToUpperInvariant(),
                MaxLength: charMaxLength,
                CharLength: charMaxLength, // MySQL reports length in characters
                Precision: numericPrecision,
                Scale: numericScale,
                IsNullable: isNullable,
                CharUsed: charSetName ?? "utf8mb4", // MySQL default charset
                DataDefault: columnType, // Store full column_type for byte analysis
                ColumnId: ordinalPosition);

            if (!tables.TryGetValue(tableName, out var list))
            {
                list = new List<ColumnDescriptor>();
                tables[tableName] = list;
            }

            list.Add(column);
        }

        // Emit one DatabaseSchemaDescriptor with all tables
        if (tables.Count > 0)
        {
            var tableDescriptors = tables.Select(kvp =>
                new DatabaseTableDescriptor(kvp.Key, kvp.Value)).ToList();

            result.Add(new DatabaseSchemaDescriptor(
                Id: $"mysql:schema:{_schema}",
                Tables: tableDescriptors,
                LengthSemantics: "CHAR")); // MySQL always uses character semantics in INFORMATION_SCHEMA
        }

        return result;
    }

    /// <summary>
    /// Maps MySQL INFORMATION_SCHEMA PARAMETER_MODE to DataGuard ParameterDirection.
    /// </summary>
    private static ParameterDirection MapDirection(string mode) => mode.ToUpperInvariant() switch
    {
        "IN" => ParameterDirection.Input,
        "OUT" => ParameterDirection.Output,
        "INOUT" => ParameterDirection.InputOutput,
        _ => ParameterDirection.Input
    };

    /// <summary>
    /// Normalizes MySQL data type names to a canonical uppercase form.
    /// Handles type aliases (e.g. BOOL → TINYINT, INTEGER → INT).
    /// </summary>
    private static string NormalizeMySqlType(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return string.Empty;
        }

        var normalized = dataType.Trim().ToUpperInvariant();

        // MySQL type aliases
        return normalized switch
        {
            "BOOL" or "BOOLEAN" => "TINYINT",
            "INTEGER" => "INT",
            "CHARACTER VARYING" => "VARCHAR",
            "DOUBLE PRECISION" => "DOUBLE",
            "REAL" => "DOUBLE",
            "FIXED" => "DECIMAL",
            "NUMERIC" => "DECIMAL",
            "STRING" => "VARCHAR",
            "LONG VARCHAR" => "MEDIUMTEXT",
            "LONG VARBINARY" => "MEDIUMBLOB",
            _ => normalized
        };
    }

    /// <summary>
    /// Safely converts a BIGINT CHARACTER_MAXIMUM_LENGTH to int?, returning null on overflow.
    /// </summary>
    private static int? NormalizeLength(long value) => value > int.MaxValue ? null : (int)value;
}
