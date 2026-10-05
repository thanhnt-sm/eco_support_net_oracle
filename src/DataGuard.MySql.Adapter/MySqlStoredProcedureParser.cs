using DataGuard.Core.Abstractions;
using MySqlConnector;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// Reads MySQL stored procedures and functions from INFORMATION_SCHEMA.ROUTINES + PARAMETERS,
/// and table columns from INFORMATION_SCHEMA.COLUMNS.
/// </summary>
public sealed class MySqlStoredProcedureParser : IContractSource
{
    public string SourceId => "mysql-sp";

    public string DisplayName => "MySQL Stored Procedures";

    private readonly string _connectionString;
    private readonly string _schema;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlStoredProcedureParser"/> class.
    /// </summary>
    /// <param name="connectionString">Connection string.</param>
    /// <param name="schema">Database (schema) to read; empty = the connection's <c>DATABASE()</c>, or every non-system schema when the connection has none.</param>
    public MySqlStoredProcedureParser(string connectionString, string schema = "")
    {
        _connectionString = connectionString;
        _schema = schema;
    }

    public async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ContractDescriptor>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var schema = await ResolveSchemaAsync(connection, cancellationToken);
        var lowerCaseTableNames = await ReadLowerCaseTableNamesAsync(connection, cancellationToken);

        result.AddRange(await ExtractStoredProceduresAsync(connection, schema, cancellationToken));
        result.AddRange(await ExtractTableColumnsAsync(connection, schema, lowerCaseTableNames, cancellationToken));

        return result;
    }

    /// <summary>
    /// Builds the dictionary key for a table name according to <c>@@lower_case_table_names</c>: 0 keeps names
    /// case-sensitive (<c>Orders</c> and <c>orders</c> are different tables); 1 and 2 compare names in lower case.
    /// </summary>
    /// <param name="schema">Table schema.</param>
    /// <param name="tableName">Table name as stored.</param>
    /// <param name="lowerCaseTableNames">Server value of <c>lower_case_table_names</c>.</param>
    /// <returns>The key.</returns>
    public static string TableKey(string schema, string tableName, int lowerCaseTableNames)
    {
        var key = string.IsNullOrEmpty(schema) ? tableName : $"{schema}.{tableName}";
        return lowerCaseTableNames == 0 ? key : key.ToLowerInvariant();
    }

    private async Task<string> ResolveSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_schema))
        {
            return _schema;
        }

        await using var command = new MySqlCommand("SELECT DATABASE()", connection);
        return await command.ExecuteScalarAsync(cancellationToken) as string ?? string.Empty;
    }

    private static async Task<int> ReadLowerCaseTableNamesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new MySqlCommand("SELECT @@lower_case_table_names", connection);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (MySqlException)
        {
            return 0; // Case-sensitive keys never merge distinct tables.
        }
    }

    private static string SchemaFilter(string column)
        => $"((@schema <> '' AND {column} = @schema) OR (@schema = '' AND {column} NOT IN ('mysql', 'sys', 'information_schema', 'performance_schema')))";

    /// <summary>
    /// Extracts stored procedure and function descriptors with their parameters from INFORMATION_SCHEMA. A function's
    /// return value (PARAMETERS row with ORDINAL_POSITION 0) becomes <see cref="StoredProcedureDescriptor.ReturnType"/>.
    /// Procedures keep the Id <c>mysql:{schema}.{name}</c>; functions (a separate MySQL namespace) use
    /// <c>mysql:{schema}.{name}#function</c>.
    /// </summary>
    private static async Task<IReadOnlyList<ContractDescriptor>> ExtractStoredProceduresAsync(
        MySqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
    {
        var result = new List<ContractDescriptor>();

        var sql = $@"
            SELECT r.ROUTINE_NAME, p.PARAMETER_NAME, p.DATA_TYPE, p.PARAMETER_MODE,
                   p.ORDINAL_POSITION, p.CHARACTER_MAXIMUM_LENGTH, p.NUMERIC_PRECISION, p.NUMERIC_SCALE,
                   r.ROUTINE_SCHEMA, r.ROUTINE_TYPE, r.DATA_TYPE
            FROM information_schema.ROUTINES r
            LEFT JOIN information_schema.PARAMETERS p
              ON r.ROUTINE_SCHEMA = p.SPECIFIC_SCHEMA AND r.SPECIFIC_NAME = p.SPECIFIC_NAME
             AND p.ROUTINE_TYPE = r.ROUTINE_TYPE
            WHERE r.ROUTINE_TYPE IN ('PROCEDURE', 'FUNCTION') AND {SchemaFilter("r.ROUTINE_SCHEMA")}
            ORDER BY r.ROUTINE_SCHEMA, r.ROUTINE_TYPE, r.ROUTINE_NAME, p.ORDINAL_POSITION";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);

        var procedures = new Dictionary<string, (string Name, string Schema, bool IsFunction, string? ReturnType, List<ParameterDescriptor> Parameters)>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var routineSchema = reader.IsDBNull(8) ? "" : reader.GetString(8);
            var isFunction = !reader.IsDBNull(9) && string.Equals(reader.GetString(9), "FUNCTION", StringComparison.OrdinalIgnoreCase);
            var qualified = string.IsNullOrEmpty(routineSchema) ? name : $"{routineSchema}.{name}";
            var key = isFunction ? qualified + "#function" : qualified;
            if (!procedures.TryGetValue(key, out var entry))
            {
                var routineReturnType = isFunction && !reader.IsDBNull(10) ? NormalizeMySqlType(reader.GetString(10)) : null;
                entry = (name, routineSchema, isFunction, routineReturnType, new List<ParameterDescriptor>());
                procedures[key] = entry;
            }

            var ordinal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

            // A LEFT JOIN filler row is the only catalog record for a procedure
            // without parameters; ordinal 0 is a function's return value.
            if (reader.IsDBNull(1) || ordinal == 0)
            {
                continue;
            }

            var paramName = reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var mode = reader.IsDBNull(3) ? "IN" : reader.GetString(3);
            var maxLength = reader.IsDBNull(5) ? null : NormalizeLength(reader.GetInt64(5));
            var precision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var scale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7);

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
                Schema: string.IsNullOrEmpty(entry.Schema) ? schema : entry.Schema,
                PackageName: "",
                Parameters: entry.Parameters,
                ResultColumns: new List<ColumnDescriptor>(),
                ReturnsRefCursor: false,
                ReturnType: entry.ReturnType));
        }

        return result;
    }

    /// <summary>
    /// Extracts table column descriptors from INFORMATION_SCHEMA.COLUMNS.
    /// Produces DatabaseSchemaDescriptor for length-mismatch and nullability rules.
    /// </summary>
    private static async Task<IReadOnlyList<ContractDescriptor>> ExtractTableColumnsAsync(
        MySqlConnection connection,
        string schema,
        int lowerCaseTableNames,
        CancellationToken cancellationToken)
    {
        var result = new List<ContractDescriptor>();

        var sql = $@"
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                   NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_TYPE,
                   COLUMN_DEFAULT, CHARACTER_SET_NAME, COLUMN_KEY, ORDINAL_POSITION, TABLE_SCHEMA
            FROM information_schema.COLUMNS
            WHERE {SchemaFilter("TABLE_SCHEMA")}
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);

        var tables = new Dictionary<string, (string Schema, string Name, List<ColumnDescriptor> Columns)>(StringComparer.Ordinal);
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
            var dataDefault = reader.IsDBNull(8) ? null : reader.GetValue(8)?.ToString();
            var charSetName = reader.IsDBNull(9) ? null : reader.GetString(9);
            var ordinalPosition = reader.IsDBNull(11) ? 0 : reader.GetInt32(11);
            var tableSchema = reader.IsDBNull(12) ? schema : reader.GetString(12);

            // MySQL INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH is in characters,
            // not bytes. The column charset (null for non-character columns) drives the
            // byte-capacity calculations in the length mismatch detector.
            var column = new ColumnDescriptor(
                Name: columnName,
                DataType: dataType.ToUpperInvariant(),
                MaxLength: charMaxLength,
                CharLength: charMaxLength, // MySQL reports length in characters
                Precision: numericPrecision,
                Scale: numericScale,
                IsNullable: isNullable,
                CharUsed: null, // MySQL has no BYTE/CHAR length semantics
                DataDefault: dataDefault,
                ColumnId: ordinalPosition,
                Charset: charSetName);

            var key = TableKey(tableSchema, tableName, lowerCaseTableNames);
            if (!tables.TryGetValue(key, out var entry))
            {
                entry = (tableSchema, tableName, new List<ColumnDescriptor>());
                tables[key] = entry;
            }

            entry.Columns.Add(column);
        }

        // Emit one DatabaseSchemaDescriptor with all tables
        if (tables.Count > 0)
        {
            var tableDescriptors = tables.Values.Select(entry =>
                new DatabaseTableDescriptor(entry.Name, entry.Columns, entry.Schema)).ToList();

            result.Add(new DatabaseSchemaDescriptor(
                Id: $"mysql:schema:{schema}",
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
