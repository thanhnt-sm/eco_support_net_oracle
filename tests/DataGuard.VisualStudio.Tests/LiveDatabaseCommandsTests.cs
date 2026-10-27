using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// Refresh Snapshot / Verify SQL Shapes: pure option validation, host extraction for the confirmation
/// modal, argv (never --config, --ide-safe or --connection), separate live consent, and outcome texts.
/// </summary>
public partial class LiveDatabaseCommandsTests
{
    private const string Sln = @"D:\repo\solution\App.sln";

    // ---- options ----
    [Theory]
    [InlineData("DATAGUARD_CONNECTION_STRING", "sqlserver", "", true)]
    [InlineData("MyDb", "oracle", "HR", true)]
    [InlineData("MyDb", "postgresql", "public", true)]
    [InlineData("MyDb", "mysql", "app$1", true)]
    [InlineData("", "sqlserver", "", false)]
    [InlineData("1abc", "sqlserver", "", false)]
    [InlineData("a b", "sqlserver", "", false)]
    [InlineData("a;b", "sqlserver", "", false)]
    [InlineData("MyDb", "oracle; drop", "", false)]
    [InlineData("MyDb", "sqlite", "", false)]
    [InlineData("MyDb", "oracle", "1bad", false)]
    [InlineData("MyDb", "oracle", "a b", false)]
    public void Validate_AcceptsIdentifiersAndProviderWhitelist(string name, string provider, string schema, bool valid)
    {
        var options = LiveDatabaseOptions.Validate(name, provider, schema);

        (options.Error == null).Should().Be(valid, options.Error);
        if (valid)
        {
            options.VariableName.Should().Be(name);
            options.Provider.Should().Be(provider);
            options.Schema.Should().Be(string.IsNullOrEmpty(schema) ? null : schema);
        }
    }

    [Fact]
    public void Validate_TrimsAndDefaultsProvider()
    {
        var options = LiveDatabaseOptions.Validate("  MyDb ", "  SqlServer ", "  dbo ");

        options.Error.Should().BeNull();
        options.VariableName.Should().Be("MyDb");
        options.Provider.Should().Be("sqlserver");
        options.Schema.Should().Be("dbo");
        LiveDatabaseOptions.Validate("MyDb", null, null).Provider.Should().Be("sqlserver");
    }

    // ---- host extraction (vectors ported from the VS Code live-database-confirmation tests, unmasked) ----
    [Theory]
    [InlineData("Server=db-prod.corp.example.com,1433;Database=app;User Id=svc;Password=hunter2;", "db-prod.corp.example.com:1433")]
    [InlineData("Data Source=tcp:sql01\\INST;Initial Catalog=app;Integrated Security=true", "sql01")]
    [InlineData("Host=10.20.30.40;Port=5432;Username=u;Password=p", "10.20.30.40:5432")]
    [InlineData("postgres://user:secretpass@pg.internal.example.org:5432/app", "pg.internal.example.org:5432")]
    [InlineData("User Id=u;Password=p;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=ora-a.example.net)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=svc)))", "ora-a.example.net:1521")]
    [InlineData("User Id=u;Password=p;Data Source=//oradb.local:1521/svc", "oradb.local:1521")]
    [InlineData("Server=localhost;Database=app", "localhost")]
    [InlineData("User Id=scott;Password=tiger;Data Source=orcl", "orcl")]
    public void Extract_ReturnsTheUnmaskedHostAndPort(string connectionString, string expected)
    {
        ConnectionHostExtractor.Extract(connectionString).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Database=app;User Id=u;Password=p")]
    [InlineData("Server=;Database=app")]
    public void Extract_WithoutHost_ReturnsUnknownHost(string? connectionString)
    {
        ConnectionHostExtractor.Extract(connectionString).Should().Be(ConnectionHostExtractor.UnknownHost);
    }

    [Fact]
    public void Extract_NeverReturnsCredentials()
    {
        ConnectionHostExtractor.Extract("Server=db.example.com;User Id=admin;Password=SuperSecret").Should().NotContainAny("Super", "admin");
        ConnectionHostExtractor.Extract("postgres://admin:SuperSecret@host.example.com/app").Should().Be("host.example.com");
    }

    // ---- argv ----
    [Fact]
    public void BuildSnapshotRefreshArguments_NeverPassesConfigIdeSafeOrConnection()
    {
        CliArgumentBuilder.BuildSnapshotRefreshArguments("MyDb", "oracle", "HR")
            .Should().Be("snapshot refresh --connection-env \"MyDb\" --provider \"oracle\" --schema \"HR\"");
        var args = CliArgumentBuilder.BuildSnapshotRefreshArguments("MyDb", "sqlserver", null);
        args.Should().Be("snapshot refresh --connection-env \"MyDb\" --provider \"sqlserver\"");
        args.Should().NotContainAny("--config", "--ide-safe", "--connection ", "--schema");
    }

    [Fact]
    public void BuildVerifyShapeArguments_ProjectJsonOutput_NoConfig()
    {
        var args = CliArgumentBuilder.BuildVerifyShapeArguments(@"D:\s", "MyDb", "postgresql", @"C:\t\summary.json");

        args.Should().Be("verify-shape --project \"D:\\s\" --connection-env \"MyDb\" --provider \"postgresql\" --format json --output \"C:\\t\\summary.json\"");
        args.Should().NotContainAny("--config", "--ide-safe", "--connection ");
    }

    [Fact]
    public void LiveCommandNames_AreTheCliCommands()
    {
        LiveDatabaseCommands.CommandName(LiveCommandKind.RefreshSnapshot).Should().Be("snapshot refresh");
        LiveDatabaseCommands.CommandName(LiveCommandKind.VerifyShape).Should().Be("verify-shape");
        CliRunSession.DescribeRunningStatus("snapshot refresh").Should().Be("StackAccord: Refreshing snapshot...");
        CliRunSession.DescribeRunningStatus("verify-shape").Should().Be("StackAccord: Verifying shapes...");
        CliRunSession.DescribeRunningStatus("validate").Should().Be("StackAccord: Validating...");
        CliRunSession.DescribeRunningStatus("assess").Should().Be("StackAccord: Assessing...");
    }

    // ---- consent ----
    [Fact]
    public void LiveConsentKey_DiffersFromValidateKey_ForTheSameSolution()
    {
        var config = Encoding.UTF8.GetBytes("GroundTruthMode: Snapshot\n");
        var validateKey = SolutionTrustGate.ComputeConsentKey(@"D:\repo", Sln, config);
        var liveKey = SolutionTrustGate.ComputeLiveConsentKey(@"D:\repo", Sln);

        liveKey.Should().NotBe(validateKey);
        liveKey.Should().EndWith(SolutionTrustGate.LiveConsentSuffix);
        SolutionTrustGate.ComputeLiveConsentKey(@"d:\REPO\", @"d:\REPO\solution\app.SLN").Should().Be(liveKey, "path casing and a trailing separator must not change the key");
    }

    [Fact]
    public void LivePromptText_NamesVariableSnapshotPathAndNotIdeSafe()
    {
        var text = SolutionTrustGate.BuildLivePromptText(@"D:\repo", "MyDb");

        text.Should().Contain("not IDE-safe");
        text.Should().Contain("MyDb");
        text.Should().Contain(Path.Combine(@"D:\repo", ".dataguard-snapshot.json"));
        text.Should().Contain(".dataguard.yml is never read");
        text.Should().NotContain("--ide-safe");
    }

    [Fact]
    public void BannerModeLine_SaysNotIdeSafeAndNoYml()
    {
        LiveDatabaseCommands.DescribeMode("MyDb").Should().Be("live database (not ide-safe; reads environment variable MyDb; .dataguard.yml is not read)");
    }

    // ---- snapshot: wrote line ----
    [Fact]
    public async Task SnapshotWroteLine_IsCapturedFromExactPrefix_AndEchoed()
    {
        var written = new StringBuilder();
        var reader = new ProgressStreamReader(new RuleInventory(), text => written.Append(text));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Connecting\nsnapshot: wrote .dataguard-snapshot.json\n[DG1290] C:\\x\\snapshot: wrote evil.json: skipped\n"));
        using var streamReader = new StreamReader(stream);

        var result = await reader.ReadAsync(streamReader);

        result.SnapshotWrittenPath.Should().Be(".dataguard-snapshot.json");
        written.ToString().Should().Contain("[StackAccord CLI] snapshot: wrote .dataguard-snapshot.json");
    }

    // ---- outcomes ----
    [Fact]
    public void Describe_SnapshotExitZero_WithEvidence_IsOk()
    {
        var lines = LiveCommandOutcome.Describe(LiveCommandKind.RefreshSnapshot, 0, new SnapshotEvidence(".dataguard-snapshot.json", true), null);

        lines.Should().ContainSingle().Which.Should().Be("[OK] Snapshot refreshed: .dataguard-snapshot.json");
    }

    [Fact]
    public void Describe_SnapshotExitZero_WithoutEvidence_IsError()
    {
        LiveCommandOutcome.Describe(LiveCommandKind.RefreshSnapshot, 0, new SnapshotEvidence(".dataguard-snapshot.json", false), null)[0]
            .Should().Be("[ERROR] CLI exited 0 but no snapshot was written in the solution directory");
        LiveCommandOutcome.Describe(LiveCommandKind.RefreshSnapshot, 0, null, null)[0]
            .Should().Be("[ERROR] CLI exited 0 but no snapshot was written in the solution directory");
    }

    [Theory]
    [InlineData(1, "[ERROR] Snapshot refresh failed: unreachable server, permissions or acquisition error (see [WARN]/[StackAccord CLI] lines)")]
    [InlineData(2, "[ERROR] Arguments or credential problem: variable MyDb is unset/empty or the provider is unsupported")]
    [InlineData(3, "[ERROR] No database connection resolved")]
    [InlineData(4, "[ERROR] The CLI refused the snapshot path")]
    [InlineData(130, "[CANCELLED] Cancelled by user.")]
    [InlineData(7, "[ERROR] Unexpected exit code 7.")]
    public void Describe_SnapshotExitCodes(int exitCode, string expected)
    {
        LiveCommandOutcome.Describe(LiveCommandKind.RefreshSnapshot, exitCode, null, null, "MyDb")[0].Should().Be(expected);
    }

    [Theory]
    [InlineData(2, "[ERROR] Arguments or credential problem: variable MyDb is unset/empty or the provider is unsupported")]
    [InlineData(3, "[ERROR] Verify shapes incomplete: contract acquisition did not complete (see [WARN]/[StackAccord CLI] lines)")]
    [InlineData(4, "[ERROR] The CLI refused to write results to the output path (see [StackAccord CLI] lines above).")]
    [InlineData(1, "[ERROR] verify-shape failed (see lines above)")]
    public void Describe_VerifyShapeExitCodes_WithoutSummary(int exitCode, string expected)
    {
        LiveCommandOutcome.Describe(LiveCommandKind.VerifyShape, exitCode, null, null, "MyDb")[0].Should().Be(expected);
    }

    private const string VerifyShapeJson = """
        {
          "provider": "sqlserver",
          "project": "D:\\repo",
          "filesScanned": 3,
          "queriesFound": 4,
          "connectionsFound": 0,
          "violationsCount": 0,
          "connections": [],
          "queriesVerified": 4,
          "results": [
            { "sql": "SELECT Id, Name FROM Users", "targetType": "User", "status": "verified", "dbColumnCount": 2, "matchedProperties": ["Id","Name"], "missingInDatabase": [], "extraInDatabase": [] },
            { "sql": "SELECT Id, Email, Phone, Street, City, Country FROM Customers WHERE Id = @id", "targetType": "Customer", "status": "mismatch", "dbColumnCount": 2, "matchedProperties": ["Id"], "missingInDatabase": ["Email"], "extraInDatabase": [] },
            { "sql": "SELECT 1", "targetType": null, "status": "untyped", "dbColumnCount": 1, "matchedProperties": [], "missingInDatabase": [], "extraInDatabase": [] },
            { "sql": "SELECT * FROM Orders", "targetType": "Order", "status": "undetermined", "dbColumnCount": 0, "matchedProperties": [], "missingInDatabase": [], "extraInDatabase": [] }
          ],
          "queries": [
            { "sql": "SELECT Id, Name FROM Users", "location": { "file": "D:\\repo\\src\\UserRepo.cs", "line": 12 }, "targetType": "User", "operation": "Read" },
            { "sql": "SELECT Id, Email, Phone, Street, City, Country FROM Customers WHERE Id = @id", "location": { "file": "D:\\repo\\src\\CustomerRepo.cs", "line": 40 }, "targetType": "Customer", "operation": "Read" },
            { "sql": "SELECT 1", "location": null, "targetType": null, "operation": "Read" },
            { "sql": "SELECT * FROM Orders", "location": { "file": "D:\\other\\OrderRepo.cs", "line": 7 }, "targetType": "Order", "operation": "Read" }
          ]
        }
        """;

    [Fact]
    public void VerifyShapeSummary_ParsesCountsAndZipsLocations()
    {
        var summary = VerifyShapeSummary.TryParse(VerifyShapeJson, @"D:\repo");

        summary.Should().NotBeNull();
        summary!.Verified.Should().Be(1);
        summary.Mismatch.Should().Be(1);
        summary.Undetermined.Should().Be(1);
        summary.Untyped.Should().Be(1);
        summary.Details.Should().HaveCount(3);
        summary.Details[0].Should().Be("mismatch src\\CustomerRepo.cs:40 \"SELECT Id, Email, Phone, Street, City, C…\" → Customer");
        summary.Details[1].Should().Be("untyped ?:? \"SELECT 1\" → (untyped)");
        summary.Details[2].Should().Be("undetermined D:\\other\\OrderRepo.cs:7 \"SELECT * FROM Orders\" → Order");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"results\": \"x\"}")]
    public void VerifyShapeSummary_RejectsMalformed(string json)
    {
        VerifyShapeSummary.TryParse(json, @"D:\repo").Should().BeNull();
    }

    [Fact]
    public void Describe_VerifyShape_WithSummary_ListsCountsAndNonVerifiedResults()
    {
        var summary = VerifyShapeSummary.TryParse(VerifyShapeJson, @"D:\repo");

        var lines = LiveCommandOutcome.Describe(LiveCommandKind.VerifyShape, 1, null, summary);

        lines[0].Should().Be("[WARN] Verify shapes: 1 verified, 1 mismatches, 1 undetermined, 1 untyped");
        lines.Should().HaveCount(4);
        lines[1].Should().Be("  mismatch src\\CustomerRepo.cs:40 \"SELECT Id, Email, Phone, Street, City, C…\" → Customer");
        lines[2].Should().Be("  untyped ?:? \"SELECT 1\" → (untyped)");

        var clean = VerifyShapeSummary.TryParse(VerifyShapeJson.Replace("\"mismatch\"", "\"verified\"").Replace("\"undetermined\"", "\"verified\"").Replace("\"untyped\"", "\"verified\""), @"D:\repo");
        LiveCommandOutcome.Describe(LiveCommandKind.VerifyShape, 0, null, clean)[0].Should().Be("[OK] Verify shapes: 4 verified, 0 mismatches, 0 undetermined, 0 untyped");
    }

    [Fact]
    public void SnapshotEvidence_RequiresExactPrefixPathInsideSolutionAndFreshFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dg_live_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var start = DateTime.UtcNow.AddSeconds(-1);
            LiveCommandOutcome.ResolveSnapshotEvidence(dir, ".dataguard-snapshot.json", start).Should().BeNull("file does not exist");
            File.WriteAllText(Path.Combine(dir, ".dataguard-snapshot.json"), "{}");
            var evidence = LiveCommandOutcome.ResolveSnapshotEvidence(dir, ".dataguard-snapshot.json", start);
            evidence.Should().NotBeNull();
            evidence!.Value.Written.Should().BeTrue();
            evidence.Value.Path.Should().Be(Path.Combine(dir, ".dataguard-snapshot.json"));
            LiveCommandOutcome.ResolveSnapshotEvidence(dir, ".dataguard-snapshot.json", DateTime.UtcNow.AddMinutes(5)).Should().BeNull("file is older than the run");
            LiveCommandOutcome.ResolveSnapshotEvidence(dir, @"..\outside.json", start).Should().BeNull("path escapes the solution directory");
            LiveCommandOutcome.ResolveSnapshotEvidence(dir, null, start).Should().BeNull("no snapshot: wrote line");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- vsct pin ----
    [Fact]
    public void Menus_DeclareLiveCommands_AndNoInit()
    {
        var vsct = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "DataGuard.VisualStudio", "Commands", "Menus.vsct"));

        vsct.Should().NotContain("InitCommand");
        vsct.Should().NotContain("Initialize DataGuard");
        vsct.Should().Contain("<IDSymbol name=\"RefreshSnapshotCommand\" value=\"0x0106\" />");
        vsct.Should().Contain("<IDSymbol name=\"VerifyShapeCommand\" value=\"0x0107\" />");
        vsct.Should().Contain("<ButtonText>Refresh Snapshot</ButtonText>");
        vsct.Should().Contain("<ButtonText>Verify SQL Shapes Against Database</ButtonText>");
        foreach (var id in new[] { "0x0100", "0x0101", "0x0102", "0x0103", "0x0104", "0x0105" })
        {
            vsct.Should().Contain("value=\"" + id + "\"");
        }
    }

    [Fact]
    public void InfoBarManager_IsGone()
    {
        File.Exists(Path.Combine(TestPaths.RepoRoot, "src", "DataGuard.VisualStudio", "UI", "InfoBarManager.cs")).Should().BeFalse();
    }
}
