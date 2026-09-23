using DataGuard.Contracts;

namespace DataGuard.Sample;

public class Order
{
    [ExpectedColumn("ORDER_ID", "int", IsNullable = false)]
    public int OrderId { get; set; }

    [ExpectedColumn("CUSTOMER_ID", "int", IsNullable = false)]
    public int CustomerId { get; set; }

    [ExpectedColumn("TOTAL_AMOUNT", "decimal")]
    public decimal TotalAmount { get; set; }
}
