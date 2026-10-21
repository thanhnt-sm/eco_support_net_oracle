using System.Text.Json;
using DataGuard.VisualStudio;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class IdeAcquisitionSummaryProcessTests
{
    [Fact]
    public void IdeSafeSummary_WithLoadedEfGroundTruthAndSourceGap_ProducesWarningCompletion()
    {
        var directory = Directory.CreateTempSubdirectory("dg-ide-acquisition-summary-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Repo.cs"), """
                public class Customer { public int Id { get; set; } public string Name { get; set; } }
                public class Connection { public void Query<T>(string sql) {} }
                public class Repo
                {
                    public void Load(Connection connection, string input)
                    {
                        var sql = "SELECT Id, Name FROM Customers";
                        sql = input;
                        connection.Query<Customer>(sql);
                    }
                }
                """);
            var snapshot = Path.Combine(directory, "AppModelSnapshot.cs");
            File.WriteAllText(snapshot, """
                class Snapshot
                {
                    void Build(ModelBuilder modelBuilder)
                    {
                        modelBuilder.Entity<Customer>(b => { b.ToTable("Customers"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); });
                    }
                }
                """);
            var sarifPath = Path.Combine(directory, "results.sarif");
            var run = CliProcessTestRunner.Run(
                directory, null, null,
                "validate", "--project", directory,
                "--ide-safe", "--progress", "--format", "sarif", "--output", sarifPath);

            Assert.True(run.ExitCode == 0, run.Stdout + run.Stderr);
            Assert.Contains("ACQUISITION:", run.Stderr);
            Assert.StartsWith("ide-safe: active", run.Stderr);
            using var summary = JsonDocument.Parse(run.Stderr.Split('\n').Single(line => line.StartsWith("{", StringComparison.Ordinal) && line.Contains("\"Kind\":\"Summary\"", StringComparison.Ordinal)));
            var data = summary.RootElement.GetProperty("Data");

            // Adapt actual CLI Summary.Data into the real host model. The Windows parser depends on VSSDK.
            var progress = new ProgressReadResult
            {
                HasSummary = true,
                ErrorCount = data.GetProperty("ErrorCount").GetInt32(),
                WarningCount = data.GetProperty("WarningCount").GetInt32(),
                AcquisitionDiagnosticCount = data.GetProperty("AcquisitionDiagnosticCount").GetInt32(),
                GroundTruth = data.GetProperty("GroundTruth").GetString(),
            };
            Assert.Equal("entities", progress.GroundTruth);
            Assert.True(progress.AcquisitionDiagnosticCount > 0);
            Assert.Equal(0, progress.ErrorCount);
            Assert.Equal(0, progress.WarningCount);
            Assert.DoesNotContain("Warning: no ground truth", run.Stderr);
            Assert.True(File.Exists(sarifPath));

            var explanation = ExitCodeExplainer.Explain("validate", new CliRunOutcome { ExitCode = run.ExitCode, Progress = progress }, File.Exists(sarifPath));
            Assert.StartsWith("[WARN] Validation incomplete: ", explanation);
            Assert.Contains("source acquisition diagnostic(s)", explanation);
            Assert.DoesNotContain("[OK]", explanation);
            Assert.Contains("acquisition diagnostics " + progress.AcquisitionDiagnosticCount, ExitCodeExplainer.DescribeStatus(progress));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
