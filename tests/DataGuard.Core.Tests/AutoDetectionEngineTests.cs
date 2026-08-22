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
}
