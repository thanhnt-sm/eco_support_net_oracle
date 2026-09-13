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
                    @OutName VARCHAR(200) OUTPUT
                AS
                BEGIN
                    SET @OutName = 'x';
                    SELECT CAST(1 AS INT) AS CustomerId, CAST('Ada' AS VARCHAR(50)) AS FullName;
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
        proc.ResultColumns.Should().Contain(c => c.Name == "FullName");

        var schema = contracts.OfType<DatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        var customers = schema.Tables.Should().Contain(table => table.Name.EndsWith(".Customers", StringComparison.OrdinalIgnoreCase)).Subject;
        customers.Columns.Should().Contain(column =>
            column.Name.Equals("Status", StringComparison.OrdinalIgnoreCase)
            && column.DataDefault != null
            && column.DataDefault.Contains("active", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLiveSqlServerRequired()
        => string.Equals(Environment.GetEnvironmentVariable(RequireLiveSqlServerVariable), "1", StringComparison.Ordinal)
            || string.Equals(Environment.GetEnvironmentVariable(RunLiveSqlServerVariable), "1", StringComparison.Ordinal);
}
