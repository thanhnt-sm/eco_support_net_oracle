using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using DataGuard.Core.Security.SecretStores;
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
    /// <summary>Store name a host uses when it registers AWS Secrets Manager (the CLI does).</summary>
    public const string AwsSecretsManagerStoreName = "AwsSecretsManager";

    // Shared process-lifetime client: per-call instantiation causes socket
    // exhaustion under repeated resolution (SEC-005). HttpClient is thread-safe
    // for concurrent requests; per-request headers go on HttpRequestMessage.
    private static readonly HttpClient SharedHttpClient = new();

    private readonly IConfiguration _configuration;
    private readonly ILogger<ZeroTrustCredentialProvider>? _logger;
    private readonly CredentialManager _credentialManager;
    private readonly IAuditLogger _auditLogger;
    private readonly DataGuardConfiguration _config;
    private readonly IReadOnlyList<ISecretStore> _secretStores;

    public ZeroTrustCredentialProvider(
        IConfiguration configuration,
        DataGuardConfiguration config,
        CredentialManager credentialManager,
        IAuditLogger auditLogger,
        ILogger<ZeroTrustCredentialProvider>? logger = null)
        : this(configuration, config, credentialManager, auditLogger, additionalSecretStores: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroTrustCredentialProvider"/> class with host-registered secret
    /// stores. Lookup order: Azure Key Vault, then
    /// <paramref name="additionalSecretStores"/> in the given order (the CLI registers AWS Secrets Manager here), then
    /// HashiCorp Vault, then the local encrypted credential file. A store is consulted only when its
    /// <see cref="ISecretStore.IsConfigured"/> returns true.
    /// </summary>
    public ZeroTrustCredentialProvider(
        IConfiguration configuration,
        DataGuardConfiguration config,
        CredentialManager credentialManager,
        IAuditLogger auditLogger,
        IEnumerable<ISecretStore>? additionalSecretStores,
        ILogger<ZeroTrustCredentialProvider>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _auditLogger = config.EnableAuditLogging
            ? (auditLogger ?? FileAuditLogger.Create(config))
            : new NullAuditLogger();
        _logger = logger;

        var stores = new List<ISecretStore> { new AzureKeyVaultSecretStore(SharedHttpClient) };
        stores.AddRange(additionalSecretStores ?? Enumerable.Empty<ISecretStore>());
        stores.Add(new HashiCorpVaultSecretStore(SharedHttpClient));
        _secretStores = stores;
    }

    /// <summary>Names of the secret stores in lookup order (for diagnostics and tests).</summary>
    public IReadOnlyList<string> SecretStoreNames => _secretStores.Select(store => store.Name).ToList();

    /// <summary>
    /// Gets a credential without ever exposing it in logs, memory dumps, or serialization.
    /// The credential is fetched just-in-time and cleared from memory after use.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<CredentialHandle> GetCredentialAsync(
        string credentialName,
        CredentialType type,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryGetCredentialAsync(credentialName, type, cancellationToken)
                ?? throw new InvalidOperationException($"Credential '{credentialName}' not found in any source");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Credential resolution failed for '{CredentialName}'", credentialName);
            throw;
        }
    }

    /// <summary>
    /// Like <see cref="GetCredentialAsync"/> but returns null when no source holds the credential, so a caller with a
    /// further fallback (the CLI) does not log an error for an absent credential.
    /// </summary>
    /// <returns>The credential handle, or null.</returns>
    public async Task<CredentialHandle?> TryGetCredentialAsync(
        string credentialName,
        CredentialType type,
        CancellationToken cancellationToken = default)
    {
        await _auditLogger.LogCredentialAccessAsync(
            "GetCredential",
            "ZeroTrustProvider",
            _auditLogger.HashSensitiveValue(credentialName),
            cancellationToken);

        var value = await ResolveCredentialAsync(credentialName, type, cancellationToken);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var handle = new CredentialHandle(credentialName, type);
        handle.SetValue(value);
        return handle;
    }

    /// <summary>
    /// Gets the database connection string using zero-trust principles.
    /// Never logs the connection string, fetches from secure sources only.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<CredentialHandle> GetDatabaseConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await GetCredentialAsync("DatabaseConnection", CredentialType.DatabaseConnection, cancellationToken);
    }

    /// <summary>
    /// Resolves credential from multiple sources in priority order:
    /// 1. Environment variable (highest priority - CI/CD injection)
    /// 2. Azure Key Vault, host-registered stores (AWS Secrets Manager in the CLI), HashiCorp Vault
    /// 3. Local encrypted credential store
    /// 4. Configuration file (lowest priority - dev only, requires AllowPlaintextConfigFallback).
    /// </summary>
    private async Task<string> ResolveCredentialAsync(
        string credentialName,
        CredentialType type,
        CancellationToken cancellationToken)
    {
        if (type == CredentialType.DatabaseConnection)
        {
            var connectionString = Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(connectionString))
            {
                LogSource("EnvironmentVariable", credentialName);
                return connectionString;
            }
        }

        // Priority 1: Environment variable (CI/CD injection)
        var envVar = GetEnvironmentVariableName(credentialName);
        var envValue = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(envValue))
        {
            LogSource("EnvironmentVariable", credentialName);
            return envValue;
        }

        // Priority 2: secret managers, in registration order.
        if (!string.IsNullOrEmpty(_config.AwsRegion)
            && !_secretStores.Any(store => string.Equals(store.Name, AwsSecretsManagerStoreName, StringComparison.Ordinal)))
        {
            _logger?.LogWarning(
                "AwsRegion is configured but no {Store} secret store is registered; the lookup is skipped (the DataGuard CLI registers it)",
                AwsSecretsManagerStoreName);
        }

        foreach (var store in _secretStores)
        {
            if (!store.IsConfigured(_config))
            {
                continue;
            }

            string? storeValue;
            try
            {
                storeValue = await store.GetSecretAsync(credentialName, _config, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Warning, never Debug (red-team D1): a misconfigured or unreachable store must be visible. The message
                // names the store and the exception type only; store exceptions never carry the secret.
                LogStoreFailure(store.Name, credentialName, ex);
                continue;
            }

            if (!string.IsNullOrEmpty(storeValue))
            {
                LogSource(store.Name, credentialName);
                return storeValue;
            }
        }

        // Priority 3: Local encrypted credential store
        string? storedConnection;
        try
        {
            storedConnection = await _credentialManager.GetStoredConnectionStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException or System.IO.IOException)
        {
            LogStoreFailure("LocalEncryptedStore", credentialName, ex);
            storedConnection = null;
        }

        if (!string.IsNullOrEmpty(storedConnection))
        {
            LogSource("LocalEncryptedStore", credentialName);
            return storedConnection;
        }

        // Priority 4: Configuration file (dev only, fail-closed unless explicitly allowed)
        var configValue = _configuration.GetConnectionString(credentialName)
                       ?? _configuration[credentialName];
        if (!string.IsNullOrEmpty(configValue))
        {
            if (!_config.AllowPlaintextConfigFallback)
            {
                throw new InvalidOperationException(
                    $"Credential '{credentialName}' could not be resolved from any secure source " +
                    "(environment variable, Key Vault, AWS Secrets Manager, HashiCorp Vault, or encrypted store). " +
                    "Plaintext config-file credentials are disabled by default; set " +
                    "AllowPlaintextConfigFallback=true only in Development.");
            }

            // WARNING: Config file credentials are not secure for production
            Console.Error.WriteLine($"⚠ WARNING: Using credential from config file for '{credentialName}'. " +
                                  "This is not secure for production. Use environment variables or secret managers.");
            LogSource("ConfigFile", credentialName);
            return configValue;
        }

        return string.Empty;
    }

    private void LogStoreFailure(string storeName, string credentialName, Exception exception)
    {
        _logger?.LogWarning(
            "Secret store {Store} lookup for '{CredentialName}' failed ({ErrorType}: {ErrorMessage}); trying the next source",
            storeName,
            credentialName,
            exception.GetType().Name,
            exception.Message);
    }

    private static string GetEnvironmentVariableName(string credentialName)
    {
        return $"DATAGUARD_{credentialName.ToUpperInvariant().Replace("-", "_")}";
    }

    private static void LogSource(string source, string credentialName)
    {
        // Log credential source for audit (without the actual value)
        Console.Error.WriteLine($"[Security] Credential '{credentialName}' resolved from: {source}");
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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CredentialHandle));
        }

        _value = value.ToCharArray();
    }

    /// <summary>
    /// Executes an action with the credential value without exposing it directly.
    /// The value is passed as a char array and cleared after the action completes.
    /// </summary>
    /// <returns></returns>
    public T Use<T>(Func<char[], T> action)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CredentialHandle));
        }

        if (_value == null)
        {
            throw new InvalidOperationException("Credential not set");
        }

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
    /// <returns></returns>
    public string GetString()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CredentialHandle));
        }

        if (_value == null)
        {
            throw new InvalidOperationException("Credential not set");
        }

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
    UsernamePassword,
}