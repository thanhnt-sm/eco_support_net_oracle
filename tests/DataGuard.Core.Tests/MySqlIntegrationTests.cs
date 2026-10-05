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
    private MySqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!LiveDbGate.IsEnabled(LiveDbTarget.Relational))
        {
            return; // Gated tests are Skipped and never construct this class; this only guards direct use.
        }

        try
        {
            _container = new MySqlBuilder("mysql:8.4")
                .WithDatabase("dataguard")
                .WithUsername("dataguard")
                .WithPassword("DataGuard_Test_1!")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw LiveDbGate.ContainerStartFailed(LiveDbTarget.Relational, "mysql:8.4", ex);
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
    public async Task ExtractContractsAsync_ReadsProcedureParametersFromLiveMySql()
    {
        Assert.NotNull(_container);

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

    [LiveDbFact(LiveDbTarget.Relational)]
    public async Task MySqlLiveQuerySchemaProvider_DescribesParameterizedQueryWithoutReturningRows()
    {
        Assert.NotNull(_container);

        await using (var connection = new MySqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE t (id INT NOT NULL PRIMARY KEY, name VARCHAR(50) NULL)";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "INSERT INTO t (id, name) VALUES (1, 'Ada')";
            await command.ExecuteNonQueryAsync();
        }

        var provider = new MySqlLiveQuerySchemaProvider(_container.GetConnectionString());

        var result = await provider.DescribeResultSetAsync("SELECT id, name FROM t WHERE id = @p", default);

        result.Status.Should().Be(DataGuard.Core.Rules.LiveSchemaStatus.Described, result.Error);
        result.Columns.Select(column => column.Name).Should().Equal("id", "name");
        result.Columns[0].DataType.Should().ContainEquivalentOf("INT");
        result.Columns[0].IsNullable.Should().BeFalse();
        result.Columns[1].DataType.Should().ContainEquivalentOf("VARCHAR");
        result.Columns[1].IsNullable.Should().BeTrue();

        var missing = await provider.DescribeResultSetAsync("SELECT id FROM no_such_table", default);
        missing.Status.Should().Be(DataGuard.Core.Rules.LiveSchemaStatus.Failed);
        missing.Columns.Should().BeEmpty();
    }

    [LiveDbFact(LiveDbTarget.Relational)]
    public async Task ExtractContractsAsync_ReadsFunctionsCharsetAndDefaultsToCurrentDatabase()
    {
        Assert.NotNull(_container);

        var rootConnection = new MySqlConnectionStringBuilder(_container.GetConnectionString()) { UserID = "root" }.ConnectionString;
        await using (var root = new MySqlConnection(rootConnection))
        {
            await root.OpenAsync();
            await using var command = root.CreateCommand();

            // Binary logging is on in mysql:8.4; let the non-SUPER test user create a function.
            command.CommandText = "SET GLOBAL log_bin_trust_function_creators = 1";
            await command.ExecuteNonQueryAsync();
        }

        await using (var connection = new MySqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE notes (id INT NOT NULL, title VARCHAR(40) CHARACTER SET latin1 NOT NULL, body TEXT CHARACTER SET utf8mb4 NULL)";
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE FUNCTION NoteTitle(note_id INT, suffix VARCHAR(10)) RETURNS VARCHAR(60)
                DETERMINISTIC NO SQL
                RETURN CONCAT('n', note_id, suffix)
                """;
            await command.ExecuteNonQueryAsync();
        }

        // Empty schema => DATABASE() of the connection, never every schema on the server.
        var parser = new MySqlStoredProcedureParser(_container.GetConnectionString(), string.Empty);
        var contracts = await parser.ExtractContractsAsync();

        var function = contracts.OfType<StoredProcedureDescriptor>()
            .Should().ContainSingle(contract => contract.Name == "NoteTitle").Subject;
        function.Id.Should().Be("mysql:dataguard.NoteTitle#function");
        function.ReturnType.Should().Be("VARCHAR");
        function.Parameters.Select(parameter => parameter.Name).Should().Equal("note_id", "suffix");
        function.Schema.Should().Be("dataguard");

        var schema = contracts.OfType<DatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        schema.Tables.Should().OnlyContain(table => table.Schema == "dataguard");
        var notes = schema.Tables.Should().ContainSingle(table => table.Name == "notes").Subject;
        notes.Columns.Single(column => column.Name == "title").Charset.Should().Be("latin1");
        notes.Columns.Single(column => column.Name == "title").CharUsed.Should().BeNull();
        notes.Columns.Single(column => column.Name == "body").Charset.Should().Be("utf8mb4");
        notes.Columns.Single(column => column.Name == "id").Charset.Should().BeNull();
    }
}
