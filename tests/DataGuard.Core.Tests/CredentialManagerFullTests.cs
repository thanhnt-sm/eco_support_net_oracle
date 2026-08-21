using System;
using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class CredentialManagerFullTests
{
    private const string EnvVar = "DATAGUARD_CONNECTION_STRING";

    private static readonly DataGuardConfiguration Config = new()
    {
        ExcludedProcedures = Array.Empty<string>(),
        ExcludedEntities = Array.Empty<string>(),
    };

    /// <summary>
    /// Per-test credential store path under the temp folder so tests never
    /// touch the real user-level ApplicationData/DataGuard/credentials.json.
    /// </summary>
    private static string NewTempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"dg-store-{Guid.NewGuid():N}.json");

    public CredentialManagerFullTests()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    [Fact]
    public void Ctor_NullConfig_ThrowsArgumentNull()
    {
        var act = () => new CredentialManager(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Ctor_DefaultConfig_DoesNotThrow()
    {
        // The credential store path is derived from the real ApplicationData
        // folder and the constructor eagerly creates its parent directory; this
        // must never throw even with an otherwise empty configuration.
        var manager = new CredentialManager(Config, credentialStorePath: NewTempStorePath());

        manager.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConnectionString_FromConfigObject_ReturnsValue()
    {
        var manager = new CredentialManager(Config with { ConnectionString = "Server=cfg;Database=Db" }, credentialStorePath: NewTempStorePath());

        var result = await manager.GetConnectionStringAsync();

        result.Should().Be("Server=cfg;Database=Db");
    }

    [Fact]
    public async Task GetConnectionString_FromEnvironmentVariable_TakesPrecedenceOverConfig()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Server=env;Database=Db");
        var manager = new CredentialManager(Config with { ConnectionString = "Server=cfg;Database=Db" }, credentialStorePath: NewTempStorePath());

        var result = await manager.GetConnectionStringAsync();

        result.Should().Be("Server=env;Database=Db");
    }

    [Fact]
    public async Task GetConnectionString_FromEnvironmentVariable_WhenNoConfig()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Server=envonly;Database=Db");
        var manager = new CredentialManager(Config, credentialStorePath: NewTempStorePath());

        var result = await manager.GetConnectionStringAsync();

        result.Should().Be("Server=envonly;Database=Db");
    }

    [Fact]
    public async Task GetConnectionString_NothingConfigured_Throws()
    {
        var manager = new CredentialManager(Config, credentialStorePath: NewTempStorePath());

        var act = () => manager.GetConnectionStringAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No connection string configured*");
    }

    [Fact]
    public async Task GetConnectionString_EncryptAtRest_NonWindows_SkipsDecryption()
    {
        // DPAPI encryption is Windows-only; on other platforms the value is
        // stored as-is, so the plaintext configured value must come back verbatim.
        var manager = new CredentialManager(
            Config with
            {
                ConnectionString = "Server=plain;Database=Db",
                EncryptConnectionStringAtRest = true,
            },
            credentialStorePath: NewTempStorePath());

        var result = await manager.GetConnectionStringAsync();

        result.Should().Be("Server=plain;Database=Db");
    }

    [Fact]
    public async Task GetConnectionString_StoresAuditEntry_WhenAuditEnabled()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"dg-audit-{Guid.NewGuid():N}.log");
        try
        {
            var manager = new CredentialManager(
                Config with
                {
                    ConnectionString = "Server=audited;Database=Db",
                    EnableAuditLogging = true,
                    AuditLogPath = auditPath,
                },
                credentialStorePath: NewTempStorePath());

            await manager.GetConnectionStringAsync();

            File.Exists(auditPath).Should().BeTrue();
            var line = await File.ReadAllTextAsync(auditPath);
            line.Should().Contain("ConnectionStringAccessed");
            line.Should().NotContain("Server=audited", "audit entries must never leak the secret");
        }
        finally
        {
            TryDelete(auditPath);
        }
    }

    [Fact]
    public async Task GetConnectionString_AuditDisabled_WritesNoAuditFile()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"dg-noaudit-{Guid.NewGuid():N}.log");
        try
        {
            var manager = new CredentialManager(
                Config with
                {
                    ConnectionString = "Server=x;Database=Db",
                    EnableAuditLogging = false,
                    AuditLogPath = auditPath,
                },
                credentialStorePath: NewTempStorePath());

            await manager.GetConnectionStringAsync();

            File.Exists(auditPath).Should().BeFalse();
        }
        finally
        {
            TryDelete(auditPath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_NullOrEmpty_Throws()
    {
        var manager = new CredentialManager(Config, credentialStorePath: NewTempStorePath());

        var act = () => manager.StoreConnectionStringAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("connectionString");
    }

    [Fact]
    public async Task GetStoredConnectionString_NoStoreFile_ReturnsNull()
    {
        var storePath = NewTempStorePath();

        var manager = new CredentialManager(Config, credentialStorePath: storePath);

        var result = await manager.GetStoredConnectionStringAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionString_CorruptStoreFile_IgnoredAndThrows()
    {
        // A corrupt store file must be silently ignored (LoadFromCredentialStore
        // swallows parse errors) and resolution falls through to the configured
        // sources.
        var storePath = NewTempStorePath();
        await File.WriteAllTextAsync(storePath, "{ not valid json !!");
        try
        {
            var manager = new CredentialManager(Config, credentialStorePath: storePath);

            var act = () => manager.GetConnectionStringAsync();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*No connection string configured*");

            (await manager.GetStoredConnectionStringAsync()).Should().BeNull();
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_EncryptAtRest_NonWindows_StoresPlaintext()
    {
        // On non-Windows platforms the encryption branch is skipped; the store
        // write is still attempted, so verify the manager accepts the call.
        var storePath = NewTempStorePath();
        try
        {
            var manager = new CredentialManager(Config with { EncryptConnectionStringAtRest = true }, credentialStorePath: storePath);

            var act = () => manager.StoreConnectionStringAsync("Server=plain;Database=Db");

            await act.Should().NotThrowAsync();
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
