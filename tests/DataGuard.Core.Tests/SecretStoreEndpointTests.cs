using System.Net;
using System.Net.Http;
using DataGuard.Core.Models;
using DataGuard.Core.Security.SecretStores;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class SecretStoreEndpointTests
{
    [Theory]
    [InlineData("https://notvault.azure.net")]
    [InlineData("https://vault.azure.net")]
    [InlineData("https://a.b.vault.azure.net")]
    [InlineData("https://name.vault.azure.net.evil.invalid")]
    [InlineData("https://user@name.vault.azure.net")]
    [InlineData("https://name.vault.azure.net/path")]
    [InlineData("https://name.vault.azure.net?query=yes")]
    [InlineData("https://name.vault.azure.net#fragment")]
    [InlineData("https://name.vault.azure.net:444")]
    [InlineData("http://name.vault.azure.net")]
    public async Task Azure_RejectsUntrustedAddressBeforeAnyRequest(string address)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var store = new AzureKeyVaultSecretStore(client);
        var act = () => store.GetSecretAsync("synthetic", new DataGuardConfiguration(KeyVaultUri: address), default);

        await act.Should().ThrowAsync<SecretStoreException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://name.vault.azure.net")]
    [InlineData("https://NAME.vault.azure.net:443/")]
    public async Task Azure_CanonicalEndpointUsesSyntheticManagedIdentityToken(string address)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var store = new AzureKeyVaultSecretStore(client);

        var value = await store.GetSecretAsync("synthetic", new DataGuardConfiguration(KeyVaultUri: address), default);

        value.Should().Be("synthetic-value");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Uri.Should().Be("https://name.vault.azure.net/secrets/synthetic?api-version=7.4");
        handler.Requests[1].Authorization.Should().Be("Bearer synthetic-token");
    }

    [Theory]
    [InlineData("https://evil.invalid", "https://vault.invalid")]
    [InlineData("https://vault.invalid", null)]
    [InlineData("https://vault.invalid", "http://vault.invalid")]
    [InlineData("https://vault.invalid:8200", "https://vault.invalid")]
    [InlineData("https://vault.invalid/path", "https://vault.invalid")]
    [InlineData("https://vault.invalid", "https://vault.invalid/path")]
    [InlineData("https://user@vault.invalid", "https://vault.invalid")]
    [InlineData("https://vault.invalid?query=yes", "https://vault.invalid")]
    [InlineData("https://vault.invalid#fragment", "https://vault.invalid")]
    public async Task Vault_RejectsUntrustedAddressBeforeTokenReadOrRequest(string address, string? trustedAddress)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var tokenReads = 0;
        var store = new HashiCorpVaultSecretStore(client, name =>
        {
            if (name == "VAULT_ADDR")
            {
                return trustedAddress;
            }

            if (name == "VAULT_TOKEN")
            {
                tokenReads++;
            }
            return "synthetic-token";
        });
        var act = () => store.GetSecretAsync("synthetic", new DataGuardConfiguration(VaultAddress: address), default);

        await act.Should().ThrowAsync<SecretStoreException>();
        tokenReads.Should().Be(0);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://vault.invalid", "https://VAULT.invalid:443/")]
    [InlineData("https://vault.invalid:8200/", "https://vault.invalid:8200")]
    public async Task Vault_TrustedOriginUsesPerRequestSyntheticToken(string address, string trustedAddress)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var store = new HashiCorpVaultSecretStore(client, name => name == "VAULT_ADDR" ? trustedAddress : "synthetic-token");

        var value = await store.GetSecretAsync("synthetic", new DataGuardConfiguration(VaultAddress: address), default);

        value.Should().Be("synthetic-value");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be($"{new Uri(address).GetLeftPart(UriPartial.Authority)}/v1/secret/data/synthetic");
        handler.Requests[0].VaultToken.Should().Be("synthetic-token");
        client.DefaultRequestHeaders.Contains("X-Vault-Token").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Vault_MissingTokenMakesNoRequest(string? token)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var store = new HashiCorpVaultSecretStore(client, name => name == "VAULT_ADDR" ? "https://vault.invalid" : token);
        var act = () => store.GetSecretAsync("synthetic", new DataGuardConfiguration(VaultAddress: "https://vault.invalid"), default);

        await act.Should().ThrowAsync<SecretStoreException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Vault_NotFoundReturnsNull()
    {
        using var handler = new RecordingHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        var store = new HashiCorpVaultSecretStore(client, name => name == "VAULT_ADDR" ? "https://vault.invalid" : "synthetic-token");

        var value = await store.GetSecretAsync("synthetic", new DataGuardConfiguration(VaultAddress: "https://vault.invalid"), default);

        value.Should().BeNull();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Vault_CancellationPropagatesToHttpHandler()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var store = new HashiCorpVaultSecretStore(client, name => name == "VAULT_ADDR" ? "https://vault.invalid" : "synthetic-token");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = () => store.GetSecretAsync("synthetic", new DataGuardConfiguration(VaultAddress: "https://vault.invalid"), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.ObservedCancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _statusCode = statusCode;
        }

        public List<(string Uri, string? Authorization, string? VaultToken)> Requests { get; } = new();

        public CancellationToken ObservedCancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ObservedCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Vault-Token", out var values) ? values.Single() : null));
            var response = request.RequestUri.Host == "169.254.169.254"
                ? "{\"access_token\":\"synthetic-token\"}"
                : "{\"value\":\"synthetic-value\",\"data\":{\"data\":{\"value\":\"synthetic-value\"}}}";
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(response) });
        }
    }
}
