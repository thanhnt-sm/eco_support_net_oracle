using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataGuard.Cli;
using DataGuard.Cli.Security;
using DataGuard.Core.Security;
using DataGuard.Core.Security.SecretStores;
using FluentAssertions;
using DataGuard.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        Environment.SetEnvironmentVariable("DATAGUARD_CONNECTION_STRING", null);
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
    public async Task GetDatabaseConnection_ResolvesFromHeadlessCiEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("DATAGUARD_CONNECTION_STRING", "Server=ci;Database=Test");
        var provider = CreateProvider();

        using var handle = await provider.GetDatabaseConnectionAsync();

        handle.GetString().Should().Be("Server=ci;Database=Test");
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

    [Fact]
    public async Task RegisteredSecretStore_IsConsultedBetweenKeyVaultAndHashiCorpVault()
    {
        var store = new FakeStore("FakeStore", "Server=from-store;Database=Db");
        var provider = CreateProviderWithStores(new DataGuardConfiguration(), new[] { store });

        provider.SecretStoreNames.Should().Equal("AzureKeyVault", "FakeStore", "HashiCorpVault");
        using var handle = await provider.GetDatabaseConnectionAsync();

        handle.GetString().Should().Be("Server=from-store;Database=Db");
        store.RequestedNames.Should().Equal("DatabaseConnection");
    }

    [Fact]
    public async Task SecretStoreFailure_IsLoggedAtWarningWithStoreName_AndNeverTheSecret()
    {
        var failing = new FakeStore("BrokenStore", value: null, failure: new InvalidOperationException("access denied"));
        var working = new FakeStore("WorkingStore", "Server=second;Password=super-secret");
        var logger = new ListLogger();
        var provider = CreateProviderWithStores(new DataGuardConfiguration(), new[] { failing, working }, logger);

        using var handle = await provider.GetDatabaseConnectionAsync();

        handle.GetString().Should().Be("Server=second;Password=super-secret");
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("BrokenStore") && entry.Message.Contains("access denied"));
        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Debug);
        logger.Entries.Should().NotContain(entry => entry.Message.Contains("super-secret"));
    }

    [Fact]
    public async Task KeyVaultMisconfiguration_IsAWarningNotSilent()
    {
        var logger = new ListLogger();
        var provider = CreateProviderWithStores(new DataGuardConfiguration { KeyVaultUri = "https://example.com/vault" }, Array.Empty<ISecretStore>(), logger);

        (await provider.TryGetCredentialAsync("TestCredential", CredentialType.ApiKey)).Should().BeNull();

        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("AzureKeyVault"));
    }

    [Fact]
    public async Task AwsRegionWithoutRegisteredStore_WarnsThatTheLookupIsSkipped()
    {
        var logger = new ListLogger();
        var provider = CreateProviderWithStores(new DataGuardConfiguration { AwsRegion = "eu-west-1" }, Array.Empty<ISecretStore>(), logger);

        (await provider.TryGetCredentialAsync("TestCredential", CredentialType.ApiKey)).Should().BeNull();

        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(ZeroTrustCredentialProvider.AwsSecretsManagerStoreName));
    }

    [Fact]
    public async Task TryGetCredential_Absent_ReturnsNullWithoutErrorLog()
    {
        var logger = new ListLogger();
        var provider = CreateProviderWithStores(new DataGuardConfiguration(), Array.Empty<ISecretStore>(), logger);

        (await provider.TryGetCredentialAsync("Absent", CredentialType.ApiKey)).Should().BeNull();

        logger.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error);
    }

    private static ZeroTrustCredentialProvider CreateProviderWithStores(
        DataGuardConfiguration config,
        IEnumerable<ISecretStore> stores,
        ILogger<ZeroTrustCredentialProvider>? logger = null) =>
        new(
            new ConfigurationBuilder().Build(),
            config,
            new CredentialManager(
                new DataGuardConfiguration { EncryptConnectionStringAtRest = false },
                credentialStorePath: Path.Combine(Path.GetTempPath(), $"dg-ztstore-{Guid.NewGuid():N}.json"),
                auditLogger: new NullAuditLogger()),
            new NullAuditLogger(),
            stores,
            logger);

    private sealed class FakeStore(string name, string? value, Exception? failure = null) : ISecretStore
    {
        public List<string> RequestedNames { get; } = new();

        public string Name => name;

        public bool IsConfigured(DataGuardConfiguration configuration) => true;

        public Task<string?> GetSecretAsync(string secretName, DataGuardConfiguration configuration, CancellationToken cancellationToken)
        {
            RequestedNames.Add(secretName);
            return failure is null ? Task.FromResult(value) : Task.FromException<string?>(failure);
        }
    }

    private sealed class ListLogger : ILogger<ZeroTrustCredentialProvider>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>
/// CLI-side credential plumbing (red-team D1): the AWS Secrets Manager store lives in the CLI and is registered with the
/// Core provider; the CLI resolution order and the provider source factory are pure enough to test in-process.
/// </summary>
[Collection("Sequential")]
public class CliCredentialResolutionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dg-cli-cred").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void AwsSecretsManagerStore_IsTheCliRegisteredAwsStore()
    {
        var store = new AwsSecretsManagerSecretStore();

        store.Name.Should().Be(ZeroTrustCredentialProvider.AwsSecretsManagerStoreName);
        store.IsConfigured(new DataGuardConfiguration()).Should().BeFalse();
        store.IsConfigured(new DataGuardConfiguration { AwsRegion = "us-east-1" }).Should().BeTrue();
        typeof(ZeroTrustCredentialProvider).Assembly.GetReferencedAssemblies()
            .Should().NotContain(name => name.Name != null && name.Name.StartsWith("AWSSDK", StringComparison.Ordinal), "Core carries no AWS SDK");
    }

    [Fact]
    public async Task ResolveConnection_Order_ArgvThenConnectionEnvThenEnvironmentThenProviderThenPlaintextOptIn()
    {
        var config = new DataGuardConfiguration { ConnectionString = "Server=yaml" };
        static Task<string?> Provider(CancellationToken _) => Task.FromResult<string?>("Server=provider");

        var argv = await CliConfigurationResolver.ResolveConnectionAsync(config, new CliConnectionRequest("Server=argv", "MY_DB"), Env(("MY_DB", "Server=named")), Provider, default);
        argv.ConnectionString.Should().Be("Server=argv");
        argv.Warnings.Should().ContainSingle().Which.Should().Be(CliConfigurationResolver.CommandLineConnectionWarning);

        var named = await CliConfigurationResolver.ResolveConnectionAsync(config, new CliConnectionRequest(null, "MY_DB"), Env(("MY_DB", "Server=named"), ("DATAGUARD_CONNECTION_STRING", "Server=env")), Provider, default);
        named.ConnectionString.Should().Be("Server=named");
        named.Source.Should().Be("--connection-env MY_DB");
        named.Warnings.Should().BeEmpty();

        var environment = await CliConfigurationResolver.ResolveConnectionAsync(config, new CliConnectionRequest(null, null), Env(("DATAGUARD_CONNECTION_STRING", "Server=env")), Provider, default);
        environment.ConnectionString.Should().Be("Server=env");

        var provider = await CliConfigurationResolver.ResolveConnectionAsync(config, new CliConnectionRequest(null, null), Env(), Provider, default);
        provider.ConnectionString.Should().Be("Server=provider");
        provider.Source.Should().Be("credential provider");

        var ignored = await CliConfigurationResolver.ResolveConnectionAsync(config, new CliConnectionRequest(null, null), Env(), null, default);
        ignored.ConnectionString.Should().BeNull();
        ignored.Warnings.Should().ContainSingle().Which.Should().Contain("ConnectionString").And.NotContain("Server=yaml");

        var allowed = await CliConfigurationResolver.ResolveConnectionAsync(config with { AllowPlaintextConfigFallback = true }, new CliConnectionRequest(null, null), Env(), null, default);
        allowed.ConnectionString.Should().Be("Server=yaml");
    }

    [Fact]
    public async Task ResolveConnection_ConnectionEnvNamingUnsetVariable_IsAnError()
    {
        var resolution = await CliConfigurationResolver.ResolveConnectionAsync(
            new DataGuardConfiguration(), new CliConnectionRequest(null, "NOT_SET_ANYWHERE"), Env(), null, default);

        resolution.Error.Should().Contain("NOT_SET_ANYWHERE").And.Contain("not set");
        resolution.ConnectionString.Should().BeNull();
    }

    [Fact]
    public void CreateCredentialProviderSource_NothingConfigured_ReturnsNullAndWritesNothing()
    {
        var config = new DataGuardConfiguration { AuditLogPath = Path.Combine(_directory, "audit.log") };

        var source = CliConfigurationResolver.CreateCredentialProviderSource(
            config, new ISecretStore[] { new AwsSecretsManagerSecretStore() }, TextWriter.Null, Env(), Path.Combine(_directory, "credentials.json"));

        source.Should().BeNull();
        File.Exists(config.AuditLogPath).Should().BeFalse("no secure source means no provider and no audit entry");
    }

    [Fact]
    public async Task CreateCredentialProviderSource_ReadsTheEncryptedCredentialFile_AndAuditsOneValidKeyedChain()
    {
        var storePath = Path.Combine(_directory, "credentials.json");
        var auditPath = Path.Combine(_directory, "audit.log");
        var keyFile = Path.Combine(_directory, "audit.key");
        await File.WriteAllTextAsync(keyFile, "cli-audit-key-0123456789");
        var config = new DataGuardConfiguration
        {
            EncryptConnectionStringAtRest = false,
            AuditLogPath = auditPath,
            AuditKeyFile = keyFile,
            ConnectionString = "Server=yaml-must-not-win",
        };
        await new CredentialManager(config, credentialStorePath: storePath, auditLogger: new NullAuditLogger())
            .StoreConnectionStringAsync("Server=from-file;Database=Db");
        var warnings = new StringWriter();

        var source = CliConfigurationResolver.CreateCredentialProviderSource(config, Array.Empty<ISecretStore>(), warnings, Env(), storePath);
        var value = await source!(default);

        value.Should().Be("Server=from-file;Database=Db");
        var verification = await new FileAuditLogger(auditPath, System.Text.Encoding.UTF8.GetBytes("cli-audit-key-0123456789")).VerifyIntegrityAsync();
        verification.Status.Should().Be(AuditIntegrityStatus.Valid, verification.Reason);
        (await File.ReadAllTextAsync(auditPath)).Should().NotContain("from-file");
    }

    [Fact]
    public async Task CreateCredentialProviderSource_StoreFailure_IsAWarningLineNamingTheStore()
    {
        var config = new DataGuardConfiguration { EnableAuditLogging = false, AwsRegion = "eu-west-1" };
        var warnings = new StringWriter();
        var failing = new ThrowingAwsLikeStore();

        var source = CliConfigurationResolver.CreateCredentialProviderSource(config, new ISecretStore[] { failing }, warnings, Env(), Path.Combine(_directory, "none.json"));
        var value = await source!(default);

        value.Should().BeNull();
        warnings.ToString().Should().StartWith("warning: Secret store AwsSecretsManager lookup").And.Contain("simulated outage");
    }

    private sealed class ThrowingAwsLikeStore : ISecretStore
    {
        public string Name => ZeroTrustCredentialProvider.AwsSecretsManagerStoreName;

        public bool IsConfigured(DataGuardConfiguration configuration) => true;

        public Task<string?> GetSecretAsync(string secretName, DataGuardConfiguration configuration, CancellationToken cancellationToken) =>
            Task.FromException<string?>(new SecretStoreException("simulated outage"));
    }
}
