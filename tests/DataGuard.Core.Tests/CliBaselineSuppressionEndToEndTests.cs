using System.Text.Json;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
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
            File.WriteAllText(config, "BaselineFilePath: baseline.json\nEnableBaseline: true\n");

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

    private const string Dg006Snapshot = """
        class Snapshot { void Build(ModelBuilder modelBuilder) {
            modelBuilder.Entity<Customer>(entity => {
                entity.ToTable("CUSTOMERS");
                entity.Property(item => item.FirstName).HasColumnName("x_y_z_unmatched");
            });
        }}
        """;

    private static string LegacyBaselineJson(string message) => JsonSerializer.Serialize(new
    {
        Version = 2,
        CreatedAt = "2026-01-01T00:00:00Z",
        SchemaVersion = "1.0",
        GroundTruthMode = "Snapshot",
        DatabaseVersion = "unknown",
        SchemaHash = new string('A', 64),
        Violations = new[] { new { ruleId = "DG006", message, severity = "Info", location = (object?)null, properties = (object?)null } },
    });

    [Fact]
    public void Validate_LegacyBaseline_StillSuppresses_AndPrintsOneUpgradeHint()
    {
        var dir = Directory.CreateTempSubdirectory("dg-baseline-legacy-hint").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "AppSnapshot.cs");
            File.WriteAllText(snapshot, Dg006Snapshot);
            var (_, firstStdout, _) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--format", "text");
            var message = Regex.Match(firstStdout, @"\[\w+\] DG006: (?<msg>.+?)(?: \(\d+:\d+\))?\r?$", RegexOptions.Multiline).Groups["msg"].Value;
            File.WriteAllText(Path.Combine(dir, "baseline.json"), LegacyBaselineJson(message));
            File.WriteAllText(Path.Combine(dir, "config.yml"), "BaselineFilePath: baseline.json\nEnableBaseline: true\n");

            var (_, stdout, stderr) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--config", "config.yml", "--format", "text");

            stdout.Should().NotContain("DG006");
            Lines(stderr).Count(line => line == "baseline contains 1 legacy entries; run 'dataguard baseline' to upgrade").Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_FingerprintBaseline_FromSarifPartialFingerprint_SuppressesWithoutLegacyHint()
    {
        var dir = Directory.CreateTempSubdirectory("dg-baseline-fp").FullName;
        try
        {
            var snapshot = Path.Combine(dir, "AppSnapshot.cs");
            File.WriteAllText(snapshot, Dg006Snapshot);
            var sarif = Path.Combine(dir, "out.sarif");
            RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--format", "sarif", "--output", sarif);
            using var document = JsonDocument.Parse(File.ReadAllText(sarif));
            var result = document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray().Single(item => item.GetProperty("ruleId").GetString() == "DG006");
            var fingerprint = result.GetProperty("partialFingerprints").GetProperty("dataguard/v2").GetString();

            // A message change (e.g. a reworded rule) no longer un-suppresses: the fingerprint is the identity.
            File.WriteAllText(Path.Combine(dir, "baseline.json"), JsonSerializer.Serialize(new
            {
                Version = 2,
                CreatedAt = "2026-01-01T00:00:00Z",
                SchemaVersion = "1.0",
                GroundTruthMode = "Snapshot",
                DatabaseVersion = "unknown",
                SchemaHash = new string('A', 64),
                Violations = new[] { new { RuleId = "DG006", Message = "reworded", Severity = "Info", Fingerprint = fingerprint, Count = 1 } },
            }));
            File.WriteAllText(Path.Combine(dir, "config.yml"), "BaselineFilePath: baseline.json\nEnableBaseline: true\n");

            var (_, stdout, stderr) = RunCli(null, dir, "validate", "--ef-snapshot", snapshot, "--config", "config.yml", "--format", "text");

            stdout.Should().NotContain("DG006");
            stderr.Should().Contain("baseline: 1 violations suppressed by baseline.json").And.NotContain("legacy entries");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Baseline_Command_RewritesLegacyEntriesAsFingerprints()
    {
        var dir = Directory.CreateTempSubdirectory("dg-baseline-upgrade").FullName;
        try
        {
            var tables = new[] { new SnapshotTable("CUSTOMERS", new[] { new SnapshotColumn("ID", "int", null, null, 10, 0, false, null) }) };
            await new BaselineManager(Path.Combine(dir, ".dataguard-snapshot.json")).CreateSnapshotAsync(
                Array.Empty<ContractViolation>(), "1.0", "16.0", tables, Array.Empty<SnapshotStoredProcedure>(), "sqlserver", null, "CHAR", null);
            var baselinePath = Path.Combine(dir, ".dataguard-baseline.json");
            File.WriteAllText(baselinePath, LegacyBaselineJson("stale finding"));

            var (exitCode, stdout, stderr) = RunCli(null, dir, "baseline");

            exitCode.Should().Be(0, stdout + stderr);
            stdout.Should().Contain("Baseline created with").And.Contain("Upgraded 1 legacy entries to fingerprint dataguard/v2");
            var rewritten = await new BaselineManager(baselinePath).LoadAsync();
            BaselineManager.CountLegacyEntries(rewritten!).Should().Be(0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
