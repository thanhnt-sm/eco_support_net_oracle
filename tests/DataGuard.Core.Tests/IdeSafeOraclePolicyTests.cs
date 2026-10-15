using DataGuard.Cli;
using DataGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class IdeSafeOraclePolicyTests
{
    private const string Suppression = "Oracle REF CURSOR describe disabled (procedure execution)";

    public static IEnumerable<object[]> Configurations()
    {
        var flags = new[] { false, true };
        return from describe in flags
               from legacy in flags
               from present in flags
               from allow in flags
               from mode in new[] { GroundTruthMode.Full, GroundTruthMode.Snapshot }
               from arguments in flags
               from columns in flags
               select new object[] { describe, legacy, present, allow, mode, arguments, columns };
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void Apply_DisablesExecutionAndPreservesMetadataWithoutMutatingInput(
        bool describe, bool legacy, bool present, bool allow, GroundTruthMode mode, bool arguments, bool columns)
    {
        var oracle = new OracleConfiguration("TEST_OWNER", legacy, arguments, columns) { DescribeRefCursors = describe };
        var original = new DataGuardConfiguration(ConnectionString: "repository-placeholder", GroundTruthMode: mode) { Oracle = oracle };
        var before = original with { };
        const string environment = "environment-placeholder";

        var result = IdeSafePolicy.Apply(original, present, allow, environment);

        result.Configuration.Oracle.Should().Be(oracle with { DescribeRefCursors = false, UseRefCursorDescribe = false });
        result.Configuration.ConnectionString.Should().Be(present && allow ? environment : null);
        result.Configuration.GroundTruthMode.Should().Be(present && allow ? mode : GroundTruthMode.Snapshot);
        result.KeptEnvironmentConnection.Should().Be(present && allow);
        result.RulesConnectionString.Should().BeNull();
        original.Should().Be(before);
        original.Oracle.Should().BeSameAs(oracle);
        oracle.DescribeRefCursors.Should().Be(describe);
        oracle.UseRefCursorDescribe.Should().Be(legacy);
        result.Suppressed.Count(s => s == Suppression).Should().Be(describe || legacy ? 1 : 0);
        if (describe || legacy)
        {
            result.Configuration.Should().NotBeSameAs(original);
            result.Configuration.Oracle.Should().NotBeSameAs(oracle);
        }

        var second = IdeSafePolicy.Apply(result.Configuration, present, allow, environment);
        second.Configuration.Should().Be(result.Configuration);
        second.Suppressed.Should().NotContain(Suppression);
        using var report = new StringWriter();
        IdeSafePolicy.WriteReport(report, result);
        report.ToString().Should().NotContain("repository-placeholder").And.NotContain(environment).And.NotContain("TEST_OWNER");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_NullOrSafeOracle_IsUnchanged(bool hasOracle)
    {
        var original = new DataGuardConfiguration { Oracle = hasOracle ? new OracleConfiguration("TEST_OWNER") : null };
        var result = IdeSafePolicy.Apply(original, false);
        result.Configuration.Should().BeSameAs(original);
        result.Configuration.Oracle.Should().BeSameAs(original.Oracle);
        result.Suppressed.Should().BeEmpty();
    }

    [Fact]
    public void Apply_NullConfiguration_Throws()
    {
        Action apply = () => IdeSafePolicy.Apply(null!, false);
        apply.Should().Throw<ArgumentNullException>();
    }
}
