using System.Text;
using System.Text.Json;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Keyed audit chain, single writer path and salted fingerprints (red-team D2): mixed writers stay valid, tampering is
/// detected, keyed and unkeyed chains are distinguishable, and the credential manager never appends raw lines.
/// </summary>
public sealed class AuditChainTests : IDisposable
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-audit-key-0123456789");
    private readonly string _directory = Directory.CreateTempSubdirectory("dg-audit-chain").FullName;

    private string LogPath => Path.Combine(_directory, "audit.log");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private DataGuardConfiguration AuditConfig(string? keyFile = null) => new()
    {
        ExcludedProcedures = Array.Empty<string>(),
        ExcludedEntities = Array.Empty<string>(),
        EncryptConnectionStringAtRest = false,
        EnableAuditLogging = true,
        AuditLogPath = LogPath,
        AuditKeyFile = keyFile,
    };

    private string NewStorePath() => Path.Combine(_directory, $"credentials-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task KeyedChain_VerifiesValid_AndMarksEveryEntry()
    {
        var logger = new FileAuditLogger(LogPath, Key);
        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await logger.LogSecurityEventAsync("two", "test", "{}");

        var result = await logger.VerifyIntegrityAsync();

        result.Status.Should().Be(AuditIntegrityStatus.Valid);
        result.EntryCount.Should().Be(2);
        foreach (var line in await File.ReadAllLinesAsync(LogPath))
        {
            using var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("HashAlgorithm").GetString().Should().Be(FileAuditLogger.KeyedAlgorithm);
        }
    }

    [Fact]
    public async Task UnkeyedChain_IsIntactButReportedUnkeyed()
    {
        var logger = new FileAuditLogger(LogPath, hmacKey: null);
        await logger.LogCredentialAccessAsync("one", "p", "h1");

        var result = await logger.VerifyIntegrityAsync();

        result.Status.Should().Be(AuditIntegrityStatus.Unkeyed);
        result.IsIntact.Should().BeTrue();
        (await File.ReadAllTextAsync(LogPath)).Should().NotContain("HashAlgorithm", "unkeyed entries keep the legacy line format");
    }

    [Fact]
    public async Task KeyedChain_WithoutKey_IsKeyRequired_AndWithWrongKey_IsTampered()
    {
        await new FileAuditLogger(LogPath, Key).LogCredentialAccessAsync("one", "p", "h1");

        (await new FileAuditLogger(LogPath, hmacKey: null).VerifyIntegrityAsync()).Status.Should().Be(AuditIntegrityStatus.KeyRequired);
        (await new FileAuditLogger(LogPath, Encoding.UTF8.GetBytes("a-different-key-of-enough-length")).VerifyIntegrityAsync())
            .Status.Should().Be(AuditIntegrityStatus.Tampered);
    }

    [Fact]
    public async Task KeyedChain_EditedEntry_IsTampered_EvenWhenAttackerRecomputesSha256()
    {
        var logger = new FileAuditLogger(LogPath, Key);
        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await logger.LogCredentialAccessAsync("two", "p", "h2");

        // An attacker without the key rewrites the second entry and recomputes an unkeyed link for it.
        var lines = await File.ReadAllLinesAsync(LogPath);
        var forged = JsonSerializer.Deserialize<AuditEntry>(lines[1])! with { Operation = "forged", Hash = null, HashAlgorithm = null };
        var previous = JsonSerializer.Deserialize<AuditEntry>(lines[0])!.Hash;
        var content = JsonSerializer.Serialize(forged with { PreviousHash = null });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(previous + content)));
        lines[1] = JsonSerializer.Serialize(forged with { Hash = hash, PreviousHash = previous });
        await File.WriteAllLinesAsync(LogPath, lines);

        var result = await logger.VerifyIntegrityAsync();

        result.Status.Should().Be(AuditIntegrityStatus.Tampered);
        result.Reason.Should().Contain("unkeyed entry after a keyed entry");
    }

    [Fact]
    public async Task KeyedChain_TailTruncation_IsTampered()
    {
        var logger = new FileAuditLogger(LogPath, Key);
        for (var i = 0; i < 3; i++)
        {
            await logger.LogCredentialAccessAsync("op", "p", $"h{i}");
        }

        var lines = (await File.ReadAllLinesAsync(LogPath)).ToList();
        lines.RemoveAt(lines.Count - 1);
        await File.WriteAllLinesAsync(LogPath, lines);

        (await logger.VerifyIntegrityAsync()).Status.Should().Be(AuditIntegrityStatus.Tampered);
    }

    [Fact]
    public async Task MixedWriters_CredentialManagerAndDirectLogger_KeepOneValidChain()
    {
        var keyFile = Path.Combine(_directory, "audit.key");
        await File.WriteAllTextAsync(keyFile, Encoding.UTF8.GetString(Key) + "\n");

        // The manager builds its own FileAuditLogger from AuditLogPath + AuditKeyFile (no injected instance); a second,
        // independent logger instance writes to the same file in between.
        var manager = new CredentialManager(
            AuditConfig(keyFile) with { ConnectionString = "Server=mixed;Database=Db" },
            credentialStorePath: NewStorePath());
        var direct = new FileAuditLogger(LogPath, Key);

        await manager.GetConnectionStringAsync();
        await direct.LogCredentialAccessAsync("direct-1", "test", direct.HashSensitiveValue("Server=mixed;Database=Db"));
        await manager.StoreConnectionStringAsync("Server=mixed;Database=Db");
        await direct.LogDatabaseOperationAsync("query", "sqlserver", "h", "SELECT 1", true);
        await manager.GetConnectionStringAsync();

        var lines = await File.ReadAllLinesAsync(LogPath);
        lines.Should().HaveCount(5);
        foreach (var line in lines)
        {
            var entry = JsonSerializer.Deserialize<AuditEntry>(line);
            entry.Should().NotBeNull();
            entry!.Hash.Should().NotBeNullOrEmpty("the manager writes through the chained logger, never raw lines");
        }

        (await File.ReadAllTextAsync(LogPath)).Should().NotContain("Server=mixed");
        (await direct.VerifyIntegrityAsync()).Status.Should().Be(AuditIntegrityStatus.Valid);
    }

    [Fact]
    public async Task MixedWriters_InjectedLoggerShared_StaysValidUnderConcurrency()
    {
        var shared = new FileAuditLogger(LogPath, Key);
        var manager = new CredentialManager(
            AuditConfig() with { ConnectionString = "Server=concurrent;Database=Db" },
            credentialStorePath: NewStorePath(),
            auditLogger: shared);
        var other = new FileAuditLogger(LogPath, Key);

        var writes = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            writes.Add(manager.GetConnectionStringAsync());
            writes.Add(other.LogCredentialAccessAsync($"op-{i}", "test", "h"));
        }

        await Task.WhenAll(writes);

        var result = await shared.VerifyIntegrityAsync();
        result.Status.Should().Be(AuditIntegrityStatus.Valid, result.Reason);
        result.EntryCount.Should().Be(20);
    }

    [Fact]
    public async Task CredentialRotation_IsAuditedWithSaltedFingerprints()
    {
        var storePath = NewStorePath();
        var logger = new FileAuditLogger(LogPath, Key);
        var config = AuditConfig() with { EnableCredentialRotationDetection = true };
        await new CredentialManager(config, credentialStorePath: storePath, auditLogger: logger)
            .StoreConnectionStringAsync("Server=old;Password=old-secret");

        await new CredentialManager(config with { ConnectionString = "Server=new;Password=new-secret" }, credentialStorePath: storePath, auditLogger: logger)
            .GetConnectionStringAsync();

        var text = await File.ReadAllTextAsync(LogPath);
        text.Should().Contain("CredentialRotationDetected");
        text.Should().NotContain("old-secret").And.NotContain("new-secret");
        text.Should().NotContain(AuditHashing.UnsaltedFingerprint("Server=old;Password=old-secret"), "rotation hashes are salted per log file");
        text.Should().Contain(logger.HashSensitiveValue("Server=old;Password=old-secret"));
        (await logger.VerifyIntegrityAsync()).Status.Should().Be(AuditIntegrityStatus.Valid);
    }

    [Fact]
    public async Task HashSensitiveValue_IsSaltedPerLogFile_AndStableWithinOne()
    {
        var first = new FileAuditLogger(LogPath, hmacKey: null);
        var second = new FileAuditLogger(Path.Combine(_directory, "other.log"), hmacKey: null);
        const string secret = "Server=db;Password=p";

        var a1 = first.HashSensitiveValue(secret);
        await first.LogCredentialAccessAsync("op", "p", a1);
        var a2 = new FileAuditLogger(LogPath, hmacKey: null).HashSensitiveValue(secret);

        a1.Should().HaveLength(16).And.Be(a2, "the salt is persisted in the checkpoint header");
        second.HashSensitiveValue(secret).Should().NotBe(a1);
        a1.Should().NotBe(AuditHashing.UnsaltedFingerprint(secret));
        (await File.ReadAllTextAsync(LogPath + ".checkpoint")).Should().Contain("\"Salt\"");
    }

    [Fact]
    public async Task LegacyBareHashCheckpoint_StillVerifies()
    {
        var logger = new FileAuditLogger(LogPath, hmacKey: null);
        await logger.LogCredentialAccessAsync("one", "p", "h1");
        var last = JsonSerializer.Deserialize<AuditEntry>((await File.ReadAllLinesAsync(LogPath))[^1])!.Hash!;
        await File.WriteAllTextAsync(LogPath + ".checkpoint", last);

        (await logger.VerifyIntegrityAsync()).IsIntact.Should().BeTrue();
    }

    [Fact]
    public void ResolveKey_EnvironmentWinsOverFile_AndRejectsShortOrMissingKeys()
    {
        var keyFile = Path.Combine(_directory, "file.key");
        File.WriteAllText(keyFile, "file-key-of-sufficient-length\n");

        AuditHashing.ResolveKey(keyFile, _ => null).Should().Equal(Encoding.UTF8.GetBytes("file-key-of-sufficient-length"));
        AuditHashing.ResolveKey(keyFile, name => name == AuditHashing.AuditKeyEnvironmentVariable ? "environment-key-0123456" : null)
            .Should().Equal(Encoding.UTF8.GetBytes("environment-key-0123456"));
        AuditHashing.ResolveKey(null, _ => null).Should().BeNull();

        var shortKey = () => AuditHashing.ResolveKey(null, _ => "short");
        shortKey.Should().Throw<InvalidOperationException>().WithMessage("*at least 16 bytes*");
        var missing = () => AuditHashing.ResolveKey(Path.Combine(_directory, "missing.key"), _ => null);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");
    }

    [Fact]
    public async Task LibraryHost_WithOnlyAuditKeyFile_WritesKeyedChainThatVerifiesValid()
    {
        // The library resolves the key the same way as the CLI (AuditHashing.ResolveKey: DATAGUARD_AUDIT_KEY first, then
        // AuditKeyFile); this host configures only the key file, so the process environment must not provide a key.
        Environment.GetEnvironmentVariable(AuditHashing.AuditKeyEnvironmentVariable).Should().BeNullOrEmpty();
        var keyFile = Path.Combine(_directory, "library.key");
        await File.WriteAllTextAsync(keyFile, "library-host-audit-key-0123456789\n");
        var config = AuditConfig(keyFile) with { EnableSmartDefaults = false };

        using (var pipeline = DataGuard.DataGuardApi.CreatePipeline(config))
        {
            var logger = pipeline.AuditLogger.Should().BeOfType<FileAuditLogger>().Subject;
            logger.IsKeyed.Should().BeTrue();
            await logger.LogSecurityEventAsync("library-host", "test", "{}");
        }

        var factoryLogger = DataGuard.DataGuardFactory.CreateAuditLogger(config).Should().BeOfType<FileAuditLogger>().Subject;
        factoryLogger.IsKeyed.Should().BeTrue();
        await factoryLogger.LogCredentialAccessAsync("factory", "test", factoryLogger.HashSensitiveValue("Server=x"));

        var verified = await FileAuditLogger.Create(config, _ => null).VerifyIntegrityAsync();
        verified.Status.Should().Be(AuditIntegrityStatus.Valid, verified.Reason);
        verified.EntryCount.Should().Be(2);
        (await new FileAuditLogger(LogPath, hmacKey: null).VerifyIntegrityAsync()).Status.Should().Be(AuditIntegrityStatus.KeyRequired);
    }

    [Fact]
    public void FileAuditLoggerCreate_UsesAuditKeyFile()
    {
        var keyFile = Path.Combine(_directory, "create.key");
        File.WriteAllText(keyFile, "configured-key-0123456789");

        FileAuditLogger.Create(AuditConfig(keyFile), _ => null).IsKeyed.Should().BeTrue();
        FileAuditLogger.Create(AuditConfig(), _ => null).IsKeyed.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "<empty>")]
    [InlineData("short", "****")]
    [InlineData("exactly12chr", "****")]
    [InlineData("Server=db;Password=hunter2", "Se****r2")]
    public void MaskValue_KeepsAtMostFourCharacters(string? value, string expected)
    {
        var masked = FileAuditLogger.MaskValue(value);

        masked.Should().Be(expected);
        masked.Replace("****", string.Empty, StringComparison.Ordinal).Replace("<empty>", string.Empty, StringComparison.Ordinal)
            .Length.Should().BeLessThanOrEqualTo(4);
    }
}
