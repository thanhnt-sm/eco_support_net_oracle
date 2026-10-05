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
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!LiveDbGate.IsEnabled(LiveDbTarget.Relational))
        {
            return; // Gated tests are Skipped and never construct this class; this only guards direct use.
        }

        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("dataguard")
                .WithUsername("dataguard")
                .WithPassword("DataGuard_Test_1!")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw LiveDbGate.ContainerStartFailed(LiveDbTarget.Relational, "postgres:16-alpine", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [LiveDbFact(LiveDbTarget.Relational)]
    public async Task ExtractContractsAsync_ReadsFunctionParametersFromLivePostgreSql()
    {
        Assert.NotNull(_container);

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

    [LiveDbFact(LiveDbTarget.Relational)]
    public async Task ExtractContractsAsync_ReadsReturnsTableDefaultsOverloadsAndMaterializedViews()
    {
        Assert.NotNull(_container);

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE public.orders (id integer NOT NULL, code varchar(12) NOT NULL);
                CREATE MATERIALIZED VIEW public.order_codes AS SELECT id, code FROM public.orders;
                CREATE FUNCTION public.find_orders(p_min integer, p_code text DEFAULT 'x')
                RETURNS TABLE(order_id integer, order_code text)
                LANGUAGE sql
                AS $$ SELECT id, code::text FROM public.orders WHERE id >= p_min $$;
                CREATE FUNCTION public.find_orders(p_code text)
                RETURNS integer
                LANGUAGE sql
                AS $$ SELECT 1 $$;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var parser = new PostgreSqlStoredProcedureParser(_container.GetConnectionString());
        var contracts = await parser.ExtractContractsAsync();

        var overloads = contracts.OfType<StoredProcedureDescriptor>().Where(contract => contract.Name == "find_orders").ToList();
        overloads.Select(contract => contract.Id).Should().BeEquivalentTo(
            "postgres:public.find_orders(int4,text)",
            "postgres:public.find_orders(text)");

        var table = overloads.Single(contract => contract.Id.EndsWith("(int4,text)", StringComparison.Ordinal));
        table.Parameters.Select(parameter => parameter.Name).Should().Equal("p_min", "p_code");
        table.Parameters.Single(parameter => parameter.Name == "p_code").HasDefault.Should().BeTrue();
        table.Parameters.Single(parameter => parameter.Name == "p_min").HasDefault.Should().BeFalse();
        table.ResultColumns.Select(column => (column.Name, column.DataType)).Should().Equal(("order_id", "int4"), ("order_code", "text"));

        var scalar = overloads.Single(contract => contract.Id.EndsWith("(text)", StringComparison.Ordinal));
        scalar.ReturnType.Should().Be("int4");
        scalar.ResultColumns.Should().BeEmpty();

        var schema = contracts.OfType<DatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        var matview = schema.Tables.Should().ContainSingle(t => t.Name == "order_codes").Subject;
        matview.Schema.Should().Be("public");
        matview.Columns.Should().Contain(column => column.Name == "code" && column.DataType == "character varying" && column.MaxLength == 12);
    }
}
