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
/// Tests tagged <c>KnownGap</c> pin today's wrong behavior; Phase 4.3 flips them.
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

    // ---- Known gaps (Phase 4.3) ----
    [Fact]
    [Trait("KnownGap", "true")]
    public async Task FromSqlRaw_OnDbSetProperty_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG001 at db.Orders.FromSqlRaw(...); the generator resolves
        // the receiver `db.Orders` and requires an IMethodSymbol on DbSet (Analyzers.cs ExtractSqlCallSite), but a
        // DbSet receiver is an IPropertySymbol, so every real FromSqlRaw call is dropped; fixed in Phase 4.3.
        // Phase 4.3: wrap the invocation in {|DG001:...|}.
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public object Load(ShopContext db) => db.Orders.FromSqlRaw("SELECT Id FROM Orders");
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task FromSqlInterpolated_OnDbSetProperty_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG001 at db.Orders.FromSqlInterpolated(...); same
        // IPropertySymbol receiver bug as FromSqlRaw; fixed in Phase 4.3.
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }

            public class Repo
            {
                public object Load(ShopContext db, int id) => db.Orders.FromSqlInterpolated($"SELECT Id FROM Orders WHERE Id = {id}");
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task FromSqlRaw_OnDbSetParameter_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG001; a DbSet<T> local/parameter receiver resolves to an
        // IParameterSymbol, not an IMethodSymbol, so the call is dropped; fixed in Phase 4.3.
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Order { public int Id { get; set; } }

            public class Repo
            {
                public object Load(DbSet<Order> orders) => orders.FromSqlRaw("SELECT Id FROM Orders");
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task NonSqlExecute_WithoutStringArgument_IsReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected no DG001; any method whose name starts with "Execute" is
        // classified as Dapper and reported even with no SQL text (ICommand.Execute, job runners, ...); fixed in
        // Phase 4.3. Phase 4.3: remove the {|DG001:...|} markup.
        await AnalyzerTestHarness.Generator("""
            public interface ICommand
            {
                void Execute(object parameter);
            }

            public class ViewModel
            {
                public void Save(ICommand saveCommand)
                {
                    {|DG001:saveCommand.Execute(null)|};
                }
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task NonSqlQueryPrefixedMethod_IsReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected no DG001; "Query*" name prefix alone triggers the Dapper path
        // (search-index APIs, CQRS query buses, ...); fixed in Phase 4.3.
        await AnalyzerTestHarness.Generator("""
            public class SearchIndex
            {
                public string[] QueryTerms(int limit) => new string[0];
            }

            public class Search
            {
                public string[] Top(SearchIndex index) => {|DG001:index.QueryTerms(5)|};
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task ExecuteNonQuery_WithoutAnySqlText_IsReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected no DG001 when no SQL text is reachable from the call site
        // (the command text is not set in scope); the generator reports every Execute*/Query* call; fixed in
        // Phase 4.3.
        await AnalyzerTestHarness.Generator("""
            using Microsoft.Data.SqlClient;

            public class Repo
            {
                public int Run(SqlCommand command) => {|DG001:command.ExecuteNonQuery()|};
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task ExecuteSqlRaw_WithDynamicSqlVariable_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG001; ExecuteSqlRaw with non-literal SQL is exactly the
        // unvalidated (dynamic) case, but ExtractSqlCallSite drops calls whose first argument is not a literal or
        // interpolated string; fixed in Phase 4.3.
        await AnalyzerTestHarness.Generator("""
            using Microsoft.EntityFrameworkCore;

            public class Repo
            {
                public void Purge(DbContext db, string table)
                {
                    var sql = "DELETE FROM " + table;
                    db.Database.ExecuteSqlRaw(sql);
                }
            }
            """).RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task HelperWithStoredProcedureCommandType_IsNotReported_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected DG001; IsPotentialSqlCall accepts any invocation passing
        // CommandType.StoredProcedure, but ExtractSqlCallSite maps a non Query*/Execute* name to SqlCallType.Unknown
        // and drops it, so predicate and transform disagree; fixed in Phase 4.3.
        await AnalyzerTestHarness.Generator("""
            using System.Data;

            public class DatabaseService
            {
                public int RunHelper(string name, CommandType commandType) => 0;
            }

            public class Repo
            {
                public int Metrics(DatabaseService db) => db.RunHelper("GET_METRICS", CommandType.StoredProcedure);
            }
            """).RunAsync();
    }
}
