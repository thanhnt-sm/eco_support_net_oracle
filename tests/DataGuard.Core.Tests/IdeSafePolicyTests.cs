using DataGuard.Cli;
using DataGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Pins the IDE-safe policy: a repository-controlled .dataguard.yml must never be able to make the CLI
/// load an assembly, open a database/secret-manager connection, or write to a repo-chosen path when an
/// IDE host passes --ide-safe.
/// </summary>
public class IdeSafePolicyTests
{
    [Fact]
    public void Apply_ManualModeWithAssemblyPath_ForcesSnapshotAndClearsAssembly()
    {
        var hostile = new DataGuardConfiguration(
            GroundTruthMode: GroundTruthMode.Manual,
            ManualAssemblyPath: @"tools\evil.dll");

        var result = IdeSafePolicy.Apply(hostile, environmentConnectionPresent: false);

        result.Configuration.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        result.Configuration.ManualAssemblyPath.Should().BeNull();
        result.Suppressed.Should().Contain(s => s.Contains("GroundTruthMode=Manual"));
        result.Suppressed.Should().Contain(s => s.Contains("ManualAssemblyPath"));
    }

    [Fact]
    public void Apply_ConnectionStringFromConfigOrEnvironment_IsRemoved()
    {
        var hostile = new DataGuardConfiguration(
            ConnectionString: "Data Source=attacker.example;Integrated Security=true",
            GroundTruthMode: GroundTruthMode.Full);

        var result = IdeSafePolicy.Apply(hostile, environmentConnectionPresent: true);

        result.Configuration.ConnectionString.Should().BeNull();
        result.Configuration.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        result.Suppressed.Should().Contain(s => s.Contains("DATAGUARD_CONNECTION_STRING"));
    }

    [Fact]
    public void Apply_SecretManagerAuditAndTelemetryPaths_AreCleared()
    {
        var hostile = new DataGuardConfiguration(
            KeyVaultUri: "https://attacker.vault.azure.net",
            AwsRegion: "us-east-1",
            VaultAddress: "https://vault.attacker",
            AuditLogPath: @"..\..\audit.log",
            EnableTelemetry: true)
        {
            TelemetryFileDirectory = @"..\telemetry",
        };

        var result = IdeSafePolicy.Apply(hostile, environmentConnectionPresent: false);

        result.Configuration.KeyVaultUri.Should().BeNull();
        result.Configuration.AwsRegion.Should().BeNull();
        result.Configuration.VaultAddress.Should().BeNull();
        result.Configuration.AuditLogPath.Should().BeNull();
        result.Configuration.EnableTelemetry.Should().BeFalse();
        result.Configuration.TelemetryFileDirectory.Should().BeNull();
        result.Suppressed.Should().HaveCount(3);
    }

    [Fact]
    public void Apply_CleanSnapshotConfig_IsUnchangedAndReportsNothing()
    {
        var clean = new DataGuardConfiguration(SnapshotFilePath: ".dataguard-snapshot.json");

        var result = IdeSafePolicy.Apply(clean, environmentConnectionPresent: false);

        result.Configuration.Should().Be(clean);
        result.Suppressed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Server=x", false, null, null, null, null, "--connection")]
    [InlineData(null, true, null, null, null, null, "--offline")]
    [InlineData(null, false, "a.dll", null, null, null, "--assembly")]
    [InlineData(null, false, null, "Snap.cs", null, null, "--ef-snapshot")]
    [InlineData(null, false, null, null, "proj", null, "--ef-project")]
    [InlineData(null, false, null, null, null, "Ctx", "--ef-context")]
    public void FirstRejectedValidateOption_ReturnsOffendingOption(
        string? connection, bool offline, string? assembly, string? efSnapshot, string? efProject, string? efContext, string expected)
    {
        IdeSafePolicy.FirstRejectedValidateOption(connection, offline, assembly, efSnapshot, efProject, efContext)
            .Should().Be(expected);
    }

    [Fact]
    public void FirstRejectedValidateOption_NoCodeLoadingOptions_ReturnsNull()
    {
        IdeSafePolicy.FirstRejectedValidateOption(null, false, null, null, null, null).Should().BeNull();
    }

    [Theory]
    [InlineData(true, null, "--allow-network")]
    [InlineData(false, "osv", "--remote-advisories")]
    [InlineData(false, null, null)]
    public void FirstRejectedAssessOption_ReturnsOffendingOption(bool allowNetwork, string? remote, string? expected)
    {
        IdeSafePolicy.FirstRejectedAssessOption(allowNetwork, remote).Should().Be(expected);
    }

    [Fact]
    public void FormatLines_AreSingleLineAndMentionIdeSafe()
    {
        IdeSafePolicy.FormatSuppressionLine(new[] { "a", "b" }).Should().Be("ide-safe: suppressed a; b");
        IdeSafePolicy.FormatRejectionLine("--offline").Should().StartWith("--offline is not allowed with --ide-safe");
        IdeSafePolicy.FormatRejectionLine("--offline").Should().NotContain("\n");
    }

    // --- Phase 1 (red-team F1): --allow-env-connection keeps only env-sourced credentials ---
    [Theory]
    [InlineData(false, false, GroundTruthMode.Snapshot, false, GroundTruthMode.Snapshot)]
    [InlineData(false, false, GroundTruthMode.Full, false, GroundTruthMode.Snapshot)]
    [InlineData(false, false, GroundTruthMode.Manual, false, GroundTruthMode.Snapshot)]
    [InlineData(false, true, GroundTruthMode.Snapshot, false, GroundTruthMode.Snapshot)]
    [InlineData(false, true, GroundTruthMode.Full, false, GroundTruthMode.Snapshot)]
    [InlineData(false, true, GroundTruthMode.Manual, false, GroundTruthMode.Snapshot)]
    [InlineData(true, false, GroundTruthMode.Snapshot, false, GroundTruthMode.Snapshot)]
    [InlineData(true, false, GroundTruthMode.Full, false, GroundTruthMode.Snapshot)]
    [InlineData(true, false, GroundTruthMode.Manual, false, GroundTruthMode.Snapshot)]
    [InlineData(true, true, GroundTruthMode.Snapshot, true, GroundTruthMode.Snapshot)]
    [InlineData(true, true, GroundTruthMode.Full, true, GroundTruthMode.Full)]
    [InlineData(true, true, GroundTruthMode.Manual, true, GroundTruthMode.Snapshot)]
    public void Apply_AllowEnvConnectionMatrix_KeepsConnectionOnlyWhenAllowedAndEnvPresent(
        bool allowEnv, bool envPresent, GroundTruthMode mode, bool expectKept, GroundTruthMode expectedMode)
    {
        const string envValue = "Server=127.0.0.1,1;Connect Timeout=1";
        var merged = new DataGuardConfiguration(
            ConnectionString: envPresent ? envValue : "Data Source=from-config",
            GroundTruthMode: mode,
            ManualAssemblyPath: @"tools\evil.dll",
            KeyVaultUri: "https://attacker.vault.azure.net",
            AuditLogPath: "audit.log",
            EnableTelemetry: true);

        var result = IdeSafePolicy.Apply(
            merged,
            environmentConnectionPresent: envPresent,
            allowEnvConnection: allowEnv,
            environmentConnection: envPresent ? envValue : null);

        result.Configuration.GroundTruthMode.Should().Be(expectedMode);
        result.Configuration.ManualAssemblyPath.Should().BeNull();
        result.Configuration.KeyVaultUri.Should().BeNull();
        result.Configuration.AuditLogPath.Should().BeNull();
        result.Configuration.EnableTelemetry.Should().BeFalse();
        if (expectKept)
        {
            result.Configuration.ConnectionString.Should().Be(envValue);
            result.Suppressed.Should().Contain("kept environment connection (--allow-env-connection)");
            result.Suppressed.Should().NotContain(s => s.Contains("database access disabled"));
        }
        else
        {
            result.Configuration.ConnectionString.Should().BeNull();
            result.Suppressed.Should().Contain(s => s.Contains("database access disabled"));
            result.Suppressed.Should().NotContain(s => s.Contains("kept environment connection"));
        }
    }

    [Fact]
    public void Apply_AllowEnvConnection_PrefersEnvironmentValueOverConfigValue()
    {
        // Defensive: even if the merged config carried a config-file value, the kept value is the env one.
        var merged = new DataGuardConfiguration(ConnectionString: "Data Source=from-config", GroundTruthMode: GroundTruthMode.Full);

        var result = IdeSafePolicy.Apply(merged, environmentConnectionPresent: true, allowEnvConnection: true, environmentConnection: "Server=env");

        result.Configuration.ConnectionString.Should().Be("Server=env");
        result.Configuration.GroundTruthMode.Should().Be(GroundTruthMode.Full);
    }

    [Fact]
    public void Apply_AllowEnvConnectionWithoutEnv_StripsConfigConnection()
    {
        var merged = new DataGuardConfiguration(ConnectionString: "Data Source=from-config", GroundTruthMode: GroundTruthMode.Full);

        var result = IdeSafePolicy.Apply(merged, environmentConnectionPresent: false, allowEnvConnection: true);

        result.Configuration.ConnectionString.Should().BeNull();
        result.Configuration.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        result.Suppressed.Should().Contain(s => s.Contains("ConnectionString from config"));
    }

    // --- Phase 1 (red-team F11): resource clamps under ide-safe ---
    [Fact]
    public void Apply_ExcessiveParallelismQueueAndTimeout_AreClampedAndReported()
    {
        var hostile = new DataGuardConfiguration(
            MaxDegreeOfParallelism: Environment.ProcessorCount * 64,
            MaxViolationQueueSize: 50_000_000,
            ValidationTimeoutSeconds: 86_400);

        var result = IdeSafePolicy.Apply(hostile, environmentConnectionPresent: false);

        result.Configuration.MaxDegreeOfParallelism.Should().Be(Environment.ProcessorCount);
        result.Configuration.MaxViolationQueueSize.Should().Be(100_000);
        result.Configuration.ValidationTimeoutSeconds.Should().Be(900);
        result.Suppressed.Should().Contain(s => s.Contains("MaxDegreeOfParallelism"));
        result.Suppressed.Should().Contain(s => s.Contains("MaxViolationQueueSize"));
        result.Suppressed.Should().Contain(s => s.Contains("ValidationTimeoutSeconds"));
    }

    [Fact]
    public void Apply_InBoundsParallelismQueueAndTimeout_AreUntouchedAndUnreported()
    {
        var inBounds = new DataGuardConfiguration(
            MaxDegreeOfParallelism: 0,
            MaxViolationQueueSize: 100_000,
            ValidationTimeoutSeconds: 900);

        var result = IdeSafePolicy.Apply(inBounds, environmentConnectionPresent: false);

        result.Configuration.Should().Be(inBounds);
        result.Suppressed.Should().BeEmpty();
    }

    [Fact]
    public void WriteReport_KeptConnection_WritesSuppressionLineThenKeptLine()
    {
        var merged = new DataGuardConfiguration(ConnectionString: "Server=env", GroundTruthMode: GroundTruthMode.Manual);
        var result = IdeSafePolicy.Apply(merged, environmentConnectionPresent: true, allowEnvConnection: true, environmentConnection: "Server=env");
        var writer = new StringWriter();

        IdeSafePolicy.WriteReport(writer, result);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("ide-safe: suppressed GroundTruthMode=Manual").And.NotContain("kept environment connection");
        lines[1].Should().Be("ide-safe: kept environment connection (--allow-env-connection)");
    }

    // --- Phase 1 (red-team F5): in-process environment scrubbing ---
    [Theory]
    [InlineData(false, "DATAGUARD_CONNECTION_STRING", true)]
    [InlineData(true, "DATAGUARD_CONNECTION_STRING", false)]
    [InlineData(true, "dataguard_connection_string", false)]
    [InlineData(false, "VAULT_TOKEN", true)]
    [InlineData(true, "VAULT_ADDR", true)]
    [InlineData(true, "AWS_SECRET_ACCESS_KEY", true)]
    [InlineData(true, "AWS_ANYTHING_ELSE", true)]
    [InlineData(true, "ConnectionStrings__DefaultConnection", true)]
    [InlineData(true, "connectionstrings__x", true)]
    [InlineData(true, "PATH", false)]
    [InlineData(true, "DATAGUARD_PROVIDER", false)]
    [InlineData(true, "AWSOME_APP", false)]
    public void SelectVariablesToClear_ClassifiesNames(bool allowEnv, string name, bool expectCleared)
    {
        var cleared = IdeSafeEnvironment.SelectVariablesToClear(allowEnv, new[] { name, "HOME" });

        cleared.Contains(name).Should().Be(expectCleared);
        cleared.Should().NotContain("HOME");
    }
}

/// <summary>Serialises the process-environment tests: other classes set/clear <c>DATAGUARD_CONNECTION_STRING</c> in-process.</summary>
[CollectionDefinition(IdeSafeEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class IdeSafeEnvironmentCollection
{
    public const string Name = "ide-safe-environment";
}

/// <summary>Integration test for <see cref="IdeSafeEnvironment.Scrub"/>; mutates and restores the real process environment.</summary>
[Collection(IdeSafeEnvironmentCollection.Name)]
public class IdeSafeEnvironmentScrubTests
{
    [Fact]
    public void Scrub_ClearsSecretVariablesInProcess_AndKeepsAllowedConnection()
    {
        const string marker = "dg-ide-safe-scrub-test";
        var names = new[] { "DATAGUARD_CONNECTION_STRING", "VAULT_TOKEN", "ConnectionStrings__DgScrubTest", "AWS_SESSION_TOKEN", "DATAGUARD_PROVIDER" };
        var previous = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, marker);
            }

            var cleared = IdeSafeEnvironment.Scrub(allowEnvConnection: true);

            Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING").Should().Be(marker);
            Environment.GetEnvironmentVariable("DATAGUARD_PROVIDER").Should().Be(marker);
            Environment.GetEnvironmentVariable("VAULT_TOKEN").Should().BeNull();
            Environment.GetEnvironmentVariable("ConnectionStrings__DgScrubTest").Should().BeNull();
            Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN").Should().BeNull();
            cleared.Should().Contain("VAULT_TOKEN").And.Contain("ConnectionStrings__DgScrubTest").And.NotContain("DATAGUARD_CONNECTION_STRING");

            IdeSafeEnvironment.Scrub(allowEnvConnection: false);
            Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING").Should().BeNull();
        }
        finally
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
