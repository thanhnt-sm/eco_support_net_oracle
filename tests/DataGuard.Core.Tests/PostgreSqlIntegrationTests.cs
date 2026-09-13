using System;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Explicit PostgreSQL provider gate. It only starts a container when the
/// operator opts in through DATAGUARD_REQUIRE_LIVE_RELATIONAL=1.
/// </summary>
public class PostgreSqlIntegrationTests : IAsyncLifetime
{
    private const string RequireLiveRelationalVariable = "DATAGUARD_REQUIRE_LIVE_RELATIONAL";
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!RequiresLiveRelational())
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine")
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
    public async Task ExtractContractsAsync_ReadsFunctionParametersFromLivePostgreSql()
    {
        if (_container == null)
        {
            return; // xUnit 2.9 has no supported dynamic skip API.
        }

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE public.customers (id integer NOT NULL, status text NOT NULL DEFAULT 'active')";
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE FUNCTION public.get_customer(customer_id integer, OUT customer_name text)
                RETURNS text
                LANGUAGE sql
                AS $$ SELECT 'Ada'::text; $$;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var parser = new PostgreSqlStoredProcedureParser(_container.GetConnectionString());
        var contracts = await parser.ExtractContractsAsync();

        var routine = contracts.OfType<StoredProcedureDescriptor>()
            .Should().ContainSingle(contract => contract.Name == "get_customer").Subject;
        routine.Parameters.Should().Contain(parameter => parameter.Name == "customer_id" && parameter.Direction == ParameterDirection.Input);
        routine.Parameters.Should().Contain(parameter => parameter.Name == "customer_name" && parameter.Direction == ParameterDirection.Output);
        var schema = contracts.OfType<DatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        schema.Tables.Should().Contain(table => table.Name == "customers" && table.Columns.Any(column =>
            column.Name == "status" && column.DataDefault != null && column.DataDefault.Contains("active", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool RequiresLiveRelational()
        => string.Equals(Environment.GetEnvironmentVariable(RequireLiveRelationalVariable), "1", StringComparison.Ordinal);
}
