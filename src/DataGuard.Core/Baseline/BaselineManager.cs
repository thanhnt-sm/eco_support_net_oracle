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
public class BaselineManager
{
    private readonly string _baselineFilePath;
    private static readonly MemoryCache _schemaHashCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 10000,
        ExpirationScanFrequency = TimeSpan.FromMinutes(5)
    });
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
            Version: 2,
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

    /// <summary>
    /// Loads baseline using memory-mapped file for large files.
    /// </summary>
    private async Task<BaselineFile?> LoadWithMemoryMappedFileAsync(CancellationToken cancellationToken)
    {
        using var mmf = MemoryMappedFile.CreateFromFile(_baselineFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        
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

    private async Task SaveAsync(BaselineFile baseline)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(baseline, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        
        if (bytes.Length > 1024 * 1024)
        {
            await SaveWithMemoryMappedFileAsync(bytes);
        }
        else
        {
            await File.WriteAllTextAsync(_baselineFilePath, json);
        }
    }

    private async Task SaveWithMemoryMappedFileAsync(byte[] data)
    {
        var tempPath = _baselineFilePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, data);
            
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
    /// Computes a schema hash for the given violations.
    /// </summary>
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
    /// Filters violations to only return new ones not in baseline.
    /// </summary>
    public IEnumerable<ContractViolation> FilterNewViolations(IEnumerable<ContractViolation> current, BaselineFile baseline)
    {
        var baselineSignatures = new HashSet<string>(
            baseline.Violations.Select(v => $"{v.RuleId}:{v.Message}"));
        
        return current.Where(v => !baselineSignatures.Contains($"{v.RuleId}:{v.Message}"));
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
public record BaselineInfo(
    string FilePath,
    long FileSizeBytes,
    DateTimeOffset LastModified,
    BaselineFile? Baseline,
    string? ErrorMessage = null
)
{
    public bool IsValid => Baseline != null;
    public bool HasViolations => Baseline?.Violations?.Count > 0;
}