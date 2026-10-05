using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace DataGuard.Core.Baseline;

/// <summary>
/// Manages baseline files for legacy codebases.
/// Supports database version tracking and schema hash for drift detection.
/// </summary>
public partial class BaselineManager
{
    /// <summary>Maximum serialized baseline size accepted for persistence and loading.</summary>
    public const long MaxBaselineBytes = 16 * 1024 * 1024;

    private readonly string _baselineFilePath;
    private readonly string _repoRoot;
    private static readonly MemoryCache _schemaHashCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 10000,
        ExpirationScanFrequency = TimeSpan.FromMinutes(5),
    });

    private static readonly ConcurrentDictionary<string, string> _fileHashCache = new();
    private static long _baselineCacheHits;
    private static long _baselineCacheMisses;

    public BaselineManager(string baselineFilePath)
        : this(baselineFilePath, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaselineManager"/> class.
    /// </summary>
    /// <param name="baselineFilePath">Baseline or snapshot file.</param>
    /// <param name="repoRoot">
    /// Root that fingerprint locations are made relative to; null uses the current directory (the same root SARIF
    /// artifact URIs use), so <c>baseline</c> and <c>validate</c> must run from the same directory.
    /// </param>
    public BaselineManager(string baselineFilePath, string? repoRoot)
    {
        _baselineFilePath = baselineFilePath ?? throw new ArgumentNullException(nameof(baselineFilePath));
        _repoRoot = string.IsNullOrWhiteSpace(repoRoot) ? Directory.GetCurrentDirectory() : repoRoot;
    }

    /// <summary>Root that fingerprint locations are made relative to.</summary>
    public string RepoRoot => _repoRoot;

    /// <summary>Returns bounded in-memory baseline-cache counters for operational observation.</summary>
    public static BaselineCacheMetrics CacheMetrics => new(Interlocked.Read(ref _baselineCacheHits), Interlocked.Read(ref _baselineCacheMisses));

    /// <summary>
    /// Creates a new baseline from current violations. Without <paramref name="schema"/> the file is format version 2
    /// (violations only, <paramref name="schemaHash"/> or the violation hash). With a schema the file is format version 4
    /// (<see cref="SnapshotFormat.WithStoredProceduresVersion"/>) with an empty stored-procedure list, the same shape
    /// <see cref="CreateSnapshotAsync"/> writes for tables-only input: the hash is always the
    /// <see cref="SnapshotFormat.CanonicalSchemaV2HashKind"/> hash recomputed over the content, so
    /// <paramref name="schemaHash"/>, <paramref name="schemaHashKind"/> and <paramref name="schemaCanonicalizationVersion"/>
    /// are ignored for schema-bearing baselines (a caller-supplied version 3 hash would fail the integrity check).
    /// Version 3 files written by earlier releases still load and verify.
    /// </summary>
    /// <returns>The persisted baseline.</returns>
    public async Task<BaselineFile> CreateBaselineAsync(
        IEnumerable<ContractViolation> violations,
        string schemaVersion,
        string groundTruthMode,
        string? databaseVersion = null,
        string? schemaHash = null,
        IReadOnlyList<SnapshotTable>? schema = null,
        string? schemaHashKind = null,
        string? provider = null,
        string? schemaScope = null,
        string? schemaCanonicalizationVersion = null,
        CancellationToken cancellationToken = default)
    {
        violations = violations.ToList();
        var baselineViolations = ToBaselineViolations(violations);
        var dbVersion = databaseVersion ?? "unknown";

        var baseline = schema is null
            ? new BaselineFile(
                Version: SnapshotFormat.ViolationsOnlyVersion,
                CreatedAt: DateTimeOffset.UtcNow,
                SchemaVersion: schemaVersion,
                GroundTruthMode: groundTruthMode,
                DatabaseVersion: dbVersion,
                SchemaHash: schemaHash ?? ComputeSchemaHash(violations),
                Violations: baselineViolations,
                SchemaHashKind: schemaHashKind ?? SnapshotFormat.ViolationHashKind,
                Provider: provider,
                SchemaScope: schemaScope)
            : BuildSnapshotFile(
                baselineViolations,
                schemaVersion,
                groundTruthMode,
                dbVersion,
                schema,
                Array.Empty<SnapshotStoredProcedure>(),
                provider,
                schemaScope,
                lengthSemantics: null,
                charset: null);

        await SaveAsync(baseline, cancellationToken);
        return baseline;
    }

    /// <summary>
    /// Loads an existing baseline file.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselineFilePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(_baselineFilePath);
        if (fileInfo.Length > MaxBaselineBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the {MaxBaselineBytes} byte limit.");
        }

        if (fileInfo.Length > 1024 * 1024)
        {
            return await LoadWithMemoryMappedFileAsync(cancellationToken);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
            var cacheKey = "baseline-content-v1:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
            if (_schemaHashCache.TryGetValue(cacheKey, out BaselineFile? cached))
            {
                Interlocked.Increment(ref _baselineCacheHits);
                return cached;
            }

            Interlocked.Increment(ref _baselineCacheMisses);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            };
            var parsed = JsonSerializer.Deserialize<BaselineFile>(json, options);
            if (parsed is not null)
            {
                _schemaHashCache.Set(cacheKey, parsed, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1), Size = 1 });
            }

            return parsed;
        }
        catch (JsonException)
        {
            // Try to load as version 1 format
            try
            {
                var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
                var legacy = JsonSerializer.Deserialize<LegacyBaselineFile>(json);
                if (legacy != null)
                {
                    return MigrateFromLegacy(legacy);
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }
    }

    /// <summary>
    /// Loads baseline using memory-mapped file for large files.
    /// </summary>
    private async Task<BaselineFile?> LoadWithMemoryMappedFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var mmf = MemoryMappedFile.CreateFromFile(_baselineFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        // The view capacity is rounded up to the page size and the tail is zero-filled, so read exactly the
        // file length: decoding Capacity bytes would hand trailing NULs to the JSON parser.
        var fileLength = new FileInfo(_baselineFilePath).Length;
        if (fileLength > MaxBaselineBytes || fileLength > accessor.Capacity)
        {
            throw new InvalidDataException($"Baseline exceeds the {MaxBaselineBytes} byte limit.");
        }

        var length = checked((int)fileLength);
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, length);

        var json = System.Text.Encoding.UTF8.GetString(buffer);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        return JsonSerializer.Deserialize<BaselineFile>(json, options);
    }

    private async Task SaveAsync(BaselineFile baseline, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var json = JsonSerializer.Serialize(baseline, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxBaselineBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the {MaxBaselineBytes} byte limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveAtomicallyAsync(bytes, cancellationToken);
    }

    private async Task SaveAtomicallyAsync(byte[] data, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_baselineFilePath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_baselineFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(_baselineFilePath))
            {
                File.Move(tempPath, _baselineFilePath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, _baselineFilePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Computes a schema hash for the given violations.
    /// </summary>
    /// <returns></returns>
    public static string ComputeSchemaHash(IEnumerable<ContractViolation> violations)
    {
        var data = string.Join("|", violations
            .OrderBy(v => v.RuleId)
            .ThenBy(v => v.Message)
            .Select(v => $"{v.RuleId}:{v.Message}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Computes a deterministic full SHA256 hex hash from the schema itself
    /// (tables/columns canonicalized by name) - not from violations, so schema
    /// changes that produce no violations still change the hash.
    /// </summary>
    public static string ComputeSchemaHash(IReadOnlyList<SnapshotTable> schema)
        => ComputeSchemaHashCore(schema, null);

    /// <summary>
    /// Computes the canonical v3 schema hash, including provider scope metadata.
    /// </summary>
    public static string ComputeSchemaHash(
        IReadOnlyList<SnapshotTable> schema,
        string? provider,
        string? schemaScope,
        string? canonicalizationVersion = "v1")
    {
        var metadata = string.Join(
            "|",
            provider ?? "unknown-provider",
            schemaScope ?? "unknown-scope",
            canonicalizationVersion ?? "unknown-canonicalizer");
        return ComputeSchemaHashCore(schema, metadata);
    }

    private static string ComputeSchemaHashCore(IReadOnlyList<SnapshotTable> schema, string? metadata)
    {
        var canonicalSchema = string.Join("|", schema
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t =>
            {
                var cols = string.Join(";", t.Columns
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => string.Join(
                        ",",
                        c.Name,
                        c.DataType,
                        c.IsNullable,
                        c.MaxLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.CharLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.Precision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.Scale?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.CharUsed ?? "null",
                        c.DataDefault ?? "null",
                        c.ColumnId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")));
                return $"{t.Name}{{{cols}}}";
            }));
        var canonical = metadata is null ? canonicalSchema : metadata + "||" + canonicalSchema;

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    /// <summary>Computes the same canonical schema hash directly from a fresh database descriptor.</summary>
    public static string ComputeSchemaHash(DatabaseSchemaDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var snapshot = schema.Tables.Select(table => new SnapshotTable(
            table.Name,
            table.Columns.Select(column => new SnapshotColumn(
                column.Name,
                column.DataType,
                column.MaxLength,
                column.CharLength,
                column.Precision,
                column.Scale,
                column.IsNullable,
                column.CharUsed,
                column.DataDefault,
                column.ColumnId)).ToArray())).ToArray();
        return ComputeSchemaHash(snapshot);
    }

    /// <summary>
    /// Violation-based hash (legacy semantics, 16-hex prefix) used when diffing
    /// snapshots that were never migrated and carry no SchemaHash.
    /// </summary>
    public static string ComputeSchemaHash(IReadOnlyList<BaselineViolation> violations)
    {
        var data = string.Join("|", violations
            .OrderBy(v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(v => v.Message, StringComparer.Ordinal)
            .Select(v => $"{v.RuleId}:{v.Message}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Filters violations to only return new ones not in baseline.
    /// </summary>
    /// <returns></returns>
    /// <remarks>
    /// Fingerprinted entries form a multiset: a current violation is new once more violations share its
    /// <see cref="ComputeFingerprint"/> than the baseline <see cref="BaselineViolation.Count"/> allows. Legacy entries
    /// without a fingerprint keep the previous <c>RuleId:Message</c> set semantics for compatibility.
    /// </remarks>
    public IEnumerable<ContractViolation> FilterNewViolations(IEnumerable<ContractViolation> current, BaselineFile baseline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);

        var remaining = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var legacySignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in baseline.Violations ?? Array.Empty<BaselineViolation>())
        {
            if (string.IsNullOrWhiteSpace(entry.Fingerprint))
            {
                legacySignatures.Add($"{entry.RuleId}:{entry.Message}");
                continue;
            }

            var allowed = entry.Count is { } count ? Math.Max(count, 0) : 1;
            remaining[entry.Fingerprint] = remaining.GetValueOrDefault(entry.Fingerprint) + allowed;
        }

        var fresh = new List<ContractViolation>();
        foreach (var violation in current)
        {
            var fingerprint = ComputeFingerprint(violation, _repoRoot);
            if (remaining.TryGetValue(fingerprint, out var left) && left > 0)
            {
                remaining[fingerprint] = left - 1;
                continue;
            }

            if (legacySignatures.Contains($"{violation.RuleId}:{violation.Message}"))
            {
                continue;
            }

            fresh.Add(violation);
        }

        return fresh;
    }

    /// <summary>Number of baseline entries that predate fingerprints and still match by <c>RuleId:Message</c>.</summary>
    public static int CountLegacyEntries(BaselineFile baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return (baseline.Violations ?? Array.Empty<BaselineViolation>()).Count(entry => string.IsNullOrWhiteSpace(entry.Fingerprint));
    }

    /// <summary>
    /// Groups <paramref name="violations"/> into fingerprinted baseline entries (one per fingerprint, with its count),
    /// ordered deterministically so a committed baseline diffs cleanly.
    /// </summary>
    public IReadOnlyList<BaselineViolation> ToBaselineViolations(IEnumerable<ContractViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        return violations
            .Select(violation => (Violation: violation, Fingerprint: ComputeFingerprint(violation, _repoRoot)))
            .GroupBy(item => item.Fingerprint, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First().Violation;
                return new BaselineViolation(
                    first.RuleId,
                    first.Message,
                    first.Severity.ToString(),
                    ToBaselineLocation(first.Location),
                    first.Properties?.ToImmutableDictionary(),
                    group.Key,
                    group.Count());
            })
            .OrderBy(entry => entry.RuleId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Location?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Location?.StartLine ?? 0)
            .ThenBy(entry => entry.Fingerprint, StringComparer.Ordinal)
            .ToList();
    }

    private static BaselineLocation? ToBaselineLocation(Microsoft.CodeAnalysis.Location? location)
    {
        if (location is null || location.Kind == Microsoft.CodeAnalysis.LocationKind.None)
        {
            return null;
        }

        var span = location.GetLineSpan();
        return new BaselineLocation(
            location.SourceTree?.FilePath ?? span.Path ?? string.Empty,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1);
    }

    private static string ExtractMajorMinor(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(version, @"(\d+)\.(\d+)");
        if (match.Success)
        {
            return $"{match.Groups[1]}.{match.Groups[2]}";
        }

        return version;
    }

    private static BaselineFile MigrateFromLegacy(LegacyBaselineFile legacy)
    {
        return new BaselineFile(
            Version: 2,
            CreatedAt: legacy.CreatedAt,
            SchemaVersion: legacy.SchemaVersion,
            GroundTruthMode: legacy.GroundTruthMode,
            DatabaseVersion: "unknown",
            SchemaHash: ComputeSchemaHashFromLegacy(legacy),
            Violations: legacy.Violations,
            SchemaHashKind: "violation-sha256-prefix");
    }

    /// <summary>
    /// Migrates a legacy (v1) baseline file to v2 in-place. Returns the migrated baseline,
    /// or null when the file is missing or already v2.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile?> MigrateBaselineAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselineFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
        LegacyBaselineFile? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyBaselineFile>(json);
        }
        catch (JsonException)
        {
            return null; // Corrupt or non-v1 file - treat as "nothing to migrate".
        }

        if (legacy == null)
        {
            return null;
        }

        var migrated = MigrateFromLegacy(legacy);
        await SaveAsync(migrated, cancellationToken);
        return migrated;
    }

    private static string ComputeSchemaHashFromLegacy(LegacyBaselineFile legacy)
    {
        var data = string.Join("|", legacy.Violations
            .OrderBy(v => v.RuleId)
            .ThenBy(v => v.Message)
            .Select(v => $"{v.RuleId}:{v.Message}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..16];
    }
}

/// <summary>Monotonic cache counters; values are process-local and intentionally contain no baseline content.</summary>
public sealed record BaselineCacheMetrics(long Hits, long Misses);

/// <summary>
/// Legacy baseline file format (version 1) for migration.
/// </summary>
[JsonSerializable(typeof(LegacyBaselineFile))]
internal record LegacyBaselineFile(
    int Version,
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    IReadOnlyList<BaselineViolation> Violations);

/// <summary>
/// Baseline file format. Version 2 adds database/hash metadata; version 3 adds
/// schema hash kind, provider scope and canonicalization metadata; version 4
/// (<see cref="SnapshotFormat.WithStoredProceduresVersion"/>) adds stored procedures,
/// length semantics, charset and the <see cref="SnapshotFormat.CanonicalSchemaV2HashKind"/> hash.
/// </summary>
[JsonSerializable(typeof(BaselineFile))]
[method: JsonConstructor]
public record BaselineFile(
    int Version,
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    string DatabaseVersion,
    string SchemaHash,
    IReadOnlyList<BaselineViolation> Violations,
    IReadOnlyList<SnapshotTable>? Schema = null,
    string? SchemaHashKind = null,
    string? Provider = null,
    string? SchemaScope = null,
    string? SchemaCanonicalizationVersion = null,
    string? LengthSemantics = null,
    string? Charset = null,
    IReadOnlyList<SnapshotStoredProcedure>? StoredProcedures = null)
{
    public BaselineFile(
        int version,
        DateTimeOffset createdAt,
        string schemaVersion,
        string groundTruthMode,
        string databaseVersion,
        string schemaHash,
        IReadOnlyList<BaselineViolation> violations,
        IReadOnlyList<SnapshotTable>? schema)
        : this(version, createdAt, schemaVersion, groundTruthMode, databaseVersion, schemaHash, violations, schema, null, null, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaselineFile"/> class with version 3 fields only (binary compatibility).
    /// </summary>
    public BaselineFile(
        int version,
        DateTimeOffset createdAt,
        string schemaVersion,
        string groundTruthMode,
        string databaseVersion,
        string schemaHash,
        IReadOnlyList<BaselineViolation> violations,
        IReadOnlyList<SnapshotTable>? schema,
        string? schemaHashKind,
        string? provider,
        string? schemaScope,
        string? schemaCanonicalizationVersion)
        : this(version, createdAt, schemaVersion, groundTruthMode, databaseVersion, schemaHash, violations, schema, schemaHashKind, provider, schemaScope, schemaCanonicalizationVersion, null, null, null)
    {
    }

    public void Deconstruct(
        out int version,
        out DateTimeOffset createdAt,
        out string schemaVersion,
        out string groundTruthMode,
        out string databaseVersion,
        out string schemaHash,
        out IReadOnlyList<BaselineViolation> violations,
        out IReadOnlyList<SnapshotTable>? schema)
    {
        version = Version;
        createdAt = CreatedAt;
        schemaVersion = SchemaVersion;
        groundTruthMode = GroundTruthMode;
        databaseVersion = DatabaseVersion;
        schemaHash = SchemaHash;
        violations = Violations;
        schema = Schema;
    }
}

/// <summary>
/// Serializable ground-truth table snapshot (used by Snapshot mode offline validation).
/// </summary>
[method: JsonConstructor]
public record SnapshotTable(
    string Name,
    IReadOnlyList<SnapshotColumn> Columns,
    string? Schema = null)
{
    public SnapshotTable(string name, IReadOnlyList<SnapshotColumn> columns)
        : this(name, columns, null)
    {
    }

    public void Deconstruct(out string name, out IReadOnlyList<SnapshotColumn> columns)
    {
        name = Name;
        columns = Columns;
    }
}

[method: JsonConstructor]
public record SnapshotColumn(
    string Name,
    string DataType,
    int? MaxLength,
    int? CharLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    string? CharUsed,
    string? DataDefault = null,
    int? ColumnId = null,
    string? Charset = null)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotColumn"/> class without a charset (binary compatibility).
    /// </summary>
    public SnapshotColumn(
        string name,
        string dataType,
        int? maxLength,
        int? charLength,
        int? precision,
        int? scale,
        bool isNullable,
        string? charUsed,
        string? dataDefault,
        int? columnId)
        : this(name, dataType, maxLength, charLength, precision, scale, isNullable, charUsed, dataDefault, columnId, null)
    {
    }

    public SnapshotColumn(
        string name,
        string dataType,
        int? maxLength,
        int? charLength,
        int? precision,
        int? scale,
        bool isNullable,
        string? charUsed)
        : this(name, dataType, maxLength, charLength, precision, scale, isNullable, charUsed, null, null, null)
    {
    }

    public void Deconstruct(
        out string name,
        out string dataType,
        out int? maxLength,
        out int? charLength,
        out int? precision,
        out int? scale,
        out bool isNullable,
        out string? charUsed)
    {
        name = Name;
        dataType = DataType;
        maxLength = MaxLength;
        charLength = CharLength;
        precision = Precision;
        scale = Scale;
        isNullable = IsNullable;
        charUsed = CharUsed;
    }
}

/// <summary>
/// A violation in the baseline file. Entries written since fingerprint v2 carry <see cref="Fingerprint"/>
/// (<see cref="BaselineManager.ComputeFingerprint"/>) and <see cref="Count"/> (how many identical findings are accepted);
/// legacy entries without a fingerprint match by <c>RuleId:Message</c>.
/// </summary>
[method: JsonConstructor]
public record BaselineViolation(
    string RuleId,
    string Message,
    string Severity,
    BaselineLocation? Location,
    IReadOnlyDictionary<string, object?>? Properties,
    string? Fingerprint = null,
    int? Count = null)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BaselineViolation"/> class without a fingerprint (binary compatibility).
    /// </summary>
    public BaselineViolation(
        string ruleId,
        string message,
        string severity,
        BaselineLocation? location,
        IReadOnlyDictionary<string, object?>? properties)
        : this(ruleId, message, severity, location, properties, null, null)
    {
    }

    public void Deconstruct(
        out string ruleId,
        out string message,
        out string severity,
        out BaselineLocation? location,
        out IReadOnlyDictionary<string, object?>? properties)
    {
        ruleId = RuleId;
        message = Message;
        severity = Severity;
        location = Location;
        properties = Properties;
    }
}

/// <summary>
/// Location in baseline file.
/// </summary>
public record BaselineLocation(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

/// <summary>
/// Summary information about a baseline file.
/// </summary>
public record BaselineInfo(
    string FilePath,
    long FileSizeBytes,
    DateTimeOffset LastModified,
    BaselineFile? Baseline,
    string? ErrorMessage = null)
{
    public bool IsValid => Baseline != null;
    public bool HasViolations => Baseline?.Violations?.Count > 0;
}
