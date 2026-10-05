using System.Text;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// End-to-end CLI follow-ups of the Program.cs split (red-team rec 20): MySQL live schema verification, the snapshot
/// provider contract (writers always record it; validate fails closed on an empty provider in a v4 file), a single
/// DG1291 line per oversized literal, and the setup wizard's answers surviving the config loader.
/// </summary>
public class CliFollowUpEndToEndTests
{
    private const string SnapshotFileName = ".dataguard-snapshot.json";

    private static readonly SnapshotTable[] Tables =
    {
        new("Users", new[] { new SnapshotColumn("Id", "int", null, null, 10, 0, false, null) }, "dbo"),
    };

    [Fact]
    public void VerifyShape_MySqlProvider_UsesLiveSchemaProvider()
    {
        var dir = Directory.CreateTempSubdirectory("dg-verify-mysql").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), """
                using Dapper;
                public class User { public int Id { get; set; } }
                public class Repo { public object F(System.Data.IDbConnection c) => c.Query<User>("SELECT Id FROM Users"); }
                """);

            // Nothing listens on port 1, so nothing is described; before the mysql case existed this exited 2 with
            // "provider 'mysql' does not support live query schema verification".
            var run = CliProcessTestRunner.Run(
                dir,
                null,
                null,
                "verify-shape", "--provider", "mysql", "--project", dir,
                "--connection", "Server=127.0.0.1;Port=1;Database=dg;User ID=dg;Password=dg;Connection Timeout=3");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            run.Stderr.Should().NotContain("does not support live query schema verification");
            run.Stdout.Should().Contain("=== DataGuard Verify-Shape Report (mysql) ===");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Baseline_RecordsProvider()
    {
        var dir = Directory.CreateTempSubdirectory("dg-baseline-provider").FullName;
        try
        {
            await new BaselineManager(Path.Combine(dir, SnapshotFileName)).CreateSnapshotAsync(
                Array.Empty<ContractViolation>(), "1.0", "16.0", Tables, Array.Empty<SnapshotStoredProcedure>(), "sqlserver", null, "CHAR", null);

            var run = CliProcessTestRunner.Run(dir, null, null, "baseline");

            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
            var baseline = await new BaselineManager(Path.Combine(dir, ".dataguard-baseline.json")).LoadAsync();
            baseline!.Provider.Should().Be("sqlserver");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_V4SnapshotWithoutProvider_IsUnevaluated()
    {
        var dir = Directory.CreateTempSubdirectory("dg-v4-no-provider").FullName;
        try
        {
            await new BaselineManager(Path.Combine(dir, SnapshotFileName)).CreateSnapshotAsync(
                Array.Empty<ContractViolation>(), "1.0", "16.0", Tables, Array.Empty<SnapshotStoredProcedure>(), provider: null, null, "CHAR", null);

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", "sqlserver");

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("UNEVALUATED: snapshot format version 4 records no provider (expected 'sqlserver')");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_V3SnapshotWithoutProvider_StaysLenient()
    {
        var dir = Directory.CreateTempSubdirectory("dg-v3-no-provider").FullName;
        try
        {
            // CreateBaselineAsync writes format 4 since Phase 5C; a tables-only v3 file is what older releases produced.
            var snapshot = await LegacySnapshotFiles.WriteV3Async(Path.Combine(dir, SnapshotFileName), Tables, provider: null);
            snapshot.Version.Should().Be(3);
            snapshot.Provider.Should().BeNull();

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--provider", "sqlserver");

            run.Stderr.Should().NotContain("records no provider").And.NotContain("does not match");
            run.ExitCode.Should().Be(0, run.Stdout + run.Stderr);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_OversizedLiteral_IsReportedOnce()
    {
        var dir = Directory.CreateTempSubdirectory("dg-dg1291").FullName;
        try
        {
            var columns = new StringBuilder();
            for (var i = 0; i < 40_000; i++)
            {
                columns.Append(i == 0 ? string.Empty : ", ").Append('c').Append(i);
            }

            File.WriteAllText(
                Path.Combine(dir, "Repo.cs"),
                $"using Dapper;\npublic class Repo {{ public void F(System.Data.IDbConnection c) {{ c.Query<int>(\"SELECT {columns} FROM T\"); }} }}\n");

            var run = CliProcessTestRunner.Run(dir, null, null, "validate", "--project", dir, "--allow-syntactic-only");
            var output = run.Stdout + run.Stderr;

            run.ExitCode.Should().Be(3, "a skipped literal still makes the result incomplete");
            output.Should().Contain("[WARN] DG1291 SQL literal in Repo.cs:2");
            CountOccurrences(output, "(cap 262144); skipped").Should().Be(1, output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void InitWizard_AnswersSurviveConfigLoader()
    {
        var dir = Directory.CreateTempSubdirectory("dg-wizard-answers").FullName;
        var configPath = Path.Combine(dir, "wizard.yml");
        try
        {
            // Answers: no connection string, naming 3 (exact match), ground truth 3 (Manual, no baseline).
            var wizard = CliProcessTestRunner.Run(dir, "\n3\n3\n", null, "init", "--wizard", "--output", configPath);
            wizard.ExitCode.Should().Be(0, wizard.Stdout + wizard.Stderr);

            var shown = CliProcessTestRunner.Run(dir, null, null, "config", "validate", "--config", configPath);

            shown.ExitCode.Should().Be(0, shown.Stdout + shown.Stderr);
            shown.Stdout.Should().Contain("GroundTruthMode: Manual")
                .And.Contain("NamingConvention: ExactMatch")
                .And.Contain("EnableBaseline: False");
            shown.Stderr.Should().NotContain("unknown configuration keys");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void VerifyShape_FailedAcquisition_Exits3InsteadOfZeroQueries()
    {
        var dir = Directory.CreateTempSubdirectory("dg-verify-shape-unevaluated").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repo.cs"), """
                using Dapper;
                public class Customer { public int Id { get; set; } }
                public class Repo { public void F(System.Data.IDbConnection c) { var rows = c.Query<Customer>("SELECT Id FROM Customers"); } }
                """);

            // Unreachable SQL Server: acquisition fails before any query can be described.
            var run = CliProcessTestRunner.Run(dir, null, null,
                "verify-shape", "--provider", "sqlserver", "--project", Path.Combine(dir, "Repo.cs"),
                "--connection", "Server=127.0.0.1,1;Database=x;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=true");

            run.ExitCode.Should().Be(3, run.Stdout + run.Stderr);
            run.Stderr.Should().Contain("UNEVALUATED");
            run.Stdout.Should().NotContain("Queries evaluated: 0");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
