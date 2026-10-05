using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Baseline;

/// <summary>Snapshot version 4 (stored procedures, length semantics, charset) and its integrity hash (red-team H4).</summary>
public partial class BaselineManager
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Writes a version 4 snapshot: tables, stored procedures, length semantics and charset, hashed with
    /// <see cref="SnapshotFormat.CanonicalSchemaV2HashKind"/>, plus fingerprinted violations.
    /// </summary>
    /// <returns>The persisted snapshot.</returns>
    public async Task<BaselineFile> CreateSnapshotAsync(
        IEnumerable<ContractViolation> violations,
        string schemaVersion,
        string? databaseVersion,
        IReadOnlyList<SnapshotTable>? schema,
        IReadOnlyList<SnapshotStoredProcedure>? storedProcedures,
        string? provider,
        string? schemaScope,
        string? lengthSemantics,
        string? charset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var snapshot = BuildSnapshotFile(
            ToBaselineViolations(violations),
            schemaVersion,
            "Snapshot",
            databaseVersion ?? "unknown",
            schema,
            storedProcedures ?? Array.Empty<SnapshotStoredProcedure>(),
            provider,
            schemaScope,
            lengthSemantics,
            charset);

        await SaveAsync(snapshot, cancellationToken);
        return snapshot;
    }

    // Single version 4 writer shape shared by CreateSnapshotAsync and the schema overload of CreateBaselineAsync.
    private static BaselineFile BuildSnapshotFile(
        IReadOnlyList<BaselineViolation> violations,
        string schemaVersion,
        string groundTruthMode,
        string databaseVersion,
        IReadOnlyList<SnapshotTable>? schema,
        IReadOnlyList<SnapshotStoredProcedure> procedures,
        string? provider,
        string? schemaScope,
        string? lengthSemantics,
        string? charset) =>
        new(
            Version: SnapshotFormat.WithStoredProceduresVersion,
            CreatedAt: DateTimeOffset.UtcNow,
            SchemaVersion: schemaVersion,
            GroundTruthMode: groundTruthMode,
            DatabaseVersion: databaseVersion,
            SchemaHash: ComputeSnapshotHash(schema, procedures, provider, schemaScope, lengthSemantics, charset),
            Violations: violations,
            Schema: schema,
            SchemaHashKind: SnapshotFormat.CanonicalSchemaV2HashKind,
            Provider: provider,
            SchemaScope: schemaScope,
            SchemaCanonicalizationVersion: SnapshotFormat.CanonicalizationV2,
            LengthSemantics: lengthSemantics,
            Charset: charset,
            StoredProcedures: procedures);

    /// <summary>
    /// Computes the <see cref="SnapshotFormat.CanonicalSchemaV2HashKind"/> hash: uppercase SHA-256 hex over a canonical
    /// JSON document of the provider, scope, length semantics, charset, tables (ordered by schema and name, columns by
    /// name) and stored procedures (ordered by identity, parameters by position; result columns keep result order).
    /// </summary>
    public static string ComputeSnapshotHash(
        IReadOnlyList<SnapshotTable>? tables,
        IReadOnlyList<SnapshotStoredProcedure>? storedProcedures,
        string? provider,
        string? schemaScope,
        string? lengthSemantics,
        string? charset)
    {
        var canonical = new CanonicalSnapshot(
            SnapshotFormat.CanonicalSchemaV2HashKind,
            Normalize(provider)?.ToLowerInvariant(),
            Normalize(schemaScope),
            Normalize(lengthSemantics)?.ToUpperInvariant(),
            Normalize(charset)?.ToUpperInvariant(),
            (tables ?? Array.Empty<SnapshotTable>())
                .OrderBy(table => table.Schema ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(table => table.Name, StringComparer.Ordinal)
                .Select(table => table with
                {
                    Columns = (table.Columns ?? Array.Empty<SnapshotColumn>())
                        .OrderBy(column => column.Name, StringComparer.Ordinal)
                        .ThenBy(column => column.ColumnId ?? 0)
                        .ToArray(),
                })
                .ToArray(),
            (storedProcedures ?? Array.Empty<SnapshotStoredProcedure>())
                .OrderBy(procedure => procedure.Id ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(procedure => procedure.Schema ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(procedure => procedure.PackageName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(procedure => procedure.Name, StringComparer.Ordinal)
                .Select(procedure => procedure with
                {
                    Parameters = (procedure.Parameters ?? Array.Empty<SnapshotParameter>())
                        .OrderBy(parameter => parameter.Overload)
                        .ThenBy(parameter => parameter.OrdinalPosition)
                        .ThenBy(parameter => parameter.Sequence)
                        .ThenBy(parameter => parameter.Name, StringComparer.Ordinal)
                        .ToArray(),
                    ResultColumns = (procedure.ResultColumns ?? Array.Empty<SnapshotColumn>()).ToArray(),
                })
                .ToArray());
        var json = JsonSerializer.Serialize(canonical, CanonicalJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Recomputes the snapshot hash over the loaded content and compares it with the stored one.</summary>
    public static SnapshotIntegrityResult VerifySnapshotIntegrity(BaselineFile snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Schema is null && snapshot.StoredProcedures is null)
        {
            return new SnapshotIntegrityResult(SnapshotIntegrityStatus.NoContent, snapshot.SchemaHash, null, "snapshot carries no schema or stored procedures");
        }

        if (string.IsNullOrWhiteSpace(snapshot.SchemaHash))
        {
            return new SnapshotIntegrityResult(SnapshotIntegrityStatus.Mismatch, snapshot.SchemaHash, null, "snapshot has no SchemaHash");
        }

        string? computed = snapshot.SchemaHashKind switch
        {
            SnapshotFormat.CanonicalSchemaV2HashKind => ComputeSnapshotHash(
                snapshot.Schema,
                snapshot.StoredProcedures,
                snapshot.Provider,
                snapshot.SchemaScope,
                snapshot.LengthSemantics,
                snapshot.Charset),
            SnapshotFormat.CanonicalSchemaV1HashKind when snapshot.Schema is not null => ComputeSchemaHash(
                snapshot.Schema,
                snapshot.Provider,
                snapshot.SchemaScope,
                snapshot.SchemaCanonicalizationVersion ?? SnapshotFormat.CanonicalizationV1),
            null when snapshot.Schema is not null && snapshot.Version <= SnapshotFormat.ViolationsOnlyVersion => ComputeSchemaHash(snapshot.Schema),
            _ => null,
        };

        if (computed is null)
        {
            return new SnapshotIntegrityResult(
                SnapshotIntegrityStatus.Unverifiable,
                snapshot.SchemaHash,
                null,
                $"hash kind '{snapshot.SchemaHashKind ?? "none"}' does not cover the snapshot content");
        }

        return string.Equals(computed, snapshot.SchemaHash, StringComparison.OrdinalIgnoreCase)
            ? new SnapshotIntegrityResult(SnapshotIntegrityStatus.Verified, snapshot.SchemaHash, computed, null)
            : new SnapshotIntegrityResult(SnapshotIntegrityStatus.Mismatch, snapshot.SchemaHash, computed, "SchemaHash does not match the snapshot content");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CanonicalSnapshot(
        string Kind,
        string? Provider,
        string? Scope,
        string? LengthSemantics,
        string? Charset,
        SnapshotTable[] Tables,
        SnapshotStoredProcedure[] StoredProcedures);
}

/// <summary>Outcome of <see cref="BaselineManager.VerifySnapshotIntegrity"/>.</summary>
public enum SnapshotIntegrityStatus
{
    /// <summary>The stored hash matches the recomputed one.</summary>
    Verified,

    /// <summary>The stored hash differs from the recomputed one (or is missing): the file was edited or corrupted.</summary>
    Mismatch,

    /// <summary>The stored hash kind cannot be recomputed from the content (for example a legacy violation hash).</summary>
    Unverifiable,

    /// <summary>The file carries no schema and no stored procedures, so there is nothing to verify.</summary>
    NoContent,
}

/// <summary>Result of a snapshot integrity check.</summary>
public sealed record SnapshotIntegrityResult(
    SnapshotIntegrityStatus Status,
    string? StoredHash,
    string? ComputedHash,
    string? Reason);
