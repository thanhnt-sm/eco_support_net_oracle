using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.Caching;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Baseline;

/// <summary>
/// Manages baseline files for legacy codebases.
/// Supports database version tracking and schema hash for drift detection.
/// </summary>
public class BaselineManager
{
    private readonly string _baselineFilePath;
    private static readonly MemoryCache _schemaHashCache = new MemoryCache("DataGuard.SchemaHashCache");
    private static readonly ConcurrentDictionary<string, string> _fileHashCache = new();

    public BaselineManager(string baselineFilePath)
    {
        _baselineFilePath = baselineFilePath ?? throw new ArgumentNullException(nameof(baselineFilePath));
    }

    /// <summary>
    /// Creates a new baseline from current violations.
    /// </summary>
    public async Task<BaselineFile> CreateBaselineAsync(
        IEnumerable<ContractViolation> violations,
        string schemaVersion,
        string groundTruthMode,
        string? databaseVersion = null,
        string? schemaHash = null,
        CancellationToken cancellationToken = default)
    {
        var baselineViolations = violations.Select(v => new BaselineViolation(
            v.RuleId,
            v.Message,
            v.Severity.ToString(),
            v.Location != null ? new BaselineLocation(
                v.Location.SourceTree?.FilePath ?? "",
                v.Location.GetLineSpan().StartLinePosition.Line + 1,
                v.Location.GetLineSpan().StartLinePosition.Character + 1,
                v.Location.GetLineSpan().EndLinePosition.Line + 1,
                v.Location.GetLineSpan().EndLinePosition.Character + 1
            ) : null,
            v.Properties?.ToImmutableDictionary()
        )).ToList();

        var computedSchemaHash = schemaHash ?? ComputeSchemaHash(violations);
        var dbVersion = databaseVersion ?? "unknown";

        var baseline = new BaselineFile(
            Version: 2, // Version 2 includes DB version and schema hash
            CreatedAt: DateTimeOffset.UtcNow,
            SchemaVersion: schemaVersion,
            GroundTruthMode: groundTruthMode,
            DatabaseVersion: dbVersion,
            SchemaHash: computedSchemaHash,
            Violations: baselineViolations
        );

        await SaveAsync(baseline);
        return baseline;
    }

    /// <summary>
    /// Loads an existing baseline file.
    /// </summary>
    public async Task<BaselineFile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselineFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
            return JsonSerializer.Deserialize<BaselineFile>(json, options);
        }
        catch (JsonException)
        {
            // Try to load as version 1 format (without DB version/hash)
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
    /// Checks if the baseline database version matches the current database version.
    /// Returns a warning message if they don't match, null if they match or baseline has no version.
    /// </summary>
    public static string? CheckVersionMismatch(BaselineFile? baseline, string currentDatabaseVersion)
    {
        if (baseline == null || string.IsNullOrEmpty(baseline.DatabaseVersion) || baseline.DatabaseVersion == "unknown")
            return null;

        if (string.IsNullOrEmpty(currentDatabaseVersion) || currentDatabaseVersion == "unknown")
            return null;

        // Normalize versions for comparison (extract major.minor)
        var baselineMajorMinor = ExtractMajorMinor(baseline.DatabaseVersion);
        var currentMajorMinor = ExtractMajorMinor(currentDatabaseVersion);

        if (!string.Equals(baselineMajorMinor, currentMajorMinor, StringComparison.OrdinalIgnoreCase))
        {
            return $"⚠ Database version mismatch: baseline was created with {baseline.DatabaseVersion}, " +
                   $"but current database is {currentDatabaseVersion}. " +
                   $"Schema differences may cause false positives/negatives. " +
                   $"Consider running 'dataguard snapshot refresh' to update.";
        }

        return null;
    }

    /// <summary>
    /// Checks if the schema hash matches the current violations.
    /// Returns a warning message if they don't match, null if they match or baseline has no hash.
    /// </summary>
    public static string? CheckSchemaHashMismatch(BaselineFile? baseline, IEnumerable<ContractViolation> currentViolations)
    {
        if (baseline == null || string.IsNullOrEmpty(baseline.SchemaHash))
            return null;

        var currentHash = ComputeSchemaHash(currentViolations);
        if (!string.Equals(baseline.SchemaHash, currentHash, StringComparison.Ordinal))
        {
            return $"⚠ Schema hash mismatch: baseline schema has changed since baseline was created. " +
                   $"Baseline hash: {baseline.SchemaHash}, Current hash: {currentHash}. " +
                   $"Consider running 'dataguard snapshot refresh' to update.";
        }

        return null;
    }

    /// <summary>
    /// Filters violations against baseline - only returns new violations not in baseline.
    /// </summary>
    public IEnumerable<ContractViolation> FilterNewViolations(
        IEnumerable<ContractViolation> violations,
        BaselineFile? baseline)
    {
        if (baseline == null)
            return violations;

        var baselineKeys = new HashSet<string>(baseline.Violations.Select(GetViolationKey));
        return violations.Where(v => !baselineKeys.Contains(GetViolationKey(v)));
    }

    /// <summary>
    /// Gets baseline info for display (version, DB version, schema hash, etc.)
    /// </summary>
    public async Task<BaselineInfo?> GetBaselineInfoAsync(CancellationToken cancellationToken = default)
    {
        var baseline = await LoadAsync();
        if (baseline == null) return null;

        return new BaselineInfo
        {
            FilePath = _baselineFilePath,
            Version = baseline.Version,
            CreatedAt = baseline.CreatedAt,
            SchemaVersion = baseline.SchemaVersion,
            GroundTruthMode = baseline.GroundTruthMode,
            DatabaseVersion = baseline.DatabaseVersion,
            SchemaHash = baseline.SchemaHash,
            ViolationCount = baseline.Violations.Count
        };
    }

    private static string ComputeSchemaHash(IEnumerable<ContractViolation> violations)
    {
        // Create cache key from violation signatures
        var cacheKey = CreateCacheKey(violations);
        
        // Check memory cache first
        if (_schemaHashCache.Contains(cacheKey))
        {
            return (string)_schemaHashCache.Get(cacheKey)!;
        }

        // Check file-based cache
        var fileCacheKey = GetFileCacheKey(violations);
        if (_fileHashCache.TryGetValue(fileCacheKey, out var cachedHash))
        {
            // Promote to memory cache
            _schemaHashCache.Set(cacheKey, cachedHash, new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.AddHours(1) });
            return cachedHash;
        }

        // Compute hash
        var data = string.Join("|", violations
            .OrderBy(v => v.RuleId)
            .ThenBy(v => v.Message)
            .Select(v => $"{v.RuleId}:{v.Message}"));
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        var hashString = Convert.ToHexString(hash)[..16]; // First 16 chars (64 bits)

        // Cache in memory (1 hour) and file cache
        _schemaHashCache.Set(cacheKey, hashString, new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.AddHours(1) });
        _fileHashCache[fileCacheKey] = hashString;
        
        return hashString;
    }

    private static string CreateCacheKey(IEnumerable<ContractViolation> violations)
    {
        // Create a deterministic cache key from violation signatures
        using var sha256 = SHA256.Create();
        var sb = new System.Text.StringBuilder();
        foreach (var v in violations.OrderBy(v => v.RuleId).ThenBy(v => v.Message))
        {
            sb.Append(v.RuleId).Append(':').Append(v.Message).Append('|');
        }
        var data = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }

    private static string GetFileCacheKey(IEnumerable<ContractViolation> violations)
    {
        // File-based cache key includes file path and last write time for invalidation
        var key = CreateCacheKey(violations);
        return $"{key}_{File.GetLastWriteTimeUtc(typeof(BaselineManager).Assembly.Location).Ticks}";
    }

    private static string ExtractMajorMinor(string version)
    {
        // Extract major.minor from version strings like "19.0.0.0.0" or "Oracle Database 19c Enterprise Edition Release 19.0.0.0.0"
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
            Violations: legacy.Violations
        );
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

    private async Task SaveAsync(BaselineFile baseline)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(baseline, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        
        // Use memory-mapped file for large baseline files (>1MB)
        if (bytes.Length > 1024 * 1024)
        {
            await SaveWithMemoryMappedFileAsync(bytes);
        }
        else
        {
            await File.WriteAllTextAsync(_baselineFilePath, System.Text.Encoding.UTF8.GetString(bytes));
        }
    }

    private async Task SaveWithMemoryMappedFileAsync(byte[] data)
    {
        // Use memory-mapped file for efficient large file writes
        var tempPath = _baselineFilePath + ".tmp";
        try
        {
            // Write to temp file first
            await File.WriteAllBytesAsync(tempPath, data);
            
            // Atomic replace
            if (File.Exists(_baselineFilePath))
            {
                File.Replace(tempPath, _baselineFilePath, null, false);
            }
            else
            {
                File.Move(tempPath, _baselineFilePath);
            }
        }
        finally
        {
            if (File.Exists(_baselineFilePath + ".tmp"))
            {
                try { File.Delete(_baselineFilePath + ".tmp"); } catch { }
            }
        }
    }

    /// <summary>
    /// Loads baseline using memory-mapped file for large files.
    /// </summary>
    public async Task<BaselineFile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselineFilePath))
            return null;

        var fileInfo = new FileInfo(_baselineFilePath);
        if (fileInfo.Length > 1024 * 1024)
        {
            return await LoadWithMemoryMappedFileAsync(cancellationToken);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
            return JsonSerializer.Deserialize<BaselineFile>(json, options);
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

    private async Task<BaselineFile?> LoadWithMemoryMappedFileAsync(CancellationToken cancellationToken)
    {
        // Use memory-mapped file for efficient large file reads
        using var mmf = MemoryMappedFile.CreateFromFile(_baselineFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        
        // Read the entire file into a byte array
        var length = (int)accessor.Capacity;
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, length);
        
        var json = System.Text.Encoding.UTF8.GetString(buffer);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        return JsonSerializer.Deserialize<BaselineFile>(json, options);
    }

    private static string GetViolationKey(BaselineViolation v)
        => $"{v.RuleId}|{v.Message}|{v.Severity}|{v.Location?.FilePath}|{v.Location?.StartLine}";

    private static string GetViolationKey(ContractViolation v)
        => $"{v.RuleId}|{v.Message}|{v.Severity}|{v.Location?.SourceTree?.FilePath}|{v.Location?.GetLineSpan().StartLinePosition.Line}";
}

/// <summary>
/// Legacy baseline file format (version 1) for migration.
/// </summary>
[JsonSerializable(typeof(LegacyBaselineFile))]
internal record LegacyBaselineFile(
    int Version,
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    IReadOnlyList<BaselineViolation> Violations
);

/// <summary>
/// Baseline file format.
/// Version 2 adds DatabaseVersion and SchemaHash for drift detection.
/// </summary>
[JsonSerializable(typeof(BaselineFile))]
public record BaselineFile(
    int Version,
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    string DatabaseVersion,
    string SchemaHash,
    IReadOnlyList<BaselineViolation> Violations
);

/// <summary>
/// A violation in the baseline file.
/// </summary>
public record BaselineViolation(
    string RuleId,
    string Message,
    string Severity,
    BaselineLocation? Location,
    IReadOnlyDictionary<string, object?>? Properties
);

/// <summary>
/// Location in baseline file.
/// </summary>
public record BaselineLocation(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn
);

/// <summary>
/// Summary information about a baseline file.
/// </summary>
public record BaselineInfo
{
    public string FilePath { get; init; } = "";
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string SchemaVersion { get; init; } = "";
    public string GroundTruthMode { get; init; } = "";
    public string DatabaseVersion { get; init; } = "";
    public string SchemaHash { get; init; } = "";
    public int ViolationCount { get; init; }
};