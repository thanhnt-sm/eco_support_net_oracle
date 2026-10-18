using System;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Rules;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Testcontainers.MsSql;
using Xunit;

namespace DataGuard.Core.Tests;

public class SqlServerLiveDescribeIntegrationTests : IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";
    private MsSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!LiveDbGate.IsEnabled(LiveDbTarget.SqlServer))
        {
            return;
        }

        try
        {
            _container = new MsSqlBuilder(Image).Build();
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
    public async Task DescribeResultSetAsync_DescribesLiteralSelect()
    {
        Assert.NotNull(_container);
        var provider = new SqlServerLiveQuerySchemaProvider(_container.GetConnectionString());

        var result = await provider.DescribeResultSetAsync("SELECT CAST(1 AS int) AS Id", CancellationToken.None);

        result.Status.Should().Be(LiveSchemaStatus.Described);
        var column = result.Columns.Should().ContainSingle().Subject;
        column.Name.Should().Be("Id");
        column.DataType.Should().Be("int");
        column.ColumnId.Should().Be(1);
    }
}
