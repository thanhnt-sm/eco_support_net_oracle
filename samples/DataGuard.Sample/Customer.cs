using DataGuard.Contracts;

namespace DataGuard.Sample;

/// <summary>
/// Entity contract declared via manual ground-truth attributes.
/// No database access is required to validate against these.
/// </summary>
public class Customer
{
    [ExpectedColumn("CUSTOMER_ID", "int", IsNullable = false)]
    public int CustomerId { get; set; }

    [ExpectedColumn("FULL_NAME", "string", MaxLength = 100, IsNullable = false)]
    public string? FullName { get; set; }

    [ExpectedColumn("EMAIL", "string", MaxLength = 255)]
    public string? Email { get; set; }

    // Deliberate mismatch: DB column is PHONE, property is PhoneNo (snake_case
    // convention violation - DG006).
    [ExpectedColumn("PHONE", "string", MaxLength = 20)]
    public string? PhoneNo { get; set; }
}

public class CustomerProcedures
{
    [ExpectedSpParameter("p_customer_id", "NUMBER", "IN")]
    [ExpectedSpParameter("p_result", "REF CURSOR", "OUT")]
    public static void GetCustomerDetails()
    {
    }
}
