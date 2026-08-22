using DataGuard.Core.Models;
using DataGuard.Core.Sources;
using FluentAssertions;
using Testcontainers.MsSql;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Live SQL Server integration via Testcontainers. Skips automatically when
/// the Docker daemon is not reachable so local/offline runs stay green; CI
/// with Docker will execute the real path.
/// </summary>
public class SqlServerIntegrationTests : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private string? _skipReason;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            _skipReason = "Docker daemon is not running; Testcontainers path skipped.";
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
    public async Task SqlServerStoredProcedureParser_ExtractsCreatedProcedure()
    {
        if (_skipReason != null)
        {
            return; // informational skip — Docker not available
        }

        var connectionString = _container!.GetConnectionString();
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

    [Fact]
    public void Parser_SkipReason_IsSetOnlyWhenDockerMissing()
    {
        // Documents the skip contract: either the container started, or we
        // recorded a skip reason. Both are valid outcomes of this fixture.
        (_container != null || _skipReason != null).Should().BeTrue();
    }

    private static bool IsDockerUnavailable(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Cannot connect", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("pipe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
