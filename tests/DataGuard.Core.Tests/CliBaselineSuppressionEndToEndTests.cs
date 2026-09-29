using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using static DataGuard.Core.Tests.IdeSafeEndToEndSupport;

namespace DataGuard.Core.Tests;

/// <summary>Process-level test that baseline suppression is visible on stderr in both text and <c>--progress</c> modes.</summary>
public class CliBaselineSuppressionEndToEndTests
{
    [Fact]
    public void Validate_BaselineSuppressesViolation_WarnsInTextAndProgressModes()
    {
        var dir = Directory.CreateTempSubdirectory("dg-baseline-warn").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "AppSnapshot.cs");
            File.WriteAllText(snapshot, """
                class Snapshot { void Build(ModelBuilder modelBuilder) {
                    modelBuilder.Entity<Customer>(entity => {
                        entity.ToTable("CUSTOMERS");
                        entity.Property(item => item.FirstName).HasColumnName("x_y_z_unmatched");
                    });
                }}
                """);

            var (_, firstStdout, _) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--format", "text");
            var match = Regex.Match(firstStdout, @"\[\w+\] DG006: (?<msg>.+?)(?: \(\d+:\d+\))?\r?$", RegexOptions.Multiline);
            match.Success.Should().BeTrue("fixture must produce one DG006; stdout: " + firstStdout);

            var baseline = Path.Combine(dir, "baseline.json");
            File.WriteAllText(baseline, System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = 2,
                CreatedAt = "2026-01-01T00:00:00Z",
                SchemaVersion = "1.0",
                GroundTruthMode = "Snapshot",
                DatabaseVersion = "unknown",
                SchemaHash = new string('A', 64),
                Violations = new[] { new { ruleId = "DG006", message = match.Groups["msg"].Value, severity = "Info", location = (object?)null, properties = (object?)null } },
            }));
            var config = Path.Combine(dir, "config.yml");
            File.WriteAllText(config, $"BaselineFilePath: {baseline}\nEnableBaseline: true\n");

            var (_, textStdout, textStderr) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--config", config, "--format", "text");
            textStdout.Should().NotContain("DG006");
            textStderr.Should().Contain("baseline: 1 violations suppressed by baseline.json");

            var (_, _, progressStderr) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--config", config, "--format", "text", "--progress");
            progressStderr.Should().Contain("\"Kind\":\"BaselineApplied\"")
                .And.Contain("\"Phase\":\"Validating rules\"")
                .And.Contain("\"Detail\":\"baseline.json\"")
                .And.Contain("\"SuppressedCount\":1");
            progressStderr.Should().NotContain("baseline: 1 violations suppressed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
