// <copyright file="ConstantConcatIntegrationTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using DataGuard.SqlComposition;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using Xunit;
using static DataGuard.Analyzers.Tests.Verification.AnalyzerTestHarness;

public class ConstantConcatIntegrationTests
{
    [Fact]
    public void SharedResolver_AlreadyCancelled_StopsBeforeResolution()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { string M() => string.Concat(\"SELECT \", \"* FROM Orders\"); }");
        var compilation = CSharpCompilation.Create("Cancelled", new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;

        Action resolve = () => ConstantStringConcatResolver.Resolve(operation, new CancellationToken(canceled: true));

        resolve.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("string.Concat(\"SELECT \", \"* FROM Orders\")")]
    [InlineData("System.String.Concat(str1: \"* FROM Orders\", str0: \"SELECT \")")]
    [InlineData("string.Concat(\"SELECT \", string.Concat(\"*\", \" FROM Orders\"))")]
    public async Task EfConstantConcat_SelectStar_IsReportedAtWholeExpression(string expression)
    {
        var test = Analyzer("""
            using Microsoft.EntityFrameworkCore;
            public class Repo
            {
                public void Run(DbContext db) => db.Database.ExecuteSqlRaw({|#0:EXPRESSION|});
            }
            """.Replace("EXPRESSION", expression));
        test.ExpectedDiagnostics.Add(
            Contract(DiagnosticIds.SelectStarUsage).WithLocation(0)
                .WithMessage("Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation."));
        await test.RunAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EfConstantConcat_MappedColumns_OnlyReportsMissing(bool includeTotal)
    {
        var test = Analyzer("""
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;
            public class Order { public int Id { get; set; } public decimal Total { get; set; } }
            public class Repo
            {
                public IEnumerable<Order> Run(DbSet<Order> orders)
                    => orders.FromSqlRaw({|#0:string.Concat("SELECT Id", "COLUMNS FROM Orders")|});
            }
            """.Replace("COLUMNS", includeTotal ? ", Total" : ""));
        if (!includeTotal)
        {
            test.ExpectedDiagnostics.Add(
                Contract(DiagnosticIds.ColumnShapeMismatch, DiagnosticSeverity.Error).WithLocation(0)
                    .WithMessage("Result set is missing required columns: Total"));
        }

        await test.RunAsync();
    }

    [Theory]
    [InlineData("string.Concat(\"SELECT \", columns, \" FROM Orders\")")]
    [InlineData("Fake.Concat(\"SELECT \", \"* FROM Orders\")")]
    [InlineData("string.Concat((object)\"SELECT \", (object)\"* FROM Orders\")")]
    public async Task UnsupportedConcat_DoesNotInventDiagnostic(string expression)
    {
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;
            public static class Fake { public static string Concat(string a, string b) => a + b; }
            public class Repo
            {
                public void Run(DbContext db, string columns) => db.Database.ExecuteSqlRaw(EXPRESSION);
            }
            """.Replace("EXPRESSION", expression)).RunAsync();
    }

    [Fact]
    public async Task SuppressedConcat_RemainsSuppressed()
    {
        await Analyzer("""
            using Microsoft.EntityFrameworkCore;
            public class Repo
            {
                public void Run(DbContext db)
                {
                    // DataGuard: acknowledged
                    db.Database.ExecuteSqlRaw(string.Concat("SELECT ", "* FROM Orders"));
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DapperConcat_RemainsOutsideThisTypingSlice()
    {
        await Analyzer("""
            using System.Data;
            using Dapper;
            public class Order { public int Id { get; set; } public string Optional { get; set; } }
            public class Repo
            {
                public void Run(IDbConnection connection)
                {
                    connection.Query<Order>(string.Concat("SELECT Id", " FROM Orders"));
                }
            }
            """).RunAsync();
    }
}
