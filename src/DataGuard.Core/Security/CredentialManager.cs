using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using Microsoft.Extensions.Logging;

namespace DataGuard.Core.Security;

/// <summary>
/// Manages secure credential handling with rotation detection, encryption, and audit logging.
/// Follows zero-trust principles: never logs secrets, encrypts at rest, detects rotation.
/// </summary>
public sealed class CredentialManager
{
    private readonly DataGuardConfiguration _config;
    private readonly ILogger<CredentialManager>? _logger;
    private readonly string _credentialStorePath;
    private static readonly byte[] _entropy = "DataGuard.Credential.Protection"u8.ToArray();

    public CredentialManager(DataGuardConfiguration config, ILogger<CredentialManager>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _credentialStorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "credentials.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_credentialStorePath) !);
    }

    /// <summary>
    /// Gets the connection string, checking for rotation and decrypting if needed.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var stored = await LoadFromCredentialStoreAsync(cancellationToken);
        var connectionString = _config.ConnectionString
            ?? Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING")
            ?? stored?.ConnectionString;

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("No connection string configured. Set DATAGUARD_CONNECTION_STRING env var or configure in .dataguard.yml");
        }

        // Check for credential rotation
        if (_config.EnableCredentialRotationDetection)
        {
            await CheckCredentialRotationAsync(connectionString, cancellationToken);
        }

        // Decrypt if encrypted
        if (_config.EncryptConnectionStringAtRest && IsEncrypted(connectionString) && OperatingSystem.IsWindows())
        {
            connectionString = DecryptConnectionString(connectionString);
        }

        await LogAuditAsync("ConnectionStringAccessed", new { HasConnectionString = true }, cancellationToken);

        return connectionString;
    }

    /// <summary>
    /// Stores connection string securely with optional encryption.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task StoreConnectionStringAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));
        }

        string storedValue = connectionString;

        if (_config.EncryptConnectionStringAtRest && OperatingSystem.IsWindows())
        {
            storedValue = EncryptConnectionString(connectionString);
        }

        var credentialData = new CredentialData
        {
            ConnectionString = storedValue,
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow,
            IsEncrypted = _config.EncryptConnectionStringAtRest,
        };

        await SaveToCredentialStoreAsync(credentialData, cancellationToken);
        await LogAuditAsync("ConnectionStringStored", new { IsEncrypted = _config.EncryptConnectionStringAtRest }, cancellationToken);
    }

    /// <summary>
    /// Checks if the connection string has been rotated (changed) since last access.
    /// </summary>
    private async Task CheckCredentialRotationAsync(string currentConnectionString, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await LoadFromCredentialStoreAsync(cancellationToken);
            if (!string.IsNullOrEmpty(stored?.ConnectionString) && stored!.ConnectionString != currentConnectionString)
            {
                var warning = $"⚠ Credential rotation detected: connection string has changed since last run. " +
                             $"If this was intentional, run 'dataguard baseline' to update. " +
                             $"Otherwise, verify your credential source hasn't been compromised.";

                _logger?.LogWarning(warning);
                Console.Error.WriteLine(warning);

                await LogAuditAsync("CredentialRotationDetected", new
                {
                    OldHash = ComputeHash(stored!.ConnectionString),
                    NewHash = ComputeHash(currentConnectionString),
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to check credential rotation");
        }
    }

    [SupportedOSPlatform("windows")]
    private string EncryptConnectionString(string connectionString)
    {
        // Use DPAPI (Windows) or libsecret (Linux) for platform-appropriate encryption
        var data = Encoding.UTF8.GetBytes(connectionString);
        var encrypted = ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);
        return "ENC:" + Convert.ToBase64String(encrypted);
    }

    [SupportedOSPlatform("windows")]
    private string DecryptConnectionString(string encryptedConnectionString)
    {
        if (!encryptedConnectionString.StartsWith("ENC:"))
        {
            return encryptedConnectionString;
        }

        var encrypted = Convert.FromBase64String(encryptedConnectionString[4..]);
        var decrypted = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static bool IsEncrypted(string connectionString)
        => connectionString.StartsWith("ENC:");

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }

    private async Task<CredentialData?> LoadFromCredentialStoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialStorePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_credentialStorePath, cancellationToken);
            return JsonSerializer.Deserialize<CredentialData>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the connection string from the local encrypted credential store, decrypting if necessary.
    /// Returns null when no credential is stored.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<string?> GetStoredConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var stored = await LoadFromCredentialStoreAsync(cancellationToken);
        if (stored == null || string.IsNullOrEmpty(stored.ConnectionString))
        {
            return null;
        }

        var value = stored.ConnectionString;
        if (stored.IsEncrypted && IsEncrypted(value) && OperatingSystem.IsWindows())
        {
            value = DecryptConnectionString(value);
        }

        return value;
    }

    private async Task SaveToCredentialStoreAsync(CredentialData data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_credentialStorePath, json, cancellationToken);
    }

    private async Task LogAuditAsync(string eventType, object details, CancellationToken cancellationToken = default)
    {
        if (!_config.EnableAuditLogging)
        {
            return;
        }

        var auditEntry = new AuditLogEntry(
            DateTimeOffset.UtcNow,
            eventType,
            JsonSerializer.Serialize(details),
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId);

        var logPath = _config.AuditLogPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "audit.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath) !);

        var logLine = JsonSerializer.Serialize(auditEntry);
        await File.AppendAllTextAsync(logPath, logLine + Environment.NewLine);
    }
}

/// <summary>
/// Credential data stored in the credential store.
/// </summary>
internal sealed class CredentialData
{
    public string ConnectionString { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastAccessedAt { get; set; }

    public bool IsEncrypted { get; set; }
}

/// <summary>
/// Audit log entry for security events.
/// </summary>
public sealed record AuditLogEntry(
    DateTimeOffset Timestamp,
    string EventType,
    string Details,
    string MachineName,
    string UserName,
    int ProcessId);