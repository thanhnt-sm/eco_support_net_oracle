using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DataGuard.Core.Models;

namespace DataGuard.Core.Security.SecretStores;

/// <summary>
/// A remote secret manager consulted by <see cref="ZeroTrustCredentialProvider"/>. Core ships the HTTP-only Azure Key
/// Vault and HashiCorp Vault stores; hosts register SDK-backed stores (the CLI registers AWS Secrets Manager) through
/// the provider constructor, so Core carries no cloud SDK dependency.
/// </summary>
public interface ISecretStore
{
    /// <summary>Stable store name used in logs and audit entries (never a secret).</summary>
    string Name { get; }

    /// <summary>True when <paramref name="configuration"/> enables this store (for example a region or address is set).</summary>
    /// <returns>Whether the store should be consulted.</returns>
    bool IsConfigured(DataGuardConfiguration configuration);

    /// <summary>
    /// Returns the secret, or null when the store does not hold <paramref name="secretName"/>. Any other failure
    /// (authentication, network, malformed response) throws; the provider logs it at Warning with <see cref="Name"/>.
    /// </summary>
    /// <returns>The secret value or null.</returns>
    Task<string?> GetSecretAsync(string secretName, DataGuardConfiguration configuration, CancellationToken cancellationToken);
}

/// <summary>Raised by a secret store for a failure that is not "secret absent". The message never contains the secret.</summary>
public sealed class SecretStoreException : Exception
{
    public SecretStoreException(string message)
        : base(message)
    {
    }

    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Azure Key Vault through the managed-identity IMDS token endpoint and the Key Vault REST API.</summary>
public sealed class AzureKeyVaultSecretStore : ISecretStore
{
    private readonly HttpClient _httpClient;

    public AzureKeyVaultSecretStore(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string Name => "AzureKeyVault";

    public bool IsConfigured(DataGuardConfiguration configuration) => !string.IsNullOrEmpty(configuration?.KeyVaultUri);

    public async Task<string?> GetSecretAsync(string secretName, DataGuardConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IsAzureKeyVaultUri(configuration.KeyVaultUri))
        {
            throw new SecretStoreException("KeyVaultUri must be an https://*.vault.azure.net address.");
        }

        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://vault.azure.net");
        tokenRequest.Headers.Add("Metadata", "true");
        using var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new SecretStoreException($"managed identity token request returned HTTP {(int)tokenResponse.StatusCode}");
        }

        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new SecretStoreException("managed identity token response has no access_token");
        }

        using var secretRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{configuration.KeyVaultUri!.TrimEnd('/')}/secrets/{Uri.EscapeDataString(secretName)}?api-version=7.4");
        secretRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var secretResponse = await _httpClient.SendAsync(secretRequest, cancellationToken);
        if (secretResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!secretResponse.IsSuccessStatusCode)
        {
            throw new SecretStoreException($"secret request returned HTTP {(int)secretResponse.StatusCode}");
        }

        using var secretDoc = JsonDocument.Parse(await secretResponse.Content.ReadAsStringAsync(cancellationToken));
        var value = secretDoc.RootElement.TryGetProperty("value", out var secret) ? secret.GetString() : null;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    internal static bool IsAzureKeyVaultUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && u.Host.EndsWith("vault.azure.net", StringComparison.OrdinalIgnoreCase);
}

/// <summary>HashiCorp Vault KV v2 (<c>/v1/secret/data/&lt;name&gt;</c>, field <c>value</c>) authenticated by <c>VAULT_TOKEN</c>.</summary>
public sealed class HashiCorpVaultSecretStore : ISecretStore
{
    private readonly HttpClient _httpClient;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public HashiCorpVaultSecretStore(HttpClient httpClient, Func<string, string?>? getEnvironmentVariable = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string Name => "HashiCorpVault";

    public bool IsConfigured(DataGuardConfiguration configuration) => !string.IsNullOrEmpty(configuration?.VaultAddress);

    public async Task<string?> GetSecretAsync(string secretName, DataGuardConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Uri.TryCreate(configuration.VaultAddress, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new SecretStoreException("VaultAddress must be an https:// address.");
        }

        var token = _getEnvironmentVariable("VAULT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            throw new SecretStoreException("VAULT_TOKEN is not set.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{configuration.VaultAddress!.TrimEnd('/')}/v1/secret/data/{Uri.EscapeDataString(secretName)}");

        // Per-request header: the client is shared process-wide, so DefaultRequestHeaders would accumulate tokens.
        request.Headers.Add("X-Vault-Token", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SecretStoreException($"secret request returned HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("data", out var secretData)
            && secretData.TryGetProperty("value", out var value))
        {
            var text = value.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }
}
