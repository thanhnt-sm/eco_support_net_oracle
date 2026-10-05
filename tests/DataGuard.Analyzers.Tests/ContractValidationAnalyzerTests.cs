// <copyright file="ContractValidationAnalyzerTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests;

using System.Collections.Immutable;
using DataGuard.Analyzers;
using DataGuard.Analyzers.Tests.Verification;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using static DataGuard.Analyzers.Tests.Verification.AnalyzerTestHarness;

/// <summary>
/// Microsoft.CodeAnalysis.Testing coverage for every descriptor the syntax-only <see cref="ContractValidationAnalyzer"/>
/// can emit from literal SQL (EF Core, ExecuteSql and Dapper calls): DG004, DG017, DG097, DG098, DG099. Every test
/// asserts the complete diagnostic set, and every expected diagnostic is located on the SQL argument with
/// <c>{|#0:...|}</c> markup, so a new, missing or misplaced diagnostic fails it.
/// </summary>
public class ContractValidationAnalyzerTests
{
    // ---- DG098 missing FROM ----
    [Fact]
    public async Task Dg098_ExecuteSqlRawSelectWithoutFrom_IsReported()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Ping(DbContext db) => db.Database.ExecuteSqlRaw({|#0:"SELECT 1"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.MissingFromClause).WithLocation(0).WithMessage("Raw SQL query missing FROM clause"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg098_DapperExecuteSelectWithoutFrom_IsReported()
    {
        var test = Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Now(IDbConnection connection) => connection.Execute({|#0:"SELECT GETDATE()"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.MissingFromClause).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg098_SelectWithFrom_IsNotReported()
    {
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Touch(DbContext db) => db.Database.ExecuteSqlRaw("SELECT Id FROM Orders WHERE Id = @id");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg098_NonSelectStatement_IsNotReported()
    {
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Rename(DbContext db) => db.Database.ExecuteSqlRaw("UPDATE Orders SET Status = @status WHERE Id = @id");
            }
            """).RunAsync();
    }

    // ---- DG099 SQL injection pattern ----
    [Theory]
    [InlineData("DELETE FROM Orders WHERE 1=1")]
    [InlineData("UPDATE Users SET Role = 'admin' WHERE Name = 'x' OR '1'='1'")]
    [InlineData("DELETE FROM Orders;-- trailing comment")]
    [InlineData("DROP TABLE Orders")]
    [InlineData("EXEC xp_cmdshell 'dir'")]
    public async Task Dg099_ExecuteSqlRawWithInjectionPattern_IsReported(string sql)
    {
        var test = Analyzer($$"""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Run(DbContext db) => db.Database.ExecuteSqlRaw({|#0:"{{sql}}"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0).WithMessage("Potential SQL injection pattern detected"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_DapperSpExecuteSql_IsReported()
    {
        var test = Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Run(IDbConnection connection, string statement)
                    => connection.Execute({|#0:"EXEC sp_executesql @statement"|}, new { statement });
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_DapperQueryUnionSelect_IsReported()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Account { public int Id { get; set; } }

            public class Repo
            {
                public IEnumerable<Account> All(IDbConnection connection)
                    => connection.Query<Account>({|#0:"SELECT Id FROM Users UNION SELECT Id FROM Admins"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_EfFromSqlRawWithTautology_IsReported()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Order> All(ShopContext db) => db.Orders.FromSqlRaw({|#0:"SELECT Id FROM Orders WHERE 1=1"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_ParameterizedQuery_IsNotReported()
    {
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Rename(DbContext db, string name, int id)
                    => db.Database.ExecuteSqlRaw("UPDATE Users SET Name = @name WHERE Id = @id", name, id);
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg099_ExecuteSqlInterpolated_IsNotReported()
    {
        // EF Core turns each interpolation hole of ExecuteSqlInterpolated into a DbParameter: no injection.
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Rename(DbContext db, string name, int id)
                    => db.Database.ExecuteSqlInterpolated($"UPDATE Users SET Name = {name} WHERE Id = {id}");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg099_StringConcatenationWithParameter_IsReported()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Find(DbContext db, string name)
                    => db.Database.ExecuteSqlRaw({|#0:"SELECT Id FROM Users WHERE Name = '" + name + "'"|});
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.SqlInjectionPattern)
                .WithLocation(0)
                .WithMessage("Potential SQL injection: SQL text passed to 'ExecuteSqlRaw' is built by string concatenation or interpolation; pass values as parameters"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_ConcatenatedSqlThroughLocalVariable_IsReportedAtTheArgument()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Purge(DbContext db, string table)
                {
                    var sql = "DELETE FROM " + table;
                    db.Database.ExecuteSqlRaw({|#0:sql|});
                }
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_ConcatenationOfConstants_IsNotReported()
    {
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                private const string Table = "Orders";

                public void Purge(DbContext db)
                {
                    var where = " WHERE Archived = 1";
                    db.Database.ExecuteSqlRaw("DELETE FROM " + Table + where);
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg099_InterpolationIntoExecuteSqlRaw_IsReported()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Find(DbContext db, string name)
                    => db.Database.ExecuteSqlRaw({|#0:$"SELECT Id FROM Users WHERE Name = '{name}'"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_InterpolationIntoDapperQuery_IsReported()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Account { public int Id { get; set; } }

            public class Repo
            {
                public IEnumerable<Account> Find(IDbConnection connection, string name)
                    => connection.Query<Account>({|#0:$"SELECT Id FROM Users WHERE Name = '{name}'"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg099_EfFromSqlRawStoredProcedureWithSpExecuteSql_IsReported()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Order> All(ShopContext db) => db.Orders.FromSqlRaw({|#0:"EXEC sp_executesql N'SELECT Id FROM Orders'"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithLocation(0));
        await test.RunAsync();
    }

    // ---- DG097 stored-procedure command text form (the analyzer's former DG002 heuristic) ----
    [Fact]
    public async Task Dg097_StoredProcedureCallWithExecPrefix_IsNotReported()
    {
        await Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Archive(IDbConnection connection) => connection.Execute("EXEC dbo.ArchiveOrders");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg097_StoredProcedureCallWithLeadingWhitespace_IsNotReported()
    {
        await Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Archive(IDbConnection connection) => connection.Execute("  EXEC dbo.ArchiveOrders");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg097_BareProcedureNameAsTextCommand_IsReported()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Archive(DbContext db, int id) => db.Database.ExecuteSqlRaw({|#0:"dbo.ArchiveOrders @id"|}, id);
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.StoredProcedureCommandText)
                .WithLocation(0)
                .WithMessage("Stored procedure call must start with EXEC or EXECUTE"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg097_ExecPrefixWithStoredProcedureCommandType_IsReported()
    {
        var test = Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Archive(IDbConnection connection)
                    => connection.Execute({|#0:"EXEC dbo.ArchiveOrders"|}, commandType: CommandType.StoredProcedure);
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.StoredProcedureCommandText)
                .WithLocation(0)
                .WithMessage("CommandType.StoredProcedure expects a bare procedure name; remove the EXEC/EXECUTE/CALL prefix"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg097_BareNameWithStoredProcedureCommandType_IsNotReported()
    {
        await Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Archive(IDbConnection connection)
                    => connection.Execute("PKG_ORDERS.ARCHIVE", commandType: CommandType.StoredProcedure);
            }
            """).RunAsync();
    }

    [Fact]
    public void Dg097_IsAnalyzerOnlyAndDg002KeepsTheEngineMeaning()
    {
        var analyzer = new ContractValidationAnalyzer();
        analyzer.SupportedDiagnostics.Should().ContainSingle(d => d.Id == "DG097")
            .Which.Title.ToString().Should().Be("Stored procedure command text form");
        analyzer.SupportedDiagnostics.Should().ContainSingle(d => d.Id == DiagnosticIds.ParameterMismatch)
            .Which.Title.ToString().Should().Be("Parameter Type Match");
    }

    // ---- DG004 result-set shape ----
    [Fact]
    public async Task Dg004_DapperQueryMissingColumn_ReportsMissingPropertyNames()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public string Email { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Customer> All(IDbConnection connection) => connection.Query<Customer>({|#0:"SELECT Id, Name FROM Customers"|});
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("Result set is missing required columns: Email"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg004_DapperQueryExtraColumn_ReportsUnmappedColumns()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Customer> All(IDbConnection connection) => connection.Query<Customer>({|#0:"SELECT Id, Name, Legacy FROM Customers"|});
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("Result set has 1 extra columns not mapped to entity properties: Legacy"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg004_EfFromSqlRawOnDbSetProperty_ReportsMissingColumn()
    {
        // The DbSet<Order> property is found syntactically through the compilation's type-shape index.
        var test = Analyzer("""
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public decimal Total { get; set; }
            }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Order> All(ShopContext db) => db.Orders.FromSqlRaw({|#0:"SELECT Id FROM Orders"|});
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("Result set is missing required columns: Total"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg004_EfFromSqlRawOnDbSetParameter_ReportsMissingColumn()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public decimal Total { get; set; }
                public List<Order> Children { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Order> All(DbSet<Order> orders) => orders.FromSqlRaw({|#0:"SELECT Id FROM Orders"|});
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithMessage("Result set is missing required columns: Total"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg004_TargetTypeNotDeclaredInCompilation_IsNotChecked()
    {
        await Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Repo
            {
                public IEnumerable<System.Uri> All(IDbConnection connection) => connection.Query<System.Uri>("SELECT Id, Name FROM Links");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg004_AmbiguousTypeName_IsNotChecked()
    {
        await Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            namespace A { public class Customer { public int Id { get; set; } public string Email { get; set; } } }
            namespace B { public class Customer { public int Id { get; set; } } }

            public class Repo
            {
                public IEnumerable<A.Customer> All(IDbConnection connection) => connection.Query<A.Customer>("SELECT Id FROM Customers");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg004_ColumnAttributeSnakeCaseAndNotMapped_AreHonored()
    {
        await Analyzer("""
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations.Schema;
            using System.Data;
            using Dapper;

            public class Customer
            {
                public int Id { get; set; }
                public string CustomerName { get; set; }
                [Column("mail_address")]
                public string Email { get; set; }
                [NotMapped]
                public string Display { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Customer> All(IDbConnection connection)
                    => connection.Query<Customer>("SELECT c.Id, c.customer_name, c.mail_address FROM Customers c");
            }
            """).RunAsync();
    }

    // ---- DG017 SELECT * ----
    [Fact]
    public async Task Dg017_ExecuteSqlRawSelectStar_IsReported()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Copy(DbContext db) => db.Database.ExecuteSqlRaw({|#0:"INSERT INTO Archive SELECT * FROM Orders"|});
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.SelectStarUsage)
                .WithLocation(0)
                .WithMessage("Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation."));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg017_QualifiedStarInDapperQuery_IsReported()
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Order { public int Id { get; set; } }

            public class Repo
            {
                public IEnumerable<Order> All(IDbConnection connection) => connection.Query<Order>({|#0:"SELECT o.* FROM Orders o"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SelectStarUsage).WithLocation(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("SELECT TOP 10 * FROM Orders")]
    [InlineData("SELECT DISTINCT * FROM Orders")]
    [InlineData("SELECT TOP (5) PERCENT * FROM [dbo].[Orders]")]
    public async Task Dg017_SelectStarAfterModifiers_IsReported(string sql)
    {
        var test = Analyzer($$"""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Order { public int Id { get; set; } }

            public class Repo
            {
                public IEnumerable<Order> All(IDbConnection connection) => connection.Query<Order>({|#0:"{{sql}}"|});
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SelectStarUsage).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg017_MultiLineRawStringSelectStar_IsReported()
    {
        var test = Analyzer(""""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Order { public int Id { get; set; } }

            public class Repo
            {
                public IEnumerable<Order> All(IDbConnection connection) => connection.Query<Order>({|#0:"""
                    SELECT
                        *
                    FROM Orders
                    """|});
            }
            """");
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SelectStarUsage).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg017_StarInsideSqlComment_IsNotReported()
    {
        await Analyzer("""
            using System.Collections.Generic;
            using System.Data;
            using Dapper;

            public class Order { public int Id { get; set; } }

            public class Repo
            {
                public IEnumerable<Order> All(IDbConnection connection)
                    => connection.Query<Order>("SELECT Id /* was SELECT * */ FROM Orders");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Dg017_CountStar_IsNotReported()
    {
        await Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Count(IDbConnection connection) => connection.QueryFirst<int>("SELECT COUNT(*) FROM Orders");
            }
            """).RunAsync();
    }

    // ---- Suppression and scope ----
    [Fact]
    public async Task SkipContractCheckFromContractsAssembly_OnClass_SuppressesAllDiagnostics()
    {
        await Analyzer("""
            using DataGuard.Contracts;
            using Microsoft.EntityFrameworkCore;

            [SkipContractCheck(Reason = "legacy maintenance scripts")]
            public class Maintenance
            {
                public void Wipe(DbContext db) => db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE 1=1");

                public void Ping(DbContext db) => db.Database.ExecuteSqlRaw("SELECT 1");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DataGuardMarkerComment_SuppressesDiagnosticsForThatStatementOnly()
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Run(DbContext db)
                {
                    // DataGuard: reviewed, health probe
                    db.Database.ExecuteSqlRaw("SELECT 1");
                    db.Database.ExecuteSqlRaw({|#0:"SELECT 2"|});
                }
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.MissingFromClause).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task GeneratedCode_IsNotAnalyzed()
    {
        await Analyzer("""
            // <auto-generated/>
            using Microsoft.EntityFrameworkCore;

            public class GeneratedRepo
            {
                public void Run(DbContext db) => db.Database.ExecuteSqlRaw("SELECT * FROM Orders WHERE 1=1");
            }
            """).RunAsync();
    }

    // ---- Descriptor surface and location ----
    [Fact]
    public async Task Dg012_IsDeclaredButHasNoEmissionPathInTheAnalyzer()
    {
        // DG012 (provider option mismatch) needs DbContextOptions + database ground truth; it is produced by the CLI
        // rules engine, not by this analyzer. The analyzer advertises it (so .editorconfig severities apply and
        // UseOracleCodeFixProvider can bind) but no AnalyzeXxx path emits it, even on an obvious UseSqlServer call.
        var analyzer = new ContractValidationAnalyzer();
        var descriptor = analyzer.SupportedDiagnostics.Should()
            .ContainSingle(d => d.Id == DiagnosticIds.ProviderOptionMismatch).Subject;
        descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        descriptor.Category.Should().Be("DataGuard.Dialect");

        await Analyzer("""
            public class OptionsBuilder
            {
                public OptionsBuilder UseSqlServer(string connectionString) => this;
            }

            public class OracleStartup
            {
                public void Configure(OptionsBuilder builder) => builder.UseSqlServer("Data Source=ORCL");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task Diagnostics_AreReportedAtTheSqlArgument()
    {
        const string source = """
            public static class Db { public static int ExecuteSqlRaw(this object db, string sql) => 0; }
            public static class Usage { public static int Run(object db) => db.ExecuteSqlRaw("SELECT * FROM Orders WHERE 1=1"); }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "LocationProbe",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Select(d => d.Id).Should().BeEquivalentTo(
            new[] { DiagnosticIds.SqlInjectionPattern, DiagnosticIds.SelectStarUsage });
        var literalStart = source.IndexOf("\"SELECT *", StringComparison.Ordinal);
        var literalLength = "\"SELECT * FROM Orders WHERE 1=1\"".Length;
        diagnostics.Should().OnlyContain(d => d.Location.IsInSource
            && d.Location.SourceTree == tree
            && d.Location.SourceSpan.Start == literalStart
            && d.Location.SourceSpan.Length == literalLength);
    }

    [Fact]
    public async Task AnalyzerOnlyLooksAtSyntax_NoDiagnosticForUnrelatedSqlLookingCalls()
    {
        // Name-only receivers: a QueryXxx/ExecuteXxx method without SQL text is not a SQL call.
        await Analyzer("""
            public class SearchIndex
            {
                public string[] QueryTerms(string text) => new string[0];
            }

            public class Jobs
            {
                public void Execute(string job) { }
            }

            public class Usage
            {
                public void Run(SearchIndex index, Jobs jobs)
                {
                    index.QueryTerms("selected products");
                    jobs.Execute("nightly-report");
                }
            }
            """).RunAsync();
    }
}
