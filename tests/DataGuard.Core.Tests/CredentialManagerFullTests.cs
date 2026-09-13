using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using DataGuard.Core.Security.SecretStores;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

[Collection("Sequential")]
public class CredentialManagerFullTests : IDisposable
{
    private const string EnvVar = "DATAGUARD_CONNECTION_STRING";

    private static readonly DataGuardConfiguration Config = new()
    {
        ExcludedProcedures = Array.Empty<string>(),
        ExcludedEntities = Array.Empty<string>(),
    };

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

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
    public async Task StoreConnectionString_EncryptAtRest_UsesAvailablePlatformStoreOrFailsClosed()
    {
        var storePath = NewTempStorePath();
        try
        {
            var manager = new CredentialManager(Config with { EncryptConnectionStringAtRest = true }, credentialStorePath: storePath);

            var act = () => manager.StoreConnectionStringAsync("Server=plain;Database=Db");

            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                await act.Should().NotThrowAsync();
                (await manager.GetStoredConnectionStringAsync()).Should().Be("Server=plain;Database=Db");
            }
            else if (OperatingSystem.IsLinux())
            {
                try
                {
                    await act();
                    (await manager.GetStoredConnectionStringAsync()).Should().Be("Server=plain;Database=Db");
                }
                catch (PlatformNotSupportedException)
                {
                    File.Exists(storePath).Should().BeFalse();
                }
                catch (InvalidOperationException)
                {
                    File.Exists(storePath).Should().BeFalse();
                }
            }
            else
            {
                await act.Should().ThrowAsync<PlatformNotSupportedException>();
                File.Exists(storePath).Should().BeFalse();
            }
        }
        finally
        {
            TryDelete(storePath);
            DeleteMacKeychainEntry(storePath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_UsesInjectedSecretStoreContract()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var storePath = NewTempStorePath();
        var secretStore = new FakeSecretStore("SECRET-SERVICE:");
        try
        {
            var manager = new CredentialManager(
                Config with { EncryptConnectionStringAtRest = true },
                credentialStorePath: storePath,
                secretStore: secretStore);

            await manager.StoreConnectionStringAsync("Server=fake;Database=Db");
            (await manager.GetStoredConnectionStringAsync()).Should().Be("Server=fake;Database=Db");
            secretStore.StoredValue.Should().Be("Server=fake;Database=Db");
            (await File.ReadAllTextAsync(storePath)).Should().Contain("SECRET-SERVICE:");
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_UnavailableInjectedStore_FailsBeforePersistence()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var storePath = NewTempStorePath();
        try
        {
            var manager = new CredentialManager(
                Config with { EncryptConnectionStringAtRest = true },
                credentialStorePath: storePath,
                secretStore: new UnavailableSecretStore());

            var act = () => manager.StoreConnectionStringAsync("Server=must-not-persist;Database=Db");

            await act.Should().ThrowAsync<PlatformNotSupportedException>();
            File.Exists(storePath).Should().BeFalse();
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_UnsupportedSecretStorePrefix_FailsClosed()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var storePath = NewTempStorePath();
        var secretStore = new FakeSecretStore("UNTRUSTED:");
        try
        {
            var manager = new CredentialManager(
                Config with { EncryptConnectionStringAtRest = true },
                credentialStorePath: storePath,
                secretStore: secretStore);

            var act = () => manager.StoreConnectionStringAsync("Server=must-not-persist;Database=Db");

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*unsupported protected reference prefix*");
            secretStore.StoredValue.Should().BeNull();
            File.Exists(storePath).Should().BeFalse();
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_ExplicitPlaintextOptOut_NeverLabelsRecordEncrypted()
    {
        var storePath = NewTempStorePath();
        try
        {
            var manager = new CredentialManager(
                Config with { EncryptConnectionStringAtRest = false },
                credentialStorePath: storePath);

            await manager.StoreConnectionStringAsync("Server=explicit-plaintext;Database=Db");

            var json = await File.ReadAllTextAsync(storePath);
            json.Should().Contain("Server=explicit-plaintext");
            json.Should().Contain("\"IsEncrypted\": false");
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task GetStoredConnectionString_EncryptedPayloadWithoutBackend_FailsClosed()
    {
        var storePath = NewTempStorePath();
        await File.WriteAllTextAsync(storePath, """{"ConnectionString":"ENC:not-a-credential","IsEncrypted":true}""");
        try
        {
            var manager = new CredentialManager(Config with { EncryptConnectionStringAtRest = true }, credentialStorePath: storePath);

            var act = () => manager.GetStoredConnectionStringAsync();

            if (OperatingSystem.IsWindows())
            {
                await act.Should().ThrowAsync<FormatException>();
            }
            else
            {
                await act.Should().ThrowAsync<PlatformNotSupportedException>();
            }
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task GetStoredConnectionString_ProtectedPayloadWithoutEncryptionFlag_FailsClosed()
    {
        var storePath = NewTempStorePath();
        await File.WriteAllTextAsync(storePath, """{"ConnectionString":"ENC:not-a-credential","IsEncrypted":false}""");
        try
        {
            var manager = new CredentialManager(Config, credentialStorePath: storePath);

            var act = () => manager.GetStoredConnectionStringAsync();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*metadata does not match*");
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task GetStoredConnectionString_OversizedStoreIsRejected()
    {
        var storePath = NewTempStorePath();
        try
        {
            await File.WriteAllBytesAsync(storePath, new byte[1_048_577]);
            var manager = new CredentialManager(Config, credentialStorePath: storePath);

            (await manager.GetStoredConnectionStringAsync()).Should().BeNull();
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task GetStoredConnectionString_PropagatesCancellation()
    {
        var storePath = NewTempStorePath();
        try
        {
            await File.WriteAllTextAsync(storePath, "{\"ConnectionString\":\"Server=test\"}");
            var manager = new CredentialManager(Config, credentialStorePath: storePath);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var act = () => manager.GetStoredConnectionStringAsync(cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public async Task StoreConnectionString_UnixStoreIsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var storePath = NewTempStorePath();
        try
        {
            var manager = new CredentialManager(Config, credentialStorePath: storePath);
            await manager.StoreConnectionStringAsync("Server=permissions;Database=Db");

            var mode = File.GetUnixFileMode(storePath);
            (mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite)).Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            (mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)).Should().Be(0);
        }
        finally
        {
            TryDelete(storePath);
        }
    }

    [Fact]
    public void Ctor_RejectsSymlinkedCredentialStoreParent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"dg-cred-link-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var act = () => new CredentialManager(Config, credentialStorePath: Path.Combine(link, "credentials.json"));
            act.Should().Throw<IOException>();
        }
        finally
        {
            TryDelete(Path.Combine(link, "credentials.json"));
            TryDelete(link);
            TryDelete(Path.Combine(target, "credentials.json"));
            TryDelete(target);
            TryDelete(root);
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    private static void DeleteMacKeychainEntry(string storePath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var pathBytes = Encoding.UTF8.GetBytes(Path.GetFullPath(storePath));
        var hash = SHA256.HashData(pathBytes);
        var service = "DataGuard.Credential." + Convert.ToHexString(hash)[..16];
        Array.Clear(pathBytes, 0, pathBytes.Length);

        using var process = Process.Start(new ProcessStartInfo("/usr/bin/security")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { "delete-generic-password", "-s", service, "-a", "connection-string" },
        });
        process?.WaitForExit();
    }

    private sealed class FakeSecretStore(string prefix) : ICredentialSecretStore
    {
        public string ReferencePrefix => prefix;

        public string? StoredValue { get; private set; }

        public void Store(string service, string account, string value) => StoredValue = value;

        public string Read(string service, string account) => StoredValue ?? throw new InvalidOperationException("No fake credential stored.");
    }

    private sealed class UnavailableSecretStore : ICredentialSecretStore
    {
        public string ReferencePrefix => "SECRET-SERVICE:";

        public void Store(string service, string account, string value) => throw new PlatformNotSupportedException();

        public string Read(string service, string account) => throw new PlatformNotSupportedException();
    }
}
