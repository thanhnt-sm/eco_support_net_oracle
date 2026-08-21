namespace DataGuard.Oracle.Adapter;

using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using global::Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Security.Cryptography;

/// <summary>
/// Reads stored procedure parameters from Oracle ALL_ARGUMENTS view.
/// Handles overloaded procedures by including sequence and overload info in the key.
/// </summary>
public class AllArgumentsReader
{
    private readonly string _connectionString;
    private readonly DataGuard.Core.Models.OracleConfiguration _config;
    private readonly DataGuard.Core.Security.IAuditLogger? _auditLogger;

    public AllArgumentsReader(string connectionString, DataGuard.Core.Models.OracleConfiguration config, DataGuard.Core.Security.IAuditLogger? auditLogger = null)
    {
        _connectionString = connectionString;
        _config = config;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Gets parameters for a specific procedure, handling overloads via sequence/overload.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<IReadOnlyList<ParameterDescriptor>> GetParametersAsync(
        string owner,
        string packageName,
        string procedureName,
        int? sequence = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<ParameterDescriptor>();

        // Include SEQUENCE and OVERLOAD to handle overloaded procedures
        // SEQUENCE: ordering within overload group
        // OVERLOAD: unique identifier for each overload (0 = not overloaded)
        var sql = @"
            SELECT 
                argument_name,
                in_out,
                data_type,
                data_length,
                data_precision,
                data_scale,
                position,
                sequence,
                overload,
                type_owner,
                type_name,
                type_subname
            FROM all_arguments
            WHERE owner = UPPER(:owner)
              AND (@packageName IS NULL OR package_name = :packageName)
              AND object_name = :procedureName";

        if (sequence.HasValue)
        {
            sql += " AND sequence = :sequence";
        }

        sql += " ORDER BY sequence, position";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = string.IsNullOrEmpty(packageName) ? DBNull.Value : packageName;
        command.Parameters.Add("procedureName", OracleDbType.Varchar2).Value = procedureName;
        if (sequence.HasValue)
        {
            command.Parameters.Add("sequence", OracleDbType.Int32).Value = sequence.Value;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var position = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
            if (reader.IsDBNull(0) || position == 0)
            {
                continue; // function return-value row - not a real parameter
            }

            var name = reader.GetString(0);
            var inOut = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var dataLength = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3);
            var precision = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4);
            var scale = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var seq = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
            var overload = ReadOverload(reader, 8);
            var typeOwner = reader.IsDBNull(9) ? null : reader.GetString(9);
            var typeName = reader.IsDBNull(10) ? null : reader.GetString(10);
            var typeSubname = reader.IsDBNull(11) ? null : reader.GetString(11);

            var direction = inOut switch
            {
                "IN" => DataGuard.Core.Abstractions.ParameterDirection.Input,
                "OUT" => DataGuard.Core.Abstractions.ParameterDirection.Output,
                "IN OUT" => DataGuard.Core.Abstractions.ParameterDirection.InputOutput,
                _ => DataGuard.Core.Abstractions.ParameterDirection.Input
            };

            // Build type name including owner for user-defined types
            var fullTypeName = BuildFullTypeName(typeOwner, typeName, typeSubname, dataType);

            parameters.Add(new ParameterDescriptor(
                Name: name,
                DataType: fullTypeName,
                Direction: direction,
                MaxLength: dataLength,
                Precision: precision,
                Scale: scale,
                IsNullable: true, // ALL_ARGUMENTS doesn't track nullability
                OrdinalPosition: position,
                Overload: overload,
                Sequence: seq,
                TypeOwner: typeOwner,
                TypeName: typeName,
                TypeSubname: typeSubname));
        }

        return parameters;
    }

    /// <summary>
    /// Gets all overloads for a procedure.
    /// </summary>
    /// <summary>
    /// Lists distinct procedure/function names in a schema (from ALL_PROCEDURES).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<IReadOnlyList<string>> GetProcedureNamesAsync(
        string owner,
        string? packageName = null,
        CancellationToken cancellationToken = default)
    {
        var sql = "SELECT DISTINCT object_name FROM all_procedures WHERE owner = UPPER(:owner)";
        if (!string.IsNullOrEmpty(packageName))
        {
            sql += " AND package_name = UPPER(:packageName)";
        }

        sql += " ORDER BY object_name";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        if (!string.IsNullOrEmpty(packageName))
        {
            command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = string.IsNullOrEmpty(packageName) ? DBNull.Value : packageName;
        }

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }

    public async Task<IReadOnlyList<ProcedureOverloadInfo>> GetOverloadsAsync(
        string owner,
        string packageName,
        string procedureName,
        CancellationToken cancellationToken = default)
    {
        var overloads = new List<ProcedureOverloadInfo>();

        const string sql = @"
            SELECT DISTINCT 
                sequence,
                overload,
                argument_name,
                in_out,
                data_type,
                data_length,
                data_precision,
                data_scale,
                position,
                subprogram_id
            FROM all_arguments
            WHERE owner = UPPER(:owner)
              AND (@packageName IS NULL OR package_name = :packageName)
              AND object_name = :procedureName
            ORDER BY overload, sequence, position";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = string.IsNullOrEmpty(packageName) ? DBNull.Value : packageName;
        command.Parameters.Add("procedureName", OracleDbType.Varchar2).Value = procedureName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var currentOverload = new ProcedureOverloadInfo();
        int? lastOverload = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            var seq = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var overload = ReadOverload(reader, 1);

            // SUBPROGRAM_ID is NUMBER and unique per overload; fall back to the
            // (possibly string-typed) OVERLOAD column when it is null.
            var groupKey = reader.IsDBNull(9) ? overload : reader.GetInt32(9);

            if (lastOverload != groupKey)
            {
                if (currentOverload.Parameters.Count > 0)
                {
                    overloads.Add(currentOverload);
                }

                currentOverload = new ProcedureOverloadInfo
                {
                    Sequence = seq,
                    Overload = groupKey,
                };
                lastOverload = groupKey;
            }

            var position = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
            if (reader.IsDBNull(2) || position == 0)
            {
                continue; // function return-value row - not a real parameter
            }

            var name = reader.GetString(2);
            var inOut = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var dataType = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var dataLength = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var precision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var scale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7);

            var direction = inOut switch
            {
                "IN" => DataGuard.Core.Abstractions.ParameterDirection.Input,
                "OUT" => DataGuard.Core.Abstractions.ParameterDirection.Output,
                "IN OUT" => DataGuard.Core.Abstractions.ParameterDirection.InputOutput,
                _ => DataGuard.Core.Abstractions.ParameterDirection.Input
            };

            currentOverload.Parameters.Add(new ParameterDescriptor(
                Name: name,
                DataType: dataType,
                Direction: direction,
                MaxLength: dataLength,
                Precision: precision,
                Scale: scale,
                IsNullable: true,
                OrdinalPosition: position,
                Overload: overload,
                Sequence: seq));
        }

        if (currentOverload.Parameters.Count > 0)
        {
            overloads.Add(currentOverload);
        }

        return overloads;
    }

    /// <summary>
    /// Reads ALL_ARGUMENTS.OVERLOAD safely: the column is reported as NUMBER in some
    /// Oracle versions and VARCHAR2 in others, so convert defensively (0 = not overloaded).
    /// </summary>
    private static int ReadOverload(global::Oracle.ManagedDataAccess.Client.OracleDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        var raw = Convert.ToString(reader.GetValue(ordinal));
        return int.TryParse(raw, out var value) ? value : 0;
    }

    private static string BuildFullTypeName(string? typeOwner, string? typeName, string? typeSubname, string fallback)
    {
        if (!string.IsNullOrEmpty(typeName))
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(typeOwner))
            {
                parts.Add(typeOwner);
            }

            parts.Add(typeName);
            if (!string.IsNullOrEmpty(typeSubname))
            {
                parts.Add(typeSubname);
            }

            return string.Join(".", parts);
        }

        return fallback;
    }

    private static string ComputeConnectionHash(string connectionString)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(connectionString));
        return Convert.ToHexString(hash)[..16];
    }
}

/// <summary>
/// Information about a procedure overload.
/// </summary>
public sealed class ProcedureOverloadInfo
{
    public int Sequence { get; init; }

    public int Overload { get; init; }

    public List<ParameterDescriptor> Parameters { get; init; } = new ();

    public string SignatureKey => $"{Sequence}:{Overload}";
}

/// <summary>
/// Reads column metadata from Oracle ALL_TAB_COLUMNS view.
/// Includes char_used (B/C) for byte/char semantics handling.
/// </summary>
public class AllTabColumnsReader
{
    private readonly string _connectionString;

    public AllTabColumnsReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<ColumnDescriptor>> GetColumnsAsync(
        string owner,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var columns = new List<ColumnDescriptor>();

        // Include char_used (B=BYTE, C=CHAR) for length semantics
        // Include data_default for default values
        const string sql = @"
            SELECT 
                column_name,
                data_type,
                data_length,
                char_length,
                data_precision,
                data_scale,
                nullable,
                char_used,
                data_default,
                column_id
            FROM all_tab_columns
            WHERE owner = UPPER(:owner)
              AND table_name = UPPER(:tableName)
            ORDER BY column_id";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        command.Parameters.Add("tableName", OracleDbType.Varchar2).Value = tableName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var dataType = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var dataLength = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2);
            var charLength = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3);
            var precision = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4);
            var scale = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var nullable = reader.IsDBNull(6) ? "Y" : reader.GetString(6);
            var charUsed = reader.IsDBNull(7) ? null : reader.GetString(7);
            var dataDefault = reader.IsDBNull(8) ? null : reader.GetString(8);
            var columnId = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);

            // Normalize char_used: 'B' = BYTE, 'C' = CHAR, null = use NLS_LENGTH_SEMANTICS
            var normalizedCharUsed = NormalizeCharUsed(charUsed);

            columns.Add(new ColumnDescriptor(
                Name: name,
                DataType: dataType,
                MaxLength: dataLength,
                CharLength: charLength,
                Precision: precision,
                Scale: scale,
                IsNullable: nullable == "Y",
                CharUsed: normalizedCharUsed,
                DataDefault: dataDefault,
                ColumnId: columnId));
        }

        return columns;
    }

    /// <summary>
    /// Reads all tables' columns for an owner, grouped by table name.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<Dictionary<string, List<ColumnDescriptor>>> GetAllColumnsAsync(
        string owner,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<ColumnDescriptor>>(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
            SELECT table_name, column_name, data_type, data_length, char_length,
                   data_precision, data_scale, nullable, char_used, data_default, column_id
            FROM all_tab_columns
            WHERE owner = UPPER(:owner)
            ORDER BY table_name, column_id";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new OracleCommand(sql, connection);
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tableName = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var column = new ColumnDescriptor(
                Name: reader.IsDBNull(1) ? "" : reader.GetString(1),
                DataType: reader.IsDBNull(2) ? "" : reader.GetString(2),
                MaxLength: reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                CharLength: reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                Precision: reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                Scale: reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                IsNullable: !reader.IsDBNull(7) && reader.GetString(7) == "Y",
                CharUsed: NormalizeCharUsed(reader.IsDBNull(8) ? null : reader.GetString(8)),
                DataDefault: reader.IsDBNull(9) ? null : reader.GetString(9),
                ColumnId: reader.IsDBNull(10) ? 0 : reader.GetInt32(10));

            if (!result.TryGetValue(tableName, out var list))
            {
                list = new List<ColumnDescriptor>();
                result[tableName] = list;
            }

            list.Add(column);
        }

        return result;
    }

    private static string? NormalizeCharUsed(string? charUsed)
    {
        return charUsed?.ToUpperInvariant() switch
        {
            "B" => "BYTE",
            "C" => "CHAR",
            _ => charUsed // Keep as-is or null
        };
    }
}

/// <summary>
/// Reads NLS session parameters for length semantics and database version.
/// </summary>
public class NlsSessionReader
{
    private readonly string _connectionString;

    public NlsSessionReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<LengthSemantics> GetLengthSemanticsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT value
            FROM nls_session_parameters
            WHERE parameter = 'NLS_LENGTH_SEMANTICS'";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;

        return value == "CHAR" ? LengthSemantics.Char : LengthSemantics.Byte;
    }

    /// <summary>
    /// Gets database version information.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<DatabaseVersionInfo> GetDatabaseVersionAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT banner
            FROM v$version
            WHERE banner LIKE 'Oracle%' AND ROWNUM = 1";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        var banner = await command.ExecuteScalarAsync(cancellationToken) as string;

        return ParseVersionBanner(banner ?? "");
    }

    /// <summary>
    /// Gets all NLS parameters relevant to length semantics and character set.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<NlsParameters> GetNlsParametersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT parameter, value
            FROM nls_session_parameters
            WHERE parameter IN (
                'NLS_LENGTH_SEMANTICS',
                'NLS_CHARACTERSET',
                'NLS_NCHAR_CHARACTERSET',
                'NLS_LANGUAGE',
                'NLS_TERRITORY'
            )";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var parameters = new Dictionary<string, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var param = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
            parameters[param] = value;
        }

        return new NlsParameters
        {
            LengthSemantics = parameters.GetValueOrDefault("NLS_LENGTH_SEMANTICS") == "CHAR"
                ? LengthSemantics.Char : LengthSemantics.Byte,
            CharacterSet = parameters.GetValueOrDefault("NLS_CHARACTERSET") ?? "UNKNOWN",
            NCharCharacterSet = parameters.GetValueOrDefault("NLS_NCHAR_CHARACTERSET") ?? "UNKNOWN",
            Language = parameters.GetValueOrDefault("NLS_LANGUAGE") ?? "UNKNOWN",
            Territory = parameters.GetValueOrDefault("NLS_TERRITORY") ?? "UNKNOWN",
            AllParameters = parameters,
        };
    }

    private static DatabaseVersionInfo ParseVersionBanner(string banner)
    {
        // Parse Oracle banner like "Oracle Database 19c Enterprise Edition Release 19.0.0.0.0 - Production"
        var info = new DatabaseVersionInfo { Banner = banner };

        // Extract version number
        var versionMatch = System.Text.RegularExpressions.Regex.Match(banner, @"Release\s+(\d+\.\d+\.\d+\.\d+\.\d+)");
        if (versionMatch.Success)
        {
            info.Version = versionMatch.Groups[1].Value;
        }
        else
        {
            // Try alternative pattern
            versionMatch = System.Text.RegularExpressions.Regex.Match(banner, @"(\d+\.\d+\.\d+\.\d+\.\d+)");
            if (versionMatch.Success)
            {
                info.Version = versionMatch.Groups[1].Value;
            }
        }

        // Extract edition
        if (banner.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            info.Edition = "Enterprise";
        }
        else if (banner.Contains("Standard", StringComparison.OrdinalIgnoreCase))
        {
            info.Edition = "Standard";
        }
        else if (banner.Contains("Express", StringComparison.OrdinalIgnoreCase) || banner.Contains("XE", StringComparison.OrdinalIgnoreCase))
        {
            info.Edition = "Express (XE)";
        }
        else if (banner.Contains("Personal", StringComparison.OrdinalIgnoreCase))
        {
            info.Edition = "Personal";
        }

        return info;
    }
}

/// <summary>
/// Database version information.
/// </summary>
public sealed class DatabaseVersionInfo
{
    public string Version { get; set; } = "unknown";

    public string Edition { get; set; } = "unknown";

    public string Banner { get; set; } = "";

    public override string ToString() => $"{Edition} {Version}";
}

/// <summary>
/// NLS session parameters.
/// </summary>
public sealed class NlsParameters
{
    public LengthSemantics LengthSemantics { get; init; }

    public string CharacterSet { get; init; } = "UNKNOWN";

    public string NCharCharacterSet { get; init; } = "UNKNOWN";

    public string Language { get; init; } = "UNKNOWN";

    public string Territory { get; init; } = "UNKNOWN";

    public Dictionary<string, string> AllParameters { get; init; } = new ();
}

/// <summary>
/// Describes REF CURSOR result sets using DBMS_SQL.
/// </summary>
public class RefCursorDescriber
{
    private readonly string _connectionString;

    public RefCursorDescriber(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<ColumnDescriptor>> DescribeRefCursorAsync(
        string owner,
        string packageName,
        string procedureName,
        IReadOnlyDictionary<string, object> sampleParameters,
        string? refCursorParameterName = null,
        CancellationToken cancellationToken = default)
    {
        // Named PL/SQL notation so the OUT cursor can sit at any parameter position.
        // Identifiers are interpolated into PL/SQL, so reject anything that is not
        // a plain Oracle identifier (bind parameters only protect values).
        ValidateIdentifier(packageName, nameof(packageName));
        ValidateIdentifier(procedureName, nameof(procedureName));
        if (!string.IsNullOrEmpty(refCursorParameterName))
        {
            ValidateIdentifier(refCursorParameterName, nameof(refCursorParameterName));
        }

        foreach (var key in sampleParameters.Keys)
        {
            ValidateIdentifier(key, "sample parameter");
        }

        var paramNames = string.Join(", ", sampleParameters.Keys.Select(k => $"{k} => :{k}"));

        // PL/SQL block: call the function/procedure that returns a SYS_REFCURSOR
        // (either as a FUNCTION return value or through an OUT SYS_REFCURSOR
        // parameter), then describe the result set with DBMS_SQL.DESCRIBE_COLUMNS3.
        var invocation = string.IsNullOrEmpty(refCursorParameterName)
            ? $"v_cursor := {packageName}.{procedureName}({paramNames});"
            : $"{packageName}.{procedureName}({refCursorParameterName} => :cursor_out{(paramNames.Length > 0 ? ", " + paramNames : "")});" + "\n    v_cursor := :cursor_out;";
        var plsql = $@"
DECLARE
    v_cursor SYS_REFCURSOR;
    v_cursor_id INTEGER;
    v_col_cnt INTEGER;
    v_desc DBMS_SQL.DESC_TAB3;
BEGIN
    {invocation}
    v_cursor_id := DBMS_SQL.TO_CURSOR_NUMBER(v_cursor);
    DBMS_SQL.DESCRIBE_COLUMNS3(v_cursor_id, v_col_cnt, v_desc);
    :cnt := v_col_cnt;
    FOR i IN 1..v_col_cnt LOOP
        :names(i) := v_desc(i).col_name;
        :types(i) := v_desc(i).col_type;
        :maxlens(i) := v_desc(i).col_max_len;
        :precisions(i) := v_desc(i).col_precision;
        :scales(i) := v_desc(i).col_scale;
        :nullables(i) := CASE WHEN v_desc(i).col_null_ok THEN 1 ELSE 0 END;
        :charsetforms(i) := NVL(v_desc(i).col_charsetform, 1);
    END LOOP;
    DBMS_SQL.CLOSE_CURSOR(v_cursor_id);
END;";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = plsql;

        // Named notation is used in the PL/SQL block; bind by name, not position.
        command.BindByName = true;
        command.CommandType = CommandType.Text;

        foreach (var param in sampleParameters)
        {
            command.Parameters.Add(new OracleParameter(param.Key, param.Value));
        }

        if (!string.IsNullOrEmpty(refCursorParameterName))
        {
            command.Parameters.Add(new OracleParameter("cursor_out", OracleDbType.RefCursor)
            {
                Direction = System.Data.ParameterDirection.Output,
            });
        }

        const int MaxColumns = 1000;

        var cntParam = new OracleParameter("cnt", OracleDbType.Int32) { Direction = System.Data.ParameterDirection.Output };
        command.Parameters.Add(cntParam);

        OracleParameter NewArrayParam(string name, OracleDbType dbType, int bindSize)
        {
            return new OracleParameter(name, dbType)
            {
                Direction = System.Data.ParameterDirection.Output,
                CollectionType = OracleCollectionType.PLSQLAssociativeArray,
                Size = MaxColumns,
                ArrayBindSize = Enumerable.Repeat(bindSize, MaxColumns).ToArray(),
            };
        }

        command.Parameters.Add(NewArrayParam("names", OracleDbType.Varchar2, 32767));
        command.Parameters.Add(NewArrayParam("types", OracleDbType.Int32, sizeof(int)));
        command.Parameters.Add(NewArrayParam("maxlens", OracleDbType.Int32, sizeof(int)));
        command.Parameters.Add(NewArrayParam("precisions", OracleDbType.Int32, sizeof(int)));
        command.Parameters.Add(NewArrayParam("scales", OracleDbType.Int32, sizeof(int)));
        command.Parameters.Add(NewArrayParam("nullables", OracleDbType.Int32, sizeof(int)));
        command.Parameters.Add(NewArrayParam("charsetforms", OracleDbType.Int32, sizeof(int)));

        await command.ExecuteNonQueryAsync(cancellationToken);

        var colCount = Convert.ToInt32(cntParam.Value);
        var names = (string[])command.Parameters["names"].Value;
        var types = (int[])command.Parameters["types"].Value;
        var maxlens = (int[])command.Parameters["maxlens"].Value;
        var precisions = (int[])command.Parameters["precisions"].Value;
        var scales = (int[])command.Parameters["scales"].Value;
        var nullables = (int[])command.Parameters["nullables"].Value;
        var charsetforms = (int[])command.Parameters["charsetforms"].Value;

        var columns = new List<ColumnDescriptor>(colCount);
        for (var i = 0; i < colCount; i++)
        {
            columns.Add(new ColumnDescriptor(
                names[i],
                MapOracleDbType(types[i], charsetforms[i]),
                maxlens[i] > 0 ? maxlens[i] : null,
                precisions[i] > 0 ? precisions[i] : null,
                scales[i] >= 0 ? scales[i] : null,
                nullables[i] == 1,
                null,
                null));
        }

        return columns;
    }

    private static void ValidateIdentifier(string identifier, string what)
    {
        if (string.IsNullOrEmpty(identifier) ||
            !System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[A-Za-z_][A-Za-z0-9_$#]*$"))
        {
            throw new ArgumentException($"Invalid Oracle identifier for {what}: '{identifier}'");
        }
    }

    private static string MapOracleDbType(int dbmsSqlType, int charsetForm = 1)
    {
        // DBMS_SQL col_type codes -> Oracle type names. charsetform=2 means the
        // column uses the National character set (NVARCHAR2/NCHAR/NCLOB); the raw
        // code alone cannot distinguish them from VARCHAR2/CHAR/CLOB.
        var national = charsetForm == 2;
        return dbmsSqlType switch
        {
            1 => national ? "NVARCHAR2" : "VARCHAR2",
            2 => "NUMBER",
            8 => "LONG",
            12 => "DATE",
            23 => "RAW",
            24 => "LONG RAW",
            96 => national ? "NCHAR" : "CHAR",
            100 => "BINARY_FLOAT",
            101 => "BINARY_DOUBLE",
            112 => national ? "NCLOB" : "CLOB",
            113 => "BLOB",
            114 => "BFILE",
            180 => "TIMESTAMP",
            181 => "TIMESTAMP WITH TIME ZONE",
            182 => "INTERVAL YEAR TO MONTH",
            183 => "INTERVAL DAY TO SECOND",
            231 => "TIMESTAMP WITH LOCAL TIME ZONE",
            _ => "UNKNOWN"
        };
    }
}