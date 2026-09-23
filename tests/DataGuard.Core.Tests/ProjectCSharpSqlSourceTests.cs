using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class ProjectCSharpSqlSourceTests : IDisposable
{
    private readonly string _tempDirectory;

    public ProjectCSharpSqlSourceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DataGuard_SourceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsDapperQueryWithTargetType()
    {
        var code = @"
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace TestNamespace;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [Column(""customer_email"")]
    public string? Email { get; set; }
}

public static class DapperExtensions
{
    public static System.Collections.Generic.IEnumerable<T> Query<T>(this IDbConnection cnn, string sql) => null!;
}

public class CustomerRepository
{
    public void GetCustomers(IDbConnection conn)
    {
        var sql = ""SELECT Id, Name, customer_email FROM Customers WHERE Id = @Id"";
        var customers = conn.Query<Customer>(sql);
    }
}";

        var filePath = Path.Combine(_tempDirectory, "CustomerRepository.cs");
        await File.WriteAllTextAsync(filePath, code);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        contracts.Should().ContainSingle();
        var rawSql = contracts.Single().Should().BeOfType<RawSqlDescriptor>().Subject;

        rawSql.SqlText.Should().Be("SELECT Id, Name, customer_email FROM Customers WHERE Id = @Id");
        rawSql.TargetTypeName.Should().Be("Customer");
        rawSql.Parameters.Should().ContainSingle(p => p.Name == "@Id");

        rawSql.ExpectedProperties.Should().HaveCount(3);
        rawSql.ExpectedProperties.Should().Contain(p => p.Name == "Id");
        rawSql.ExpectedProperties.Should().Contain(p => p.Name == "Name");
        rawSql.ExpectedProperties.Should().Contain(p => p.Name == "Email" && p.ColumnName == "customer_email");
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsEfCoreFromSqlRaw()
    {
        var code = @"
namespace TestNamespace;

public class Order
{
    public int OrderId { get; set; }
    public decimal TotalAmount { get; set; }
}

public class DbSet<T> where T : class
{
    public System.Collections.Generic.IEnumerable<T> FromSqlRaw(string sql) => null!;
}

public class OrderService
{
    public void LoadOrders(DbSet<Order> orders)
    {
        orders.FromSqlRaw(""SELECT OrderId, TotalAmount FROM Orders"");
    }
}";

        var filePath = Path.Combine(_tempDirectory, "OrderService.cs");
        await File.WriteAllTextAsync(filePath, code);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        contracts.Should().ContainSingle();
        var rawSql = contracts.Single().Should().BeOfType<RawSqlDescriptor>().Subject;

        rawSql.SqlText.Should().Be("SELECT OrderId, TotalAmount FROM Orders");
        rawSql.TargetTypeName.Should().Be("Order");
        rawSql.ExpectedProperties.Should().HaveCount(2);
        rawSql.ExpectedProperties.Should().Contain(p => p.Name == "OrderId");
        rawSql.ExpectedProperties.Should().Contain(p => p.Name == "TotalAmount");
    }

    [Fact]
    public async Task ExtractContractsAsync_EmitsProgressEvent()
    {
        var code = @"
namespace TestNamespace;

public class Product
{
    public int Id { get; set; }
}

public static class DapperExtensions
{
    public static System.Collections.Generic.IEnumerable<T> Query<T>(this object cnn, string sql) => null!;
}

public class ProductCatalog
{
    public void Fetch(object conn)
    {
        conn.Query<Product>(""SELECT Id FROM Products"");
    }
}";

        var filePath = Path.Combine(_tempDirectory, "ProductCatalog.cs");
        await File.WriteAllTextAsync(filePath, code);

        using var stringWriter = new StringWriter();
        var progress = new ProgressEmitter(stringWriter, enabled: true);

        var source = new ProjectCSharpSqlSource(_tempDirectory, progress);
        await source.ExtractContractsAsync();

        var output = stringWriter.ToString();
        output.Should().Contain("ContractDiscovered");
        output.Should().Contain("Found SQL in ProductCatalog.cs:");
        output.Should().Contain("targeting Product");
    }
}
