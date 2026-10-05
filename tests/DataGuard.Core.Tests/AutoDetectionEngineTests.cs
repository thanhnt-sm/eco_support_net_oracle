using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.AutoDetection;
using DataGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

[Collection("Sequential")]
public class AutoDetectionEngineTests : IDisposable
{
    public AutoDetectionEngineTests()
    {
        // Clear env vars that may leak from other test classes running in parallel
        Environment.SetEnvironmentVariable("DATAGUARD_CONNECTION_STRING", null);
        Environment.SetEnvironmentVariable("DATAGUARD_PROVIDER", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DATAGUARD_CONNECTION_STRING", null);
        Environment.SetEnvironmentVariable("DATAGUARD_PROVIDER", null);
    }

    private static string CreateProject(params (string relativePath, string content)[] files)
    {
        var root = Directory.CreateTempSubdirectory("dg-autodetect").FullName;
        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        return root;
    }

    [Fact]
    public async Task DetectAsync_EmptyProject_ReturnsDefaults()
    {
        var root = CreateProject();

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.Should().NotBeNull();
        config.ConnectionString.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_AppSettingsWithSqlServerConnection_DetectsProviderAndConnection()
    {
        var root = CreateProject(
            ("appsettings.json", """{ "ConnectionStrings": { "Default": "Server=localhost;Database=Orders;Trusted_Connection=True" }, "Provider": "SqlServer" }"""),
            ("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"));

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.ConnectionString.Should().Be("Server=localhost;Database=Orders;Trusted_Connection=True");
        config.SqlServer.Should().NotBeNull("provider defaults are applied for the detected provider");
    }

    [Fact]
    public async Task DetectAsync_AppSettingsWithOracleConnection_DetectsOracle()
    {
        var root = CreateProject(
            ("appsettings.json", """{ "ConnectionStrings": { "Default": "User Id=app;Password=pw;Data Source=oraclehost:1521/ORCL" } }"""),
            ("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"));

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.Oracle.Should().NotBeNull("provider defaults are applied for the detected provider");
    }

    [Fact]
    public async Task DetectAsync_DataguardYamlProvider_DetectsProvider()
    {
        var root = CreateProject(
            (".dataguard.yml", "provider: oracle\n"),
            ("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"));

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.Oracle.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_EfCorePackage_DetectsEfCore()
    {
        var root = CreateProject(
            ("App.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />
                  </ItemGroup>
                </Project>
                """));

        // DetectAsync must not throw and must complete the scan.
        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_DapperPackage_Completes()
    {
        var root = CreateProject(
            ("App.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Dapper" Version="2.1.35" />
                  </ItemGroup>
                </Project>
                """));

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_SnakeCaseHeavyCode_DetectsSnakeCaseConvention()
    {
        var root = CreateProject(
            ("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"),
            ("Code.cs", "var customer_id = 1; var order_date = 2; var total_amount = 3;"));

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.NamingConvention.Should().Be(NamingConvention.SnakeCaseToPascalCase);
    }

    [Theory]
    [InlineData("Host=db;Port=5432;Database=shop;Username=app;Password=pw", DatabaseProvider.PostgreSQL, "postgresql")]
    [InlineData("postgresql://app:pw@db:5432/shop", DatabaseProvider.PostgreSQL, "postgresql")]
    [InlineData("Server=db;Port=3306;Database=shop;Uid=app;Pwd=pw", DatabaseProvider.MySQL, "mysql")]
    [InlineData("Server=localhost;Initial Catalog=Orders;Encrypt=True", DatabaseProvider.SqlServer, "sqlserver")]
    [InlineData("User Id=app;Password=pw;Data Source=oraclehost:1521/ORCL", DatabaseProvider.Oracle, "oracle")]
    public async Task DetectProviderAsync_ScoresConnectionStringsForEveryProvider(string connection, DatabaseProvider expected, string key)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { ConnectionStrings = new { Default = connection } });
        var root = CreateProject(("appsettings.json", json));

        var engine = new AutoDetectionEngine(root);
        (await engine.DetectProviderAsync()).Should().Be(expected);
        (await engine.DetectAsync()).DefaultProvider.Should().Be(key);
    }

    [Theory]
    [InlineData("<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" Version=\"9.0.0\" />", DatabaseProvider.PostgreSQL)]
    [InlineData("<PackageReference Include=\"Pomelo.EntityFrameworkCore.MySql\" Version=\"9.0.0\" />", DatabaseProvider.MySQL)]
    [InlineData("<PackageReference Include=\"MySqlConnector\" Version=\"2.4.0\" />", DatabaseProvider.MySQL)]
    [InlineData("<PackageReference Include=\"Oracle.ManagedDataAccess.Core\" Version=\"23.6.0\" />", DatabaseProvider.Oracle)]
    [InlineData("<PackageReference Include=\"Microsoft.Data.SqlClient\" Version=\"5.2.0\" />", DatabaseProvider.SqlServer)]
    public async Task DetectProviderAsync_ScoresPackageReferences(string reference, DatabaseProvider expected)
    {
        var root = CreateProject(("App.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{reference}</ItemGroup></Project>"));

        (await new AutoDetectionEngine(root).DetectProviderAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task DetectProviderAsync_ScoresUseProviderCallsAndTiesAreUnknown()
    {
        var npgsql = CreateProject(("Startup.cs", "class S { void C(O o) { o.UseNpgsql(cs); } }"));
        (await new AutoDetectionEngine(npgsql).DetectProviderAsync()).Should().Be(DatabaseProvider.PostgreSQL);

        var tie = CreateProject(
            ("A.cs", "class A { void C(O o) { o.UseNpgsql(cs); } }"),
            ("B.cs", "class B { void C(O o) { o.UseMySql(cs, v); } }"));
        (await new AutoDetectionEngine(tie).DetectProviderAsync()).Should().BeNull("an even score decides nothing");
    }

    [Fact]
    public async Task DetectProviderAsync_ExplicitYamlAndEnvironmentWinOverEvidence()
    {
        var root = CreateProject(
            (".dataguard.yml", "DefaultProvider: postgres # local dev\n"),
            ("appsettings.json", """{ "ConnectionStrings": { "Default": "Server=localhost;Initial Catalog=Orders;Encrypt=True" } }"""));
        (await new AutoDetectionEngine(root).DetectProviderAsync()).Should().Be(DatabaseProvider.PostgreSQL);

        var envRoot = CreateProject(("appsettings.json", """{ "ConnectionStrings": { "Default": "Server=localhost;Initial Catalog=Orders;Encrypt=True" } }"""));
        Environment.SetEnvironmentVariable("DATAGUARD_PROVIDER", "mysql");
        (await new AutoDetectionEngine(envRoot).DetectProviderAsync()).Should().Be(DatabaseProvider.MySQL);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData(".git")]
    [InlineData(".vs")]
    public async Task DetectAsync_PrunesBuildAndToolDirectories(string pruned)
    {
        var root = CreateProject(
            ($"{pruned}/appsettings.json", """{ "ConnectionStrings": { "Default": "Host=db;Port=5432;Database=shop;Username=app" } }"""),
            ($"{pruned}/Gen.cs", "class Gen : DbContext { }"));

        var engine = new AutoDetectionEngine(root);
        (await engine.DetectProviderAsync()).Should().BeNull($"{pruned}/ must not be scanned");
        (await engine.DetectEfCoreAsync()).Should().BeFalse();
        (await engine.DetectAsync()).ConnectionString.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_PrefersShallowestAppSettings()
    {
        var root = CreateProject(
            ("src/Deep/appsettings.json", """{ "ConnectionStrings": { "Default": "Host=deep;Database=a" } }"""),
            ("appsettings.json", """{ "ConnectionStrings": { "Default": "Host=top;Database=b" } }"""));

        (await new AutoDetectionEngine(root).DetectAsync()).ConnectionString.Should().Be("Host=top;Database=b");
    }

    [Fact]
    public async Task DetectAsync_MissingRoot_ReturnsDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "dg-autodetect-missing-" + Guid.NewGuid().ToString("N"));

        var config = await new AutoDetectionEngine(root).DetectAsync();

        config.ConnectionString.Should().BeNull();
        config.DefaultProvider.Should().BeNull();
    }

    [Theory]
    [InlineData("public int Id { get; set; }", 1)]
    [InlineData("public string Name { get; set; }", 1)]
    [InlineData("public string CustomerOrderReference { get; set; }", 1)]
    [InlineData("public int? ParentId { get; set; }", 1)]
    [InlineData("public List<OrderLine> OrderLines { get; set; } = new();", 1)]
    [InlineData("public Dictionary<string, List<int>> ByKey { get; init; }", 1)]
    [InlineData("public byte[] RowVersion { get; set; }", 1)]
    [InlineData("public virtual Customer Customer { get; set; }", 1)]
    [InlineData("public string customer_id { get; set; }", 0)]
    [InlineData("private string Hidden { get; set; }", 0)]
    public void CountPascalCaseMembers_MatchesAnyNumberOfHumps(string source, int expected)
    {
        AutoDetectionEngine.CountPascalCaseMembers(source).Should().Be(expected);
    }

    [Fact]
    public async Task DetectAsync_PascalCaseHeavyCode_DetectsPascalConvention()
    {
        var root = CreateProject(("Order.cs", """
            public class Order
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public decimal TotalAmountIncludingTax { get; set; }
                public DateTime? ShippedAt { get; set; }
            }
            """));

        (await new AutoDetectionEngine(root).DetectAsync()).NamingConvention.Should().Be(NamingConvention.PascalCaseToSnakeCase);
    }

    [Theory]
    [InlineData(null, GroundTruthMode.Snapshot, false)]
    [InlineData("", GroundTruthMode.Snapshot, false)]
    [InlineData("1", GroundTruthMode.Snapshot, false)]
    [InlineData("2", GroundTruthMode.Snapshot, true)]
    [InlineData(" 2 ", GroundTruthMode.Snapshot, true)]
    [InlineData("3", GroundTruthMode.Manual, false)]
    [InlineData("9", GroundTruthMode.Snapshot, false)]
    public void Wizard_BaselineChoiceMapsToDistinctModes(string? choice, GroundTruthMode mode, bool enableBaseline)
    {
        InteractiveConfigBuilder.MapBaselineChoice(choice).Should().Be((mode, enableBaseline));
    }

    [Fact]
    public async Task Wizard_WritesChosenNamingBaselineAndDetectedProvider()
    {
        var root = CreateProject(("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Npgsql\" Version=\"9.0.0\" /></ItemGroup></Project>"));
        var configPath = Path.Combine(root, "wizard.yml");
        var console = new ScriptedConsole("", "3", "2");

        var config = await InteractiveConfigBuilder.RunWizardAsync(root, console, configPath);

        config.Should().BeEquivalentTo(new
        {
            NamingConvention = NamingConvention.ExactMatch,
            GroundTruthMode = GroundTruthMode.Snapshot,
            EnableBaseline = true,
            DefaultProvider = "postgresql",
            BaselineFilePath = ".dataguard-baseline.json",
        });
        var yaml = await File.ReadAllTextAsync(configPath);
        yaml.Should().Contain("NamingConvention: ExactMatch")
            .And.Contain("EnableBaseline: True")
            .And.Contain("DefaultProvider: postgresql")
            .And.NotContain("Password", "the wizard never persists a connection string");
    }

    [Fact]
    public async Task Wizard_InfersProviderFromEnteredConnectionStringWhenProjectHasNoEvidence()
    {
        var root = CreateProject();
        var config = await InteractiveConfigBuilder.RunWizardAsync(
            root,
            new ScriptedConsole("Server=db;Port=3306;Database=shop;Uid=app;Pwd=secret", "1", "1"),
            Path.Combine(root, "wizard.yml"));

        config.DefaultProvider.Should().Be("mysql");
        config.EnableBaseline.Should().BeFalse("choice 1 is a snapshot without a frozen baseline");
        (await File.ReadAllTextAsync(Path.Combine(root, "wizard.yml"))).Should().NotContain("secret");
    }

    private sealed class ScriptedConsole : IConsole
    {
        private readonly Queue<string> _answers;

        public ScriptedConsole(params string[] answers) => _answers = new Queue<string>(answers);

        public void Write(string value)
        {
        }

        public void WriteLine(string value)
        {
        }

        public string? ReadLine() => _answers.Count > 0 ? _answers.Dequeue() : null;

        public ConsoleKeyInfo ReadKey(bool intercept) => default;
    }
}
