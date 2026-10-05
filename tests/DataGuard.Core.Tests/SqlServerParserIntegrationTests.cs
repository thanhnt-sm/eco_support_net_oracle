using System;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Live SQL Server tests via Testcontainers. Gated by <see cref="LiveDbFactAttribute"/>: reported as Skipped unless
/// DATAGUARD_REQUIRE_LIVE_SQLSERVER=1, and the fixture fails loudly when that variable is set and the container cannot
/// start.
/// </summary>
public class SqlServerParserIntegrationTests : IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";
    private MsSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!LiveDbGate.IsEnabled(LiveDbTarget.SqlServer))
        {
            return; // Gated tests are Skipped and never construct this class; this only guards direct use.
        }

        try
        {
            _container = new MsSqlBuilder(Image)
                .WithPassword("DataGuard_Test_1!")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw LiveDbGate.ContainerStartFailed(LiveDbTarget.SqlServer, Image, ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [LiveDbFact(LiveDbTarget.SqlServer)]
    public async Task ExtractContractsAsync_ReadsProcedureParametersAndResultSet()
    {
        Assert.NotNull(_container);

        var cs = _container.GetConnectionString();
        await using (var conn = new SqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE dbo.Customers (Id INT NOT NULL, Status NVARCHAR(20) NULL DEFAULT N'active')";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = """
                CREATE PROCEDURE dbo.GetCustomer
                    @Id INT,
                    @Note VARCHAR(50) = NULL,
                    @OutName VARCHAR(200) OUTPUT,
                    @Display NVARCHAR(50) = NULL,
                    @Body NVARCHAR(MAX) = NULL
                AS
                BEGIN
                    SET @OutName = 'x';
                    SELECT CAST(1 AS INT) AS CustomerId, CAST('Ada' AS VARCHAR(50)) AS FullName, CAST(N'Ada' AS NVARCHAR(40)) AS DisplayName;
                END
                """;
            await cmd.ExecuteNonQueryAsync();

            // One T-SQL default: sys.parameters.has_default_value is 0 for it (only CLR procedures populate it).
            cmd.CommandText = """
                CREATE PROCEDURE dbo.ListOrders
                    @CustomerId INT,
                    @Status NVARCHAR(20) = N'open'
                AS
                    SELECT @CustomerId AS CustomerId, @Status AS Status;
                """;
            await cmd.ExecuteNonQueryAsync();

            // sp_describe_first_result_set cannot describe a temp-table result (error 11526): must not abort extraction.
            cmd.CommandText = """
                CREATE PROCEDURE dbo.UsesTempTable
                AS
                BEGIN
                    CREATE TABLE #t (Id INT);
                    SELECT Id FROM #t;
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var parser = new SqlServerStoredProcedureParser(cs, new DataGuardConfiguration());
        var contracts = await parser.ExtractContractsAsync();

        var proc = contracts.OfType<DataGuard.Core.Abstractions.StoredProcedureDescriptor>()
            .Should().ContainSingle(p => p.Name == "GetCustomer").Subject;
        proc.Parameters.Should().Contain(p => p.Name == "@Id" && p.DataType.Contains("int", StringComparison.OrdinalIgnoreCase));
        proc.Parameters.Should().Contain(p => p.Name == "@OutName");
        proc.ResultColumns.Should().Contain(c => c.Name == "CustomerId");
        proc.ResultColumns.Should().Contain(c => c.Name == "FullName" && c.MaxLength == 50);
        proc.ResultColumns.Should().Contain(c => c.Name == "DisplayName" && c.MaxLength == 40);
        proc.Parameters.Should().Contain(p => p.Name == "@Display" && p.MaxLength == 50);
        proc.Parameters.Should().Contain(p => p.Name == "@Body" && p.MaxLength == null);
        proc.Parameters.Should().Contain(p => p.Name == "@Note" && p.MaxLength == 50);
        proc.ReturnType.Should().BeNull();
        proc.Parameters.Where(p => p.HasDefault).Select(p => p.Name).Should().BeEquivalentTo("@Note", "@Display", "@Body");

        var listOrders = contracts.OfType<DataGuard.Core.Abstractions.StoredProcedureDescriptor>()
            .Should().ContainSingle(p => p.Name == "ListOrders").Subject;
        listOrders.Parameters.Should().ContainSingle(p => p.Name == "@Status").Which.HasDefault.Should().BeTrue();
        listOrders.Parameters.Should().ContainSingle(p => p.Name == "@CustomerId").Which.HasDefault.Should().BeFalse();

        // DG101: omitting the defaulted @Status is a valid call; omitting the required @CustomerId is not.
        var dg101 = new DataGuard.Core.Rules.ParameterCountRule("sqlserver", SqlServerTypeCompatibility.Instance);
        var withoutDefault = Call("EXEC dbo.ListOrders @CustomerId = 7");
        (await dg101.ValidateAsync(withoutDefault, contracts.Append(withoutDefault).ToList(), default)).Should().BeEmpty();
        var withoutRequired = Call("EXEC dbo.ListOrders @Status = N'closed'");
        (await dg101.ValidateAsync(withoutRequired, contracts.Append(withoutRequired).ToList(), default))
            .Should().ContainSingle(v => v.RuleId == "DG101").Which.Message.Should().Contain("@CustomerId").And.NotContain("@Status");

        var failing = contracts.OfType<DataGuard.Core.Abstractions.StoredProcedureDescriptor>()
            .Should().ContainSingle(p => p.Name == "UsesTempTable").Subject;
        failing.ResultColumns.Should().BeEmpty();
        failing.ReturnType.Should().StartWith("unknown:");

        var schema = contracts.OfType<DatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        var customers = schema.Tables.Should().Contain(table => table.Name == "Customers" && table.Schema == "dbo").Subject;
        customers.Columns.Should().Contain(column =>
            column.Name.Equals("Status", StringComparison.OrdinalIgnoreCase)
            && column.DataDefault != null
            && column.DataDefault.Contains("active", StringComparison.OrdinalIgnoreCase));
    }

    private static RawSqlDescriptor Call(string sql) => new(
        $"call:{sql}",
        sql,
        Array.Empty<ParameterDescriptor>(),
        Array.Empty<ColumnDescriptor>(),
        ConnectionProviderHint: "sqlserver");
}

/// <summary>Parameter defaults parsed from a T-SQL procedure definition (no database).</summary>
public class SqlServerParameterDefaultTests
{
    [Fact]
    public void ParseDefaultedParameters_ReturnsOnlyParametersWithADefault()
    {
        const string definition = """
            CREATE PROCEDURE [dbo].[Search]
                @Term NVARCHAR(100),
                @Page INT = 1,
                @Size int=20,
                @Since DATETIME2 = NULL,
                @Total INT OUTPUT,
                @Mode VARCHAR(10) = 'all' OUTPUT
            AS
            BEGIN
                DECLARE @Local INT = 5; -- a local variable default is not a parameter default
                SET @Total = 0;
            END
            """;

        var defaulted = SqlServerStoredProcedureParser.ParseDefaultedParameters(definition);

        defaulted.Should().BeEquivalentTo(new[] { "@Page", "@Size", "@Since", "@Mode" });
        defaulted.Contains("@page").Should().BeTrue("parameter names are case-insensitive");
    }

    [Theory]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.P @A INT = 0 AS SELECT @A")]
    [InlineData("ALTER PROCEDURE dbo.P @A INT = 0 AS SELECT @A")]
    public void ParseDefaultedParameters_HandlesAlterForms(string definition) =>
        SqlServerStoredProcedureParser.ParseDefaultedParameters(definition).Should().Equal("@A");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CREATE PROCEDURE dbo.P @A INT = AS")]
    public void ParseDefaultedParameters_NoDefinitionOrUnparsable_IsEmpty(string? definition) =>
        SqlServerStoredProcedureParser.ParseDefaultedParameters(definition).Should().BeEmpty();
}

/// <summary>
/// SQL Server length normalization without a database.
/// </summary>
public class SqlServerLengthNormalizationTests
{
    [Theory]
    [InlineData(100, 231, 50)] // nvarchar(50): sys.parameters reports 100 bytes
    [InlineData(100, 239, 50)] // nchar(50)
    [InlineData(50, 167, 50)] // varchar(50)
    [InlineData(-1, 231, null)] // nvarchar(max)
    [InlineData(-1, 167, null)] // varchar(max)
    [InlineData(4, 56, 4)] // int keeps its byte size
    public void NormalizeMaxLength_ReturnsCharacters(int maxLength, int systemTypeId, int? expected)
    {
        SqlServerStoredProcedureParser.NormalizeMaxLength(maxLength, systemTypeId).Should().Be(expected);
    }
}
