using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Npgsql;
using NpgsqlTypes;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// Reads PostgreSQL stored procedures and functions from pg_proc + pg_type.
/// Also reads table, view and materialized-view columns for length/dialect rules.
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
                p.pronargdefaults                               AS num_defaults,
                p.proretset                                     AS returns_set
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
        var routines = new List<PostgreSqlRoutineInfo>();
        await using (var cmd = new NpgsqlCommand(routineSql, connection))
        {
            cmd.Parameters.AddWithValue("schema", _schema);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var routine = new PostgreSqlRoutineInfo
                {
                    Name = reader.GetString(0),
                    Schema = reader.GetString(1),
                    ArgNames = reader.IsDBNull(2) ? null : reader.GetFieldValue<string[]>(2),
                    InArgTypes = reader.IsDBNull(3) ? null : reader.GetFieldValue<uint[]>(3),
                    AllArgTypes = reader.IsDBNull(4) ? null : reader.GetFieldValue<uint[]>(4),
                    ArgModes = reader.IsDBNull(5) ? null : reader.GetFieldValue<char[]>(5),
                    ReturnType = reader.IsDBNull(6) ? 0 : reader.GetFieldValue<uint>(6),
                    Oid = reader.GetFieldValue<uint>(7),
                    Kind = reader.GetFieldValue<char>(8),
                    NumArgs = reader.GetInt32(9),
                    NumDefaults = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    ReturnsSet = !reader.IsDBNull(11) && reader.GetBoolean(11),
                };
                routines.Add(routine);
            }
        }

        if (routines.Count == 0)
        {
            result.Add(await BuildSchemaDescriptorAsync(cancellationToken));
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
            cmd.Parameters.Add("oids", NpgsqlDbType.Array | NpgsqlDbType.Oid).Value = allOids.ToArray();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                typeMap[reader.GetFieldValue<uint>(0)] = reader.GetString(1);
            }
        }

        // Step 3: Build StoredProcedureDescriptor for each routine.
        foreach (var routine in routines)
        {
            result.Add(BuildDescriptor(routine, typeMap, _schema));
        }

        // Persist structural ground truth alongside routine contracts so callers
        // can create schema-bearing snapshots for PostgreSQL.
        result.Add(await BuildSchemaDescriptorAsync(cancellationToken));

        return result;
    }

    /// <summary>
    /// Builds the descriptor of one pg_proc row. The Id carries the IN-argument signature
    /// (<c>postgres:{schema}.{name}(int4,text)</c>) so overloads stay distinct and stable across dump/restore (OIDs are not).
    /// </summary>
    /// <param name="routine">The pg_proc row.</param>
    /// <param name="typeMap">Type OID to <c>typname</c>.</param>
    /// <param name="schema">Schema the routine was read from.</param>
    /// <returns>The descriptor.</returns>
    public static StoredProcedureDescriptor BuildDescriptor(PostgreSqlRoutineInfo routine, IReadOnlyDictionary<uint, string> typeMap, string schema)
    {
        ArgumentNullException.ThrowIfNull(routine);
        ArgumentNullException.ThrowIfNull(typeMap);
        var (parameters, resultColumns) = BuildParameters(routine, typeMap);
        string? returnTypeName = routine.ReturnType != 0 && typeMap.TryGetValue(routine.ReturnType, out var rt) ? rt : null;
        var signature = string.Join(",", (routine.InArgTypes ?? Array.Empty<uint>()).Select(oid => TypeName(typeMap, oid)));

        return new StoredProcedureDescriptor(
            Id: $"postgres:{schema}.{routine.Name}({signature})",
            Name: routine.Name,
            Schema: schema,
            PackageName: "",
            Parameters: parameters,
            ResultColumns: resultColumns,
            ReturnsRefCursor: string.Equals(returnTypeName, "refcursor", StringComparison.OrdinalIgnoreCase),
            ReturnType: routine.Kind == 'p' ? null : returnTypeName);
    }

    /// <summary>
    /// Builds parameter descriptors from pg_proc fields.
    /// PostgreSQL has three representations:
    ///   - proargtypes: oidvector of IN-only params (older style)
    ///   - proallargtypes: array of ALL param types (when modes are mixed)
    ///   - proargmodes: 'i' IN, 'o' OUT, 'b' INOUT, 'v' VARIADIC, 't' TABLE (RETURNS TABLE output column).
    /// 't' arguments are result columns, not parameters. <c>pronargdefaults</c> marks the trailing N input
    /// (i/b/v) parameters <see cref="ParameterDescriptor.HasDefault"/>. Unnamed arguments are named <c>p{i}</c>.
    /// </summary>
    internal static (List<ParameterDescriptor> Parameters, List<ColumnDescriptor> ResultColumns) BuildParameters(
        PostgreSqlRoutineInfo routine,
        IReadOnlyDictionary<uint, string> typeMap)
    {
        var parameters = new List<ParameterDescriptor>();
        var resultColumns = new List<ColumnDescriptor>();

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
            return (parameters, resultColumns); // No parameters.
        }

        var argNames = routine.ArgNames;
        var argModes = hasExplicitModes ? routine.ArgModes : null;

        for (int i = 0; i < typeOids.Length; i++)
        {
            var typeName = TypeName(typeMap, typeOids[i]);
            var rawName = (argNames != null && i < argNames.Length) ? argNames[i] : null;
            var paramName = string.IsNullOrEmpty(rawName) ? $"p{i + 1}" : rawName;
            var mode = (argModes != null && i < argModes.Length) ? argModes[i] : 'i';

            if (mode == 't')
            {
                resultColumns.Add(new ColumnDescriptor(
                    Name: paramName,
                    DataType: typeName,
                    MaxLength: null,
                    Precision: null,
                    Scale: null,
                    IsNullable: true,
                    CharUsed: null,
                    ColumnId: resultColumns.Count + 1));
                continue;
            }

            var direction = mode switch
            {
                'o' => ParameterDirection.Output,
                'b' => ParameterDirection.InputOutput,
                _ => ParameterDirection.Input, // 'i' and 'v' (VARIADIC is an input array)
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

        // pg_proc.pronargdefaults counts defaults on the trailing input arguments.
        var remaining = routine.NumDefaults;
        for (var i = parameters.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (parameters[i].Direction is ParameterDirection.Input or ParameterDirection.InputOutput)
            {
                parameters[i] = parameters[i] with { HasDefault = true };
                remaining--;
            }
        }

        return (parameters, resultColumns);
    }

    private static string TypeName(IReadOnlyDictionary<uint, string> typeMap, uint oid)
        => typeMap.TryGetValue(oid, out var name) ? name : $"oid_{oid}";

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
                ordinal_position,
                column_default
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
                DataDefault: reader.IsDBNull(7) ? null : reader.GetString(7),
                ColumnId: ordinal));
        }

        return columns;
    }

    /// <summary>
    /// Reads all tables', views' and materialized views' columns in the schema, grouped by relation name.
    /// Keys are the exact (case-sensitive) catalog names compared ordinally, because PostgreSQL can hold
    /// <c>"Orders"</c> and <c>orders</c> side by side; callers fold references with
    /// <see cref="SchemaObjectName.Canonical(string?, string?)"/> only as a fallback.
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
                ordinal_position,
                column_default
            FROM information_schema.columns
            WHERE table_schema = @schema
            ORDER BY table_name, ordinal_position";

        // information_schema.columns does not list materialized views; read them from pg_attribute.
        const string matviewSql = @"
            SELECT
                c.relname::text                                                        AS table_name,
                a.attname::text                                                        AS column_name,
                pg_catalog.format_type(a.atttypid, NULL)                               AS data_type,
                CASE WHEN a.atttypid IN (1042, 1043) AND a.atttypmod > 4
                     THEN a.atttypmod - 4 END                                          AS character_maximum_length,
                CASE WHEN a.atttypid = 1700 AND a.atttypmod > 4
                     THEN ((a.atttypmod - 4) >> 16) & 65535 END                        AS numeric_precision,
                CASE WHEN a.atttypid = 1700 AND a.atttypmod > 4
                     THEN (a.atttypmod - 4) & 65535 END                                AS numeric_scale,
                CASE WHEN a.attnotnull THEN 'NO' ELSE 'YES' END                        AS is_nullable,
                a.attnum::int                                                          AS ordinal_position,
                NULL::text                                                             AS column_default
            FROM pg_catalog.pg_matviews mv
            JOIN pg_catalog.pg_namespace n ON n.nspname = mv.schemaname
            JOIN pg_catalog.pg_class c ON c.relnamespace = n.oid AND c.relname = mv.matviewname
            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid
            WHERE mv.schemaname = @schema
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY 1, 8";

        var result = new Dictionary<string, List<ColumnDescriptor>>(StringComparer.Ordinal);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var statement in new[] { sql, matviewSql })
        {
            await using var command = new NpgsqlCommand(statement, connection);
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
                    DataDefault: reader.IsDBNull(8) ? null : reader.GetString(8),
                    ColumnId: reader.IsDBNull(7) ? 0 : reader.GetInt32(7));

                if (!result.TryGetValue(tableName, out var list))
                {
                    list = new List<ColumnDescriptor>();
                    result[tableName] = list;
                }

                list.Add(column);
            }
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
            new DatabaseTableDescriptor(kvp.Key, kvp.Value, _schema)).ToList();

        return new DatabaseSchemaDescriptor(
            Id: $"postgres:{_schema}",
            Tables: tables,
            LengthSemantics: "CHAR"); // PostgreSQL always uses character semantics
    }
}

/// <summary>
/// One pg_proc row as read by <see cref="PostgreSqlStoredProcedureParser"/>.
/// </summary>
public sealed class PostgreSqlRoutineInfo
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

    /// <summary>Gets <c>pronargdefaults</c>: the number of trailing input arguments that have defaults.</summary>
    public int NumDefaults { get; init; }

    /// <summary>Gets <c>proretset</c> (SETOF / RETURNS TABLE).</summary>
    public bool ReturnsSet { get; init; }
}
