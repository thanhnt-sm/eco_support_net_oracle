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
    private OracleContainer? _container;

    public async Task InitializeAsync()
    {
        if (!LiveDbGate.IsEnabled(LiveDbTarget.Relational))
        {
            return; // Gated tests are Skipped and never construct this class; this only guards direct use.
        }

        try
        {
            _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart")
                .WithDatabase("FREEPDB1")
                .WithUsername("dataguard")
                .WithPassword("DataGuard_Test_1!")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw LiveDbGate.ContainerStartFailed(LiveDbTarget.Relational, "gvenzl/oracle-free:23-slim-faststart", ex);
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
    public async Task AllArgumentsReader_ReadsProcedureParametersFromLiveOracle()
    {
        Assert.NotNull(_container);

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
}
