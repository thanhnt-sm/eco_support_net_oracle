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

    [Fact]
    public async Task ExtractContractsAsync_ExtractsUnreferencedConstantSql_Pass5()
    {
        var code = @"
namespace TestApp;

public static class SqlQueries
{
    public const string SelectAllCustomers = ""SELECT CUSTOMER_ID, FULL_NAME FROM CUSTOMERS"";
    public static readonly string DeleteInactive = ""DELETE FROM USERS WHERE STATUS = 'INACTIVE'"";
}";
        var filePath = Path.Combine(_tempDirectory, "Constants.cs");
        await File.WriteAllTextAsync(filePath, code);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        contracts.Should().HaveCount(2);
        var sqls = contracts.OfType<RawSqlDescriptor>().Select(s => s.SqlText).ToList();
        sqls.Should().Contain("SELECT CUSTOMER_ID, FULL_NAME FROM CUSTOMERS");
        sqls.Should().Contain("DELETE FROM USERS WHERE STATUS = 'INACTIVE'");
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsPropertiesFromRecordParameters()
    {
        var code = @"
namespace TestApp;

public record CustomerDto(int Id, string Name, string? Email);

public class Repo
{
    public void Run()
    {
        var sql = ""SELECT ID, NAME, EMAIL FROM CUSTOMERS"";
        Dapper.SqlMapper.Query<CustomerDto>(null!, sql);
    }
}";
        var filePath = Path.Combine(_tempDirectory, "RecordModel.cs");
        await File.WriteAllTextAsync(filePath, code);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "CustomerDto");
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Should().HaveCount(3);
        rawSql.ExpectedProperties.Select(p => p.Name).Should().Contain(new[] { "Id", "Name", "Email" });
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsPropertiesFromPartialClassesAcrossFiles()
    {
        var part1 = @"
namespace TestApp;

public partial class MultiPartDto
{
    public int Id { get; set; }
    public string Name { get; set; } = """";
}";
        var part2 = @"
namespace TestApp;

public partial class MultiPartDto
{
    public string? Email { get; set; }
    public decimal Balance { get; set; }
}";
        var repo = @"
namespace TestApp;

public class Repo
{
    public void Run()
    {
        var sql = ""SELECT ID, NAME, EMAIL, BALANCE FROM CUSTOMERS"";
        Dapper.SqlMapper.Query<MultiPartDto>(null!, sql);
    }
}";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "Part1.cs"), part1);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "Part2.cs"), part2);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "Repo.cs"), repo);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "MultiPartDto");
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Should().HaveCount(4);
        rawSql.ExpectedProperties.Select(p => p.Name).Should().Contain(new[] { "Id", "Name", "Email", "Balance" });
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsRawStringLiteralSql()
    {
        var fileContent = "namespace TestApp;\n\npublic class RawStringRepo\n{\n    public const string Query = \"\"\"\n        SELECT ID, NAME, EMAIL FROM CUSTOMERS WHERE IS_ACTIVE = 1\n        \"\"\";\n\n    public void Run()\n    {\n        Dapper.SqlMapper.Query<CustomerDto>(null!, Query);\n    }\n}\npublic record CustomerDto(int Id, string Name, string Email);\n";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "RawStringRepo.cs"), fileContent);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "CustomerDto");
        rawSql.Should().NotBeNull();
        rawSql!.SqlText.Should().Contain("SELECT ID, NAME, EMAIL FROM CUSTOMERS");
    }

    [Fact]
    public void ClassifySqlOperation_IgnoresComments()
    {
        var sql = "SELECT ID FROM USERS\n-- DELETE FROM USERS WHERE 1=1\n/* UPDATE USERS SET A = 1 */";
        var op = ProjectCSharpSqlSource.ClassifySqlOperation(sql);
        op.Should().Be(SqlOperationType.Read);

        var tables = ProjectCSharpSqlSource.ExtractReferencedTables(sql);
        tables.Should().ContainSingle().Which.Should().Be("USERS");
    }

    [Theory]
    [InlineData("MySqlConnection", "mysql")]
    [InlineData("MySqlConnector.MySqlConnection", "mysql")]
    [InlineData("Microsoft.Data.SqlClient.SqlConnection", "sqlserver")]
    [InlineData("System.Data.SqlClient.SqlConnection", "sqlserver")]
    [InlineData("Npgsql.NpgsqlConnection", "postgresql")]
    [InlineData("Oracle.ManagedDataAccess.Client.OracleConnection", "oracle")]
    public void InferProviderHint_ResolvesCorrectProvider(string typeName, string expected)
    {
        var hint = ProjectCSharpSqlSource.InferProviderHint(typeName);
        hint.Should().Be(expected);
    }

    [Fact]
    public async Task ExtractContractsAsync_NonRecordClassPrimaryConstructorParametersNotTreatedAsProperties()
    {
        var fileContent = "namespace TestApp;\n\npublic class CustomerRepo(string connectionString, int timeout)\n{\n    public void Run()\n    {\n        Dapper.SqlMapper.Query<CustomerRepo>(null!, \"SELECT 1 FROM DUAL\");\n    }\n}\n";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "CustomerRepo.cs"), fileContent);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "CustomerRepo");
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsPropertiesFromInterfaceEntities()
    {
        var fileContent = "namespace TestApp;\n\npublic interface ICustomerEntity\n{\n    int Id { get; set; }\n    string Name { get; set; }\n    string? Email { get; set; }\n}\n\npublic class Service\n{\n    public void Run()\n    {\n        Dapper.SqlMapper.Query<ICustomerEntity>(null!, \"SELECT Id, Name, Email FROM Customers\");\n    }\n}\n";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "InterfaceRepo.cs"), fileContent);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "ICustomerEntity");
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Select(p => p.Name).Should().Contain(new[] { "Id", "Name", "Email" });
    }

    [Fact]
    public async Task ExtractContractsAsync_ExtractsAttributesFromRecordPositionalParameters()
    {
        var fileContent = "namespace TestApp;\n\npublic class ColumnAttribute : System.Attribute { public ColumnAttribute(string name) { Name = name; } public string Name { get; } }\n\npublic record UserModel([Column(\"user_id\")] int Id, [Column(\"user_name\")] string Name);\n\npublic class Service\n{\n    public void Run()\n    {\n        Dapper.SqlMapper.Query<UserModel>(null!, \"SELECT user_id, user_name FROM Users\");\n    }\n}\n";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "RecordRepo.cs"), fileContent);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "UserModel");
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Select(p => p.ColumnName).Should().Contain(new[] { "user_id", "user_name" });
    }

    [Fact]
    public async Task ExtractContractsAsync_ResolvesSqlFromProperty()
    {
        var fileContent = "namespace TestApp;\n\npublic class PropRepo\n{\n    public static string SqlQuery => \"SELECT id, name FROM users\";\n\n    public void Run()\n    {\n        Dapper.SqlMapper.Query<UserDto>(null!, SqlQuery);\n    }\n}\npublic record UserDto(int id, string name);\n";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "PropRepo.cs"), fileContent);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault(d => d.TargetTypeName == "UserDto");
        rawSql.Should().NotBeNull();
        rawSql!.SqlText.Should().Be("SELECT id, name FROM users");
    }

    [Fact]
    public async Task ExtractContractsAsync_DisambiguatesTypesAcrossNamespaces()
    {
        var file1 = "namespace Domain;\npublic class Customer { public int Id { get; set; } }\n";
        var file2 = "namespace Dto;\npublic class Customer { public string Name { get; set; } }\n";
        var file3 = "namespace App;\npublic class Service\n{\n    public void Run()\n    {\n        Dapper.SqlMapper.Query<Domain.Customer>(null!, \"SELECT Id FROM Customers\");\n    }\n}\n";

        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "DomainCust.cs"), file1);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "DtoCust.cs"), file2);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "AppService.cs"), file3);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault();
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Select(p => p.Name).Should().Contain("Id");
        rawSql.ExpectedProperties.Select(p => p.Name).Should().NotContain("Name");
    }

    [Fact]
    public async Task ExtractContractsAsync_ResolvesDeeplyConcatenatedMultiLineSql()
    {
        var file = @"namespace DeepConcat;
public record Item(int Id);
public class ConcatRepo
{
    public void Run()
    {
        var sql = ""SELECT "" +
                  ""c1, "" +
                  ""c2, "" +
                  ""c3, "" +
                  ""c4, "" +
                  ""c5, "" +
                  ""c6, "" +
                  ""c7, "" +
                  ""c8, "" +
                  ""c9, "" +
                  ""c10, "" +
                  ""c11, "" +
                  ""c12 "" +
                  ""FROM BigTable "";
        Dapper.SqlMapper.Query<Item>(null!, sql);
    }
}";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "DeepConcat.cs"), file);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault();
        rawSql.Should().NotBeNull();
        rawSql!.SqlText.Should().Contain("SELECT c1, c2, c3, c4, c5, c6, c7, c8, c9, c10, c11, c12 FROM BigTable");
    }
    [Fact]
    public async Task ExtractContractsAsync_ResolvesNestedTypeHierarchyProperties()
    {
        var file = @"namespace NestedSample;
public class OuterContainer
{
    public class NestedItem
    {
        public int ItemId { get; set; }
        public string ItemTitle { get; set; } = """";
    }
}
public class NestedRepo
{
    public void Run()
    {
        Dapper.SqlMapper.Query<OuterContainer.NestedItem>(null!, ""SELECT ItemId, ItemTitle FROM Items"");
    }
}";
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "NestedSample.cs"), file);

        var source = new ProjectCSharpSqlSource(_tempDirectory);
        var contracts = await source.ExtractContractsAsync();

        var rawSql = contracts.OfType<RawSqlDescriptor>().FirstOrDefault();
        rawSql.Should().NotBeNull();
        rawSql!.ExpectedProperties.Select(p => p.Name).Should().Contain("ItemId");
        rawSql.ExpectedProperties.Select(p => p.Name).Should().Contain("ItemTitle");
    }
}
