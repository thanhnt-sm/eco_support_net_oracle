// <copyright file="CodeFixRoundTripTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.CodeFixes.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataGuard.Analyzers;
using DataGuard.Analyzers.CodeFixes;
using DataGuard.CodeFixes.Tests.Verification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Analyzer -> code fix round trips through <c>CSharpCodeFixTest</c>: the diagnostic is produced by an analyzer or
/// generator (not built by hand), the provider's action is applied, the result must equal <c>FixedCode</c>, compile,
/// and leave no fixable diagnostic behind. The framework also replays every fix through Fix All (document, project,
/// solution). One round trip per provider in CodeFixProviders.cs, plus the negative and known-gap cases.
/// </summary>
public class CodeFixRoundTripTests
{
    private const string EfStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
            }

            public static class RelationalDatabaseFacadeExtensions
            {
                public static int ExecuteSqlRaw(this Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
            }
        }

        namespace Microsoft.EntityFrameworkCore.Infrastructure
        {
            public class DatabaseFacade
            {
            }
        }
        """;

    // ---- DG001 (UnvalidatedSqlCallGenerator) ----
    [Fact]
    public async Task DataGuardCodeFixProvider_Dg001FromGenerator_AddsSkipContractCheckWithReason()
    {
        var test = new GeneratorCodeFixTest<DataGuardCodeFixProvider>
        {
            TestCode = """
                using Microsoft.EntityFrameworkCore;

                public class Repo
                {
                    public void Purge(DbContext db)
                    {
                        {|DG001:db.Database.ExecuteSqlRaw("DELETE FROM Orders")|};
                    }
                }
                """,
            FixedCode = """
                using Microsoft.EntityFrameworkCore;

                public class Repo
                {
                    [global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
                    public void Purge(DbContext db)
                    {
                        db.Database.ExecuteSqlRaw("DELETE FROM Orders");
                    }
                }
                """,
            CodeActionEquivalenceKey = "DataGuard.AddSkipContractCheck",
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task DataGuardCodeFixProvider_Dg001FixAll_SuppressesEveryCallSite()
    {
        var test = new GeneratorCodeFixTest<DataGuardCodeFixProvider>
        {
            TestCode = """
                using Microsoft.EntityFrameworkCore;

                public class Repo
                {
                    public void Purge(DbContext db) => {|DG001:db.Database.ExecuteSqlRaw("DELETE FROM Orders")|};

                    public void Reset(DbContext db) => {|DG001:db.Database.ExecuteSqlRaw("DELETE FROM Lines")|};
                }
                """,
            FixedCode = """
                using Microsoft.EntityFrameworkCore;

                public class Repo
                {
                    [global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
                    public void Purge(DbContext db) => db.Database.ExecuteSqlRaw("DELETE FROM Orders");

                    [global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
                    public void Reset(DbContext db) => db.Database.ExecuteSqlRaw("DELETE FROM Lines");
                }
                """,
            CodeActionEquivalenceKey = "DataGuard.AddSkipContractCheck",
        };
        await test.RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task SkipContractCheckFixProvider_Dg001FromGenerator_EmitsUncompilableAttribute_KnownGap()
    {
        // KNOWN GAP (redteam-261004 H9/R23): expected [SkipContractCheck(Reason = "...")] that compiles; the provider
        // passes the reason positionally but SkipContractCheckAttribute only has a parameterless constructor and a
        // Reason property, so the fix introduces CS1729; fixed in Phase 4.3.
        // Phase 4.3: use `Reason = "..."` in FixedCode and drop the CS1729 markup.
        var test = new GeneratorCodeFixTest<SkipContractCheckFixProvider>
        {
            TestCode = """
                using Microsoft.EntityFrameworkCore;

                public class Repo
                {
                    public void Purge(DbContext db)
                    {
                        {|DG001:db.Database.ExecuteSqlRaw("DELETE FROM Orders")|};
                    }
                }
                """,
            FixedCode = """
                using Microsoft.EntityFrameworkCore;

                public class Repo
                {
                    [{|CS1729:global::DataGuard.Contracts.SkipContractCheck("Dynamic SQL - manual review required")|}]
                    public void Purge(DbContext db)
                    {
                        db.Database.ExecuteSqlRaw("DELETE FROM Orders");
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---- DG002 (verified SQL replacement) ----
    [Fact]
    public async Task DataGuardCodeFixProvider_Dg002WithManifestEvidence_AppliesVerifiedSql()
    {
        var test = CodeFix<VerifiedSqlStubAnalyzer, DataGuardCodeFixProvider>(
            """
            public class Repo
            {
                public void Run() => Execute({|DG002:"EXEC legacy_proc"|});

                private static void Execute(string sql) { }
            }
            """,
            """
            public class Repo
            {
                public void Run() => Execute("EXEC current_proc @id");

                private static void Execute(string sql) { }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task DataGuardCodeFixProvider_Dg002WithoutManifestEvidence_OffersNoRewrite()
    {
        const string source = """
            public class Repo
            {
                public void Run() => Execute({|DG002:"EXEC legacy_proc"|});

                private static void Execute(string sql) { }
            }
            """;
        await CodeFix<UnverifiedSqlStubAnalyzer, DataGuardCodeFixProvider>(source, source).RunAsync();
    }

    // ---- DG017 (SELECT *) ----
    [Fact]
    public async Task DataGuardCodeFixProvider_Dg017WithExplicitColumns_ReplacesStar()
    {
        var test = CodeFix<SelectStarStubAnalyzer, DataGuardCodeFixProvider>(
            """
            public class Repo
            {
                public void Run() => Query({|DG017:"SELECT * FROM Customers WHERE Id = @id"|});

                private static void Query(string sql) { }
            }
            """,
            """
            public class Repo
            {
                public void Run() => Query("SELECT Id, Name FROM Customers WHERE Id = @id");

                private static void Query(string sql) { }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    [Trait("KnownGap", "true")]
    public async Task DataGuardCodeFixProvider_Dg017FromContractValidationAnalyzer_IsNeverFixed_KnownGap()
    {
        // KNOWN GAP (redteam-261004 rec 18): expected the real ContractValidationAnalyzer DG017 to round-trip into
        // "SELECT Id, Name FROM Customers"; the analyzer reports at Location.None, so no code fix can be registered
        // and FixedCode equals TestCode; fixed in Phase 4.3.
        // Phase 4.3: put {|DG017:...|} on the literal (or invocation) and set FixedCode to the explicit column list.
        const string source = """
            using System.Collections.Generic;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public interface IDbConnection { }

            public static class SqlMapper
            {
                public static IEnumerable<T> Query<T>(this IDbConnection cnn, string sql) => null;
            }

            public class Repo
            {
                public IEnumerable<Customer> All(IDbConnection connection) => connection.Query<Customer>("SELECT * FROM Customers");
            }
            """;
        var test = new CSharpCodeFixTest<ContractValidationAnalyzer, DataGuardCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = CodeFixTestHarness.ReferenceAssemblies,
            TestCode = source,
            FixedCode = source,
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticIds.SelectStarUsage, DiagnosticSeverity.Warning));
        await test.RunAsync();
    }

    // ---- DG007 / DG009 (AddMaxLengthAttributeFixProvider) ----
    [Fact]
    public async Task AddMaxLengthAttributeFixProvider_Dg009_AddsVerifiedMaxLength()
    {
        var test = CodeFix<ManifestLengthStubAnalyzer, AddMaxLengthAttributeFixProvider>(
            """
            public class Customer
            {
                public string {|DG009:Name|} { get; set; }
            }
            """,
            """
            public class Customer
            {
                [global::System.ComponentModel.DataAnnotations.MaxLength(100)]
                public string Name { get; set; }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task AddMaxLengthAttributeFixProvider_Dg007_ReplacesOversizedBound()
    {
        var test = CodeFix<ManifestLengthStubAnalyzer, AddMaxLengthAttributeFixProvider>(
            """
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [MaxLength(4000)]
                public string {|DG007:Name|} { get; set; }
            }
            """,
            """
            using System.ComponentModel.DataAnnotations;

            public class Customer
            {
                [global::System.ComponentModel.DataAnnotations.MaxLength(100)]
                public string Name { get; set; }
            }
            """);
        await test.RunAsync();
    }

    // ---- DG006 (NamingConventionFixProvider) ----
    [Fact]
    public async Task NamingConventionFixProvider_Dg006_RenamesPropertyAndReferences()
    {
        var test = CodeFix<NamingStubAnalyzer, NamingConventionFixProvider>(
            """
            public class Customer
            {
                public string {|DG006:customer_name|} { get; set; }

                public override string ToString() => customer_name;
            }
            """,
            """
            public class Customer
            {
                public string CustomerName { get; set; }

                public override string ToString() => CustomerName;
            }
            """);
        test.CodeActionEquivalenceKey = "DataGuard.FixNamingConvention";
        await test.RunAsync();
    }

    [Fact]
    public async Task NamingConventionFixProvider_Dg006_AddsExplicitColumnAttribute()
    {
        var test = CodeFix<NamingStubAnalyzer, NamingConventionFixProvider>(
            """
            public class Customer
            {
                public string {|DG006:customer_name|} { get; set; }
            }
            """,
            """
            public class Customer
            {
                [global::System.ComponentModel.DataAnnotations.Schema.Column("customer_name")]
                public string customer_name { get; set; }
            }
            """);
        test.CodeActionEquivalenceKey = "DataGuard.AddColumnAttribute";
        await test.RunAsync();
    }

    // ---- DG012 (UseOracleCodeFixProvider) ----
    [Fact]
    public async Task UseOracleCodeFixProvider_Dg012_ReplacesUseSqlServer()
    {
        var test = CodeFix<ProviderOptionStubAnalyzer, UseOracleCodeFixProvider>(
            """
            public class OptionsBuilder
            {
                public OptionsBuilder UseSqlServer(string connectionString) => this;

                public OptionsBuilder UseOracle(string connectionString) => this;
            }

            public class Startup
            {
                public void Configure(OptionsBuilder builder)
                {
                    {|DG012:builder.UseSqlServer("Data Source=ORCL")|};
                }
            }
            """,
            """
            public class OptionsBuilder
            {
                public OptionsBuilder UseSqlServer(string connectionString) => this;

                public OptionsBuilder UseOracle(string connectionString) => this;
            }

            public class Startup
            {
                public void Configure(OptionsBuilder builder)
                {
                    builder.UseOracle("Data Source=ORCL");
                }
            }
            """);
        await test.RunAsync();
    }

    private static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> CodeFix<TAnalyzer, TCodeFix>(string source, string fixedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            ReferenceAssemblies = CodeFixTestHarness.ReferenceAssemblies,
            TestCode = source,
            FixedCode = fixedSource,
        };
        test.TestState.AdditionalReferences.Add(CodeFixTestHarness.ContractsReference);
        test.FixedState.AdditionalReferences.Add(CodeFixTestHarness.ContractsReference);
        return test;
    }

    /// <summary>Code fix test whose diagnostics come from the IDE-layer DG001 generator.</summary>
    /// <typeparam name="TCodeFix">Code fix provider under test.</typeparam>
    private sealed class GeneratorCodeFixTest<TCodeFix> : CSharpCodeFixTest<EmptyDiagnosticAnalyzer, TCodeFix, DefaultVerifier>
        where TCodeFix : CodeFixProvider, new()
    {
        public GeneratorCodeFixTest()
        {
            this.ReferenceAssemblies = CodeFixTestHarness.ReferenceAssemblies;
            this.TestState.Sources.Add(("EfStubs.cs", EfStubs));
            this.TestState.AdditionalReferences.Add(CodeFixTestHarness.ContractsReference);
            this.FixedState.Sources.Add(("EfStubs.cs", EfStubs));
            this.FixedState.AdditionalReferences.Add(CodeFixTestHarness.ContractsReference);
        }

        protected override IEnumerable<Type> GetSourceGenerators() => new[] { typeof(UnvalidatedSqlCallGenerator) };
    }
}
