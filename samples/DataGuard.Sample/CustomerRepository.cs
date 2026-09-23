using System.Data;

namespace DataGuard.Sample;

public static class DapperShim
{
    public static System.Collections.Generic.IEnumerable<T> Query<T>(this IDbConnection conn, string sql) => null!;
}

public class CustomerRepository
{
    public void GetCustomers(IDbConnection conn)
    {
        conn.Query<Customer>("SELECT CUSTOMER_ID, FULL_NAME, EMAIL, PHONE FROM CUSTOMERS");
    }
}
