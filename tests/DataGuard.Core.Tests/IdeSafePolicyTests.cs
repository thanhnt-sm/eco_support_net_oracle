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
}
