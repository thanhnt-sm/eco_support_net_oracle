using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataGuard.Core.Security;

/// <summary>
/// Interface for audit logging of security-relevant operations.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a database operation for audit trail.
    /// </summary>
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
    Task LogCredentialAccessAsync(
        string operation,
        string provider,
        string connectionStringHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a configuration change.
    /// </summary>
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
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileAuditLogger(string? logPath = null)
    {
        _logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "audit.log");
        
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
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
        if (string.IsNullOrEmpty(value)) return "<empty>";
        if (value.Length <= 8) return "****";
        return value[..4] + "****" + value[^4..];
    }

    private async Task WriteEntryAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "audit.log");

        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        var json = System.Text.Json.JsonSerializer.Serialize(entry);
        await File.AppendAllTextAsync(_logPath, json + Environment.NewLine, cancellationToken);
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
    int ProcessId = 0
);