using DataGuard.Cli.Services;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class MemberSqlAcquisitionProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownMember_DefaultFullValidationRemainsIncomplete_WithZeroOrMixedContracts(bool includeExact)
    {
        var directory = Directory.CreateTempSubdirectory("dg-member-acquisition-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "Repo.cs"), """
                public class Order { public int Id { get; set; } }
                public class Connection { public void Query<T>(string sql) {} }
                public class Repo
                {
                    readonly string sql = "SELECT SentinelSecret FROM PrivateSentinel";
                    public void Load(Connection c) { c.Query<Order>(sql); EXACT }
                }
                """.Replace("EXACT", includeExact ? "c.Query<Order>(\"SELECT Id FROM Orders\");" : "", StringComparison.Ordinal));
            var acquisition = await ContractAcquisition.AcquireContractsAsync(new DataGuardConfiguration(), "sqlserver", projectPath: directory.FullName);
            acquisition.Status.Should().Be(ContractAcquisitionStatus.Incomplete);
            acquisition.Contracts.Should().HaveCount(includeExact ? 1 : 0);
            acquisition.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(AcquisitionDiagnosticKind.UnresolvedSqlValue);

            var output = Path.Combine(directory.FullName, "evidence.json");
            var result = CliProcessTestRunner.Run(directory.FullName, null, null, "validate", "--project", directory.FullName, "--allow-syntactic-only", "--format", "evidence", "--output", output);
            result.ExitCode.Should().Be(3, "stdout: {0}; stderr: {1}", result.Stdout, result.Stderr);
            result.Stderr.Should().Contain("ACQUISITION:").And.Contain("incomplete");
            (result.Stdout + result.Stderr).Should().NotContain("SentinelSecret").And.NotContain("PrivateSentinel");
            File.Exists(output).Should().BeTrue();
            var report = File.ReadAllText(output);
            report.Should().NotContain("SentinelSecret").And.NotContain("PrivateSentinel");
            using var document = System.Text.Json.JsonDocument.Parse(report);
            document.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
