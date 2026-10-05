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
}
