// <copyright file="GeneratorExecutionTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

using System.Collections.Immutable;
using DataGuard.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DataGuard.Analyzers.Tests;

/// <summary>
/// Executes the incremental generator against real C# source and asserts the
/// DG001 "SQL call not validated" diagnostic is produced for raw SQL calls.
/// </summary>
public class GeneratorExecutionTests
{
    [Fact]
    public async Task SemanticAnalyzer_EmitsMissingFrom_ForExecuteSqlRawLiteral()
    {
        const string source = """
            public sealed class Db
            {
                public void ExecuteSqlRaw(string sql) { }
            }
            public static class Usage
            {
                public static void Run(Db db) => db.ExecuteSqlRaw("SELECT 1");
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == DiagnosticIds.MissingFromClause);
    }

    [Fact]
    public async Task SemanticAnalyzer_EmitsColumnShapeMismatch_ForDapperQueryMissingProperty()
    {
        const string source = """
            using System.Collections.Generic;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public string Email { get; set; }
            }

            public interface IDbConnection { }

            public static class DapperExtensions
            {
                public static IEnumerable<T> Query<T>(this IDbConnection cnn, string sql) => null;
            }

            public static class Usage
            {
                public static void Run(IDbConnection connection)
                {
                    connection.Query<Customer>("SELECT Id, Name FROM Customers");
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == DiagnosticIds.ColumnShapeMismatch);
    }

    [Fact]
    public async Task SemanticAnalyzer_EmitsColumnShapeMismatch_ForDapperQueryExtraColumn()
    {
        const string source = """
            using System.Collections.Generic;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public interface IDbConnection { }

            public static class DapperExtensions
            {
                public static IEnumerable<T> Query<T>(this IDbConnection cnn, string sql) => null;
            }

            public static class Usage
            {
                public static void Run(IDbConnection connection)
                {
                    connection.Query<Customer>("SELECT Id, Name, ExtraColumn FROM Customers");
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == DiagnosticIds.ColumnShapeMismatch);
    }

    [Fact]
    public async Task SemanticAnalyzer_EmitsSelectStarUsage_ForDapperQueryWithSelectStar()
    {
        const string source = """
            using System.Collections.Generic;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public string Email { get; set; }
            }

            public interface IDbConnection { }

            public static class DapperExtensions
            {
                public static IEnumerable<T> Query<T>(this IDbConnection cnn, string sql) => null;
            }

            public static class Usage
            {
                public static void Run(IDbConnection connection)
                {
                    connection.Query<Customer>("SELECT * FROM Customers");
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var selectStarDiag = diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == DiagnosticIds.SelectStarUsage).Subject;
        selectStarDiag.Properties.Should().ContainKey("ExplicitColumns");
        selectStarDiag.Properties["ExplicitColumns"].Should().Be("Id, Name, Email");
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == DiagnosticIds.ColumnShapeMismatch);
    }

    [Fact]
    public async Task SemanticAnalyzer_DoesNotEmitColumnMismatch_WhenAllPropertiesMatch()
    {
        const string source = """
            using System.Collections.Generic;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public interface IDbConnection { }

            public static class DapperExtensions
            {
                public static IEnumerable<T> Query<T>(this IDbConnection cnn, string sql) => null;
            }

            public static class Usage
            {
                public static void Run(IDbConnection connection)
                {
                    connection.Query<Customer>("SELECT Id, Name FROM Customers");
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == DiagnosticIds.ColumnShapeMismatch);
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == DiagnosticIds.SelectStarUsage);
    }

    [Fact]
    public async Task SemanticAnalyzer_EmitsSelectStarUsage_ForEfCoreFromSqlRaw()
    {
        const string source = """
            public class Order
            {
                public int Id { get; set; }
            }

            public interface IQueryable<T> { }
            public class DbSet<T> : IQueryable<T> { }

            public static class RelationalExtensions
            {
                public static IQueryable<T> FromSqlRaw<T>(this DbSet<T> source, string sql, params object[] parameters) => source;
            }

            public static class Usage
            {
                public static void Run(DbSet<Order> orders)
                {
                    orders.FromSqlRaw("SELECT * FROM Orders");
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        var selectStarDiag = diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == DiagnosticIds.SelectStarUsage).Subject;
        selectStarDiag.Properties.Should().ContainKey("ExplicitColumns");
        selectStarDiag.Properties["ExplicitColumns"].Should().Be("Id");
    }

    [Fact]
    public async Task SemanticAnalyzer_SuppressesSqlDiagnostics_WhenCallerHasSkipContractCheckAttribute()
    {
        const string source = """
            using System;
            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
            public sealed class SkipContractCheckAttribute : Attribute { }
            public sealed class Db
            {
                public void ExecuteSqlRaw(string sql) { }
            }
            public static class Usage
            {
                [SkipContractCheck]
                public static void Run(Db db) => db.ExecuteSqlRaw("SELECT 1");
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == DiagnosticIds.MissingFromClause || diagnostic.Id == DiagnosticIds.UnvalidatedSqlCall);
    }

    [Fact]
    public void Generator_EmitsUnvalidatedSqlCall_ForExecuteSqlRawCall()
    {
        const string source = """
            namespace TestApp;
            public class Ctx
            {
                public object ExecuteSqlRaw(string sql) => null;
            }
            public class Usage
            {
                public void Run(Ctx ctx)
                {
                    var x = ctx.ExecuteSqlRaw("SELECT * FROM ORDERS WHERE id = 1");
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestApp",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator());

        // GeneratorDriver is immutable: the updated driver is returned, and only
        // the returned instance carries the run results.
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var runResult = driver.GetRunResult();
        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == DiagnosticIds.UnvalidatedSqlCall)
            .ToList();

        diagnostics.Should().NotBeEmpty("the generator must flag unvalidated raw SQL calls with DG001");
    }

    [Fact]
    public void Generator_DoesNotEmit_ForNonSqlInvocation()
    {
        const string source = """
            namespace TestApp;
            public class Ctx
            {
                public object GetData(int id) => null;
            }
            public class Usage
            {
                public void Run(Ctx ctx)
                {
                    var x = ctx.GetData(1);
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestApp",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(d => d.Id == DiagnosticIds.UnvalidatedSqlCall)
            .ToList();

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_DoesNotEmit_WhenCallerHasSkipContractCheckAttribute()
    {
        const string source = """
            using System;
            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
            public sealed class SkipContractCheckAttribute : Attribute { }
            public class Ctx
            {
                public object ExecuteSqlRaw(string sql) => null;
            }
            public class Usage
            {
                [SkipContractCheck]
                public void Run(Ctx ctx)
                {
                    var x = ctx.ExecuteSqlRaw("SELECT * FROM ORDERS");
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestApp",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        driver.GetRunResult().Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == DiagnosticIds.UnvalidatedSqlCall);
    }
}
