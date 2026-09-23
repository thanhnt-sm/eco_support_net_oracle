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

    [Fact]
    public async Task ExtractContractsAsync_DetectsCommandTextAndNewCommand_WithProviderHint()
    {
        var code = @"
namespace TestNamespace;

public class OracleCommand
{
    public string CommandText { get; set; } = """";
    public OracleCommand(string cmdText) { CommandText = cmdText; }
    public OracleCommand() { }
}

public class Repo
{
    public void Run()
    {
        var cmd = new OracleCommand();
        cmd.CommandText = ""SELECT u.Id, u.Name FROM Users u JOIN Orders o ON u.Id = o.UserId"";
        var cmd2 = new OracleCommand(""INSERT INTO Logs (Message) VALUES (:msg)"");
    }
}";
        var filePath = Path.Combine(_tempDirectory, "Repo.cs");
        await File.WriteAllTextAsync(filePath, code);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        contracts.Should().HaveCount(2);
        var readSql = contracts.OfType<RawSqlDescriptor>().First(c => c.OperationType == SqlOperationType.Join);
        readSql.ReferencedTables.Should().Contain("Users");
        readSql.ReferencedTables.Should().Contain("Orders");
        readSql.ConnectionProviderHint.Should().Be("oracle");

        var writeSql = contracts.OfType<RawSqlDescriptor>().First(c => c.OperationType == SqlOperationType.Write);
        writeSql.ReferencedTables.Should().Contain("Logs");
        writeSql.Parameters.Should().Contain(p => p.Name == ":msg");
    }

    [Fact]
    public void ExtractParameters_MultiDialect_WithLiteralMasking()
    {
        var sql = "SELECT * FROM T WHERE a = @sqlParam AND b = :oracleParam AND c = $1 AND d = TO_CHAR(sysdate, 'HH24:MI:SS')";
        var method = typeof(ProjectCSharpSqlSource).GetMethod("ExtractParameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var parameters = (IReadOnlyList<ParameterDescriptor>)method.Invoke(null, new object[] { sql })!;

        parameters.Select(p => p.Name).Should().BeEquivalentTo(new[] { "@sqlParam", ":oracleParam", "$1" });
    }

    [Fact]
    public void ExtractReferencedTables_HandlesBracketsAndBackticks_AndSchemas()
    {
        var sql = @"
SELECT u.Id, o.Total
FROM [dbo].[Users] u
JOIN `analytics`.`user_orders` o ON u.Id = o.UserId
WHERE u.Status = 'ACTIVE'";

        var tables = ProjectCSharpSqlSource.ExtractReferencedTables(sql);

        tables.Should().Contain("Users");
        tables.Should().Contain("user_orders");
    }

    [Fact]
    public void MaskSqlStringLiterals_MasksCommasAndColonsInsideQuotes()
    {
        var sql = "SELECT 'Doe, John' AS Name, TO_CHAR(sysdate, 'HH24:MI:SS') FROM Users";
        var masked = ProjectCSharpSqlSource.MaskSqlStringLiterals(sql);

        masked.Should().NotContain("Doe, John");
        masked.Should().NotContain("HH24:MI:SS");
        masked.Length.Should().Be(sql.Length);
    }
}
