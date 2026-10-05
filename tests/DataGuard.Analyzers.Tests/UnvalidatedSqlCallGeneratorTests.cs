// <copyright file="UnvalidatedSqlCallGeneratorTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests;

using DataGuard.Analyzers;
using DataGuard.Analyzers.Tests.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// DG001 trigger matrix for the IDE-layer <see cref="UnvalidatedSqlCallGenerator"/>, verified with
/// Microsoft.CodeAnalysis.Testing. Expected locations are asserted with <c>{|DG001:...|}</c> markup
/// (the whole invocation expression), so an unexpected or missing DG001 anywhere fails the test.
/// The generator is syntax-only: EF raw-SQL APIs match by method name on any receiver, Dapper/ADO calls need SQL
/// text reachable syntactically (literal, local/const initializer, CommandText) or CommandType.StoredProcedure.
/// </summary>
public class UnvalidatedSqlCallGeneratorTests
{
    // ---- Triggers ----
    [Fact]
    public async Task ExecuteSqlRaw_WithLiteralSql_ReportsDg001AtInvocationWithMethodName()
    {
        var test = AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Purge(DbContext db)
                {
                    {|#0:db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE Id = 1")|};
                }
            }
            """);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticIds.UnvalidatedSqlCall, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("ExecuteSqlRaw")
                .WithMessage("SQL call 'ExecuteSqlRaw' not validated - run 'dataguard check' for full validation"));
        await test.RunAsync();
    }

    [Fact]
    public async Task ExecuteSqlRawAsync_WithLiteralSql_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public Task<int> Purge(DbContext db)
                    => {|DG001:db.Database.ExecuteSqlRawAsync("DELETE FROM Orders")|};
            }
            """).RunAsync();
    }

    [Fact]
    public async Task ExecuteSqlInterpolated_WithInterpolatedSql_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Purge(DbContext db, int id)
                {
                    {|DG001:db.Database.ExecuteSqlInterpolated($"DELETE FROM Orders WHERE Id = {id}")|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperQueryOfT_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;

            public class Customer { public int Id { get; set; } }

            public class Repo
            {
                public void Load(IDbConnection connection)
                {
                    var rows = {|DG001:connection.Query<Customer>("SELECT Id FROM Customers")|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperQueryAsyncOfT_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;

            public class Customer { public int Id { get; set; } }

            public class Repo
            {
                public async System.Threading.Tasks.Task Load(IDbConnection connection)
                {
                    var rows = await {|DG001:connection.QueryAsync<Customer>("SELECT Id FROM Customers")|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperQueryFirstOfT_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;

            public class Customer { public int Id { get; set; } }

            public class Repo
            {
                public Customer Load(IDbConnection connection, int id)
                    => {|DG001:connection.QueryFirst<Customer>("SELECT Id FROM Customers WHERE Id = @id", new { id })|};
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperExecute_WithStoredProcedureCommandType_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;

            public class Repo
            {
                public void Archive(IDbConnection connection)
                {
                    {|DG001:connection.Execute("PKG_ORDERS.ARCHIVE", commandType: CommandType.StoredProcedure)|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task SqlCommandExecuteReader_WithCommandText_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.Data.SqlClient;

            public class Repo
            {
                public void Read(SqlCommand command)
                {
                    command.CommandText = "SELECT Id FROM Orders";
                    var reader = {|DG001:command.ExecuteReader()|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task MultipleCallsInOneMethod_EachReportedAtItsOwnSpan()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Sync(DbContext db, IDbConnection connection)
                {
                    {|DG001:db.Database.ExecuteSqlRaw("DELETE FROM Staging")|};
                    {|DG001:connection.Execute("INSERT INTO Audit (Id) VALUES (1)")|};
                }
            }
            """).RunAsync();
    }

    // ---- Non-triggers ----
    [Fact]
    public async Task SkipContractCheckOnMethod_SuppressesDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using DataGuard.Contracts;
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                [SkipContractCheck(Reason = "reviewed dynamic SQL")]
                public void Purge(DbContext db)
                {
                    db.Database.ExecuteSqlRaw("DELETE FROM Orders");
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task SkipContractCheckOnClass_SuppressesDg001ForEveryMember()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;
            using DataGuard.Contracts;
            using Microsoft.EntityFrameworkCore;

            [SkipContractCheck]
            public class Repo
            {
                public void Purge(DbContext db) => db.Database.ExecuteSqlRaw("DELETE FROM Orders");

                public int Archive(IDbConnection connection) => connection.Execute("EXEC dbo.Archive");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DataGuardMarkerComment_OnStatement_SuppressesDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Purge(DbContext db)
                {
                    // DataGuard: validated against manifest 2026-10-01
                    db.Database.ExecuteSqlRaw("DELETE FROM Orders");
                    {|DG001:db.Database.ExecuteSqlRaw("DELETE FROM Lines")|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task SqlInsideCommentOnly_DoesNotReportDg001()
    {
        await AnalyzerTestHarness.Generator("""
            public class Calculator
            {
                public int Total(int a, int b)
                {
                    // SELECT SUM(Amount) FROM Orders -- kept for reference
                    /* EXEC dbo.RecalculateTotals */
                    return Add(a, b);
                }

                private static int Add(int a, int b) => a + b;
            }
            """).RunAsync();
    }

    [Fact]
    public async Task SqlLiteralPassedToUnrelatedMethod_DoesNotReportDg001()
    {
        await AnalyzerTestHarness.Generator("""
            public class Logger
            {
                public void Log(string message) { }

                public void Run() => Log("SELECT Id FROM Orders");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task NonSqlInvocation_DoesNotReportDg001()
    {
        await AnalyzerTestHarness.Generator("""
            public class Service
            {
                public int GetData(int id) => id;

                public int Run() => GetData(1);
            }
            """).RunAsync();
    }

    // ---- Redteam-261004 H9/R23 (Phase 4.3): name-based EF detection, SQL-text gate for Dapper/ADO ----
    [Fact]
    public async Task FromSqlRaw_OnDbSetProperty_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public object Load(ShopContext db) => {|DG001:db.Orders.FromSqlRaw("SELECT Id FROM Orders")|};
            }
            """).RunAsync();
    }

    [Fact]
    public async Task FromSqlInterpolated_OnDbSetProperty_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public object Load(ShopContext db, int id) => {|DG001:db.Orders.FromSqlInterpolated($"SELECT Id FROM Orders WHERE Id = {id}")|};
            }
            """).RunAsync();
    }

    [Fact]
    public async Task FromSqlRaw_OnDbSetParameter_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class Repo
            {
                public object Load(DbSet<Order> orders) => {|DG001:orders.FromSqlRaw("SELECT Id FROM Orders")|};
            }
            """).RunAsync();
    }

    [Fact]
    public async Task NonSqlExecute_WithoutStringArgument_IsNotReported()
    {
        await AnalyzerTestHarness.Generator("""
            public interface ICommand
            {
                void Execute(object parameter);
            }

            public class ViewModel
            {
                public void Save(ICommand saveCommand)
                {
                    saveCommand.Execute(null);
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task NonSqlQueryPrefixedMethod_IsNotReported()
    {
        await AnalyzerTestHarness.Generator("""
            public class SearchIndex
            {
                public string[] QueryTerms(int limit) => new string[0];

                public string[] QueryText(string text) => new string[0];
            }

            public class Search
            {
                public string[] Top(SearchIndex index) => index.QueryTerms(5);

                public string[] Find(SearchIndex index) => index.QueryText("selected products");
            }
            """).RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_WithoutAnySqlText_IsNotReported()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.Data.SqlClient;

            public class Repo
            {
                public int Run(SqlCommand command) => command.ExecuteNonQuery();
            }
            """).RunAsync();
    }

    [Fact]
    public async Task ExecuteNonQuery_WithConstructorCommandText_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.Data.SqlClient;

            public class Repo
            {
                public int Run()
                {
                    var command = new SqlCommand { CommandText = "DELETE FROM Staging" };
                    return {|DG001:command.ExecuteNonQuery()|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task ExecuteSqlRaw_WithDynamicSqlVariable_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Purge(DbContext db, string table)
                {
                    var sql = "DELETE FROM " + table;
                    {|DG001:db.Database.ExecuteSqlRaw(sql)|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperQuery_WithSqlInLocalVariable_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;

            public class Customer { public int Id { get; set; } }

            public class Repo
            {
                private const string ByName = "SELECT Id FROM Customers WHERE Name = @name";

                public void Load(IDbConnection connection, string name)
                {
                    const string all = "SELECT Id FROM Customers";
                    var rows = {|DG001:connection.Query<Customer>(all)|};
                    var named = {|DG001:connection.Query<Customer>(ByName, new { name })|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperQuery_WithUnknownSqlParameter_IsNotReported()
    {
        // Nothing syntactic says the string is SQL (a method parameter): no DG001.
        await AnalyzerTestHarness.Generator("""
            using System.Data;
            using Dapper;

            public class Customer { public int Id { get; set; } }

            public class Repo
            {
                public object Load(IDbConnection connection, string sql) => connection.Query<Customer>(sql);
            }
            """).RunAsync();
    }

    [Fact]
    public async Task HelperWithStoredProcedureCommandType_ReportsDg001()
    {
        await AnalyzerTestHarness.Generator("""
            using System.Data;

            public class DatabaseService
            {
                public int RunHelper(string name, CommandType commandType) => 0;
            }

            public class Repo
            {
                public int Metrics(DatabaseService db) => {|DG001:db.RunHelper("GET_METRICS", CommandType.StoredProcedure)|};
            }
            """).RunAsync();
    }

    [Fact]
    public async Task SkipContractCheckAttributeFullNameOnEnclosingType_SuppressesNestedTypeCalls()
    {
        await AnalyzerTestHarness.Generator("""
            using DataGuard.Contracts;
            using Microsoft.EntityFrameworkCore;

            [SkipContractCheckAttribute]
            public class Outer
            {
                public class Repo
                {
                    public void Purge(DbContext db) => db.Database.ExecuteSqlRaw("DELETE FROM Orders");
                }
            }
            """).RunAsync();
    }

    [Fact]
    public void Model_IsEquatableAndHoldsNoSyntax()
    {
        var first = new SqlCallModel("/a.cs", 10, 20, "ExecuteSqlRaw", SqlCallType.ExecuteSql, "DELETE FROM T", "Repo", default);
        var second = new SqlCallModel("/a.cs", 10, 20, "ExecuteSqlRaw", SqlCallType.ExecuteSql, "DELETE FROM T", "Repo", default);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.DoesNotContain(
            typeof(SqlCallModel).GetProperties(),
            property => typeof(SyntaxNode).IsAssignableFrom(property.PropertyType) || property.PropertyType == typeof(Location));
    }

    [Fact]
    public void Generator_SecondRunWithUnrelatedEdit_ServesCallSiteModelsFromCache()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "public class Db { public int ExecuteSqlRaw(string sql) => 0; }\n" +
            "public class Repo { public int Run(Db db) => db.ExecuteSqlRaw(\"DELETE FROM T\"); }\n",
            path: "/0/Repo.cs");
        var other = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class Other { }", path: "/0/Other.cs");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Cache",
            new[] { tree, other },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
            new[] { new UnvalidatedSqlCallGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(other, other.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From("public class Other { int x; }")));
        driver = driver.RunGenerators(edited);

        var result = driver.GetRunResult();
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.UnvalidatedSqlCall);
        var outputs = result.Results[0].TrackedSteps[UnvalidatedSqlCallGenerator.ModelTrackingName].SelectMany(run => run.Outputs);
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"unexpected step reason {output.Reason}"));
    }
}
