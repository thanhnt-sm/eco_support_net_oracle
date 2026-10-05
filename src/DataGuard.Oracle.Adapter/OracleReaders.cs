namespace DataGuard.Oracle.Adapter;

using System.Text;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using global::Oracle.ManagedDataAccess.Client;
using System.Data;

/// <summary>
/// Reads stored procedure parameters from the Oracle <c>ALL_ARGUMENTS</c> view (plus <c>ALL_PROCEDURES</c> for
/// subprograms that have no argument rows). Packaged subprograms and overloads are keyed by
/// <c>(package_name, object_name, subprogram_id)</c>; <c>ALL_PROCEDURES</c> has no <c>PACKAGE_NAME</c> column
/// (packaged subprograms are rows with <c>OBJECT_TYPE = 'PACKAGE'</c> and <c>PROCEDURE_NAME</c> set).
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
    /// Reads every callable subprogram of <paramref name="owner"/> with one statement over <c>ALL_ARGUMENTS</c>
    /// (<c>DATA_LEVEL = 0</c>) united with <c>ALL_PROCEDURES</c> header rows, grouped by
    /// <c>(package_name, object_name, subprogram_id)</c>. 0-argument subprograms and every overload are kept;
    /// <c>DEFAULTED = 'Y'</c> sets <see cref="ParameterDescriptor.HasDefault"/>; the function return row becomes
    /// <see cref="OracleProcedureCatalogEntry.ReturnType"/>.
    /// </summary>
    /// <param name="owner">Schema owner (compared upper-cased).</param>
    /// <param name="packageFilter">Null = every package and standalone unit; empty = standalone units only; otherwise that package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog entries.</returns>
    public Task<IReadOnlyList<OracleProcedureCatalogEntry>> GetProceduresAsync(
        string owner,
        string? packageFilter = null,
        CancellationToken cancellationToken = default)
        => QueryCatalogAsync(owner, packageFilter, null, cancellationToken);

    /// <summary>
    /// Gets parameters for a specific procedure. An empty <paramref name="packageName"/> means a standalone procedure
    /// (<c>package_name IS NULL</c>). With overloads, all overloads' parameters are returned unless
    /// <paramref name="sequence"/> selects one <c>SUBPROGRAM_ID</c>.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<IReadOnlyList<ParameterDescriptor>> GetParametersAsync(
        string owner,
        string packageName,
        string procedureName,
        int? sequence = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await QueryCatalogAsync(owner, packageName ?? string.Empty, procedureName, cancellationToken);
        return entries
            .Where(entry => sequence is null || entry.SubprogramId == sequence.Value)
            .SelectMany(entry => entry.Parameters)
            .ToList();
    }

    /// <summary>
    /// Lists procedure/function names from <c>ALL_PROCEDURES</c>: the subprograms of <paramref name="packageName"/>
    /// (<c>OBJECT_TYPE = 'PACKAGE'</c>, <c>PROCEDURE_NAME</c>), or standalone procedures/functions when it is null or empty.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<IReadOnlyList<string>> GetProcedureNamesAsync(
        string owner,
        string? packageName = null,
        CancellationToken cancellationToken = default)
    {
        var packaged = !string.IsNullOrEmpty(packageName);
        var sql = packaged
            ? """
              SELECT DISTINCT procedure_name
              FROM all_procedures
              WHERE owner = UPPER(:owner)
                AND object_type = 'PACKAGE'
                AND UPPER(object_name) = UPPER(:packageName)
                AND procedure_name IS NOT NULL
              ORDER BY procedure_name
              """
            : """
              SELECT DISTINCT object_name
              FROM all_procedures
              WHERE owner = UPPER(:owner)
                AND object_type IN ('PROCEDURE', 'FUNCTION')
              ORDER BY object_name
              """;

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.BindByName = true;
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        if (packaged)
        {
            command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = packageName;
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

    /// <summary>
    /// Gets all overloads (one per <c>SUBPROGRAM_ID</c>, including 0-argument overloads) of a procedure. An empty
    /// <paramref name="packageName"/> means a standalone procedure.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<IReadOnlyList<ProcedureOverloadInfo>> GetOverloadsAsync(
        string owner,
        string packageName,
        string procedureName,
        CancellationToken cancellationToken = default)
    {
        var entries = await QueryCatalogAsync(owner, packageName ?? string.Empty, procedureName, cancellationToken);
        return entries
            .Select(entry => new ProcedureOverloadInfo
            {
                Sequence = 0,
                Overload = entry.SubprogramId,
                SubprogramId = entry.SubprogramId,
                ReturnType = entry.ReturnType,
                Parameters = entry.Parameters.ToList(),
            })
            .ToList();
    }

    /// <summary>Builds the single catalog statement for the given filters (exposed for tests).</summary>
    /// <param name="packageFilter">Null = all; empty = standalone only; otherwise one package.</param>
    /// <param name="filterByName">Whether a <c>:procedureName</c> filter is applied.</param>
    /// <returns>The SQL text.</returns>
    public static string BuildCatalogSql(string? packageFilter, bool filterByName)
    {
        var argumentFilter = new StringBuilder();
        var headerFilter = new StringBuilder();
        if (packageFilter is not null)
        {
            if (packageFilter.Length == 0)
            {
                argumentFilter.Append(" AND a.package_name IS NULL");
                headerFilter.Append(" AND p.object_type IN ('PROCEDURE', 'FUNCTION')");
            }
            else
            {
                argumentFilter.Append(" AND UPPER(a.package_name) = UPPER(:packageName)");
                headerFilter.Append(" AND p.object_type = 'PACKAGE' AND UPPER(p.object_name) = UPPER(:packageName)");
            }
        }

        if (filterByName)
        {
            argumentFilter.Append(" AND UPPER(a.object_name) = UPPER(:procedureName)");
            headerFilter.Append(" AND UPPER(NVL(p.procedure_name, p.object_name)) = UPPER(:procedureName)");
        }

        return $"""
            SELECT 'A' AS row_kind, a.package_name, a.object_name, a.subprogram_id, a.overload, a.position, a.sequence,
                   a.argument_name, a.in_out, a.data_type, a.data_length, a.data_precision, a.data_scale,
                   a.char_used, a.char_length, a.defaulted, a.type_owner, a.type_name, a.type_subname,
                   CAST(NULL AS VARCHAR2(23)) AS object_type
            FROM all_arguments a
            WHERE a.owner = UPPER(:owner)
              AND a.data_level = 0{argumentFilter}
            UNION ALL
            SELECT 'P', CASE WHEN p.object_type = 'PACKAGE' THEN p.object_name END, NVL(p.procedure_name, p.object_name),
                   p.subprogram_id, p.overload, NULL, NULL,
                   NULL, NULL, NULL, NULL, NULL, NULL,
                   NULL, NULL, NULL, NULL, NULL, NULL,
                   p.object_type
            FROM all_procedures p
            WHERE p.owner = UPPER(:owner)
              AND (p.object_type IN ('PROCEDURE', 'FUNCTION') OR (p.object_type = 'PACKAGE' AND p.procedure_name IS NOT NULL)){headerFilter}
            ORDER BY 2 NULLS FIRST, 3, 4, 1 DESC, 6
            """;
    }

    private async Task<IReadOnlyList<OracleProcedureCatalogEntry>> QueryCatalogAsync(
        string owner,
        string? packageFilter,
        string? procedureName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        var normalizedPackage = packageFilter?.Trim();
        var sql = BuildCatalogSql(normalizedPackage, procedureName is not null);

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new OracleCommand(sql, connection);
        command.BindByName = true;
        command.Parameters.Add("owner", OracleDbType.Varchar2).Value = owner;
        if (!string.IsNullOrEmpty(normalizedPackage))
        {
            command.Parameters.Add("packageName", OracleDbType.Varchar2).Value = normalizedPackage;
        }

        if (procedureName is not null)
        {
            command.Parameters.Add("procedureName", OracleDbType.Varchar2).Value = procedureName;
        }

        var rows = new List<OracleArgumentRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OracleArgumentRow(
                PackageName: GetString(reader, 1),
                ObjectName: GetString(reader, 2) ?? string.Empty,
                SubprogramId: GetInt(reader, 3) ?? 0,
                Overload: reader.IsDBNull(4) ? null : Convert.ToString(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture),
                Position: GetInt(reader, 5),
                Sequence: GetInt(reader, 6),
                ArgumentName: GetString(reader, 7),
                InOut: GetString(reader, 8),
                DataType: GetString(reader, 9),
                DataLength: GetInt(reader, 10),
                DataPrecision: GetInt(reader, 11),
                DataScale: GetInt(reader, 12),
                CharUsed: GetString(reader, 13),
                CharLength: GetInt(reader, 14),
                Defaulted: GetString(reader, 15),
                TypeOwner: GetString(reader, 16),
                TypeName: GetString(reader, 17),
                TypeSubname: GetString(reader, 18),
                IsHeader: string.Equals(GetString(reader, 0), "P", StringComparison.Ordinal),
                ObjectType: GetString(reader, 19)));
        }

        return OracleCatalog.Group(owner, rows);
    }

    private static string? GetString(OracleDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    private static int? GetInt(OracleDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Information about a procedure overload.
/// </summary>
public sealed class ProcedureOverloadInfo
{
    public int Sequence { get; init; }

    /// <summary>Gets the overload discriminator; equals <see cref="SubprogramId"/>.</summary>
    public int Overload { get; init; }

    /// <summary>Gets the ALL_ARGUMENTS SUBPROGRAM_ID of this overload.</summary>
    public int SubprogramId { get; init; }

    /// <summary>Gets the function return type, or null for a procedure.</summary>
    public string? ReturnType { get; init; }

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
            "B" or "BYTE" => "B",
            "C" or "CHAR" => "C",
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
    /// Gets the NLS facts relevant to length checks. <c>NLS_LENGTH_SEMANTICS</c>, <c>NLS_LANGUAGE</c> and
    /// <c>NLS_TERRITORY</c> come from <c>nls_session_parameters</c>; the character sets exist only in
    /// <c>nls_database_parameters</c>; <c>MAX_STRING_SIZE</c> is read from <c>v$parameter</c> when the account can see it
    /// (otherwise <c>STANDARD</c>, the conservative 4000-byte limit).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<NlsParameters> GetNlsParametersAsync(CancellationToken cancellationToken = default)
    {
        const string sessionSql = @"
            SELECT parameter, value
            FROM nls_session_parameters
            WHERE parameter IN ('NLS_LENGTH_SEMANTICS', 'NLS_LANGUAGE', 'NLS_TERRITORY')";
        const string databaseSql = @"
            SELECT parameter, value
            FROM nls_database_parameters
            WHERE parameter IN ('NLS_CHARACTERSET', 'NLS_NCHAR_CHARACTERSET')";
        const string maxStringSizeSql = "SELECT value FROM v$parameter WHERE name = 'max_string_size'";

        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sql in new[] { sessionSql, databaseSql })
        {
            await using var command = new OracleCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var param = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                parameters[param] = value;
            }
        }

        var maxStringSize = "STANDARD";
        try
        {
            await using var command = new OracleCommand(maxStringSizeSql, connection);
            if (await command.ExecuteScalarAsync(cancellationToken) is string value && !string.IsNullOrWhiteSpace(value))
            {
                maxStringSize = value.Trim().ToUpperInvariant();
            }
        }
        catch (OracleException)
        {
            // ORA-00942 without SELECT on v$parameter: keep the conservative STANDARD limit.
        }

        parameters["MAX_STRING_SIZE"] = maxStringSize;
        return new NlsParameters
        {
            LengthSemantics = string.Equals(parameters.GetValueOrDefault("NLS_LENGTH_SEMANTICS"), "CHAR", StringComparison.OrdinalIgnoreCase)
                ? LengthSemantics.Char : LengthSemantics.Byte,
            CharacterSet = parameters.GetValueOrDefault("NLS_CHARACTERSET") ?? "UNKNOWN",
            NCharCharacterSet = parameters.GetValueOrDefault("NLS_NCHAR_CHARACTERSET") ?? "UNKNOWN",
            Language = parameters.GetValueOrDefault("NLS_LANGUAGE") ?? "UNKNOWN",
            Territory = parameters.GetValueOrDefault("NLS_TERRITORY") ?? "UNKNOWN",
            MaxStringSize = maxStringSize,
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
/// NLS session and database parameters.
/// </summary>
public sealed class NlsParameters
{
    public LengthSemantics LengthSemantics { get; init; }

    public string CharacterSet { get; init; } = "UNKNOWN";

    public string NCharCharacterSet { get; init; } = "UNKNOWN";

    public string Language { get; init; } = "UNKNOWN";

    public string Territory { get; init; } = "UNKNOWN";

    /// <summary>Gets the <c>MAX_STRING_SIZE</c> initialization parameter (STANDARD = 4000-byte VARCHAR2, EXTENDED = 32767).</summary>
    public string MaxStringSize { get; init; } = "STANDARD";

    public Dictionary<string, string> AllParameters { get; init; } = new();
}

/// <summary>
/// Describes REF CURSOR result sets using DBMS_SQL. <b>This executes the procedure or function</b> (inside an anonymous
/// PL/SQL block, with the supplied sample values bound to its IN parameters), so it is only used when
/// <see cref="DataGuard.Core.Models.OracleConfiguration.DescribeRefCursors"/> is explicitly enabled and the account is suitable.
/// </summary>
public class RefCursorDescriber
{
    private readonly string _connectionString;

    public RefCursorDescriber(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Executes <c>[owner.][package.]procedure</c> and describes the REF CURSOR it returns, either as a function result
    /// (<paramref name="refCursorParameterName"/> null) or through the named OUT parameter.
    /// </summary>
    /// <param name="owner">Schema owner; empty to rely on the session's current schema.</param>
    /// <param name="packageName">Package name; empty for a standalone procedure/function.</param>
    /// <param name="procedureName">Procedure or function name.</param>
    /// <param name="sampleParameters">IN parameter values by formal parameter name (use <see cref="DBNull.Value"/> for NULL).</param>
    /// <param name="refCursorParameterName">OUT SYS_REFCURSOR parameter name, or null for a function returning a cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The described columns.</returns>
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
        if (!string.IsNullOrEmpty(owner))
        {
            ValidateIdentifier(owner, nameof(owner));
        }

        if (!string.IsNullOrEmpty(packageName))
        {
            ValidateIdentifier(packageName, nameof(packageName));
        }

        ValidateIdentifier(procedureName, nameof(procedureName));
        if (!string.IsNullOrEmpty(refCursorParameterName))
        {
            ValidateIdentifier(refCursorParameterName, nameof(refCursorParameterName));
        }

        var sampleNames = sampleParameters.Keys.ToList();
        foreach (var key in sampleNames)
        {
            ValidateIdentifier(key, "sample parameter");
        }

        // Bind samples as :a0, :a1, ... so a formal parameter name can never collide with the describe binds.
        var paramNames = string.Join(", ", sampleNames.Select((k, i) => $"{k} => :a{i}"));
        var target = string.Join(".", new[] { owner, packageName, procedureName }.Where(part => !string.IsNullOrEmpty(part)));

        // PL/SQL block: call the function/procedure that returns a SYS_REFCURSOR
        // (either as a FUNCTION return value or through an OUT SYS_REFCURSOR
        // parameter), then describe the result set with DBMS_SQL.DESCRIBE_COLUMNS3.
        var invocation = string.IsNullOrEmpty(refCursorParameterName)
            ? $"v_cursor := {target}({paramNames});"
            : $"{target}({refCursorParameterName} => v_cursor{(paramNames.Length > 0 ? ", " + paramNames : "")});";
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

        for (var i = 0; i < sampleNames.Count; i++)
        {
            command.Parameters.Add(new OracleParameter($"a{i}", sampleParameters[sampleNames[i]] ?? DBNull.Value));
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

        // ODP.NET returns OUT binds as provider types (OracleDecimal, OracleDecimal[], OracleString[]), not CLR ints/strings.
        var colCount = ToInt(cntParam.Value);
        var names = ToStrings(command.Parameters["names"].Value);
        var types = ToInts(command.Parameters["types"].Value);
        var maxlens = ToInts(command.Parameters["maxlens"].Value);
        var precisions = ToInts(command.Parameters["precisions"].Value);
        var scales = ToInts(command.Parameters["scales"].Value);
        var nullables = ToInts(command.Parameters["nullables"].Value);
        var charsetforms = ToInts(command.Parameters["charsetforms"].Value);

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

    private static int ToInt(object? value) => value switch
    {
        null or DBNull => 0,
        global::Oracle.ManagedDataAccess.Types.OracleDecimal oracle => oracle.IsNull ? 0 : oracle.ToInt32(),
        IConvertible convertible => convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture),
        _ => 0,
    };

    private static int[] ToInts(object? value)
        => value is Array array ? array.Cast<object?>().Select(ToInt).ToArray() : Array.Empty<int>();

    private static string[] ToStrings(object? value)
        => value is Array array
            ? array.Cast<object?>().Select(item => item switch
            {
                global::Oracle.ManagedDataAccess.Types.OracleString oracle => oracle.IsNull ? string.Empty : oracle.Value,
                null or DBNull => string.Empty,
                _ => Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            }).ToArray()
            : Array.Empty<string>();

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
