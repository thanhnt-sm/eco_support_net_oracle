using System.Reflection;
using DataGuard.Cli.Services;
using DataGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Typed <c>.dataguard.yml</c> binding (red-team rec 20 follow-up): <see cref="DataGuardConfiguration"/> is a positional
/// record without a parameterless constructor, so YamlDotNet used to fail on it and every file fell back to the
/// top-level scalar mapping, which dropped nested blocks, lists and newer keys. <see cref="ConfigDocument"/> now binds
/// every key; the scalar mapping is only the last resort.
/// </summary>
public class CliConfigBindingTests
{
    [Fact]
    public void DeserializeConfig_BindsNestedOracleSqlServerAndPluginsBlocks()
    {
        var config = ConfigLoader.DeserializeConfig("""
            # comment
            DefaultProvider: oracle
            Oracle:
              Owner: HR
              UseAllArguments: false
              DescribeRefCursors: true
            SqlServer:
              Schema: sales
              UseFirstResultSet: false
            Plugins:
              AllowUnsignedLocal: true
            """);

        config.DefaultProvider.Should().Be("oracle");
        config.Oracle.Should().NotBeNull();
        config.Oracle!.Owner.Should().Be("HR");
        config.Oracle.UseAllArguments.Should().BeFalse();
        config.Oracle.UseAllTabColumns.Should().BeTrue("an absent nested key keeps its default");
        config.Oracle.DescribeRefCursors.Should().BeTrue();
        config.SqlServer.Should().Be(new SqlServerConfiguration("sales", UseFirstResultSet: false));
        config.Plugins!.AllowUnsignedLocal.Should().BeTrue();
    }

    [Fact]
    public void DeserializeConfig_BindsBlockAndFlowLists()
    {
        var config = ConfigLoader.DeserializeConfig("""
            ExcludedProcedures:
              - dbo.LegacyImport
              - "pkg_old.run"
            ExcludedEntities: [AuditRow, 'TempRow']
            """);

        config.ExcludedProcedures.Should().Equal("dbo.LegacyImport", "pkg_old.run");
        config.ExcludedEntities.Should().Equal("AuditRow", "TempRow");
    }

    [Fact]
    public void DeserializeConfig_BindsAuditStoreProcedureAndSnapshotAgeKeys()
    {
        var config = ConfigLoader.DeserializeConfig("""
            AuditKeyFile: /etc/dataguard/audit.key
            RequireEncryptedCredentialStore: true
            StrictProcedureContracts: true
            SnapshotMaxAgeDays: 14
            """);

        config.AuditKeyFile.Should().Be("/etc/dataguard/audit.key");
        config.RequireEncryptedCredentialStore.Should().BeTrue();
        config.StrictProcedureContracts.Should().BeTrue();
        config.SnapshotMaxAgeDays.Should().Be(14);
    }

    [Fact]
    public void DeserializeConfig_AbsentAndEmptyKeysKeepRecordDefaults()
    {
        var config = ConfigLoader.DeserializeConfig("""
            DefaultSchema:
            Oracle:
            EnableBaseline: False
            NamingConvention: ExactMatch
            UnknownKey: ignored by binding
            """);

        config.DefaultSchema.Should().BeNull("an empty scalar is not an empty schema name");
        config.Oracle.Should().BeNull();
        config.EnableBaseline.Should().BeFalse("the wizard writes C# casing (True/False)");
        config.NamingConvention.Should().Be(NamingConvention.ExactMatch);
        config.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        config.SnapshotMaxAgeDays.Should().Be(90);
        config.TelemetryServiceName.Should().Be("dataguard");
        config.ExcludedProcedures.Should().BeEmpty();
        config.ExcludedEntities.Should().BeEmpty();
    }

    [Fact]
    public void DeserializeConfig_EmptyDocumentYieldsDefaults()
    {
        var config = ConfigLoader.DeserializeConfig(string.Empty);

        config.GroundTruthMode.Should().Be(GroundTruthMode.Snapshot);
        config.ExcludedProcedures.Should().BeEmpty();
    }

    [Fact]
    public void DeserializeConfig_RoundTripsSerializedConfiguration()
    {
        var original = new DataGuardConfiguration
        {
            GroundTruthMode = GroundTruthMode.Manual,
            ExcludedProcedures = new[] { "a", "b" },
            ExcludedEntities = Array.Empty<string>(),
            Oracle = new OracleConfiguration(Owner: "APP") { DescribeRefCursors = true },
            DefaultProvider = "oracle",
            StrictProcedureContracts = true,
            AuditKeyFile = "audit.key",
            Plugins = new PluginConfiguration { AllowUnsignedLocal = true },
        };

        var roundTripped = ConfigLoader.DeserializeConfig(ConfigLoader.SerializeConfig(original));

        roundTripped.GroundTruthMode.Should().Be(GroundTruthMode.Manual);
        roundTripped.ExcludedProcedures.Should().Equal("a", "b");
        roundTripped.Oracle.Should().Be(original.Oracle);
        roundTripped.DefaultProvider.Should().Be("oracle");
        roundTripped.StrictProcedureContracts.Should().BeTrue();
        roundTripped.AuditKeyFile.Should().Be("audit.key");
        roundTripped.Plugins!.AllowUnsignedLocal.Should().BeTrue();
    }

    [Fact]
    public void DeserializeConfig_FallsBackToScalarsOnlyWhenTypedBindingFails()
    {
        // A scalar where a block is expected fails the typed binding; the scalar mapping still reads the rest.
        var config = ConfigLoader.DeserializeConfig("""
            Oracle: HR
            DefaultSchema: SALES
            RequireEncryptedCredentialStore: true
            """);

        config.DefaultSchema.Should().Be("SALES");
        config.RequireEncryptedCredentialStore.Should().BeTrue();
        config.Oracle.Should().BeNull();
    }

    [Fact]
    public void DeserializeConfig_InvalidScalarValueStillFails()
    {
        var act = () => ConfigLoader.DeserializeConfig("EnableBaseline: maybe\n");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ConfigDocument_CoversEveryConfigurationProperty()
    {
        PropertyNames(typeof(ConfigDocument)).Should().BeEquivalentTo(PropertyNames(typeof(DataGuardConfiguration)));
        PropertyNames(typeof(ConfigDocument.OracleDocument)).Should().BeEquivalentTo(PropertyNames(typeof(OracleConfiguration)));
        PropertyNames(typeof(ConfigDocument.SqlServerDocument)).Should().BeEquivalentTo(PropertyNames(typeof(SqlServerConfiguration)));
        PropertyNames(typeof(ConfigDocument.PluginsDocument)).Should().BeEquivalentTo(PropertyNames(typeof(PluginConfiguration)));

        static IEnumerable<string> PropertyNames(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);
    }
}
