using DataGuard.Core.Models;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Testcontainers.MsSql;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Live SQL Server integration via Testcontainers. Gated by <see cref="LiveDbFactAttribute"/>: reported as Skipped
/// unless DATAGUARD_REQUIRE_LIVE_SQLSERVER=1, and the fixture fails loudly when that variable is set and the container
/// cannot start.
/// </summary>
public class SqlServerIntegrationTests : IAsyncLifetime
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
    public async Task SqlServerStoredProcedureParser_ExtractsCreatedProcedure()
    {
        Assert.NotNull(_container);
        var connectionString = _container.GetConnectionString();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE PROCEDURE dbo.GetCustomer
                    @CustomerId INT,
                    @FullName NVARCHAR(100) OUTPUT
                AS
                BEGIN
                    SELECT @FullName = N'test' WHERE @CustomerId = 1;
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var parser = new SqlServerStoredProcedureParser(connectionString, new DataGuardConfiguration());
        var contracts = await parser.ExtractContractsAsync();

        var proc = contracts.OfType<DataGuard.Core.Abstractions.StoredProcedureDescriptor>()
            .Should().Contain(c => c.Name == "GetCustomer").Subject;
        proc.Parameters.Should().Contain(p => p.Name == "@CustomerId");
        proc.Parameters.Should().Contain(p => p.Name == "@FullName");
    }

    [LiveDbFact(LiveDbTarget.SqlServer)]
    public void Fixture_WhenGateEnabled_StartsContainer()
    {
        // The gate has no "degrade to skip" path: when the test runs, the container is up (or InitializeAsync threw).
        _container.Should().NotBeNull();
        _container!.State.Should().Be(DotNet.Testcontainers.Containers.TestcontainersStates.Running);
    }
}
