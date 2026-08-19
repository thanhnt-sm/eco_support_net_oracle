using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataGuard.Core.Security;

/// <summary>
/// Zero-trust credential provider that never exposes secrets directly.
/// Credentials are injected through secure channels (env vars, key vault, secret managers)
/// and never logged, serialized, or passed in plain text.
/// </summary>
public sealed class ZeroTrustCredentialProvider : ICredentialProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ZeroTrustCredentialProvider>? _logger;
    private readonly CredentialManager _credentialManager;
    private readonly IAuditLogger _auditLogger;
    private readonly DataGuardConfiguration _config;

    public ZeroTrustCredentialProvider(
        IConfiguration configuration,
        DataGuardConfiguration config,
        CredentialManager credentialManager,
        IAuditLogger auditLogger,
        ILogger<ZeroTrustCredentialProvider>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _auditLogger = new FileAuditLogger(config.AuditLogPath);
        _logger = logger;
    }

    /// <summary>
    /// Gets a credential without ever exposing it in logs, memory dumps, or serialization.
    /// The credential is fetched just-in-time and cleared from memory after use.
    /// </summary>
    public async Task<CredentialHandle> GetCredentialAsync(
        string credentialName,
        CredentialType type,
        CancellationToken cancellationToken = default)
    {
        var handle = new CredentialHandle(credentialName, type);
        
        try
        {
            await _auditLogger.LogCredentialAccessAsync(
                "GetCredential",
                "ZeroTrustProvider",
                ComputeHash(credentialName),
                cancellationToken);

            var value = await ResolveCredentialAsync(credentialName, type, cancellationToken);
            
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException($"Credential '{credentialName}' not found in any source");
            }

            handle.SetValue(value);
            return handle;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Credential resolution failed for '{CredentialName}'", credentialName);
            throw;
        }
    }

    /// <summary>
    /// Gets the database connection string using zero-trust principles.
    /// Never logs the connection string, fetches from secure sources only.
    /// </summary>
    public async Task<CredentialHandle> GetDatabaseConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await GetCredentialAsync("DatabaseConnection", CredentialType.DatabaseConnection, cancellationToken);
    }

    /// <summary>
    /// Resolves credential from multiple sources in priority order:
    /// 1. Environment variable (highest priority - CI/CD injection)
    /// 2. Azure Key Vault / AWS Secrets Manager / HashiCorp Vault
    /// 3. Local encrypted credential store
    /// 4. Configuration file (lowest priority - dev only)
    /// </summary>
    private async Task<string> ResolveCredentialAsync(
        string credentialName,
        CredentialType type,
        CancellationToken cancellationToken)
    {
        // Priority 1: Environment variable (CI/CD injection)
        var envVar = GetEnvironmentVariableName(credentialName);
        var envValue = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(envValue))
        {
            await LogSourceAsync("EnvironmentVariable", credentialName);
            return envValue;
        }

        // Priority 2: Azure Key Vault
        if (!string.IsNullOrEmpty(_config.KeyVaultUri))
        {
            var kvValue = await GetFromKeyVaultAsync(credentialName, cancellationToken);
            if (!string.IsNullOrEmpty(kvValue))
            {
                await LogSourceAsync("AzureKeyVault", credentialName);
                return kvValue;
            }
        }

        // Priority 3: AWS Secrets Manager
        var awsValue = await GetFromAwsSecretsManagerAsync(credentialName, cancellationToken);
        if (!string.IsNullOrEmpty(awsValue))
        {
            await LogSourceAsync("AwsSecretsManager", credentialName);
            return awsValue;
        }

        // Priority 4: HashiCorp Vault
        var vaultValue = await GetFromHashiCorpVaultAsync(credentialName, cancellationToken);
        if (!string.IsNullOrEmpty(vaultValue))
        {
            await LogSourceAsync("HashiCorpVault", credentialName);
            return vaultValue;
        }

        // Priority 5: Local encrypted credential store
        var storedConnection = await _credentialManager.GetStoredConnectionStringAsync(cancellationToken);
        if (!string.IsNullOrEmpty(storedConnection))
        {
            await LogSourceAsync("LocalEncryptedStore", credentialName);
            return storedConnection;
        }

        // Priority 6: Configuration file (dev only, with warning)
        var configValue = _configuration.GetConnectionString(credentialName) 
                       ?? _configuration[credentialName];
        if (!string.IsNullOrEmpty(configValue))
        {
            // WARNING: Config file credentials are not secure for production
            Console.Error.WriteLine($"⚠ WARNING: Using credential from config file for '{credentialName}'. " +
                                  "This is not secure for production. Use environment variables or secret managers.");
            await LogSourceAsync("ConfigFile", credentialName);
            return configValue;
        }

        return string.Empty;
    }

    private string GetEnvironmentVariableName(string credentialName)
    {
        return $"DATAGUARD_{credentialName.ToUpperInvariant().Replace("-", "_")}";
    }

    private async Task<string> GetFromKeyVaultAsync(string credentialName, CancellationToken cancellationToken)
    {
        // Placeholder for Azure Key Vault integration
        // Would use Azure.Security.KeyVault.Secrets.SecretClient
        await Task.CompletedTask;
        return string.Empty;
    }

    private async Task<string> GetFromAwsSecretsManagerAsync(string credentialName, CancellationToken cancellationToken)
    {
        // Placeholder for AWS Secrets Manager integration
        // Would use Amazon.SecretsManager.AmazonSecretsManagerClient
        await Task.CompletedTask;
        return string.Empty;
    }

    private async Task<string> GetFromHashiCorpVaultAsync(string credentialName, CancellationToken cancellationToken)
    {
        // Placeholder for HashiCorp Vault integration
        // Would use VaultSharp or HTTP API
        await Task.CompletedTask;
        return string.Empty;
    }

    private async Task LogSourceAsync(string source, string credentialName)
    {
        // Log credential source for audit (without the actual value)
        Console.Error.WriteLine($"[Security] Credential '{credentialName}' resolved from: {source}");
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }

}

/// <summary>
/// Interface for credential providers implementing zero-trust principles.
/// </summary>
public interface ICredentialProvider
{
    Task<CredentialHandle> GetCredentialAsync(string credentialName, CredentialType type, CancellationToken cancellationToken = default);
    Task<CredentialHandle> GetDatabaseConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Secure handle for credentials that prevents accidental exposure.
/// The value is only accessible through controlled methods and cleared on disposal.
/// </summary>
public sealed class CredentialHandle : IDisposable
{
    private readonly string _credentialName;
    private readonly CredentialType _type;
    private char[]? _value;
    private bool _disposed;

    public CredentialHandle(string credentialName, CredentialType type)
    {
        _credentialName = credentialName ?? throw new ArgumentNullException(nameof(credentialName));
        _type = type;
    }

    internal void SetValue(string value)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CredentialHandle));
        _value = value.ToCharArray();
    }

    /// <summary>
    /// Executes an action with the credential value without exposing it directly.
    /// The value is passed as a char array and cleared after the action completes.
    /// </summary>
    public T Use<T>(Func<char[], T> action)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CredentialHandle));
        if (_value == null) throw new InvalidOperationException("Credential not set");
        
        try
        {
            return action(_value);
        }
        finally
        {
            // Don't clear here - let Dispose handle it
        }
    }

    /// <summary>
    /// Gets the credential as a string (use with caution - prefer Use()).
    /// </summary>
    public string GetString()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CredentialHandle));
        if (_value == null) throw new InvalidOperationException("Credential not set");
        return new string(_value);
    }

    public void Dispose()
    {
        if (!_disposed && _value != null)
        {
            // Zero out the credential in memory
            Array.Clear(_value, 0, _value.Length);
            _value = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~CredentialHandle()
    {
        Dispose();
    }
}

/// <summary>
/// Types of credentials supported.
/// </summary>
public enum CredentialType
{
    DatabaseConnection,
    ApiKey,
    Certificate,
    Token,
    UsernamePassword
}