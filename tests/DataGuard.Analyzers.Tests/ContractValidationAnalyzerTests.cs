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
/// Microsoft.CodeAnalysis.Testing coverage for every descriptor <see cref="ContractValidationAnalyzer"/> can emit
/// from literal SQL (AnalyzeEfCoreFromSql / AnalyzeExecuteSql / AnalyzeDapperQuery): DG002, DG004, DG017, DG098,
/// DG099. Every test asserts the complete diagnostic set, so a new or missing diagnostic fails it.
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
                public void Ping(DbContext db) => db.Database.ExecuteSqlRaw("SELECT 1");
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.MissingFromClause).WithMessage("Raw SQL query missing FROM clause"));
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
                public int Now(IDbConnection connection) => connection.Execute("SELECT GETDATE()");
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.MissingFromClause));
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
                public void Run(DbContext db) => db.Database.ExecuteSqlRaw("{{sql}}");
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern).WithMessage("Potential SQL injection pattern detected"));
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
                    => connection.Execute("EXEC sp_executesql @statement", new { statement });
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern));
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
                    => connection.Query<Account>("SELECT Id FROM Users UNION SELECT Id FROM Admins");
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern));
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
                public IEnumerable<Order> All(ShopContext db) => db.Orders.FromSqlRaw("SELECT Id FROM Orders WHERE 1=1");
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern));
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
    [Trait("KnownGap", "true")]
    public async Task Dg099_StringConcatenationWithParameter_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG099 for "... '" + name + "'" passed to ExecuteSqlRaw;
        // ExtractSqlFromArguments only reads constant strings or interpolated syntax, so concatenation (the actual
        // injection shape) yields no SQL text and no diagnostic; fixed in Phase 4.3.
        // Phase 4.3: add test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SqlInjectionPattern)).
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Find(DbContext db, string name)
                    => db.Database.ExecuteSqlRaw("SELECT Id FROM Users WHERE Name = '" + name + "'");
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task Dg099_InterpolationIntoExecuteSqlRaw_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG099; $"...{name}..." passed to ExecuteSqlRaw is inlined (not
        // parameterized) but the analyzer only pattern-matches the literal text; fixed in Phase 4.3 (mark Dynamic).
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Find(DbContext db, string name)
                    => db.Database.ExecuteSqlRaw($"SELECT Id FROM Users WHERE Name = '{name}'");
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task Dg099_EfFromSqlRawStoredProcedureWithSpExecuteSql_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG099 like the Dapper/ExecuteSql paths; ValidateEntityContract
        // skips the injection check whenever the SQL starts with EXEC, so sp_executesql through FromSqlRaw is silent;
        // fixed in Phase 4.3.
        await Analyzer("""
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public IEnumerable<Order> All(ShopContext db) => db.Orders.FromSqlRaw("EXEC sp_executesql N'SELECT Id FROM Orders'");
            }
            """).RunAsync();
    }

    // ---- DG002 stored-procedure prefix ----
    [Fact]
    public async Task Dg002_StoredProcedureCallWithExecPrefix_IsNotReported()
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
    [Trait("KnownGap", "true")]
    public async Task Dg002_StoredProcedureCallWithLeadingWhitespace_IsReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected no diagnostic; isStoredProc is computed on TrimStart() but the
        // EXEC-prefix check in ValidateRawSqlContract uses the untrimmed text, so "  EXEC ..." raises a false DG002
        // (Error); fixed in Phase 4.3. Phase 4.3: remove the expectation below.
        var test = Analyzer("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public int Archive(IDbConnection connection) => connection.Execute("  EXEC dbo.ArchiveOrders");
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ParameterMismatch, DiagnosticSeverity.Error)
                .WithMessage("Stored procedure call must start with EXEC or EXECUTE"));
        await test.RunAsync();
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
                public IEnumerable<Customer> All(IDbConnection connection) => connection.Query<Customer>("SELECT Id, Name FROM Customers");
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
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
                public IEnumerable<Customer> All(IDbConnection connection) => connection.Query<Customer>("SELECT Id, Name, Legacy FROM Customers");
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
                .WithMessage("Result set has 1 extra columns not mapped to entity properties: Legacy"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Dg004_EfFromSqlRawOnDbSetProperty_ReportsMissingColumn()
    {
        // Unlike the DG001 generator, the operation-based analyzer does see FromSqlRaw on a DbSet property.
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
                public IEnumerable<Order> All(ShopContext db) => db.Orders.FromSqlRaw("SELECT Id FROM Orders");
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error)
                .WithMessage("Result set is missing required columns: Total"));
        await test.RunAsync();
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
                public void Copy(DbContext db) => db.Database.ExecuteSqlRaw("INSERT INTO Archive SELECT * FROM Orders");
            }
            """);
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.SelectStarUsage)
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
                public IEnumerable<Order> All(IDbConnection connection) => connection.Query<Order>("SELECT o.* FROM Orders o");
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.SelectStarUsage));
        await test.RunAsync();
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
                    db.Database.ExecuteSqlRaw("SELECT 2");
                }
            }
            """);
        test.ExpectedDiagnostics.Add(Contract(DiagnosticIds.MissingFromClause));
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
    [Trait("KnownGap", "true")]
    public async Task Diagnostics_AreReportedWithoutSourceLocation_KnownGap()
    {
        // KNOWN GAP (redteam-261004 rec 18): expected every ContractValidationAnalyzer diagnostic at the invocation
        // (or SQL literal) span; AnalyzerViolation is created with Location.None and `violation.Location ??
        // invocation.Syntax.GetLocation()` never falls back, so the IDE shows no squiggle, #pragma cannot suppress,
        // and code fixes (DG017 -> DataGuardCodeFixProvider) are never offered; fixed in Phase 4.3.
        // Phase 4.3: assert diagnostic.Location.IsInSource and the span of the ExecuteSqlRaw invocation.
        const string source = """
            public static class Db { public static int ExecuteSqlRaw(this object db, string sql) => 0; }
            public static class Usage { public static int Run(object db) => db.ExecuteSqlRaw("SELECT * FROM Orders WHERE 1=1"); }
            """;
        var compilation = CSharpCompilation.Create(
            "LocationProbe",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Select(d => d.Id).Should().BeEquivalentTo(
            new[] { DiagnosticIds.SqlInjectionPattern, DiagnosticIds.SelectStarUsage });
        diagnostics.Should().OnlyContain(d => d.Location == Location.None);
    }
}
