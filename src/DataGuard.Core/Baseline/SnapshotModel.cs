using System;
using System.Collections.Generic;
using System.Linq;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Baseline;

/// <summary>
/// Version and hash-kind constants of the persisted snapshot/baseline file (<see cref="BaselineFile"/>).
/// </summary>
/// <remarks>
/// The red-team remediation plan calls the procedure-bearing format "snapshot v3"; on disk it is
/// <see cref="WithStoredProceduresVersion"/> (4) so it cannot be confused with the existing version 3,
/// which already meant "tables plus canonical-schema-v1 hash metadata".
/// </remarks>
public static class SnapshotFormat
{
    /// <summary>Violations only (baseline, or a snapshot without a persisted schema).</summary>
    public const int ViolationsOnlyVersion = 2;

    /// <summary>Tables only, hashed with <see cref="CanonicalSchemaV1HashKind"/> plus provider/scope metadata.</summary>
    public const int TablesOnlyVersion = 3;

    /// <summary>
    /// Tables (with schema and column charset), stored procedures (parameters, overloads, result columns),
    /// length semantics and charset, all covered by <see cref="CanonicalSchemaV2HashKind"/>.
    /// </summary>
    public const int WithStoredProceduresVersion = 4;

    /// <summary>The newest format this build reads.</summary>
    public const int LatestVersion = WithStoredProceduresVersion;

    /// <summary>Legacy 16-hex prefix of a SHA-256 over <c>RuleId:Message</c>; not a schema hash.</summary>
    public const string ViolationHashKind = "violation-sha256-prefix";

    /// <summary>SHA-256 over tables/columns plus provider, scope and canonicalizer metadata (version 3).</summary>
    public const string CanonicalSchemaV1HashKind = "canonical-schema-v1";

    /// <summary>SHA-256 over the canonical JSON of tables, stored procedures, length semantics and charset (version 4).</summary>
    public const string CanonicalSchemaV2HashKind = "canonical-schema-v2";

    /// <summary>Canonicalizer revision written with <see cref="CanonicalSchemaV1HashKind"/>.</summary>
    public const string CanonicalizationV1 = "v1";

    /// <summary>Canonicalizer revision written with <see cref="CanonicalSchemaV2HashKind"/>.</summary>
    public const string CanonicalizationV2 = "v2";
}

/// <summary>
/// Serializable stored procedure (or function / package subprogram) captured by <c>snapshot refresh</c>.
/// </summary>
/// <param name="Id">Provider identity of the subprogram, including the overload/subprogram key when the provider has one.</param>
/// <param name="Name">Procedure name.</param>
/// <param name="Schema">Owning schema (owner).</param>
/// <param name="PackageName">Oracle package name; empty or null for standalone procedures.</param>
/// <param name="Parameters">Catalog parameters.</param>
/// <param name="ResultColumns">Described result-set (or ref cursor) columns, in result order.</param>
/// <param name="ReturnsRefCursor">True when the procedure returns a ref cursor.</param>
/// <param name="ReturnType">Function return type, when the subprogram is a function.</param>
public sealed record SnapshotStoredProcedure(
    string Id,
    string Name,
    string? Schema,
    string? PackageName,
    IReadOnlyList<SnapshotParameter> Parameters,
    IReadOnlyList<SnapshotColumn>? ResultColumns = null,
    bool ReturnsRefCursor = false,
    string? ReturnType = null);

/// <summary>
/// Serializable catalog parameter of a <see cref="SnapshotStoredProcedure"/>. Call-site fields
/// (<see cref="ParameterDescriptor.ClrType"/>, <see cref="ParameterDescriptor.CallSiteDirection"/>) are not persisted.
/// </summary>
public sealed record SnapshotParameter(
    string Name,
    string DataType,
    ParameterDirection Direction,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    int OrdinalPosition,
    bool HasDefault = false,
    int Overload = 0,
    int Sequence = 0,
    string? TypeOwner = null,
    string? TypeName = null,
    string? TypeSubname = null);

/// <summary>
/// Converts between acquired contract descriptors and their persisted snapshot form.
/// </summary>
public static class SnapshotConversion
{
    /// <summary>Length semantics assumed for snapshots written before version 4 (the previous hard-coded value).</summary>
    public const string LegacyLengthSemantics = "CHAR";

    /// <summary>Captures every table of <paramref name="schema"/>, including the table schema and column charset.</summary>
    public static IReadOnlyList<SnapshotTable> FromSchema(DatabaseSchemaDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.Tables
            .Select(table => new SnapshotTable(table.Name, table.Columns.Select(FromColumn).ToList(), table.Schema))
            .ToList();
    }

    /// <summary>Captures one column, including its charset.</summary>
    public static SnapshotColumn FromColumn(ColumnDescriptor column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SnapshotColumn(
            column.Name,
            column.DataType,
            column.MaxLength,
            column.CharLength,
            column.Precision,
            column.Scale,
            column.IsNullable,
            column.CharUsed,
            column.DataDefault,
            column.ColumnId,
            column.Charset);
    }

    /// <summary>Captures one stored procedure with its catalog parameters and result columns.</summary>
    public static SnapshotStoredProcedure FromProcedure(StoredProcedureDescriptor procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return new SnapshotStoredProcedure(
            procedure.Id,
            procedure.Name,
            procedure.Schema,
            procedure.PackageName,
            (procedure.Parameters ?? Array.Empty<ParameterDescriptor>()).Select(parameter => new SnapshotParameter(
                parameter.Name,
                parameter.DataType,
                parameter.Direction,
                parameter.MaxLength,
                parameter.Precision,
                parameter.Scale,
                parameter.IsNullable,
                parameter.OrdinalPosition,
                parameter.HasDefault,
                parameter.Overload,
                parameter.Sequence,
                parameter.TypeOwner,
                parameter.TypeName,
                parameter.TypeSubname)).ToList(),
            (procedure.ResultColumns ?? Array.Empty<ColumnDescriptor>()).Select(FromColumn).ToList(),
            procedure.ReturnsRefCursor,
            procedure.ReturnType);
    }

    /// <summary>Captures every stored procedure in <paramref name="contracts"/>.</summary>
    public static IReadOnlyList<SnapshotStoredProcedure> FromProcedures(IEnumerable<ContractDescriptor> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        return contracts.OfType<StoredProcedureDescriptor>().Select(FromProcedure).ToList();
    }

    /// <summary>
    /// Returns the single charset shared by every column that declares one, or null when columns declare none or several.
    /// <see cref="DatabaseSchemaDescriptor"/> has no database-level charset, so this is the best snapshot-level summary.
    /// </summary>
    public static string? ResolveUniformCharset(DatabaseSchemaDescriptor? schema)
    {
        if (schema is null)
        {
            return null;
        }

        var charsets = schema.Tables
            .SelectMany(table => table.Columns)
            .Select(column => column.Charset)
            .Where(charset => !string.IsNullOrWhiteSpace(charset))
            .Select(charset => charset!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return charsets.Count == 1 ? charsets[0] : null;
    }

    /// <summary>Materializes the persisted schema with the length semantics recorded in the file.</summary>
    /// <returns>Null when the file carries no schema.</returns>
    public static DatabaseSchemaDescriptor? ToSchemaDescriptor(BaselineFile snapshot, string id = "snapshot-schema")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Schema is null)
        {
            return null;
        }

        return new DatabaseSchemaDescriptor(
            Id: id,
            Tables: snapshot.Schema
                .Select(table => new DatabaseTableDescriptor(
                    table.Name,
                    (table.Columns ?? Array.Empty<SnapshotColumn>()).Select(ToColumn).ToList(),
                    table.Schema))
                .ToList(),
            LengthSemantics: string.IsNullOrWhiteSpace(snapshot.LengthSemantics) ? LegacyLengthSemantics : snapshot.LengthSemantics);
    }

    /// <summary>Materializes one persisted column.</summary>
    public static ColumnDescriptor ToColumn(SnapshotColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new ColumnDescriptor(
            column.Name,
            column.DataType,
            column.MaxLength,
            column.Precision,
            column.Scale,
            column.IsNullable,
            column.CharUsed,
            column.CharLength,
            column.DataDefault,
            column.ColumnId ?? 0,
            column.Charset);
    }

    /// <summary>Materializes the persisted stored procedures; empty for files written before version 4.</summary>
    public static IReadOnlyList<StoredProcedureDescriptor> ToProcedures(BaselineFile snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return (snapshot.StoredProcedures ?? Array.Empty<SnapshotStoredProcedure>()).Select(ToProcedure).ToList();
    }

    /// <summary>Materializes one persisted stored procedure.</summary>
    public static StoredProcedureDescriptor ToProcedure(SnapshotStoredProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var id = string.IsNullOrWhiteSpace(procedure.Id)
            ? $"snapshot:{procedure.Schema}.{procedure.PackageName}.{procedure.Name}"
            : procedure.Id;
        return new StoredProcedureDescriptor(
            id,
            procedure.Name,
            procedure.Schema ?? string.Empty,
            procedure.PackageName ?? string.Empty,
            (procedure.Parameters ?? Array.Empty<SnapshotParameter>()).Select(parameter => new ParameterDescriptor(
                parameter.Name,
                parameter.DataType,
                parameter.Direction,
                parameter.MaxLength,
                parameter.Precision,
                parameter.Scale,
                parameter.IsNullable,
                parameter.OrdinalPosition,
                parameter.Overload,
                parameter.Sequence,
                parameter.TypeOwner,
                parameter.TypeName,
                parameter.TypeSubname,
                HasDefault: parameter.HasDefault)).ToList(),
            (procedure.ResultColumns ?? Array.Empty<SnapshotColumn>()).Select(ToColumn).ToList(),
            procedure.ReturnsRefCursor,
            ReturnType: procedure.ReturnType);
    }
}
