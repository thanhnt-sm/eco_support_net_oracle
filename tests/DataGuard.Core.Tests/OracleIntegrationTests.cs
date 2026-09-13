using System;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Explicit Oracle Free provider gate enabled by
/// DATAGUARD_REQUIRE_LIVE_RELATIONAL=1.
/// </summary>
public class OracleIntegrationTests : IAsyncLifetime
{
    private const string RequireLiveRelationalVariable = "DATAGUARD_REQUIRE_LIVE_RELATIONAL";
    private OracleContainer? _container;

    public async Task InitializeAsync()
    {
        if (!RequiresLiveRelational())
        {
            return;
        }

        _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart")
            .WithDatabase("FREEPDB1")
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
    public async Task AllArgumentsReader_ReadsProcedureParametersFromLiveOracle()
    {
        if (_container == null)
        {
            return; // xUnit 2.9 has no supported dynamic skip API.
        }

        await using (var connection = new OracleConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE OR REPLACE PROCEDURE GET_CUSTOMER(
                    P_ID IN NUMBER,
                    P_NAME OUT VARCHAR2)
                AS
                BEGIN
                    P_NAME := 'Ada';
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var reader = new AllArgumentsReader(_container.GetConnectionString(), new DataGuard.Core.Models.OracleConfiguration());
        var parameters = await reader.GetParametersAsync("DATAGUARD", string.Empty, "GET_CUSTOMER");

        parameters.Should().Contain(parameter => parameter.Name == "P_ID" && parameter.Direction == ParameterDirection.Input);
        parameters.Should().Contain(parameter => parameter.Name == "P_NAME" && parameter.Direction == ParameterDirection.Output);
    }

    private static bool RequiresLiveRelational()
        => string.Equals(Environment.GetEnvironmentVariable(RequireLiveRelationalVariable), "1", StringComparison.Ordinal);
}
