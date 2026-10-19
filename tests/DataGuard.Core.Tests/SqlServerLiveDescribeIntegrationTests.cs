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

    [LiveDbFact(LiveDbTarget.SqlServer)]
    public async Task DescribeResultSetAsync_NormalizesUnicodeLengthsAndMax()
    {
        Assert.NotNull(_container);
        var provider = new SqlServerLiveQuerySchemaProvider(_container.GetConnectionString());
        var result = await provider.DescribeResultSetAsync(
            "SELECT CAST(NULL AS nvarchar(36)) AS UnicodeValue, CAST(NULL AS nchar(32)) AS FixedUnicode, " +
            "CAST(NULL AS nvarchar(max)) AS UnicodeMax, CAST(NULL AS varchar(36)) AS AnsiValue, " +
            "CAST(NULL AS varbinary(16)) AS BinaryValue, CAST(NULL AS varchar(max)) AS AnsiMax, " +
            "CAST(NULL AS varbinary(max)) AS BinaryMax", CancellationToken.None);

        result.Status.Should().Be(LiveSchemaStatus.Described);
        result.Columns.Should().HaveCount(7);
        int?[] expected = { 36, 32, null, 36, 16, null, null };
        for (var index = 0; index < expected.Length; index++)
        {
            result.Columns[index].MaxLength.Should().Be(expected[index]);
            result.Columns[index].CharLength.Should().Be(expected[index]);
        }
    }
}
