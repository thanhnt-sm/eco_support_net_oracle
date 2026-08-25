using DataGuard.Core.Abstractions;
using Npgsql;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// Reads PostgreSQL stored procedures and functions from pg_proc + pg_type.
/// Also reads table columns from information_schema.columns for length/dialect rules.
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

        // Query pg_proc + pg_type for stored procedure/function parameters.
        // PostgreSQL stores routines in pg_proc; parameter types reference pg_type.
        // proargtypes is an oidvector of IN params; proallargtypes is an array of all param types.
        const string routineSql = @"
            SELECT
                p.proname                                       AS routine_name,
                n.nspname                                       AS schema_name,
                p.proargnames                                   AS arg_names,
                p.proargtypes                                   AS in_arg_types,
                p.proallargtypes                                AS all_arg_types,
                p.proargmodes                                   AS arg_modes,
                p.prorettype                                    AS return_type,
                p.oid                                           AS proc_oid,
                p.prokind                                       AS proc_kind,
                p.pronargs                                      AS num_args,
                p.pronargdefaults                               AS num_defaults
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = @schema
              AND p.prokind IN ('f', 'p')   -- 'f' = function, 'p' = procedure
            ORDER BY p.proname, p.oid";

        // Resolve type oids to human-readable names.
        const string typeSql = @"
            SELECT oid, typname FROM pg_type WHERE oid = ANY(@oids)";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Step 1: Read all routines.
        var routines = new List<RoutineInfo>();
        await using (var cmd = new NpgsqlCommand(routineSql, connection))
        {
            cmd.Parameters.AddWithValue("schema", _schema);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var routine = new RoutineInfo
                {
                    Name = reader.GetString(0),
                    Schema = reader.GetString(1),
                    ArgNames = reader.IsDBNull(2) ? null : reader.GetFieldValue<string[]>(2),
                    InArgTypes = reader.IsDBNull(3) ? null : reader.GetFieldValue<uint[]>(3),
                    AllArgTypes = reader.IsDBNull(4) ? null : reader.GetFieldValue<uint[]>(4),
                    ArgModes = reader.IsDBNull(5) ? null : reader.GetFieldValue<char[]>(5),
                    ReturnType = reader.IsDBNull(6) ? 0 : reader.GetFieldValue<uint>(6),
                    Oid = reader.GetFieldValue<uint>(7),
                    Kind = reader.GetString(8)[0],
                    NumArgs = reader.GetInt32(9),
                };
                routines.Add(routine);
            }
        }

        if (routines.Count == 0)
        {
            return result;
        }

        // Step 2: Collect all unique type oids and resolve to names.
        var allOids = new HashSet<uint>();
        foreach (var r in routines)
        {
            if (r.InArgTypes != null)
            {
                foreach (var oid in r.InArgTypes)
                {
                    allOids.Add(oid);
                }
            }

            if (r.AllArgTypes != null)
            {
                foreach (var oid in r.AllArgTypes)
                {
                    allOids.Add(oid);
                }
            }

            if (r.ReturnType != 0)
            {
                allOids.Add(r.ReturnType);
            }
        }

        var typeMap = new Dictionary<uint, string>();
        await using (var cmd = new NpgsqlCommand(typeSql, connection))
        {
            cmd.Parameters.AddWithValue("oids", allOids.ToArray());
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                typeMap[reader.GetFieldValue<uint>(0)] = reader.GetString(1);
            }
        }

        // Step 3: Build StoredProcedureDescriptor for each routine.
        foreach (var routine in routines)
        {
            var parameters = BuildParameters(routine, typeMap);
            var returnTypeName = typeMap.TryGetValue(routine.ReturnType, out var rt) ? rt : "unknown";

            result.Add(new StoredProcedureDescriptor(
                Id: $"postgres:{_schema}.{routine.Oid}",
                Name: routine.Name,
                Schema: _schema,
                PackageName: "",
                Parameters: parameters,
                ResultColumns: new List<ColumnDescriptor>(),
                ReturnsRefCursor: string.Equals(returnTypeName, "refcursor", StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    /// <summary>
    /// Builds parameter descriptors from pg_proc fields.
    /// PostgreSQL has three representations:
    ///   - proargtypes: oidvector of IN-only params (older style)
    ///   - proallargtypes: array of ALL param types (when modes are mixed)
    ///   - proargmodes: array of 'i'/'o'/'b'/'v' chars (IN/OUT/INOUT/VARIADIC)
    /// </summary>
    private static List<ParameterDescriptor> BuildParameters(RoutineInfo routine, Dictionary<uint, string> typeMap)
    {
        var parameters = new List<ParameterDescriptor>();

        // Determine which type array to use.
        uint[]? typeOids;
        bool hasExplicitModes = routine.ArgModes != null && routine.ArgModes.Length > 0;

        if (hasExplicitModes && routine.AllArgTypes != null)
        {
            typeOids = routine.AllArgTypes;
        }
        else if (routine.InArgTypes != null && routine.InArgTypes.Length > 0)
        {
            typeOids = routine.InArgTypes;
        }
        else
        {
            return parameters; // No parameters.
        }

        var argNames = routine.ArgNames;
        var argModes = routine.ArgModes;

        for (int i = 0; i < typeOids.Length; i++)
        {
            var typeOid = typeOids[i];
            var typeName = typeMap.TryGetValue(typeOid, out var tn) ? tn : $"oid_{typeOid}";
            var paramName = (argNames != null && i < argNames.Length) ? argNames[i] : $"p{i + 1}";
            var mode = (argModes != null && i < argModes.Length) ? argModes[i] : 'i';

            var direction = mode switch
            {
                'i' => ParameterDirection.Input,
                'o' => ParameterDirection.Output,
                'b' => ParameterDirection.InputOutput,
                'v' => ParameterDirection.Input, // VARIADIC treated as input
                _ => ParameterDirection.Input,
            };

            // Resolve length/precision from pg_type for known types.
            var (maxLength, precision, scale) = ResolveTypeAttributes(typeName);

            parameters.Add(new ParameterDescriptor(
                Name: paramName,
                DataType: typeName,
                Direction: direction,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: true, // pg_proc does not track parameter nullability
                OrdinalPosition: i + 1));
        }

        return parameters;
    }

    /// <summary>
    /// Returns typical length/precision attributes for well-known PostgreSQL types.
    /// </summary>
    private static (int? maxLength, int? precision, int? scale) ResolveTypeAttributes(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "varchar" or "character varying" => (null, null, null), // user-specified at column level
            "char" or "character" => (1, null, null),
            "text" => (null, null, null), // unlimited
            "numeric" or "decimal" => (null, null, null), // user-specified
            "integer" or "int" or "int4" => (null, 10, 0),
            "bigint" or "int8" => (null, 19, 0),
            "smallint" or "int2" => (null, 5, 0),
            "real" or "float4" => (null, 24, null),
            "double precision" or "float8" => (null, 53, null),
            "json" or "jsonb" => (null, null, null),
            "uuid" => (null, null, null),
            "bytea" => (null, null, null),
            _ => (null, null, null),
        };
    }

    /// <summary>
    /// Reads table columns from information_schema.columns for length mismatch detection.
    /// </summary>
    public async Task<IReadOnlyList<ColumnDescriptor>> GetTableColumnsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                column_name,
                data_type,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                is_nullable,
                ordinal_position
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            ORDER BY ordinal_position";

        var columns = new List<ColumnDescriptor>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", _schema);
        command.Parameters.AddWithValue("table", tableName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var dataType = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var maxLength = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2);
            var precision = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3);
            var scale = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4);
            var isNullable = !reader.IsDBNull(5) && string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase);
            var ordinal = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);

            columns.Add(new ColumnDescriptor(
                Name: name,
                DataType: dataType,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale,
                IsNullable: isNullable,
                CharUsed: null, // PostgreSQL uses character semantics natively
                ColumnId: ordinal));
        }

        return columns;
    }

    /// <summary>
    /// Reads all tables' columns in the schema, grouped by table name.
    /// </summary>
    public async Task<Dictionary<string, List<ColumnDescriptor>>> GetAllTableColumnsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                table_name,
                column_name,
                data_type,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                is_nullable,
                ordinal_position
            FROM information_schema.columns
            WHERE table_schema = @schema
            ORDER BY table_name, ordinal_position";

        var result = new Dictionary<string, List<ColumnDescriptor>>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", _schema);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tableName = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var column = new ColumnDescriptor(
                Name: reader.IsDBNull(1) ? "" : reader.GetString(1),
                DataType: reader.IsDBNull(2) ? "" : reader.GetString(2),
                MaxLength: reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                Precision: reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                Scale: reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                IsNullable: !reader.IsDBNull(6) && string.Equals(reader.GetString(6), "YES", StringComparison.OrdinalIgnoreCase),
                CharUsed: null, // PostgreSQL uses character semantics natively
                ColumnId: reader.IsDBNull(7) ? 0 : reader.GetInt32(7));

            if (!result.TryGetValue(tableName, out var list))
            {
                list = new List<ColumnDescriptor>();
                result[tableName] = list;
            }

            list.Add(column);
        }

        return result;
    }

    /// <summary>
    /// Builds a DatabaseSchemaDescriptor from all tables in the schema.
    /// Used by length mismatch and dialect rules as ground-truth.
    /// </summary>
    public async Task<DatabaseSchemaDescriptor> BuildSchemaDescriptorAsync(CancellationToken cancellationToken = default)
    {
        var allColumns = await GetAllTableColumnsAsync(cancellationToken);
        var tables = allColumns.Select(kvp =>
            new DatabaseTableDescriptor(kvp.Key, kvp.Value)).ToList();

        return new DatabaseSchemaDescriptor(
            Id: $"postgres:{_schema}",
            Tables: tables,
            LengthSemantics: "CHAR"); // PostgreSQL always uses character semantics
    }

    /// <summary>
    /// Internal struct for pg_proc row data.
    /// </summary>
    private sealed class RoutineInfo
    {
        public string Name { get; init; } = "";

        public string Schema { get; init; } = "";

        public string[]? ArgNames { get; init; }

        public uint[]? InArgTypes { get; init; }

        public uint[]? AllArgTypes { get; init; }

        public char[]? ArgModes { get; init; }

        public uint ReturnType { get; init; }

        public uint Oid { get; init; }

        public char Kind { get; init; }

        public int NumArgs { get; init; }
    }
}
