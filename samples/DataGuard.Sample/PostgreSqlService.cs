using System;
using System.Data;

namespace DataGuard.Sample;

public class NpgsqlCommand : IDisposable
{
    public string CommandText { get; set; } = string.Empty;

    public NpgsqlCommand(string commandText)
    {
        this.CommandText = commandText;
    }

    public NpgsqlCommand()
    {
    }

    public void ExecuteNonQuery()
    {
    }

    public IDataReader ExecuteReader()
    {
        return null!;
    }

    public void Dispose()
    {
    }
}

public class NpgsqlConnection : IDisposable
{
    public NpgsqlConnection(string connectionString)
    {
    }

    public void Open()
    {
    }

    public void Dispose()
    {
    }
}

public class PostgreSqlService
{
    public void QueryOrders(string connectionString)
    {
        using var conn = new NpgsqlConnection(connectionString);
        using var cmd = new NpgsqlCommand("SELECT ORDER_ID, CUSTOMER_ID, TOTAL_AMOUNT FROM ORDERS WHERE CUSTOMER_ID = $1");
        using var reader = cmd.ExecuteReader();
    }
}
