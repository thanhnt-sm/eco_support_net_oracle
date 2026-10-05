using System;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Live SQL Server tests via Testcontainers. Skipped automatically when Docker
/// is not available so local/offline CI still passes.
/// </summary>
public class SqlServerParserIntegrationTests : IAsyncLifetime
{
    private const string RequireLiveSqlServerVariable = "DATAGUARD_REQUIRE_LIVE_SQLSERVER";
    private const string RunLiveSqlServerVariable = "DATAGUARD_RUN_SQLSERVER_INTEGRATION";
    private MsSqlContainer? _container;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("DataGuard_Test_1!")
                .Build();
            await _container.StartAsync();
            _dockerAvailable = true;
        }
        catch (Exception ex)
        {
            if (IsLiveSqlServerRequired())
            {
                throw new InvalidOperationException("Live SQL Server was required but the Testcontainers fixture could not start.", ex);
            }

            _dockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExtractContractsAsync_ReadsProcedureParametersAndResultSet()
    {
        if (!_dockerAvailable || _container == null)
        {
            return; // xUnit 2.9 has no supported dynamic skip API.
        }

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

    private static bool IsLiveSqlServerRequired()
        => string.Equals(Environment.GetEnvironmentVariable(RequireLiveSqlServerVariable), "1", StringComparison.Ordinal)
            || string.Equals(Environment.GetEnvironmentVariable(RunLiveSqlServerVariable), "1", StringComparison.Ordinal);
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
