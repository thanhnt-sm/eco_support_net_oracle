using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using DataGuard.Core.Security.SecretStores;
using Microsoft.Extensions.Logging;

namespace DataGuard.Core.Security;

/// <summary>
/// Manages secure credential handling with rotation detection, encryption, and audit logging.
/// Follows zero-trust principles: never logs secrets, encrypts at rest, detects rotation.
/// </summary>
public sealed class CredentialManager
{
    private const int MaxCredentialStoreBytes = 1_048_576;
    private const string KeychainAccount = "connection-string";
    private readonly DataGuardConfiguration _config;
    private readonly ILogger<CredentialManager>? _logger;
    private readonly string _credentialStorePath;
    private readonly string _keychainService;
    private readonly ICredentialSecretStore? _secretStore;
    private static readonly byte[] _entropy = "DataGuard.Credential.Protection"u8.ToArray();

    public CredentialManager(
        DataGuardConfiguration config,
        ILogger<CredentialManager>? logger = null,
        string? credentialStorePath = null,
        ICredentialSecretStore? secretStore = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _credentialStorePath = credentialStorePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "credentials.json");
        _keychainService = "DataGuard.Credential." + ComputeHash(Path.GetFullPath(_credentialStorePath));
        _secretStore = secretStore;

        var storeDirectory = Path.GetDirectoryName(_credentialStorePath)!;

        // Validate existing ancestors before creating anything, then validate
        // again after creation so a newly materialized path cannot bypass the
        // reparse-point policy.
        ValidateCredentialStorePath();
        Directory.CreateDirectory(storeDirectory);
        ValidateCredentialStorePath();
    }

    /// <summary>
    /// Gets the connection string, checking for rotation and decrypting if needed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        // Environment variables win over config-file values (zero-trust/CI
        // convention, matching ZeroTrustCredentialProvider priority order).
        var connectionString = Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING")
            ?? _config.ConnectionString;

        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = await GetStoredConnectionStringAsync(cancellationToken);
        }

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
        if (IsKeychainReference(connectionString) && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Encrypted credential data requires the macOS Keychain backend on this installation.");
        }

        if (IsKeychainReference(connectionString))
        {
            connectionString = (_secretStore ?? PlatformCredentialSecretStore.Create()).Read(_keychainService, KeychainAccount);
        }
        else if (IsSecretServiceReference(connectionString))
        {
            connectionString = (_secretStore ?? PlatformCredentialSecretStore.Create()).Read(_keychainService, KeychainAccount);
        }
        else if (_config.EncryptConnectionStringAtRest && IsEncrypted(connectionString) && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Encrypted credential data requires the Windows DPAPI backend on this installation.");
        }

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

        if (_config.EncryptConnectionStringAtRest && !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Encrypted credential storage requires the Windows DPAPI backend. Disable EncryptConnectionStringAtRest only when explicit plaintext storage is authorized.");
        }

        if (_config.EncryptConnectionStringAtRest && OperatingSystem.IsMacOS())
        {
            var secretStore = _secretStore ?? PlatformCredentialSecretStore.Create();
            var reference = BuildProtectedReference(secretStore);
            secretStore.Store(_keychainService, KeychainAccount, connectionString);
            storedValue = reference;
        }
        else if (_config.EncryptConnectionStringAtRest && OperatingSystem.IsLinux())
        {
            var secretStore = _secretStore ?? PlatformCredentialSecretStore.Create();
            var reference = BuildProtectedReference(secretStore);
            secretStore.Store(_keychainService, KeychainAccount, connectionString);
            storedValue = reference;
        }
        else if (_config.EncryptConnectionStringAtRest && OperatingSystem.IsWindows())
        {
            storedValue = EncryptConnectionString(connectionString);
        }

        var credentialData = new CredentialData
        {
            ConnectionString = storedValue,
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow,
            IsEncrypted = IsEncrypted(storedValue) || IsKeychainReference(storedValue) || IsSecretServiceReference(storedValue),
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
            var stored = await GetStoredConnectionStringAsync(cancellationToken);
            if (!string.IsNullOrEmpty(stored) && stored != currentConnectionString)
            {
                var warning = $"⚠ Credential rotation detected: connection string has changed since last run. " +
                             $"If this was intentional, run 'dataguard baseline' to update. " +
                             $"Otherwise, verify your credential source hasn't been compromised.";

                _logger?.LogWarning(warning);
                Console.Error.WriteLine(warning);

                await LogAuditAsync("CredentialRotationDetected", new
                {
                    OldHash = ComputeHash(stored),
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
        // This file-backed implementation uses Windows DPAPI only. Other platform
        // backends must be explicit secret-store implementations, never a plaintext fallback.
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

    private static bool IsKeychainReference(string connectionString)
        => connectionString.StartsWith("KEYCHAIN:", StringComparison.Ordinal);

    private static bool IsSecretServiceReference(string connectionString)
        => connectionString.StartsWith("SECRET-SERVICE:", StringComparison.Ordinal);

    private string BuildProtectedReference(ICredentialSecretStore secretStore)
    {
        var prefix = secretStore.ReferencePrefix;
        if (!string.Equals(prefix, "KEYCHAIN:", StringComparison.Ordinal)
            && !string.Equals(prefix, "SECRET-SERVICE:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Credential secret-store returned an unsupported protected reference prefix.");
        }

        return prefix + _keychainService;
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }

    private async Task<CredentialData?> LoadFromCredentialStoreAsync(CancellationToken cancellationToken)
    {
        ValidateCredentialStorePath();
        if (!File.Exists(_credentialStorePath))
        {
            return null;
        }

        try
        {
            if (new FileInfo(_credentialStorePath).Length > MaxCredentialStoreBytes)
            {
                return null;
            }
            var json = await File.ReadAllTextAsync(_credentialStorePath, cancellationToken);
            return JsonSerializer.Deserialize<CredentialData>(json);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        // Never return a protected payload as if it were a usable connection
        // string when legacy or tampered metadata clears the encryption flag.
        if (!stored.IsEncrypted && (IsEncrypted(value) || IsKeychainReference(value) || IsSecretServiceReference(value)))
        {
            throw new InvalidOperationException("Credential store metadata does not match its protected payload.");
        }

        if (stored.IsEncrypted && IsKeychainReference(value))
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("Encrypted credential data requires the macOS Keychain backend on this installation.");
            }

            return (_secretStore ?? PlatformCredentialSecretStore.Create()).Read(_keychainService, KeychainAccount);
        }

        if (stored.IsEncrypted && IsSecretServiceReference(value))
        {
            return (_secretStore ?? PlatformCredentialSecretStore.Create()).Read(_keychainService, KeychainAccount);
        }

        if (stored.IsEncrypted && IsEncrypted(value) && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Encrypted credential data requires the Windows DPAPI backend on this installation.");
        }

        if (stored.IsEncrypted && IsEncrypted(value) && OperatingSystem.IsWindows())
        {
            value = DecryptConnectionString(value);
        }

        return value;
    }

    private async Task SaveToCredentialStoreAsync(CredentialData data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxCredentialStoreBytes)
        {
            throw new InvalidOperationException("Credential store record exceeds the 1 MiB safety limit.");
        }

        ValidateCredentialStorePath();
        var directory = Path.GetDirectoryName(_credentialStorePath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_credentialStorePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            ValidateCredentialStorePath();
            File.Move(temporaryPath, _credentialStorePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Preserve the original save/cancellation failure.
            }
        }
    }

    private void ValidateCredentialStorePath()
    {
        var path = Path.GetFullPath(_credentialStorePath);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tempRootResolved = new DirectoryInfo(tempRoot).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (current != null)
        {
            // macOS exposes /tmp as the documented system alias for /private/tmp;
            // permit that fixed OS temp root while rejecting user-controlled links
            // below it.
            var currentPath = current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var isSystemTempAncestor = tempRoot.StartsWith(currentPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || tempRootResolved.StartsWith(currentPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(currentPath, tempRoot, StringComparison.Ordinal)
                || string.Equals(currentPath, tempRootResolved, StringComparison.Ordinal);
            if (!isSystemTempAncestor
                && current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Credential store path traverses a symbolic link or reparse point.");
            }
            current = current.Parent;
        }

        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Credential store file is a symbolic link or reparse point.");
        }
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

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var logLine = JsonSerializer.Serialize(auditEntry);
        await File.AppendAllTextAsync(logPath, logLine + Environment.NewLine, cancellationToken);
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
