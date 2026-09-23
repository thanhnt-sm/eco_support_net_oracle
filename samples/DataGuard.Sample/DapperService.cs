using System.Data;

namespace DataGuard.Sample;

public class DapperService
{
    public void GetOrdersWithCustomers(IDbConnection conn)
    {
        conn.Query<Order>("SELECT o.ORDER_ID, o.CUSTOMER_ID, o.TOTAL_AMOUNT FROM ORDERS o JOIN CUSTOMERS c ON o.CUSTOMER_ID = c.CUSTOMER_ID");
    }

    public void BadQuerySelectStar(IDbConnection conn)
    {
        // Deliberate DG017 violation: SELECT * in production query
        conn.Query<Order>("SELECT * FROM ORDERS");
    }
}
