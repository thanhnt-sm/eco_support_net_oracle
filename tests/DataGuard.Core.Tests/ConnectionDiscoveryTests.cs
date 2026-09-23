using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

public class ConnectionDiscoveryTests
{
    [Fact]
    public void MaskConnectionString_ReplacesPasswords()
    {
        var raw1 = "Server=myServerAddress;Database=myDataBase;Uid=myUsername;Pwd=mySecretPassword;";
        var masked1 = ConnectionDiscovery.MaskConnectionString(raw1);
        masked1.Should().Contain("Pwd=***");
        masked1.Should().NotContain("mySecretPassword");

        var raw2 = "Data Source=oracle.host;User Id=scott;Password=tiger;";
        var masked2 = ConnectionDiscovery.MaskConnectionString(raw2);
        masked2.Should().Contain("Password=***");
        masked2.Should().NotContain("tiger");
    }

    [Fact]
    public void MaskConnectionString_HandlesUriWithAtSignInPassword_AndEscapedQuotes()
    {
        var uriWithAt = "postgres://user:p@ssword@localhost:5432/orders";
        var maskedUri = ConnectionDiscovery.MaskConnectionString(uriWithAt);
        maskedUri.Should().Be("postgres://user:***@localhost:5432/orders");

        var quotedEscaped = @"Server=localhost;Password=""my""""pass"";Database=test;";
        var maskedQuoted = ConnectionDiscovery.MaskConnectionString(quotedEscaped);
        maskedQuoted.Should().Contain("Password=***");
        maskedQuoted.Should().NotContain("my");
        maskedQuoted.Should().NotContain("pass");
    }

    [Fact]
    public void InferProviderFromTypeName_IdentifiesKnownProviders()
    {
        ConnectionDiscovery.InferProviderFromTypeName("OracleConnection").Should().Be("oracle");
        ConnectionDiscovery.InferProviderFromTypeName("NpgsqlConnection").Should().Be("postgresql");
        ConnectionDiscovery.InferProviderFromTypeName("SqlConnection").Should().Be("sqlserver");
        ConnectionDiscovery.InferProviderFromTypeName("MySqlConnection").Should().Be("mysql");
    }

    [Fact]
    public async Task DiscoverConnections_ScansAppSettingsAndSyntax()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_conn_test_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var appSettings = @"{
  ""ConnectionStrings"": {
    ""DefaultConnection"": ""Server=localhost;Database=testdb;User Id=sa;Password=supersecret;"",
    ""OracleDb"": ""Data Source=oracle.local;User Id=app;Password=oraclepass;""
  }
}";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "appsettings.json"), appSettings);

            var code = @"
namespace App;
public class Service
{
    public void Init()
    {
        var c = new Microsoft.Data.SqlClient.SqlConnection(""Server=127.0.0.1;Password=secret"");
    }
}";
            var tree = CSharpSyntaxTree.ParseText(code);

            var connections = ConnectionDiscovery.DiscoverConnections(tempDir, new[] { tree });

            connections.Should().Contain(c => c.Name == "DefaultConnection" && c.Provider == "sqlserver");
            connections.Should().Contain(c => c.Name == "OracleDb" && c.Provider == "oracle");

            var defConn = connections.First(c => c.Name == "DefaultConnection");
            defConn.ConnectionStringHint.Should().NotContain("supersecret");
            defConn.ConnectionStringHint.Should().Contain("Password=***");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
