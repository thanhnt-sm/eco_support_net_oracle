using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;

namespace DataGuard.Core.Security;

/// <summary>
/// Interface for audit logging of security-relevant operations.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a database operation for audit trail.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task LogDatabaseOperationAsync(
        string operation,
        string provider,
        string connectionStringHash,
        string details,
        bool success,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a credential access event.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task LogCredentialAccessAsync(
        string operation,
        string provider,
        string connectionStringHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a configuration change.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task LogConfigurationChangeAsync(
        string setting,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a security event (credential stored, rotation detected, ...) with secret-free details. The default
    /// implementation records it as a credential access so existing implementations keep a single writer path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task LogSecurityEventAsync(
        string eventType,
        string source,
        string details,
        CancellationToken cancellationToken = default)
        => LogCredentialAccessAsync(eventType, source, string.Empty, cancellationToken);

    /// <summary>
    /// Returns a short, non-reversible fingerprint of a sensitive value (a connection string or credential name) for
    /// correlation inside the audit log. <see cref="FileAuditLogger"/> salts it per log file; the default is unsalted.
    /// </summary>
    /// <returns>16 upper-case hex characters.</returns>
    string HashSensitiveValue(string value) => AuditHashing.UnsaltedFingerprint(value);
}

/// <summary>Outcome of <see cref="FileAuditLogger.VerifyIntegrityAsync"/>.</summary>
public enum AuditIntegrityStatus
{
    /// <summary>Every entry is HMAC-SHA256 chained with the configured key and the checkpoint matches.</summary>
    Valid,

    /// <summary>
    /// The chain is consistent but at least one entry uses the unkeyed SHA-256 chain (no audit key was configured when it
    /// was written, or the verifier has no key). Anyone with write access to the directory could have rewritten it.
    /// </summary>
    Unkeyed,

    /// <summary>An entry, the chain order or the checkpoint does not match: the log was modified or truncated.</summary>
    Tampered,

    /// <summary>The log contains HMAC entries but the verifier has no key, so they cannot be checked.</summary>
    KeyRequired,
}

/// <summary>Result of verifying an audit log.</summary>
/// <param name="Status">Verification outcome.</param>
/// <param name="EntryCount">Number of entries read.</param>
/// <param name="Reason">Human-readable reason for any status other than <see cref="AuditIntegrityStatus.Valid"/>.</param>
public sealed record AuditIntegrityResult(AuditIntegrityStatus Status, int EntryCount, string? Reason = null)
{
    /// <summary>True when no tampering was detected (keyed or unkeyed chain intact).</summary>
    public bool IsIntact => Status is AuditIntegrityStatus.Valid or AuditIntegrityStatus.Unkeyed;
}

/// <summary>Fingerprint helpers shared by audit writers.</summary>
public static class AuditHashing
{
    /// <summary>Environment variable holding the audit HMAC key (trimmed UTF-8 text, at least 16 bytes).</summary>
    public const string AuditKeyEnvironmentVariable = "DATAGUARD_AUDIT_KEY";

    /// <summary>Minimum audit key length in bytes.</summary>
    public const int MinimumKeyBytes = 16;

    /// <summary>Unsalted SHA-256 fingerprint, first 16 hex characters. Only for loggers without a per-file salt.</summary>
    /// <returns>16 upper-case hex characters.</returns>
    public static string UnsaltedFingerprint(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    }

    /// <summary>
    /// Resolves the audit HMAC key: <see cref="AuditKeyEnvironmentVariable"/> first, then <paramref name="auditKeyFile"/>.
    /// Returns null when neither is set. Throws <see cref="InvalidOperationException"/> when the key file is missing or a
    /// key is shorter than <see cref="MinimumKeyBytes"/>, so a misconfigured key never silently downgrades the chain.
    /// </summary>
    /// <returns>The key bytes, or null for an unkeyed chain.</returns>
    public static byte[]? ResolveKey(string? auditKeyFile, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var environmentKey = getEnvironmentVariable(AuditKeyEnvironmentVariable)?.Trim();
        if (!string.IsNullOrEmpty(environmentKey))
        {
            return ValidateKey(Encoding.UTF8.GetBytes(environmentKey), AuditKeyEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(auditKeyFile))
        {
            return null;
        }

        if (!File.Exists(auditKeyFile))
        {
            throw new InvalidOperationException($"AuditKeyFile '{auditKeyFile}' does not exist.");
        }

        var fileKey = File.ReadAllText(auditKeyFile).Trim();
        return ValidateKey(Encoding.UTF8.GetBytes(fileKey), "AuditKeyFile");
    }

    private static byte[] ValidateKey(byte[] key, string source)
    {
        if (key.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException($"The audit key from {source} must be at least {MinimumKeyBytes} bytes.");
        }

        return key;
    }
}

/// <summary>
/// File-based audit logger: newline-delimited JSON entries chained by hash. With a key (<c>DATAGUARD_AUDIT_KEY</c> or
/// <c>AuditKeyFile</c>) each link is HMAC-SHA256, so an attacker who can write the directory but does not hold the key
/// cannot forge or re-chain entries; without a key the chain is SHA-256 and only detects accidental or naive edits.
/// Every instance (and every <see cref="CredentialManager"/> that writes through one) appends under the same per-path
/// lock and re-reads the chain tail before each write, so several writers in one process keep one valid chain.
/// The checkpoint file (<c>&lt;log&gt;.checkpoint</c>) records the last hash, whether the chain is keyed, and the
/// per-file salt used by <see cref="HashSensitiveValue"/>.
/// </summary>
public sealed class FileAuditLogger : IAuditLogger
{
    /// <summary>Per-entry marker for HMAC-SHA256 links.</summary>
    public const string KeyedAlgorithm = "HMAC-SHA256";

    private const string CheckpointFormat = "dataguard-audit-checkpoint/2";
    private const int TailReadBytes = 256 * 1024;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _logPath;
    private readonly byte[]? _key;
    private readonly SemaphoreSlim _writeLock;

    private string CheckpointPath => _logPath + ".checkpoint";

    /// <summary>
    /// Initializes a new instance of the <see cref="FileAuditLogger"/> class at <paramref name="logPath"/> (default:
    /// ApplicationData/DataGuard/audit.log), keyed from <c>DATAGUARD_AUDIT_KEY</c> when that variable is set.
    /// </summary>
    public FileAuditLogger(string? logPath = null)
        : this(logPath, AuditHashing.ResolveKey(auditKeyFile: null))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileAuditLogger"/> class with an explicit HMAC key; null keeps the
    /// unkeyed SHA-256 chain.
    /// </summary>
    public FileAuditLogger(string? logPath, byte[]? hmacKey)
    {
        _logPath = Path.GetFullPath(logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "audit.log"));
        if (hmacKey is { Length: < AuditHashing.MinimumKeyBytes })
        {
            throw new ArgumentException($"The audit key must be at least {AuditHashing.MinimumKeyBytes} bytes.", nameof(hmacKey));
        }

        _key = hmacKey is null ? null : (byte[])hmacKey.Clone();
        _writeLock = PathLocks.GetOrAdd(_logPath, static _ => new SemaphoreSlim(1, 1));

        ValidateLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        ValidateLogPath();
    }

    /// <summary>True when entries are chained with HMAC-SHA256.</summary>
    public bool IsKeyed => _key is not null;

    /// <summary>
    /// Creates the logger for <paramref name="config"/>: <see cref="DataGuardConfiguration.AuditLogPath"/> and the key
    /// from <c>DATAGUARD_AUDIT_KEY</c> or <see cref="DataGuardConfiguration.AuditKeyFile"/>.
    /// </summary>
    /// <returns>The configured logger.</returns>
    public static FileAuditLogger Create(DataGuardConfiguration config, Func<string, string?>? getEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new FileAuditLogger(config.AuditLogPath, AuditHashing.ResolveKey(config.AuditKeyFile, getEnvironmentVariable));
    }

    public async Task LogDatabaseOperationAsync(
        string operation,
        string provider,
        string connectionStringHash,
        string details,
        bool success,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            DateTimeOffset.UtcNow,
            "DatabaseOperation",
            operation,
            provider,
            connectionStringHash,
            SanitizeSensitiveText(details) ?? string.Empty,
            success,
            SanitizeSensitiveText(errorMessage),
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId);

        await WriteEntryAsync(entry, cancellationToken);
    }

    public async Task LogCredentialAccessAsync(
        string operation,
        string provider,
        string connectionStringHash,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            DateTimeOffset.UtcNow,
            "CredentialAccess",
            operation,
            provider,
            connectionStringHash,
            $"Credential access: {operation}",
            true,
            null,
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId);

        await WriteEntryAsync(entry, cancellationToken);
    }

    public async Task LogConfigurationChangeAsync(
        string setting,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            DateTimeOffset.UtcNow,
            "ConfigurationChange",
            "SettingChanged",
            "DataGuard",
            "",
            $"Setting: {setting}, Old: {MaskValue(oldValue)}, New: {MaskValue(newValue)}",
            true,
            null,
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId);

        await WriteEntryAsync(entry, cancellationToken);
    }

    public async Task LogSecurityEventAsync(
        string eventType,
        string source,
        string details,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            DateTimeOffset.UtcNow,
            "SecurityEvent",
            eventType,
            source,
            "",
            SanitizeSensitiveText(details) ?? string.Empty,
            true,
            null,
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId);

        await WriteEntryAsync(entry, cancellationToken);
    }

    /// <summary>
    /// HMAC-SHA256 of <paramref name="value"/> under this log file's random salt (created on first use and stored in
    /// the checkpoint), first 16 hex characters. Equal values correlate within one log; they do not across logs, and a
    /// dictionary of candidate connection strings cannot be precomputed.
    /// </summary>
    /// <returns>16 upper-case hex characters.</returns>
    public string HashSensitiveValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _writeLock.Wait();
        try
        {
            var checkpoint = ReadCheckpoint();
            var salt = checkpoint?.SaltBytes;
            if (salt is null)
            {
                salt = RandomNumberGenerator.GetBytes(32);
                WriteCheckpoint(new Checkpoint(CheckpointFormat, Convert.ToBase64String(salt), checkpoint?.Keyed ?? IsKeyed, checkpoint?.LastHash));
            }

            return Convert.ToHexString(HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(value)))[..16];
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Keeps at most four characters of a value (two leading, two trailing) and only when it is longer than 12.</summary>
    internal static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        if (value.Length <= 12)
        {
            return "****";
        }

        return value[..2] + "****" + value[^2..];
    }

    private static string? SanitizeSensitiveText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var sanitized = Regex.Replace(
            value,
            @"(?ix)\b(password|passwd|pwd|secret|token|api[_ -]?key|connection[_ -]?string)\s*[""']?\s*([=:])\s*[""'][^""']*[""']",
            "$1$2[REDACTED]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?ix)\b(password|passwd|pwd|secret|token|api[_ -]?key|connection[_ -]?string)\s*[""']?\s*([=:])\s*[""']?[^""'\s;,]+",
            "$1$2[REDACTED]");
        sanitized = Regex.Replace(sanitized, @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+", "bearer [REDACTED]");
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096];
    }

    private void ValidateLogPath()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(_logPath)!);
        if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Audit log directory is a symbolic link or reparse point.");
        }

        if (File.Exists(_logPath) && File.GetAttributes(_logPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Audit log file is a symbolic link or reparse point.");
        }
    }

    private async Task WriteEntryAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            ValidateLogPath();
            await using var stream = await OpenExclusiveAsync(cancellationToken);

            // Re-read the tail on every write: another instance (or process) may have appended since our last entry.
            var (previousHash, endsWithNewline) = await ReadLastHashAsync(stream, cancellationToken);

            var algorithm = _key is null ? null : KeyedAlgorithm;
            var unsigned = entry with { Hash = null, PreviousHash = null, HashAlgorithm = algorithm };
            var hash = ComputeLink(previousHash, JsonSerializer.Serialize(unsigned), _key);
            var json = JsonSerializer.Serialize(unsigned with { Hash = hash, PreviousHash = previousHash });

            stream.Seek(0, SeekOrigin.End);
            var line = (endsWithNewline ? string.Empty : Environment.NewLine) + json + Environment.NewLine;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line), cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var checkpoint = ReadCheckpoint();
            WriteCheckpoint(new Checkpoint(
                CheckpointFormat,
                checkpoint?.SaltBytes is null ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : checkpoint.Salt,
                IsKeyed,
                hash));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<FileStream> OpenExclusiveAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // FileShare.Read: other processes may read (verify) but not append concurrently.
                return new FileStream(_logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, useAsync: true);
            }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(25 * attempt, cancellationToken);
            }
        }
    }

    private static async Task<(string? Hash, bool EndsWithNewline)> ReadLastHashAsync(FileStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length == 0)
        {
            return (null, true);
        }

        var length = (int)Math.Min(stream.Length, TailReadBytes);
        stream.Seek(-length, SeekOrigin.End);
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        var endsWithNewline = read > 0 && buffer[read - 1] == (byte)'\n';
        var lines = Encoding.UTF8.GetString(buffer, 0, read).Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<AuditEntry>(line);
                if (parsed?.Hash != null)
                {
                    return (parsed.Hash, endsWithNewline);
                }
            }
            catch (JsonException)
            {
                // One corrupt line must not take down the whole logger; chain from the last valid entry.
            }
        }

        return (null, endsWithNewline);
    }

    /// <summary>
    /// Verifies the hash-chain integrity of the audit log: every link, the keyed/unkeyed downgrade rule (an unkeyed
    /// entry after a keyed one is tampering) and the checkpoint (tail truncation).
    /// </summary>
    /// <returns><see cref="AuditIntegrityStatus.Valid"/> for an intact keyed chain, <see cref="AuditIntegrityStatus.Unkeyed"/> for an intact chain with SHA-256 links (or an empty log without a key).</returns>
    public async Task<AuditIntegrityResult> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var emptyStatus = IsKeyed ? AuditIntegrityStatus.Valid : AuditIntegrityStatus.Unkeyed;
        if (!File.Exists(_logPath))
        {
            return new AuditIntegrityResult(emptyStatus, 0, IsKeyed ? null : "no audit key configured");
        }

        string[] lines;
        using (var reader = new StreamReader(new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
        {
            lines = (await reader.ReadToEndAsync(cancellationToken)).Split('\n');
        }

        string? previousHash = null;
        var count = 0;
        var sawKeyed = false;
        var sawUnkeyed = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            count++;
            AuditEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<AuditEntry>(line);
            }
            catch (JsonException)
            {
                return Tampered(count, "entry is not valid JSON");
            }

            if (entry?.Hash is null)
            {
                return Tampered(count, "entry has no hash");
            }

            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return Tampered(count, "entry does not chain to the previous entry");
            }

            byte[]? key;
            if (entry.HashAlgorithm is null)
            {
                if (sawKeyed)
                {
                    return Tampered(count, "unkeyed entry after a keyed entry");
                }

                sawUnkeyed = true;
                key = null;
            }
            else if (string.Equals(entry.HashAlgorithm, KeyedAlgorithm, StringComparison.Ordinal))
            {
                if (_key is null)
                {
                    return new AuditIntegrityResult(AuditIntegrityStatus.KeyRequired, count, $"entry {count} is HMAC-chained; set {AuditHashing.AuditKeyEnvironmentVariable} or AuditKeyFile to verify it");
                }

                sawKeyed = true;
                key = _key;
            }
            else
            {
                return Tampered(count, $"unknown hash algorithm '{entry.HashAlgorithm}'");
            }

            var expected = ComputeLink(previousHash, JsonSerializer.Serialize(entry with { Hash = null, PreviousHash = null }), key);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(entry.Hash), Encoding.ASCII.GetBytes(expected)))
            {
                return Tampered(count, "entry hash does not match its content");
            }

            previousHash = entry.Hash;
        }

        // Detect tail truncation: the log's last hash must match the checkpoint.
        var checkpoint = ReadCheckpoint();
        if (checkpoint is not null)
        {
            if (!string.Equals(checkpoint.LastHash, previousHash, StringComparison.Ordinal))
            {
                return Tampered(count, "checkpoint does not match the last entry (truncated log)");
            }

            if (checkpoint.Keyed && count > 0 && !sawKeyed)
            {
                return Tampered(count, "checkpoint records a keyed chain but no entry is keyed");
            }
        }

        if (sawUnkeyed || (count == 0 && !IsKeyed))
        {
            return new AuditIntegrityResult(AuditIntegrityStatus.Unkeyed, count, "chain is SHA-256 without a key; set DATAGUARD_AUDIT_KEY or AuditKeyFile");
        }

        return new AuditIntegrityResult(AuditIntegrityStatus.Valid, count);

        static AuditIntegrityResult Tampered(int entry, string reason) =>
            new(AuditIntegrityStatus.Tampered, entry, $"entry {entry}: {reason}");
    }

    private static string ComputeLink(string? previousHash, string content, byte[]? key)
    {
        var bytes = Encoding.UTF8.GetBytes((previousHash ?? string.Empty) + content);
        return Convert.ToHexString(key is null ? SHA256.HashData(bytes) : HMACSHA256.HashData(key, bytes));
    }

    private Checkpoint? ReadCheckpoint()
    {
        if (!File.Exists(CheckpointPath))
        {
            return null;
        }

        var text = File.ReadAllText(CheckpointPath).Trim();
        if (!text.StartsWith('{'))
        {
            // Legacy checkpoint: the bare last hash, no salt, unkeyed.
            return new Checkpoint(null, null, false, text.Length == 0 ? null : text);
        }

        try
        {
            return JsonSerializer.Deserialize<Checkpoint>(text);
        }
        catch (JsonException)
        {
            // A corrupt checkpoint verifies as a mismatch (its LastHash is unknown).
            return new Checkpoint(null, null, false, "<corrupt>");
        }
    }

    private void WriteCheckpoint(Checkpoint checkpoint)
    {
        var temporaryPath = CheckpointPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(checkpoint));
            File.Move(temporaryPath, CheckpointPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Checkpoint header persisted next to the log.</summary>
    private sealed record Checkpoint(string? Format, string? Salt, bool Keyed, string? LastHash)
    {
        [JsonIgnore]
        public byte[]? SaltBytes
        {
            get
            {
                if (string.IsNullOrEmpty(Salt))
                {
                    return null;
                }

                try
                {
                    var bytes = Convert.FromBase64String(Salt);
                    return bytes.Length >= 16 ? bytes : null;
                }
                catch (FormatException)
                {
                    return null;
                }
            }
        }
    }
}

/// <summary>
/// Null audit logger for when audit logging is disabled.
/// </summary>
public sealed class NullAuditLogger : IAuditLogger
{
    public Task LogDatabaseOperationAsync(string operation, string provider, string connectionStringHash, string details, bool success, string? errorMessage = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task LogCredentialAccessAsync(string operation, string provider, string connectionStringHash, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task LogConfigurationChangeAsync(string setting, string? oldValue, string? newValue, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task LogSecurityEventAsync(string eventType, string source, string details, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Audit log entry.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string EventType,
    string Operation,
    string Provider,
    string ConnectionStringHash,
    string Details,
    bool Success,
    string? ErrorMessage = null,
    string MachineName = "",
    string UserName = "",
    int ProcessId = 0,
    string? Hash = null,
    string? PreviousHash = null)
{
    /// <summary>
    /// <see cref="FileAuditLogger.KeyedAlgorithm"/> for HMAC-SHA256 links; null (omitted from JSON, so entries written
    /// before this field existed verify unchanged) for the unkeyed SHA-256 chain. Covered by the link itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HashAlgorithm { get; init; }
}
