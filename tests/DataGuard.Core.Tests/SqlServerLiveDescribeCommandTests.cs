using System.Data;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace DataGuard.Core.Tests;

public class SqlServerLiveDescribeCommandTests
{
    [Fact]
    public void CreateDescribeCommand_InvokesSystemProcedureWithParameterizedSql()
    {
        using var connection = new SqlConnection();
        const string sql = "SELECT N'quote''; SELECT 2 -- literal' AS Value";
        using var command = SqlServerLiveQuerySchemaProvider.CreateDescribeCommand(connection, sql);

        command.Connection.Should().BeSameAs(connection);
        command.CommandType.Should().Be(CommandType.Text);
        command.CommandText.Should().Be("EXEC sys.sp_describe_first_result_set @tsql = @tsql, @params = NULL, @browse_information_mode = 0");
        command.CommandText.Should().NotContain(sql);
        command.CommandTimeout.Should().Be(5);
        command.Parameters.Count.Should().Be(1);
        command.Parameters[0].ParameterName.Should().Be("@tsql");
        command.Parameters[0].Value.Should().Be(sql);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5000)]
    public void CreateDescribeCommand_UsesUnicodeMaxParameterWithoutTruncation(int length)
    {
        using var connection = new SqlConnection();
        var sql = "SELECT N'" + new string('\u03bb', length) + "' AS Value";
        using var command = SqlServerLiveQuerySchemaProvider.CreateDescribeCommand(connection, sql);

        command.Parameters[0].SqlDbType.Should().Be(SqlDbType.NVarChar);
        command.Parameters[0].Size.Should().Be(-1);
        command.Parameters[0].Value.Should().Be(sql);
    }
}
