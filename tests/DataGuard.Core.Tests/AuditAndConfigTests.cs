using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class AuditAndConfigTests
{
    private static string NewTempLogPath()
        => Path.Combine(Path.GetTempPath(), $"dg-audit-{Guid.NewGuid():N}.log");

    private static async Task<string[]> ReadLinesAndCleanup(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var checkpoint = path + ".checkpoint";
        if (File.Exists(checkpoint))
        {
            File.Delete(checkpoint);
        }

        return lines;
    }

    [Fact]
    public void DefaultConstructor_UsesApplicationDataPath_AndCreatesDirectory()
    {
        // The default path lives under the real ApplicationData folder; the
        // constructor must create its parent directory without throwing.
        var logger = new FileAuditLogger();

        logger.Should().NotBeNull();
    }

    [Fact]
    public async Task LogDatabaseOperationAsync_WritesJsonLine()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogDatabaseOperationAsync(
            "SELECT", "SqlServer", "abc123", "SELECT * FROM T", true, "err-detail");

        var lines = await ReadLinesAndCleanup(path);
        lines.Should().ContainSingle();
        using var doc = JsonDocument.Parse(lines[0]);
        doc.RootElement.GetProperty("EventType").GetString().Should().Be("DatabaseOperation");
        doc.RootElement.GetProperty("Operation").GetString().Should().Be("SELECT");
        doc.RootElement.GetProperty("Provider").GetString().Should().Be("SqlServer");
        doc.RootElement.GetProperty("ConnectionStringHash").GetString().Should().Be("abc123");
        doc.RootElement.GetProperty("Details").GetString().Should().Be("SELECT * FROM T");
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("ErrorMessage").GetString().Should().Be("err-detail");
        doc.RootElement.GetProperty("Hash").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LogCredentialAccessAsync_WritesJsonLine()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogCredentialAccessAsync("GetCredential", "ZeroTrustProvider", "hash-1");

        var lines = await ReadLinesAndCleanup(path);
        lines.Should().ContainSingle();
        using var doc = JsonDocument.Parse(lines[0]);
        doc.RootElement.GetProperty("EventType").GetString().Should().Be("CredentialAccess");
        doc.RootElement.GetProperty("Operation").GetString().Should().Be("GetCredential");
        doc.RootElement.GetProperty("Provider").GetString().Should().Be("ZeroTrustProvider");
        doc.RootElement.GetProperty("ConnectionStringHash").GetString().Should().Be("hash-1");
        doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LogConfigurationChangeAsync_MasksSecrets()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogConfigurationChangeAsync("ConnectionString", "Server=old;Password=supersecret", "Server=new;Password=anothersecret");

        var lines = await ReadLinesAndCleanup(path);
        lines.Should().ContainSingle();
        using var doc = JsonDocument.Parse(lines[0]);
        var details = doc.RootElement.GetProperty("Details").GetString()!;

        // Masking applies to the whole value: first 4 + **** + last 4 chars.
        // The full secrets must never appear in the audit log.
        details.Should().Contain("Serv****cret");
        details.Should().NotContain("supersecret");
        details.Should().NotContain("anothersecret");
    }

    [Fact]
    public async Task LogConfigurationChangeAsync_ShortValue_MasksFully()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogConfigurationChangeAsync("Password", "short", "tiny");

        var lines = await ReadLinesAndCleanup(path);
        lines.Should().ContainSingle();
        using var doc = JsonDocument.Parse(lines[0]);
        var details = doc.RootElement.GetProperty("Details").GetString()!;
        details.Should().Contain("****");
        details.Should().NotContain("short");
        details.Should().NotContain("tiny");
    }

    [Fact]
    public async Task LogConfigurationChangeAsync_NullValues_UseEmptyPlaceholder()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogConfigurationChangeAsync("AutoDetectProvider", null, "true");

        var lines = await ReadLinesAndCleanup(path);
        lines.Should().ContainSingle();
        using var doc = JsonDocument.Parse(lines[0]);
        var details = doc.RootElement.GetProperty("Details").GetString()!;
        details.Should().Contain("<empty>");
    }

    [Fact]
    public async Task MultipleEntries_AppendAsNewlineDelimitedJson()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await logger.LogCredentialAccessAsync("two", "p", "h2");
        await logger.LogConfigurationChangeAsync("S", "a", "b");

        var lines = await ReadLinesAndCleanup(path);
        lines.Should().HaveCount(3);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("EventType").GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task WriteEntry_ChainsHashes_AndVerifiesIntegrity()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await logger.LogCredentialAccessAsync("two", "p", "h2");

        var lines = await File.ReadAllLinesAsync(path);
        using (var first = JsonDocument.Parse(lines[0]))
        using (var second = JsonDocument.Parse(lines[1]))
        {
            first.RootElement.GetProperty("PreviousHash").ValueKind.Should().Be(JsonValueKind.Null);
            var firstHash = first.RootElement.GetProperty("Hash").GetString();
            second.RootElement.GetProperty("PreviousHash").GetString().Should().Be(firstHash);
            second.RootElement.GetProperty("Hash").GetString().Should().NotBe(firstHash);
        }

        (await logger.VerifyIntegrityAsync()).Should().BeTrue();

        await ReadLinesAndCleanup(path);
    }

    [Fact]
    public async Task VerifyIntegrity_NoLogFile_ReturnsTrue()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        (await logger.VerifyIntegrityAsync()).Should().BeTrue();

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyIntegrity_MissingHashEntry_Fails()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await File.AppendAllTextAsync(path, "{\"Timestamp\":\"2020-01-01T00:00:00+00:00\",\"EventType\":\"Forged\"}\n");

        (await logger.VerifyIntegrityAsync()).Should().BeFalse();

        await ReadLinesAndCleanup(path);
    }

    [Fact]
    public async Task VerifyIntegrity_NonJsonLine_Fails()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await File.AppendAllTextAsync(path, "not-json\n");

        (await logger.VerifyIntegrityAsync()).Should().BeFalse();

        await ReadLinesAndCleanup(path);
    }

    [Fact]
    public async Task LogAfterTamperedLog_RechainsFromLastGoodEntry()
    {
        var path = NewTempLogPath();
        var logger = new FileAuditLogger(path);

        await logger.LogCredentialAccessAsync("one", "p", "h1");
        await File.AppendAllTextAsync(path, "garbage-line\n");
        await logger.LogCredentialAccessAsync("two", "p", "h2");

        // ReadLastHashAsync skips the corrupt tail line and re-chains from the
        // last valid entry; the new entry's PreviousHash must equal the first
        // entry's Hash and the full-chain verification still fails (garbage).
        var lines = await File.ReadAllLinesAsync(path);
        using (var first = JsonDocument.Parse(lines[0]))
        using (var last = JsonDocument.Parse(lines[^1]))
        {
            last.RootElement.GetProperty("PreviousHash").GetString()
                .Should().Be(first.RootElement.GetProperty("Hash").GetString());
        }

        (await logger.VerifyIntegrityAsync()).Should().BeFalse();

        await ReadLinesAndCleanup(path);
    }
}

public class ConfigurationDefaultsTests
{
    [Fact]
    public void DataGuardConfiguration_PositionalDefaults_AreSensible()
    {
        var config = new DataGuardConfiguration();

        config.ConnectionString.Should().BeNull();
        config.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        config.NamingConvention.Should().Be(NamingConvention.SnakeCaseToPascalCase);
        config.EnableBaseline.Should().BeTrue();
        config.MaxDegreeOfParallelism.Should().Be(0);
        config.EnableConcurrentValidation.Should().BeTrue();
        config.ValidationTimeoutSeconds.Should().Be(300);
        config.MaxViolationQueueSize.Should().Be(100000);
        config.EnableCredentialRotationDetection.Should().BeTrue();
        config.CredentialRotationWarningDays.Should().Be(30);
        config.EncryptConnectionStringAtRest.Should().BeFalse();
        config.EnableAuditLogging.Should().BeTrue();
        config.AllowPlaintextConfigFallback.Should().BeFalse("fail-closed is the default posture");
        config.AutoDetectProvider.Should().BeTrue();
        config.AutoDetectEFContext.Should().BeTrue();
        config.AutoDetectDapper.Should().BeTrue();
        config.EnableSmartDefaults.Should().BeTrue();
        config.EnableTelemetry.Should().BeFalse();
    }

    [Fact]
    public void DataGuardConfiguration_WithExplicitValues_OverridesDefaults()
    {
        var config = new DataGuardConfiguration(
            ConnectionString: "Server=db",
            GroundTruthMode: GroundTruthMode.Full,
            NamingConvention: NamingConvention.ExactMatch,
            EnableBaseline: false,
            MaxDegreeOfParallelism: 4,
            EnableConcurrentValidation: false,
            ValidationTimeoutSeconds: 60,
            EnableCredentialRotationDetection: false,
            AllowPlaintextConfigFallback: true,
            EnableTelemetry: true);

        config.ConnectionString.Should().Be("Server=db");
        config.GroundTruthMode.Should().Be(GroundTruthMode.Full);
        config.NamingConvention.Should().Be(NamingConvention.ExactMatch);
        config.EnableBaseline.Should().BeFalse();
        config.MaxDegreeOfParallelism.Should().Be(4);
        config.EnableConcurrentValidation.Should().BeFalse();
        config.ValidationTimeoutSeconds.Should().Be(60);
        config.EnableCredentialRotationDetection.Should().BeFalse();
        config.AllowPlaintextConfigFallback.Should().BeTrue();
        config.EnableTelemetry.Should().BeTrue();
    }

    [Fact]
    public void OracleConfiguration_Defaults()
    {
        var oracle = new OracleConfiguration();

        oracle.Owner.Should().BeNull();
        oracle.UseRefCursorDescribe.Should().BeTrue();
        oracle.UseAllArguments.Should().BeTrue();
        oracle.UseAllTabColumns.Should().BeTrue();
    }

    [Fact]
    public void OracleConfiguration_WithValues_OverridesDefaults()
    {
        var oracle = new OracleConfiguration(Owner: "APP", UseRefCursorDescribe: false);

        oracle.Owner.Should().Be("APP");
        oracle.UseRefCursorDescribe.Should().BeFalse();
        oracle.UseAllArguments.Should().BeTrue();
        oracle.UseAllTabColumns.Should().BeTrue();
    }

    [Fact]
    public void SqlServerConfiguration_Defaults()
    {
        var sql = new SqlServerConfiguration();

        sql.Schema.Should().Be("dbo");
        sql.UseFirstResultSet.Should().BeTrue();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_Default_CreatesSensibleDefaults()
    {
        var config = DataGuardConfigurationExtensions.Default();

        config.Should().NotBeNull();
        config.EnableSmartDefaults.Should().BeTrue();
        config.AutoDetectProvider.Should().BeTrue();
        config.AutoDetectEFContext.Should().BeTrue();
        config.AutoDetectDapper.Should().BeTrue();
        config.ExcludedProcedures.Should().BeEmpty();
        config.ExcludedEntities.Should().BeEmpty();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_Disabled_ReturnsSameInstance()
    {
        var config = new DataGuardConfiguration { EnableSmartDefaults = false };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.Should().BeSameAs(config);
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_SetsDboSchema_WhenNoProvider()
    {
        // "Database=" alone matches neither the Oracle nor the SQL Server
        // signatures, so no provider is detected and only DefaultSchema applies.
        var config = new DataGuardConfiguration { ConnectionString = "Database=Db" };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.DefaultSchema.Should().Be("dbo");
        result.SqlServer.Should().BeNull();
        result.Oracle.Should().BeNull();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_SqlServerConnection_DetectsProvider()
    {
        var config = new DataGuardConfiguration { ConnectionString = "Server=db;Database=Db;Trusted_Connection=True" };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.SqlServer.Should().NotBeNull();
        result.Oracle.Should().BeNull();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_OracleConnection_DetectsProvider()
    {
        var config = new DataGuardConfiguration { ConnectionString = "User Id=app;Password=pw;Data Source=oraclehost:1521/ORCL" };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.Oracle.Should().NotBeNull();
        result.SqlServer.Should().BeNull();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_OracleServiceName_DetectsProvider()
    {
        var config = new DataGuardConfiguration { ConnectionString = "Data Source=host;Service_Name=ORCL;User Id=app" };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.Oracle.Should().NotBeNull();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_UnknownConnectionString_NoProvider()
    {
        var config = new DataGuardConfiguration { ConnectionString = "just-a-string" };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.SqlServer.Should().BeNull();
        result.Oracle.Should().BeNull();
    }

    [Fact]
    public void DataGuardConfigurationExtensions_WithSmartDefaults_ExplicitOracle_Preserved()
    {
        var config = new DataGuardConfiguration
        {
            ConnectionString = "Server=db;Database=Db",
            Oracle = new OracleConfiguration(Owner: "APP"),
        };

        var result = DataGuardConfigurationExtensions.WithSmartDefaults(config);

        result.Oracle.Should().BeSameAs(config.Oracle);
        result.SqlServer.Should().BeNull();
    }
}
