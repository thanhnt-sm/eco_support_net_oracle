using System;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.MySql.Adapter;
using FluentAssertions;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Explicit MySQL provider gate enabled by DATAGUARD_REQUIRE_LIVE_RELATIONAL=1.
/// </summary>
public class MySqlIntegrationTests : IAsyncLifetime
{
    private const string RequireLiveRelationalVariable = "DATAGUARD_REQUIRE_LIVE_RELATIONAL";
    private MySqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!RequiresLiveRelational())
        {
            return;
        }

        _container = new MySqlBuilder("mysql:8.4")
            .WithDatabase("dataguard")
            .WithUsername("dataguard")
            .WithPassword("DataGuard_Test_1!")
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExtractContractsAsync_ReadsProcedureParametersFromLiveMySql()
    {
        if (_container == null)
        {
            return; // xUnit 2.9 has no supported dynamic skip API.
        }

        await using (var connection = new MySqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE customers (id INT NOT NULL, status VARCHAR(20) NOT NULL DEFAULT 'active')";
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE PROCEDURE GetCustomer(IN customer_id INT, OUT customer_name VARCHAR(100))
                BEGIN
                    SET customer_name = 'Ada';
                END
                """;
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE PROCEDURE HealthCheck()
                BEGIN
                    SELECT 1;
                END
                """;
            await command.ExecuteNonQueryAsync();
        }

        var parser = new MySqlStoredProcedureParser(_container.GetConnectionString(), "dataguard");
        var contracts = await parser.ExtractContractsAsync();

        var procedure = contracts.OfType<StoredProcedureDescriptor>()
            .Should().ContainSingle(contract => contract.Name == "GetCustomer").Subject;
        procedure.Parameters.Should().Contain(parameter => parameter.Name == "customer_id" && parameter.Direction == ParameterDirection.Input);
        procedure.Parameters.Should().Contain(parameter => parameter.Name == "customer_name" && parameter.Direction == ParameterDirection.Output);
        var parameterless = contracts.OfType<StoredProcedureDescriptor>()
            .Should().ContainSingle(contract => contract.Name == "HealthCheck").Subject;
        parameterless.Parameters.Should().BeEmpty();
        var schema = contracts.OfType<DatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        schema.Tables.Should().Contain(table => table.Name.Equals("customers", StringComparison.OrdinalIgnoreCase)
            && table.Columns.Any(column => column.Name.Equals("status", StringComparison.OrdinalIgnoreCase)
                && column.DataDefault != null
                && column.DataDefault.Contains("active", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool RequiresLiveRelational()
        => string.Equals(Environment.GetEnvironmentVariable(RequireLiveRelationalVariable), "1", StringComparison.Ordinal);
}
