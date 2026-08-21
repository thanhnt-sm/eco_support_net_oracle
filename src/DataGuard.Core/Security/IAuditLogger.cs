using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;

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
}

/// <summary>
/// File-based audit logger implementation.
/// </summary>
public sealed class FileAuditLogger : IAuditLogger
{
    private readonly string _logPath;
    private readonly SemaphoreSlim _writeLock = new (1, 1);
    private string? _lastHash;

    private string CheckpointPath => _logPath + ".checkpoint";

    public FileAuditLogger(string? logPath = null)
    {
        _logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "audit.log");

        Directory.CreateDirectory(Path.GetDirectoryName(_logPath) !);
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
            details,
            success,
            errorMessage,
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

    private static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        if (value.Length <= 8)
        {
            return "****";
        }

        return value[..4] + "****" + value[^4..];
    }

    private async Task WriteEntryAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var previousHash = _lastHash ?? await ReadLastHashAsync(cancellationToken);

            var content = JsonSerializer.Serialize(entry with { Hash = null, PreviousHash = null });
            var hash = ComputeHash((previousHash ?? "") + content);

            var chained = entry with { Hash = hash, PreviousHash = previousHash };
            var json = JsonSerializer.Serialize(chained);
            await File.AppendAllTextAsync(_logPath, json + Environment.NewLine, cancellationToken);

            _lastHash = hash;
            await File.WriteAllTextAsync(CheckpointPath, hash, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Verifies the hash-chain integrity of the audit log. Returns false on any tampering.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<bool> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_logPath))
        {
            return true;
        }

        var lines = await File.ReadAllLinesAsync(_logPath, cancellationToken);
        string? previousHash = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AuditEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<AuditEntry>(line);
            }
            catch (JsonException)
            {
                return false;
            }

            if (entry == null)
            {
                return false;
            }

            var content = JsonSerializer.Serialize(entry with { Hash = null, PreviousHash = null });
            var expected = ComputeHash((previousHash ?? "") + content);
            if (!string.Equals(entry.Hash, expected, StringComparison.Ordinal))
            {
                return false;
            }

            previousHash = entry.Hash;
        }

        // Detect tail truncation: the log's last hash must match the checkpoint.
        if (File.Exists(CheckpointPath))
        {
            var checkpoint = (await File.ReadAllTextAsync(CheckpointPath, cancellationToken)).Trim();
            if (!string.Equals(checkpoint, previousHash, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string?> ReadLastHashAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_logPath))
        {
            return null;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(_logPath, cancellationToken);
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<AuditEntry>(lines[i]);
                if (entry?.Hash != null)
                {
                    return entry.Hash;
                }
            }
        }
        catch (JsonException)
        {
            // One corrupt line must not take down the whole logger.
        }

        return null;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
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
    string? PreviousHash = null);