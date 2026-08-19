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
            WHERE owner = :owner
              AND package_name = :packageName
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
        command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = packageName;
        command.Parameters.Add("procedureName", OracleDbType.Varchar2).Value = procedureName;
        if (sequence.HasValue)
        {
            command.Parameters.Add("sequence", OracleDbType.Int32).Value = sequence.Value;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var inOut = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var dataType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var dataLength = reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3);
            var precision = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4);
            var scale = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var position = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
            var seq = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
            var overload = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
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
                TypeSubname: typeSubname
            ));
        }

        return parameters;
    }

    /// <summary>
    /// Gets all overloads for a procedure.
    /// </summary>
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
                position
            FROM all_arguments
            WHERE owner = :owner
              AND package_name = :packageName
              AND object_name = :procedureName
            ORDER BY sequence, overload, position";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = packageName;
        command.Parameters.Add("procedureName", OracleDbType.Varchar2).Value = procedureName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        var currentOverload = new ProcedureOverloadInfo();
        int? lastSequence = null;
        int? lastOverload = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            var seq = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var overload = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

            if (lastSequence != seq || lastOverload != overload)
            {
                if (currentOverload.Parameters.Count > 0)
                {
                    overloads.Add(currentOverload);
                }
                currentOverload = new ProcedureOverloadInfo
                {
                    Sequence = seq,
                    Overload = overload
                };
                lastSequence = seq;
                lastOverload = overload;
            }

            var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var inOut = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var dataType = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var dataLength = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5);
            var precision = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);
            var scale = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7);
            var position = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);

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
                Sequence: seq
            ));
        }

        if (currentOverload.Parameters.Count > 0)
        {
            overloads.Add(currentOverload);
        }

        return overloads;
    }

    private static string BuildFullTypeName(string? typeOwner, string? typeName, string? typeSubname, string fallback)
    {
        if (!string.IsNullOrEmpty(typeName))
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(typeOwner)) parts.Add(typeOwner);
            parts.Add(typeName);
            if (!string.IsNullOrEmpty(typeSubname)) parts.Add(typeSubname);
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
    public List<ParameterDescriptor> Parameters { get; init; } = new();
    
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
            WHERE owner = :owner
              AND table_name = :tableName
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
                ColumnId: columnId
            ));
        }

        return columns;
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
            AllParameters = parameters
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
            info.Edition = "Enterprise";
        else if (banner.Contains("Standard", StringComparison.OrdinalIgnoreCase))
            info.Edition = "Standard";
        else if (banner.Contains("Express", StringComparison.OrdinalIgnoreCase) || banner.Contains("XE", StringComparison.OrdinalIgnoreCase))
            info.Edition = "Express (XE)";
        else if (banner.Contains("Personal", StringComparison.OrdinalIgnoreCase))
            info.Edition = "Personal";

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
    public Dictionary<string, string> AllParameters { get; init; } = new();
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
        CancellationToken cancellationToken = default)
    {
        var columns = new List<ColumnDescriptor>();

        // Build PL/SQL block to describe ref cursor
        var paramNames = string.Join(", ", sampleParameters.Keys.Select(k => $":{k}"));
        var paramDeclarations = string.Join(", ", sampleParameters.Keys.Select((k, i) => $"{k} IN OUT SYS_REFCURSOR"));
        
        var plsql = $@"
            DECLARE
                v_cursor SYS_REFCURSOR;
                v_col_cnt INTEGER;
                v_desc DBMS_SQL.DESC_TAB;
                v_sql VARCHAR2(32767);
            BEGIN
                v_sql := 'BEGIN {packageName}.{procedureName}({paramNames}); END;';
                EXECUTE IMMEDIATE v_sql USING {paramNames};
                -- In practice, would use DBMS_SQL.TO_CURSOR_NUMBER and DBMS_SQL.DESCRIBE_COLUMNS
                -- This is a placeholder for the actual implementation
            END;";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Actual implementation would:
        // 1. Open a cursor with DBMS_SQL.OPEN_CURSOR
        // 2. Parse the PL/SQL block
        // 3. Bind variables
        // 4. Execute
        // 5. Use DBMS_SQL.DESCRIBE_COLUMNS to get column metadata
        // 6. Convert to ColumnDescriptor list

        return columns;
    }
}