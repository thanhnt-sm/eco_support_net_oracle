// <copyright file="AnalyzerTestHarness.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests.Verification;

using DataGuard.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

/// <summary>
/// Shared Microsoft.CodeAnalysis.Testing configuration for the analyzer test suite.
/// The analyzer ships as netstandard2.0 but the consumer compilation under test targets net9.0,
/// which is what real DataGuard users compile against.
/// </summary>
internal static class AnalyzerTestHarness
{
    /// <summary>Gets the reference assemblies used for every test compilation.</summary>
    public static ReferenceAssemblies ReferenceAssemblies { get; } = ReferenceAssemblies.Net.Net90;

    /// <summary>
    /// Gets the minimal EF Core / Dapper / SqlClient surface the call sites under test bind to.
    /// Shapes and namespaces follow the real libraries so method and containing-type names match
    /// what the analyzer sees in production code.
    /// </summary>
    public const string DataAccessStubs = """
        using System.Collections.Generic;
        using System.Data;
        using System.Threading.Tasks;

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public Infrastructure.DatabaseFacade Database { get; } = new Infrastructure.DatabaseFacade();
            }

            public abstract class DbSet<TEntity>
                where TEntity : class
            {
            }

            public static class RelationalQueryableExtensions
            {
                public static IEnumerable<TEntity> FromSqlRaw<TEntity>(this DbSet<TEntity> source, string sql, params object[] parameters)
                    where TEntity : class => null;

                public static IEnumerable<TEntity> FromSqlInterpolated<TEntity>(this DbSet<TEntity> source, System.FormattableString sql)
                    where TEntity : class => null;
            }

            public static class RelationalDatabaseFacadeExtensions
            {
                public static int ExecuteSqlRaw(this Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;

                public static Task<int> ExecuteSqlRawAsync(this Infrastructure.DatabaseFacade databaseFacade, string sql, params object[] parameters) => null;

                public static int ExecuteSqlInterpolated(this Infrastructure.DatabaseFacade databaseFacade, System.FormattableString sql) => 0;
            }
        }

        namespace Microsoft.EntityFrameworkCore.Infrastructure
        {
            public class DatabaseFacade
            {
            }
        }

        namespace Dapper
        {
            public static class SqlMapper
            {
                public static IEnumerable<T> Query<T>(this IDbConnection cnn, string sql, object param = null, IDbTransaction transaction = null, bool buffered = true, int? commandTimeout = null, CommandType? commandType = null) => null;

                public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection cnn, string sql, object param = null, IDbTransaction transaction = null, int? commandTimeout = null, CommandType? commandType = null) => null;

                public static T QueryFirst<T>(this IDbConnection cnn, string sql, object param = null, IDbTransaction transaction = null, int? commandTimeout = null, CommandType? commandType = null) => default;

                public static int Execute(this IDbConnection cnn, string sql, object param = null, IDbTransaction transaction = null, int? commandTimeout = null, CommandType? commandType = null) => 0;
            }
        }

        namespace Microsoft.Data.SqlClient
        {
            public sealed class SqlDataReader
            {
            }

            public sealed class SqlCommand
            {
                public string CommandText { get; set; }

                public SqlDataReader ExecuteReader() => null;

                public int ExecuteNonQuery() => 0;
            }
        }
        """;

    /// <summary>Gets the DataGuard.Contracts metadata reference ([SkipContractCheck], [DataContract], ...).</summary>
    public static MetadataReference ContractsReference { get; } =
        MetadataReference.CreateFromFile(typeof(DataGuard.Contracts.SkipContractCheckAttribute).Assembly.Location);

    /// <summary>Creates a generator test for the IDE-layer DG001 generator with the shared stubs.</summary>
    /// <param name="source">Test source; DG001 locations are given with <c>{|DG001:...|}</c> markup.</param>
    /// <returns>A configured generator test.</returns>
    public static CSharpSourceGeneratorTest<UnvalidatedSqlCallGenerator, DefaultVerifier> Generator(string source)
    {
        var test = new CSharpSourceGeneratorTest<UnvalidatedSqlCallGenerator, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies,
            TestCode = source,
        };
        test.TestState.Sources.Add(("DataAccessStubs.cs", DataAccessStubs));
        test.TestState.AdditionalReferences.Add(ContractsReference);
        return test;
    }

    /// <summary>Creates an analyzer test for <see cref="ContractValidationAnalyzer"/> with the shared stubs.</summary>
    /// <param name="source">Test source.</param>
    /// <returns>A configured analyzer test.</returns>
    public static CSharpAnalyzerTest<ContractValidationAnalyzer, DefaultVerifier> Analyzer(string source)
    {
        var test = new CSharpAnalyzerTest<ContractValidationAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies,
            TestCode = source,
        };
        test.TestState.Sources.Add(("DataAccessStubs.cs", DataAccessStubs));
        test.TestState.AdditionalReferences.Add(ContractsReference);
        return test;
    }

    /// <summary>
    /// Expected <see cref="ContractValidationAnalyzer"/> diagnostic. The analyzer currently reports every
    /// violation at <see cref="Location.None"/> (it passes <c>Location.None</c>, never <c>null</c>, to
    /// <c>violation.Location ?? invocation.Syntax.GetLocation()</c>), so expectations carry no span.
    /// KNOWN GAP (redteam-261004 rec 18): expected the invocation location; fixed in Phase 4.3, which
    /// should switch this helper to markup spans. See <c>ContractValidationAnalyzerTests.Diagnostics_AreReportedWithoutSourceLocation</c>.
    /// </summary>
    /// <param name="id">Diagnostic ID.</param>
    /// <param name="severity">Expected effective severity.</param>
    /// <returns>An expected diagnostic without location.</returns>
    public static DiagnosticResult Contract(string id, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        => new(id, severity);
}
