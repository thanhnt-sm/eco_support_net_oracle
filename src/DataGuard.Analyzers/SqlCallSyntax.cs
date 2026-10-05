// <copyright file="SqlCallSyntax.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DataGuard.SqlClassification;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>SQL call families recognised by <see cref="SqlCallSyntax"/>.</summary>
internal enum SqlCallType : byte
{
    /// <summary>Not a SQL call.</summary>
    Unknown = 0,

    /// <summary>EF Core query APIs: FromSqlRaw, FromSqlInterpolated, FromSql, SqlQueryRaw, SqlQuery.</summary>
    EfCore = 1,

    /// <summary>EF Core command APIs: ExecuteSqlRaw(Async), ExecuteSqlInterpolated(Async), ExecuteSql(Async).</summary>
    ExecuteSql = 2,

    /// <summary>Dapper-style Query*/Execute* call with SQL text or CommandType.StoredProcedure.</summary>
    Dapper = 3,

    /// <summary>ADO.NET ExecuteReader/ExecuteNonQuery/ExecuteScalar on a command whose text is set in scope.</summary>
    AdoCommand = 4,

    /// <summary>Any other call that passes CommandType.StoredProcedure.</summary>
    StoredProcedureHelper = 5,
}

/// <summary>SQL text recovered syntactically from an argument expression.</summary>
internal readonly struct SqlText
{
    public SqlText(string? text, bool isDynamic, bool isInterpolated)
    {
        Text = text;
        IsDynamic = isDynamic;
        IsInterpolated = isInterpolated;
    }

    /// <summary>Gets the SQL text with non-constant parts replaced by <c>@pN</c> placeholders, or null when unknown.</summary>
    public string? Text { get; }

    /// <summary>Gets a value indicating whether the text contains non-constant parts (concatenation or interpolation holes).</summary>
    public bool IsDynamic { get; }

    /// <summary>Gets a value indicating whether the text comes from an interpolated string.</summary>
    public bool IsInterpolated { get; }
}

/// <summary>One recognised SQL call site.</summary>
internal sealed class SqlCall
{
    public SqlCall(
        InvocationExpressionSyntax invocation,
        string method,
        SqlCallType kind,
        ArgumentSyntax? sqlArgument,
        SqlText sql,
        bool isStoredProcedureCommand)
    {
        Invocation = invocation;
        Method = method;
        Kind = kind;
        SqlArgument = sqlArgument;
        Sql = sql;
        IsStoredProcedureCommand = isStoredProcedureCommand;
    }

    public InvocationExpressionSyntax Invocation { get; }

    public string Method { get; }

    public SqlCallType Kind { get; }

    /// <summary>Gets the argument carrying the SQL text (null for ADO.NET commands whose text is set elsewhere).</summary>
    public ArgumentSyntax? SqlArgument { get; }

    public SqlText Sql { get; }

    /// <summary>Gets a value indicating whether the call passes CommandType.StoredProcedure.</summary>
    public bool IsStoredProcedureCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the API turns interpolation holes into DbParameters
    /// (FromSqlInterpolated, ExecuteSqlInterpolated, FromSql, ExecuteSql, SqlQuery).
    /// </summary>
    public bool IsParameterizingApi => SqlCallSyntax.IsParameterizingMethod(Method);
}

/// <summary>
/// Syntax-only recognition of SQL call sites shared by <see cref="UnvalidatedSqlCallGenerator"/> and
/// <see cref="ContractValidationAnalyzer"/>. Nothing here binds symbols: receivers are matched by method name and
/// variables are followed only to a declaration with an initializer in an enclosing block (or a field of the
/// enclosing type), so the cost per call site is bounded by the size of its enclosing member.
/// </summary>
internal static class SqlCallSyntax
{
    private const int MaximumResolutionDepth = 4;

    private static readonly Dictionary<string, SqlCallType> EfMethods = new(StringComparer.Ordinal)
    {
        ["FromSqlRaw"] = SqlCallType.EfCore,
        ["FromSqlInterpolated"] = SqlCallType.EfCore,
        ["FromSql"] = SqlCallType.EfCore,
        ["SqlQueryRaw"] = SqlCallType.EfCore,
        ["SqlQuery"] = SqlCallType.EfCore,
        ["ExecuteSqlRaw"] = SqlCallType.ExecuteSql,
        ["ExecuteSqlRawAsync"] = SqlCallType.ExecuteSql,
        ["ExecuteSqlInterpolated"] = SqlCallType.ExecuteSql,
        ["ExecuteSqlInterpolatedAsync"] = SqlCallType.ExecuteSql,
        ["ExecuteSql"] = SqlCallType.ExecuteSql,
        ["ExecuteSqlAsync"] = SqlCallType.ExecuteSql,
    };

    private static readonly HashSet<string> ParameterizingMethods = new(StringComparer.Ordinal)
    {
        "FromSqlInterpolated", "FromSql", "SqlQuery", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
        "ExecuteSql", "ExecuteSqlAsync",
    };

    private static readonly HashSet<string> AdoMethods = new(StringComparer.Ordinal)
    {
        "ExecuteReader", "ExecuteReaderAsync", "ExecuteNonQuery", "ExecuteNonQueryAsync", "ExecuteScalar",
        "ExecuteScalarAsync",
    };

    /// <summary>
    /// Cheap per-node filter (no tree walks): an invocation whose method name is an EF raw-SQL API, a Dapper or
    /// ADO.NET Query*/Execute* name, or that passes CommandType.StoredProcedure.
    /// </summary>
    /// <param name="node">Syntax node.</param>
    /// <returns>True when the node deserves a full <see cref="Classify"/>.</returns>
    public static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var name = GetMethodName(invocation);
        if (name == null)
        {
            return false;
        }

        if (EfMethods.ContainsKey(name)
            || name.StartsWith("Query", StringComparison.Ordinal)
            || name.StartsWith("Execute", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (IsStoredProcedureCommandType(argument.Expression))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Classifies a candidate invocation; returns null when it is not a SQL call or is suppressed.</summary>
    /// <param name="invocation">Invocation to classify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recognised call, or null.</returns>
    public static SqlCall? Classify(InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var name = GetMethodName(invocation);
        if (name == null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var call = ClassifyCore(invocation, name);
        if (call == null || IsSuppressed(invocation))
        {
            return null;
        }

        return call;
    }

    /// <summary>
    /// Returns whether a <c>// DataGuard:</c> marker comment precedes the enclosing statement, or
    /// <c>[SkipContractCheck]</c> decorates an enclosing member, local function or type.
    /// </summary>
    /// <param name="node">Node inside the member to check.</param>
    /// <returns>True when diagnostics for <paramref name="node"/> are suppressed.</returns>
    public static bool IsSuppressed(SyntaxNode node) => HasDataGuardMarkerComment(node) || HasSkipContractCheckAttribute(node);

    /// <summary>Returns whether a <c>// DataGuard:</c> marker comment precedes the statement containing <paramref name="node"/>.</summary>
    /// <param name="node">Node to check.</param>
    /// <returns>True when the marker comment is present.</returns>
    public static bool HasDataGuardMarkerComment(SyntaxNode node)
    {
        // Quick-fixes emit "// DataGuard: ..." notes; a marker comment on the enclosing statement suppresses
        // the diagnostics (the developer acknowledged the call).
        var statement = node.FirstAncestorOrSelf<StatementSyntax>() ?? node;
        foreach (var trivia in statement.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                && trivia.ToString().IndexOf("DataGuard:", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the invoked method's simple name (without type arguments), or null.</summary>
    /// <param name="invocation">Invocation.</param>
    /// <returns>Method name.</returns>
    public static string? GetMethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => null,
    };

    /// <summary>Returns the simple-name syntax of the invoked method (to read explicit type arguments).</summary>
    /// <param name="invocation">Invocation.</param>
    /// <returns>Method name syntax.</returns>
    public static SimpleNameSyntax? GetMethodNameSyntax(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        SimpleNameSyntax simple => simple,
        _ => null,
    };

    /// <summary>Returns whether <paramref name="method"/> parameterizes interpolation holes.</summary>
    /// <param name="method">Method name.</param>
    /// <returns>True for FormattableString-based EF Core APIs.</returns>
    public static bool IsParameterizingMethod(string method) => ParameterizingMethods.Contains(method);

    /// <summary>Recovers SQL text from an expression: literals, interpolation, concatenation and local/const lookups.</summary>
    /// <param name="expression">Expression.</param>
    /// <returns>The recovered text (Text is null when nothing is known).</returns>
    public static SqlText ResolveText(ExpressionSyntax expression)
    {
        var placeholders = 0;
        return Resolve(expression, 0, ref placeholders);
    }

    /// <summary>Returns the right-most identifier of a type syntax (<c>Ns.Order</c> → <c>Order</c>), or null for generic/array/predefined types.</summary>
    /// <param name="type">Type syntax.</param>
    /// <returns>Simple type name.</returns>
    public static string? GetSimpleTypeName(TypeSyntax? type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => GetSimpleTypeName(qualified.Right),
        AliasQualifiedNameSyntax alias => GetSimpleTypeName(alias.Name),
        _ => null,
    };

    private static SqlCall? ClassifyCore(InvocationExpressionSyntax invocation, string name)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var storedProcedure = false;
        foreach (var argument in arguments)
        {
            storedProcedure |= IsStoredProcedureCommandType(argument.Expression);
        }

        if (EfMethods.TryGetValue(name, out var efKind))
        {
            var sqlArgument = SelectSqlArgument(invocation);
            var text = sqlArgument != null ? ResolveText(sqlArgument.Expression) : default;
            return new SqlCall(invocation, name, efKind, sqlArgument, text, storedProcedure);
        }

        if (name.StartsWith("Query", StringComparison.Ordinal) || name.StartsWith("Execute", StringComparison.Ordinal))
        {
            var sqlArgument = SelectSqlArgument(invocation);
            if (sqlArgument != null)
            {
                var text = ResolveText(sqlArgument.Expression);
                if (storedProcedure || SqlClassifier.LooksLikeSql(text.Text))
                {
                    return new SqlCall(invocation, name, SqlCallType.Dapper, sqlArgument, text, storedProcedure);
                }
            }

            if (AdoMethods.Contains(name) && !HasStringArgument(arguments)
                && TryFindCommandText(invocation, out var commandText, out var commandIsProcedure))
            {
                return new SqlCall(invocation, name, SqlCallType.AdoCommand, sqlArgument: null, commandText, commandIsProcedure);
            }
        }

        if (storedProcedure)
        {
            ArgumentSyntax? sqlArgument = null;
            var text = default(SqlText);
            foreach (var argument in arguments)
            {
                var candidate = ResolveText(argument.Expression);
                if (candidate.Text != null)
                {
                    sqlArgument = argument;
                    text = candidate;
                    break;
                }
            }

            return new SqlCall(invocation, name, SqlCallType.StoredProcedureHelper, sqlArgument, text, isStoredProcedureCommand: true);
        }

        return null;
    }

    private static ArgumentSyntax? SelectSqlArgument(InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;
        foreach (var argument in arguments)
        {
            var parameter = argument.NameColon?.Name.Identifier.ValueText;
            if (parameter is "sql" or "commandText" or "query")
            {
                return argument;
            }
        }

        // Extension method called in static form (SqlMapper.Query(cnn, sql), XxxExtensions.ExecuteSqlRaw(db, sql)):
        // the first argument is the receiver.
        var skip = invocation.Expression is MemberAccessExpressionSyntax { Expression: var receiver }
            && GetSimpleTypeName(receiver as TypeSyntax) is { } receiverName
            && (receiverName == "SqlMapper" || receiverName.EndsWith("Extensions", StringComparison.Ordinal))
            ? 1
            : 0;
        for (var index = skip; index < arguments.Count; index++)
        {
            if (arguments[index].NameColon == null)
            {
                return arguments[index];
            }
        }

        return null;
    }

    private static bool HasStringArgument(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        foreach (var argument in arguments)
        {
            if (ResolveText(argument.Expression).Text != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStoredProcedureCommandType(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText == "StoredProcedure"
            && GetRightmostName(access.Expression) == "CommandType",
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "StoredProcedure",
        _ => false,
    };

    private static string? GetRightmostName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        _ => null,
    };

    private static SqlText Resolve(ExpressionSyntax expression, int depth, ref int placeholders)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return new SqlText(literal.Token.ValueText, isDynamic: false, isInterpolated: false);
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                return new SqlText(literal.Token.ValueText, isDynamic: false, isInterpolated: false);
            case InterpolatedStringExpressionSyntax interpolated:
                {
                    var builder = new StringBuilder();
                    var dynamic = false;
                    foreach (var content in interpolated.Contents)
                    {
                        if (content is InterpolatedStringTextSyntax text)
                        {
                            builder.Append(text.TextToken.ValueText);
                        }
                        else
                        {
                            builder.Append("@p").Append(placeholders++);
                            dynamic = true;
                        }
                    }

                    return new SqlText(builder.ToString(), dynamic, isInterpolated: true);
                }

            case ParenthesizedExpressionSyntax parenthesized:
                return Resolve(parenthesized.Expression, depth, ref placeholders);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                {
                    var left = Resolve(binary.Left, depth, ref placeholders);
                    var leftText = left.Text ?? "@p" + placeholders++;
                    var right = Resolve(binary.Right, depth, ref placeholders);
                    if (left.Text == null && right.Text == null)
                    {
                        return default;
                    }

                    var rightText = right.Text ?? "@p" + placeholders++;
                    return new SqlText(
                        leftText + rightText,
                        left.IsDynamic || right.IsDynamic || left.Text == null || right.Text == null,
                        left.IsInterpolated || right.IsInterpolated);
                }

            case IdentifierNameSyntax identifier when depth < MaximumResolutionDepth:
                {
                    var initializer = FindInitializer(identifier);
                    return initializer != null ? Resolve(initializer, depth + 1, ref placeholders) : default;
                }

            default:
                return default;
        }
    }

    /// <summary>
    /// Finds the initializer of a local declared before <paramref name="identifier"/> in an enclosing block, or of a
    /// const/readonly field of an enclosing type. Parameters and anything else resolve to null.
    /// </summary>
    private static ExpressionSyntax? FindInitializer(IdentifierNameSyntax identifier)
    {
        var name = identifier.Identifier.ValueText;
        for (var current = identifier.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case BlockSyntax block:
                    foreach (var statement in block.Statements)
                    {
                        if (statement.Span.Contains(identifier.Span))
                        {
                            break;
                        }

                        if (statement is LocalDeclarationStatementSyntax local && FindDeclarator(local.Declaration, name) is { } declarator)
                        {
                            return declarator.Initializer?.Value;
                        }
                    }

                    break;
                case CompilationUnitSyntax unit:
                    foreach (var member in unit.Members)
                    {
                        if (member.Span.Contains(identifier.Span))
                        {
                            break;
                        }

                        if (member is GlobalStatementSyntax { Statement: LocalDeclarationStatementSyntax local }
                            && FindDeclarator(local.Declaration, name) is { } declarator)
                        {
                            return declarator.Initializer?.Value;
                        }
                    }

                    return null;
                case BaseMethodDeclarationSyntax method when HasParameter(method.ParameterList, name):
                    return null;
                case LocalFunctionStatementSyntax localFunction when HasParameter(localFunction.ParameterList, name):
                    return null;
                case TypeDeclarationSyntax type:
                    foreach (var member in type.Members)
                    {
                        if (member is FieldDeclarationSyntax field
                            && (field.Modifiers.Any(SyntaxKind.ConstKeyword) || field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                            && FindDeclarator(field.Declaration, name) is { } declarator)
                        {
                            return declarator.Initializer?.Value;
                        }
                    }

                    break;
            }
        }

        return null;
    }

    private static VariableDeclaratorSyntax? FindDeclarator(VariableDeclarationSyntax declaration, string name)
    {
        foreach (var variable in declaration.Variables)
        {
            if (variable.Identifier.ValueText == name)
            {
                return variable;
            }
        }

        return null;
    }

    private static bool HasParameter(ParameterListSyntax? parameters, string name)
    {
        if (parameters == null)
        {
            return false;
        }

        foreach (var parameter in parameters.Parameters)
        {
            if (parameter.Identifier.ValueText == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// For <c>cmd.ExecuteReader()</c>: finds <c>cmd.CommandText = ...</c>, <c>cmd.CommandType = StoredProcedure</c>,
    /// <c>new XxxCommand("...")</c> or <c>new XxxCommand { CommandText = ... }</c> for the same identifier in the
    /// enclosing member, or a command created inline as the receiver.
    /// </summary>
    private static bool TryFindCommandText(InvocationExpressionSyntax invocation, out SqlText text, out bool isStoredProcedure)
    {
        text = default;
        isStoredProcedure = false;
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return false;
        }

        if (access.Expression is ObjectCreationExpressionSyntax inlineCreation)
        {
            return TryReadCreation(inlineCreation, ref text, ref isStoredProcedure);
        }

        if (access.Expression is not IdentifierNameSyntax receiver)
        {
            return false;
        }

        var name = receiver.Identifier.ValueText;
        var member = invocation.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        var scope = member is null or GlobalStatementSyntax ? invocation.SyntaxTree.GetRoot() : member;
        var found = false;
        foreach (var node in scope.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment
                    when assignment.Left is MemberAccessExpressionSyntax target
                        && target.Expression is IdentifierNameSyntax owner
                        && owner.Identifier.ValueText == name:
                    if (target.Name.Identifier.ValueText == "CommandText")
                    {
                        var assigned = ResolveText(assignment.Right);
                        text = assigned.Text != null ? assigned : new SqlText(string.Empty, isDynamic: true, isInterpolated: false);
                        found = true;
                    }
                    else if (target.Name.Identifier.ValueText == "CommandType" && IsStoredProcedureCommandType(assignment.Right))
                    {
                        isStoredProcedure = true;
                        found = true;
                    }

                    break;
                case AssignmentExpressionSyntax assignment
                    when assignment.Left is IdentifierNameSyntax owner
                        && owner.Identifier.ValueText == name
                        && assignment.Right is BaseObjectCreationExpressionSyntax creation:
                    found |= TryReadCreation(creation, ref text, ref isStoredProcedure);
                    break;
                case VariableDeclaratorSyntax declarator
                    when declarator.Identifier.ValueText == name
                        && declarator.Initializer?.Value is BaseObjectCreationExpressionSyntax creation:
                    found |= TryReadCreation(creation, ref text, ref isStoredProcedure);
                    break;
            }
        }

        return found;
    }

    private static bool TryReadCreation(BaseObjectCreationExpressionSyntax creation, ref SqlText text, ref bool isStoredProcedure)
    {
        var found = false;
        var first = creation.ArgumentList?.Arguments.Count > 0 ? creation.ArgumentList.Arguments[0] : null;
        if (first != null && ResolveText(first.Expression) is { Text: not null } constructorText)
        {
            text = constructorText;
            found = true;
        }

        if (creation.Initializer != null)
        {
            foreach (var expression in creation.Initializer.Expressions)
            {
                if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax property } assignment)
                {
                    if (property.Identifier.ValueText == "CommandText")
                    {
                        var assigned = ResolveText(assignment.Right);
                        text = assigned.Text != null ? assigned : new SqlText(string.Empty, isDynamic: true, isInterpolated: false);
                        found = true;
                    }
                    else if (property.Identifier.ValueText == "CommandType" && IsStoredProcedureCommandType(assignment.Right))
                    {
                        isStoredProcedure = true;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private static bool HasSkipContractCheckAttribute(SyntaxNode node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            var attributeLists = current switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                LocalFunctionStatementSyntax localFunction => localFunction.AttributeLists,
                _ => default,
            };

            foreach (var list in attributeLists)
            {
                foreach (var attribute in list.Attributes)
                {
                    var name = GetSimpleTypeName(attribute.Name);
                    if (name is "SkipContractCheck" or "SkipContractCheckAttribute")
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
