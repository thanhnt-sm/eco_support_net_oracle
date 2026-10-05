namespace DataGuard.Oracle.Adapter;

using System.Globalization;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;

/// <summary>
/// One callable Oracle subprogram (standalone procedure/function or packaged subprogram overload) read from
/// <c>ALL_ARGUMENTS</c> + <c>ALL_PROCEDURES</c>. Overloads are distinct entries keyed by <see cref="SubprogramId"/>.
/// </summary>
/// <param name="Owner">Schema owner (upper case as stored in the dictionary).</param>
/// <param name="PackageName">Package name, or null for a standalone procedure/function.</param>
/// <param name="Name">Subprogram name.</param>
/// <param name="SubprogramId">ALL_ARGUMENTS/ALL_PROCEDURES <c>SUBPROGRAM_ID</c>; unique per overload inside a package (1 for standalone units).</param>
/// <param name="Overload">ALL_ARGUMENTS <c>OVERLOAD</c> as a number (0 when the subprogram is not overloaded).</param>
/// <param name="Parameters">Formal parameters in declaration order (the function return row is excluded).</param>
/// <param name="ReturnType">Function return type, or null for a procedure.</param>
/// <param name="ObjectType">ALL_PROCEDURES <c>OBJECT_TYPE</c> (PACKAGE, PROCEDURE, FUNCTION) when known.</param>
public sealed record OracleProcedureCatalogEntry(
    string Owner,
    string? PackageName,
    string Name,
    int SubprogramId,
    int Overload,
    IReadOnlyList<ParameterDescriptor> Parameters,
    string? ReturnType,
    string? ObjectType = null)
{
    /// <summary>Gets the names of OUT / IN OUT parameters whose type is a REF CURSOR.</summary>
    public IReadOnlyList<string> RefCursorParameters { get; init; } = Array.Empty<string>();

    /// <summary>Gets a value indicating whether the subprogram is a function.</summary>
    public bool IsFunction => ReturnType is not null;

    /// <summary>Gets a value indicating whether the function returns a REF CURSOR.</summary>
    public bool ReturnsRefCursorValue => OracleCatalog.IsRefCursorType(ReturnType);

    /// <summary>Gets a value indicating whether the subprogram hands a REF CURSOR back to the caller (return value or OUT parameter).</summary>
    public bool ReturnsRefCursor => ReturnsRefCursorValue || RefCursorParameters.Count > 0;

    /// <summary>Gets the stable descriptor id: <c>oracle:{OWNER}.{PACKAGE or _}.{NAME}#{SUBPROGRAM_ID}</c>.</summary>
    public string Id => OracleCatalog.BuildProcedureId(Owner, PackageName, Name, SubprogramId);
}

/// <summary>
/// One row of the combined <c>ALL_ARGUMENTS</c> / <c>ALL_PROCEDURES</c> catalog query. <see cref="IsHeader"/> rows come
/// from <c>ALL_PROCEDURES</c> and guarantee that subprograms without any argument row (0-argument procedures, which
/// Oracle 18c+ does not list in <c>ALL_ARGUMENTS</c>) are still catalogued.
/// </summary>
public sealed record OracleArgumentRow(
    string? PackageName,
    string ObjectName,
    int SubprogramId,
    string? Overload,
    int? Position,
    int? Sequence,
    string? ArgumentName,
    string? InOut,
    string? DataType,
    int? DataLength,
    int? DataPrecision,
    int? DataScale,
    string? CharUsed,
    int? CharLength,
    string? Defaulted,
    string? TypeOwner,
    string? TypeName,
    string? TypeSubname,
    bool IsHeader = false,
    string? ObjectType = null);

/// <summary>
/// Pure catalog helpers shared by the readers and the CLI (row grouping, ids, REF CURSOR detection).
/// </summary>
public static class OracleCatalog
{
    /// <summary>Data type reported by ALL_ARGUMENTS for SYS_REFCURSOR and declared REF CURSOR types.</summary>
    public const string RefCursorDataType = "REF CURSOR";

    /// <summary>Builds <c>oracle:{OWNER}.{PACKAGE or _}.{NAME}#{SUBPROGRAM_ID}</c>.</summary>
    /// <returns>The descriptor id.</returns>
    public static string BuildProcedureId(string owner, string? packageName, string name, int subprogramId)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"oracle:{owner.ToUpperInvariant()}.{(string.IsNullOrEmpty(packageName) ? "_" : packageName.ToUpperInvariant())}.{name.ToUpperInvariant()}#{subprogramId}");

    /// <summary>Returns true for the ALL_ARGUMENTS REF CURSOR data type.</summary>
    /// <returns>True when <paramref name="dataType"/> is a REF CURSOR.</returns>
    public static bool IsRefCursorType(string? dataType)
        => string.Equals(dataType?.Trim(), RefCursorDataType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataType?.Trim(), "SYS_REFCURSOR", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Groups catalog rows by <c>(package_name, object_name, subprogram_id)</c>. Header rows (ALL_PROCEDURES) create the
    /// group so 0-argument subprograms survive; argument rows add parameters; the function return row (position 0, no
    /// argument name) becomes <see cref="OracleProcedureCatalogEntry.ReturnType"/>; legacy placeholder rows (no argument
    /// name and no data type, emitted by older releases for 0-argument procedures) are ignored. Argument groups of a
    /// package that has no ALL_PROCEDURES row (object-type methods) are dropped.
    /// </summary>
    /// <param name="owner">Owner the rows were read for.</param>
    /// <param name="rows">Rows in any order.</param>
    /// <returns>Entries ordered by package (standalone first), name and subprogram id.</returns>
    public static IReadOnlyList<OracleProcedureCatalogEntry> Group(string owner, IEnumerable<OracleArgumentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var groups = new Dictionary<(string Package, string Name, int SubprogramId), GroupState>();
        var packagesWithHeader = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var key = (row.PackageName ?? string.Empty, row.ObjectName, row.SubprogramId);
            if (!groups.TryGetValue(key, out var state))
            {
                state = new GroupState(row.PackageName, row.ObjectName, row.SubprogramId);
                groups[key] = state;
            }

            if (row.IsHeader)
            {
                state.HasHeader = true;
                state.ObjectType ??= row.ObjectType;
                state.Overload ??= row.Overload;
                if (row.PackageName is not null)
                {
                    packagesWithHeader.Add(row.PackageName);
                }

                continue;
            }

            state.Overload ??= row.Overload;
            if (row.ArgumentName is null)
            {
                if (!string.IsNullOrEmpty(row.DataType) && (row.Position ?? 0) == 0)
                {
                    state.ReturnType = BuildFullTypeName(row.TypeOwner, row.TypeName, row.TypeSubname, row.DataType);
                    state.ReturnDataType = row.DataType;
                }

                continue;
            }

            state.Rows.Add(row);
        }

        var result = new List<OracleProcedureCatalogEntry>(groups.Count);
        foreach (var state in groups.Values)
        {
            if (!state.HasHeader && state.PackageName is not null && !packagesWithHeader.Contains(state.PackageName))
            {
                continue;
            }

            var parameters = new List<ParameterDescriptor>(state.Rows.Count);
            var refCursors = new List<string>();
            foreach (var row in state.Rows.OrderBy(r => r.Position ?? int.MaxValue).ThenBy(r => r.Sequence ?? 0))
            {
                var direction = MapDirection(row.InOut);
                if (IsRefCursorType(row.DataType) && direction != ParameterDirection.Input)
                {
                    refCursors.Add(row.ArgumentName!);
                }

                parameters.Add(new ParameterDescriptor(
                    Name: row.ArgumentName!,
                    DataType: BuildFullTypeName(row.TypeOwner, row.TypeName, row.TypeSubname, row.DataType ?? string.Empty),
                    Direction: direction,
                    MaxLength: row.DataLength,
                    Precision: row.DataPrecision,
                    Scale: row.DataScale,
                    IsNullable: true, // ALL_ARGUMENTS does not track nullability
                    OrdinalPosition: row.Position ?? 0,
                    Overload: ParseOverload(row.Overload),
                    Sequence: row.Sequence ?? 0,
                    TypeOwner: row.TypeOwner,
                    TypeName: row.TypeName,
                    TypeSubname: row.TypeSubname,
                    HasDefault: string.Equals(row.Defaulted, "Y", StringComparison.OrdinalIgnoreCase)));
            }

            result.Add(new OracleProcedureCatalogEntry(
                Owner: owner.ToUpperInvariant(),
                PackageName: state.PackageName,
                Name: state.Name,
                SubprogramId: state.SubprogramId,
                Overload: ParseOverload(state.Overload),
                Parameters: parameters,
                ReturnType: state.ReturnDataType is not null && IsRefCursorType(state.ReturnDataType) ? RefCursorDataType : state.ReturnType,
                ObjectType: state.ObjectType ?? (state.PackageName is null ? (state.ReturnType is null ? "PROCEDURE" : "FUNCTION") : "PACKAGE"))
            {
                RefCursorParameters = refCursors,
            });
        }

        return result
            .OrderBy(e => e.PackageName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.SubprogramId)
            .ToList();
    }

    /// <summary>Converts a catalog entry into the provider-neutral stored-procedure descriptor.</summary>
    /// <param name="entry">The catalog entry.</param>
    /// <param name="resultColumns">REF CURSOR result columns when described; empty otherwise.</param>
    /// <returns>The descriptor.</returns>
    public static StoredProcedureDescriptor ToDescriptor(OracleProcedureCatalogEntry entry, IReadOnlyList<ColumnDescriptor>? resultColumns = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new StoredProcedureDescriptor(
            Id: entry.Id,
            Name: entry.Name,
            Schema: entry.Owner,
            PackageName: entry.PackageName ?? string.Empty,
            Parameters: entry.Parameters,
            ResultColumns: resultColumns ?? Array.Empty<ColumnDescriptor>(),
            ReturnsRefCursor: entry.ReturnsRefCursor,
            ReturnType: entry.ReturnType);
    }

    /// <summary>
    /// Returns true when <see cref="RefCursorDescriber"/> can describe the entry without inventing values for other
    /// outputs: a function returning a REF CURSOR with no OUT parameters, or a procedure with exactly one OUT REF CURSOR
    /// and no other OUT / IN OUT parameters. IN parameters are bound as NULL (or omitted when they have a default).
    /// </summary>
    /// <returns>True when describable.</returns>
    public static bool IsDescribable(OracleProcedureCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var outputs = entry.Parameters.Count(p => p.Direction != ParameterDirection.Input);
        if (entry.ReturnsRefCursorValue)
        {
            return outputs == 0;
        }

        return !entry.IsFunction && entry.RefCursorParameters.Count == 1 && outputs == 1;
    }

    /// <summary>Character set that applies to a column of <paramref name="dataType"/>: national types use the NCHAR set.</summary>
    /// <returns>The character set, or null for non-character types.</returns>
    public static string? CharsetForColumn(string? dataType, string? databaseCharset, string? nationalCharset)
    {
        var type = dataType?.Trim().ToUpperInvariant() ?? string.Empty;
        return type switch
        {
            "NVARCHAR2" or "NCHAR" or "NCLOB" => NullIfUnknown(nationalCharset),
            "VARCHAR2" or "VARCHAR" or "CHAR" or "CLOB" or "LONG" => NullIfUnknown(databaseCharset),
            _ => null,
        };
    }

    internal static string BuildFullTypeName(string? typeOwner, string? typeName, string? typeSubname, string fallback)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return fallback;
        }

        var parts = new List<string>(3);
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

    internal static ParameterDirection MapDirection(string? inOut) => inOut?.Trim().ToUpperInvariant() switch
    {
        "OUT" => ParameterDirection.Output,
        "IN/OUT" or "IN OUT" => ParameterDirection.InputOutput,
        _ => ParameterDirection.Input,
    };

    internal static int ParseOverload(string? overload)
        => int.TryParse(overload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string? NullIfUnknown(string? charset)
        => string.IsNullOrWhiteSpace(charset) || string.Equals(charset, "UNKNOWN", StringComparison.OrdinalIgnoreCase) ? null : charset;

    private sealed class GroupState
    {
        public GroupState(string? packageName, string name, int subprogramId)
        {
            PackageName = packageName;
            Name = name;
            SubprogramId = subprogramId;
        }

        public string? PackageName { get; }

        public string Name { get; }

        public int SubprogramId { get; }

        public bool HasHeader { get; set; }

        public string? ObjectType { get; set; }

        public string? Overload { get; set; }

        public string? ReturnType { get; set; }

        public string? ReturnDataType { get; set; }

        public List<OracleArgumentRow> Rows { get; } = new();
    }
}

/// <summary>
/// Oracle ground-truth schema with the database-level facts the length rules need: character sets and
/// <c>MAX_STRING_SIZE</c>. Columns additionally carry their own <see cref="ColumnDescriptor.Charset"/>.
/// </summary>
public sealed record OracleDatabaseSchemaDescriptor(
    string Id,
    IReadOnlyList<DatabaseTableDescriptor> Tables,
    string LengthSemantics,
    string? DatabaseCharset,
    string? NationalCharset,
    string MaxStringSize = "STANDARD") : DatabaseSchemaDescriptor(Id, Tables, LengthSemantics);

/// <summary>
/// Builds the Oracle catalog (stored procedures + schema) used by <c>validate</c> and <c>snapshot refresh</c>.
/// </summary>
public static class OracleCatalogBuilder
{
    /// <summary>
    /// Reads procedures/functions (all packages and standalone units of <paramref name="owner"/>), table columns and NLS
    /// facts. When <see cref="OracleConfiguration.DescribeRefCursors"/> is true, procedures that hand back a REF CURSOR are
    /// <b>executed</b> (IN parameters bound as NULL) so <see cref="RefCursorDescriber"/> can read the cursor shape; a failing
    /// describe leaves <see cref="StoredProcedureDescriptor.ResultColumns"/> empty.
    /// </summary>
    /// <returns>Stored-procedure descriptors followed by one <see cref="OracleDatabaseSchemaDescriptor"/>.</returns>
    public static async Task<IReadOnlyList<ContractDescriptor>> BuildAsync(
        string connectionString,
        string owner,
        OracleConfiguration? configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentException.ThrowIfNullOrEmpty(owner);
        var config = configuration ?? new OracleConfiguration();
        var contracts = new List<ContractDescriptor>();

        var argumentsReader = new AllArgumentsReader(connectionString, config);
        var entries = await argumentsReader.GetProceduresAsync(owner, null, cancellationToken);
        var describer = config.DescribeRefCursors ? new RefCursorDescriber(connectionString) : null;
        foreach (var entry in entries)
        {
            IReadOnlyList<ColumnDescriptor>? resultColumns = null;
            if (describer is not null && entry.ReturnsRefCursor && OracleCatalog.IsDescribable(entry))
            {
                resultColumns = await TryDescribeAsync(describer, entry, cancellationToken);
            }

            contracts.Add(OracleCatalog.ToDescriptor(entry, resultColumns));
        }

        var nls = await new NlsSessionReader(connectionString).GetNlsParametersAsync(cancellationToken);
        var allColumns = await new AllTabColumnsReader(connectionString).GetAllColumnsAsync(owner, cancellationToken);
        contracts.Add(BuildSchemaDescriptor(owner, allColumns, nls));
        return contracts;
    }

    /// <summary>Builds the schema descriptor, stamping each character column with the charset that stores it.</summary>
    /// <returns>The schema descriptor.</returns>
    public static OracleDatabaseSchemaDescriptor BuildSchemaDescriptor(
        string owner,
        IReadOnlyDictionary<string, List<ColumnDescriptor>> columnsByTable,
        NlsParameters nls)
    {
        ArgumentNullException.ThrowIfNull(columnsByTable);
        ArgumentNullException.ThrowIfNull(nls);
        var schemaOwner = owner.ToUpperInvariant();
        var tables = columnsByTable
            .Select(pair => new DatabaseTableDescriptor(
                pair.Key,
                pair.Value
                    .Select(column => column with
                    {
                        Charset = column.Charset ?? OracleCatalog.CharsetForColumn(column.DataType, nls.CharacterSet, nls.NCharCharacterSet),
                    })
                    .ToList(),
                schemaOwner))
            .ToList();

        return new OracleDatabaseSchemaDescriptor(
            Id: $"oracle:schema:{schemaOwner}",
            Tables: tables,
            LengthSemantics: nls.LengthSemantics == LengthSemantics.Byte ? "BYTE" : "CHAR",
            DatabaseCharset: string.Equals(nls.CharacterSet, "UNKNOWN", StringComparison.OrdinalIgnoreCase) ? null : nls.CharacterSet,
            NationalCharset: string.Equals(nls.NCharCharacterSet, "UNKNOWN", StringComparison.OrdinalIgnoreCase) ? null : nls.NCharCharacterSet,
            MaxStringSize: nls.MaxStringSize);
    }

    private static async Task<IReadOnlyList<ColumnDescriptor>?> TryDescribeAsync(
        RefCursorDescriber describer,
        OracleProcedureCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        var samples = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var parameter in entry.Parameters)
        {
            if (parameter.Direction == ParameterDirection.Input && !parameter.HasDefault)
            {
                samples[parameter.Name] = DBNull.Value;
            }
        }

        try
        {
            return await describer.DescribeRefCursorAsync(
                entry.Owner,
                entry.PackageName ?? string.Empty,
                entry.Name,
                samples,
                entry.ReturnsRefCursorValue ? null : entry.RefCursorParameters[0],
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is global::Oracle.ManagedDataAccess.Client.OracleException or InvalidOperationException or ArgumentException or InvalidCastException)
        {
            // The procedure may need real arguments or side effects; its shape stays unknown.
            return null;
        }
    }
}
