using System;
using System.Data;

namespace DataGuard.Sample;

public class OracleCommand : IDisposable
{
    public string CommandText { get; set; } = string.Empty;

    public OracleCommand(string commandText)
    {
        this.CommandText = commandText;
    }

    public OracleCommand()
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

public class OracleConnection : IDisposable
{
    public OracleConnection(string connectionString)
    {
    }

    public void Open()
    {
    }

    public void Dispose()
    {
    }
}

public class OracleRepository
{
    public void FetchOracleCustomers(string connectionString)
    {
        using var conn = new OracleConnection(connectionString);
        using var cmd = new OracleCommand();
        cmd.CommandText = "SELECT CUSTOMER_ID, FULL_NAME, EMAIL FROM CUSTOMERS WHERE CUSTOMER_ID = :id";
        using var reader = cmd.ExecuteReader();
    }

    public void InsertCustomerLog(string connectionString)
    {
        using var conn = new OracleConnection(connectionString);
        using var cmd = new OracleCommand("INSERT INTO CUSTOMER_LOGS (CUSTOMER_ID, ACTION) VALUES (:id, :action)");
        cmd.ExecuteNonQuery();
    }
}
