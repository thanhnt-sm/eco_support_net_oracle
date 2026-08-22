using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataGuard.Core.Security;
using FluentAssertions;
using DataGuard.Core.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DataGuard.Core.Tests;

[Collection("Sequential")]
public class ZeroTrustCredentialProviderTests : IDisposable
{
    private const string EnvVar = "DATAGUARD_TESTCREDENTIAL";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
        Environment.SetEnvironmentVariable("DATAGUARD_DATABASECONNECTION", null);
    }

    private static ZeroTrustCredentialProvider CreateProvider(
        DataGuardConfiguration? config = null,
        Dictionary<string, string?>? configValues = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();
        return new ZeroTrustCredentialProvider(
            configuration,
            config ?? new DataGuardConfiguration(),
            new CredentialManager(
                new DataGuardConfiguration(),
                credentialStorePath: Path.Combine(Path.GetTempPath(), $"dg-ztstore-{Guid.NewGuid():N}.json")),
            new NullAuditLogger());
    }

    [Fact]
    public async Task GetCredential_FromEnvironmentVariable_Resolves()
    {
        Environment.SetEnvironmentVariable(EnvVar, "env-secret-value");
        var provider = CreateProvider();

        using var handle = await provider.GetCredentialAsync("TestCredential", CredentialType.ApiKey);

        handle.GetString().Should().Be("env-secret-value");
    }

    [Fact]
    public async Task GetCredential_NotFoundAnywhere_Throws()
    {
        var provider = CreateProvider();

        var act = () => provider.GetCredentialAsync("MissingCredential", CredentialType.ApiKey);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found in any source*");
    }

    [Fact]
    public async Task GetCredential_ConfigFileFallbackDisabledByDefault_FailsClosed()
    {
        // Plaintext config credential present, AllowPlaintextConfigFallback defaults
        // to false -> must throw instead of silently using the insecure source.
        var provider = CreateProvider(
            configValues: new Dictionary<string, string?> { ["FallbackCredential"] = "plaintext-secret" });

        var act = () => provider.GetCredentialAsync("FallbackCredential", CredentialType.ApiKey);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Plaintext config-file credentials are disabled by default*");
    }

    [Fact]
    public async Task GetCredential_ConfigFileFallbackExplicitlyAllowed_Resolves()
    {
        var provider = CreateProvider(
            config: new DataGuardConfiguration { AllowPlaintextConfigFallback = true },
            configValues: new Dictionary<string, string?> { ["FallbackCredential"] = "plaintext-secret" });

        using var handle = await provider.GetCredentialAsync("FallbackCredential", CredentialType.ApiKey);

        handle.GetString().Should().Be("plaintext-secret");
    }

    [Fact]
    public async Task GetDatabaseConnection_ResolvesFromEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("DATAGUARD_DATABASECONNECTION", "Server=localhost;Database=Test");
        var provider = CreateProvider();

        using var handle = await provider.GetDatabaseConnectionAsync();

        handle.GetString().Should().Be("Server=localhost;Database=Test");
    }

    [Fact]
    public async Task GetCredential_KeyVaultUriNotAzure_SkippedWithoutNetworkCall()
    {
        // A non-Azure Key Vault URI must be rejected before any HTTP call, so
        // resolution falls through to not-found.
        var provider = CreateProvider(new DataGuardConfiguration { KeyVaultUri = "https://example.com/vault" });

        var act = () => provider.GetCredentialAsync("TestCredential", CredentialType.ApiKey);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetCredential_VaultAddressNotHttps_SkippedWithoutNetworkCall()
    {
        var provider = CreateProvider(new DataGuardConfiguration { VaultAddress = "http://vault.local:8200" });

        var act = () => provider.GetCredentialAsync("TestCredential", CredentialType.ApiKey);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void CredentialHandle_ConstructorNullName_Throws()
    {
        var act = () => new CredentialHandle(null!, CredentialType.ApiKey);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CredentialHandle_UseBeforeSet_Throws()
    {
        var handle = new CredentialHandle("test", CredentialType.ApiKey);

        var act = () => handle.Use(chars => 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not set*");
    }
}
