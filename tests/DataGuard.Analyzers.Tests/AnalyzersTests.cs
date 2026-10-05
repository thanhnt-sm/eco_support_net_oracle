// <copyright file="AnalyzersTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests;

using System.Linq;
using DataGuard.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

public class AnalyzersTests
{
    [Fact]
    public void IsPotentialSqlCall_RecognizesDapperStoredProcedureCall()
    {
        const string code = """
            class Repo
            {
                void Call(System.Data.IDbConnection conn)
                {
                    conn.Execute("SP_GET_DATA", commandType: System.Data.CommandType.StoredProcedure);
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var isSql = UnvalidatedSqlCallGenerator.IsPotentialSqlCall(invocation);
        isSql.Should().BeTrue("Dapper stored procedure invocations must be recognized as potential SQL calls");
    }

    [Fact]
    public void IsPotentialSqlCall_AdoNetExecuteWithoutCommandTextInScope_IsNotSqlCall()
    {
        const string code = """
            class Repo
            {
                void Call(System.Data.IDbCommand cmd)
                {
                    cmd.ExecuteNonQuery();
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var isSql = UnvalidatedSqlCallGenerator.IsPotentialSqlCall(invocation);
        isSql.Should().BeFalse("without CommandText in the enclosing member there is no SQL text to flag");
    }

    [Fact]
    public void IsPotentialSqlCall_RecognizesAdoNetStoredProcedureInvocation()
    {
        const string code = """
            class Repo
            {
                void Call(System.Data.IDbCommand cmd)
                {
                    cmd.CommandText = "PKG_ORDERS.ARCHIVE";
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    cmd.ExecuteNonQuery();
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        var isSql = UnvalidatedSqlCallGenerator.IsPotentialSqlCall(invocation);
        isSql.Should().BeTrue("ADO.NET ExecuteNonQuery with CommandText/CommandType in scope must be recognized");
    }

    [Fact]
    public void IsPotentialSqlCall_RecognizesCustomInvocationWithStoredProcedureArg()
    {
        const string code = """
            class Repo
            {
                void Call(DatabaseService db)
                {
                    db.RunHelper("GET_METRICS", System.Data.CommandType.StoredProcedure);
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var isSql = UnvalidatedSqlCallGenerator.IsPotentialSqlCall(invocation);
        isSql.Should().BeTrue("Invocations passing CommandType.StoredProcedure must be recognized as potential SQL calls");
    }

    [Fact]
    public void IsPotentialSqlCall_ArgumentWithStoredProcedureSuffix_IsNotDetectedAsSqlCall()
    {
        const string code = """
            class Repo
            {
                void Call(DatabaseService db)
                {
                    db.CustomMethod("arg1", myStoredProcedure);
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(code);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var isSql = UnvalidatedSqlCallGenerator.IsPotentialSqlCall(invocation);
        isSql.Should().BeFalse("Arbitrary identifiers ending with StoredProcedure must not be falsely detected as SQL calls");
    }
}
