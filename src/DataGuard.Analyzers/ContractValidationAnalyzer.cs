// <copyright file="ContractValidationAnalyzer.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DataGuard.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// IDE layer: syntax-only heuristics over the literal SQL of each recognised SQL call (no symbol binding, no
/// database, no IO). Reports DG099 (injection markers; concatenation/interpolation into a raw SQL API), DG098
/// (SELECT without FROM), DG097 (stored-procedure command text form), DG017 (SELECT *) and DG004 (literal SELECT
/// list vs. the properties of the mapped type, when that type is declared in this compilation). Every diagnostic is
/// reported at the SQL argument. Database-backed checks (DG002-DG016) run in the CLI rules engine; their descriptors
/// are advertised here so .editorconfig severities and code fixes bind to the same IDs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractValidationAnalyzer : DiagnosticAnalyzer
{
    private const string MissingFromMessage = "Raw SQL query missing FROM clause";
    private const string InjectionPatternMessage = "Potential SQL injection pattern detected";
    private const string SelectStarMessage = "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.";

    /// <summary>Gets the diagnostics supported by this analyzer.</summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticDescriptors.AnalyzerDescriptors;

    /// <summary>Registers the syntax-node callback for invocation expressions.</summary>
    /// <param name="context">Analysis context.</param>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // The type-shape index is per compilation and lazy: it is built (syntax only, declarations only) the first
        // time a SQL call has a target type, so edits without SQL calls never pay for it.
        context.RegisterCompilationStartAction(start =>
        {
            var trees = start.Compilation.SyntaxTrees;
            var shapes = new Lazy<TypeShapeIndex>(() => TypeShapeIndex.Build(trees), LazyThreadSafetyMode.ExecutionAndPublication);
            start.RegisterSyntaxNodeAction(nodeContext => AnalyzeInvocation(nodeContext, shapes), SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, Lazy<TypeShapeIndex> shapes)
    {
        if (!SqlCallSyntax.IsCandidate(context.Node))
        {
            return;
        }

        var invocation = (InvocationExpressionSyntax)context.Node;
        var call = SqlCallSyntax.Classify(invocation, context.CancellationToken);
        if (call?.SqlArgument == null || call.Kind == SqlCallType.AdoCommand)
        {
            return;
        }

        var location = call.SqlArgument.Expression.GetLocation();
        var sql = call.Sql.Text;

        // DG099: injection markers in the text, or values spliced into a raw (non-parameterizing) SQL API.
        if (sql != null && SqlTextHeuristics.ContainsInjectionPattern(sql))
        {
            Report(context, DiagnosticDescriptors.SqlInjectionPattern, location, InjectionPatternMessage);
        }
        else if (call.Sql.IsDynamic && !call.IsParameterizingApi)
        {
            Report(
                context,
                DiagnosticDescriptors.SqlInjectionPattern,
                location,
                $"Potential SQL injection: SQL text passed to '{call.Method}' is built by string concatenation or interpolation; pass values as parameters");
        }

        if (sql == null)
        {
            return;
        }

        // DG097: stored-procedure command text form.
        if (call.IsStoredProcedureCommand && SqlTextHeuristics.HasProcedurePrefix(sql))
        {
            Report(
                context,
                DiagnosticDescriptors.StoredProcedureCommandText,
                location,
                "CommandType.StoredProcedure expects a bare procedure name; remove the EXEC/EXECUTE/CALL prefix");
        }
        else if (!call.IsStoredProcedureCommand && !call.Sql.IsDynamic && SqlTextHeuristics.IsBareProcedureCall(sql))
        {
            Report(context, DiagnosticDescriptors.StoredProcedureCommandText, location, "Stored procedure call must start with EXEC or EXECUTE");
        }

        if (call.IsStoredProcedureCommand)
        {
            return;
        }

        // DG098: SELECT without FROM.
        if (SqlTextHeuristics.IsSelectWithoutFrom(sql))
        {
            Report(context, DiagnosticDescriptors.MissingFromClause, location, MissingFromMessage);
        }

        // DG017 / DG004 against the mapped type(s), when declared in this compilation.
        var targetTypes = GetTargetTypeNames(call, shapes);
        if (SqlTextHeuristics.ContainsSelectStar(sql))
        {
            var properties = ImmutableDictionary<string, string?>.Empty;
            if (targetTypes.Count > 0)
            {
                var explicitColumns = targetTypes.SelectMany(type => shapes.Value.GetColumns(type)).Select(column => column.EffectiveName).ToList();
                if (explicitColumns.Count > 0)
                {
                    properties = properties.Add("ExplicitColumns", string.Join(", ", explicitColumns));
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.SelectStarUsage, location, properties, SelectStarMessage));
            return;
        }

        if (targetTypes.Count > 0 && !call.Sql.IsDynamic)
        {
            ValidateShape(context, location, sql, targetTypes.SelectMany(type => shapes.Value.GetColumns(type)).ToList());
        }
    }

    private static void ValidateShape(SyntaxNodeAnalysisContext context, Location location, string sql, IReadOnlyList<MappedColumn> mapped)
    {
        if (mapped.Count == 0)
        {
            return;
        }

        var columnNames = SqlTextHeuristics.ExtractColumnNames(sql);
        if (columnNames.Count == 0)
        {
            return;
        }

        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var column in mapped)
        {
            var candidates = new[] { column.PropertyName, NameConventions.ToSnakeCase(column.PropertyName), column.ColumnName };
            foreach (var candidate in candidates)
            {
                if (candidate != null)
                {
                    knownNames.Add(candidate);
                }
            }

            if (!candidates.Any(candidate => candidate != null && columnNames.Contains(candidate)))
            {
                missing.Add(column.PropertyName);
            }
        }

        if (missing.Count > 0)
        {
            Report(
                context,
                DiagnosticDescriptors.ColumnShapeMismatch,
                location,
                $"Result set is missing required columns: {string.Join(", ", missing.Take(5))}");
        }

        var extra = columnNames.Where(column => !knownNames.Contains(column)).ToList();
        if (extra.Count > 0)
        {
            Report(
                context,
                DiagnosticDescriptors.ColumnShapeMismatch,
                location,
                $"Result set has {extra.Count} extra columns not mapped to entity properties: {string.Join(", ", extra.Take(5))}");
        }
    }

    /// <summary>
    /// Returns the simple names of the type(s) the SQL result maps to: explicit method type arguments
    /// (<c>Query&lt;T&gt;</c>, multi-mapping <c>Query&lt;T1, T2, TReturn&gt;</c> → T1, T2; <c>FromSqlRaw&lt;T&gt;</c>,
    /// <c>SqlQueryRaw&lt;T&gt;</c>), else for EF Core the <c>DbSet&lt;T&gt;</c> receiver found syntactically.
    /// </summary>
    private static IReadOnlyList<string> GetTargetTypeNames(SqlCall call, Lazy<TypeShapeIndex> shapes)
    {
        var names = new List<string>();
        if (call.Kind == SqlCallType.Dapper && !call.Method.StartsWith("Query", StringComparison.Ordinal))
        {
            return names;
        }

        if (call.Kind is not (SqlCallType.Dapper or SqlCallType.EfCore))
        {
            return names;
        }

        if (SqlCallSyntax.GetMethodNameSyntax(call.Invocation) is GenericNameSyntax generic)
        {
            var arguments = generic.TypeArgumentList.Arguments;
            var count = call.Kind == SqlCallType.Dapper && arguments.Count > 1 ? arguments.Count - 1 : Math.Min(1, arguments.Count);
            for (var index = 0; index < count; index++)
            {
                if (SqlCallSyntax.GetSimpleTypeName(arguments[index]) is { } name)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        if (call.Kind == SqlCallType.EfCore && call.Invocation.Expression is MemberAccessExpressionSyntax access
            && DbSetReceiver.GetEntityTypeName(access.Expression, shapes) is { } entity)
        {
            names.Add(entity);
        }

        return names;
    }

    private static void Report(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor, Location location, string message) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));

    /// <summary>Syntactic resolution of the entity type of a <c>DbSet&lt;T&gt;</c> receiver.</summary>
    private static class DbSetReceiver
    {
        public static string? GetEntityTypeName(ExpressionSyntax receiver, Lazy<TypeShapeIndex> shapes) => receiver switch
        {
            // db.Orders → the DbSet<T> property named Orders declared in the compilation.
            MemberAccessExpressionSyntax member => shapes.Value.GetDbSetEntity(member.Name.Identifier.ValueText),

            // db.Set<Order>()
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "Set" } set } } =>
                set.TypeArgumentList.Arguments.Count == 1 ? SqlCallSyntax.GetSimpleTypeName(set.TypeArgumentList.Arguments[0]) : null,

            // orders (parameter, local or field typed DbSet<T>)
            IdentifierNameSyntax identifier => FromDeclaredType(identifier),
            _ => null,
        };

        private static string? FromDeclaredType(IdentifierNameSyntax identifier)
        {
            var name = identifier.Identifier.ValueText;
            for (var current = identifier.Parent; current != null; current = current.Parent)
            {
                TypeSyntax? declared = current switch
                {
                    BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.FirstOrDefault(p => p.Identifier.ValueText == name)?.Type,
                    LocalFunctionStatementSyntax local => local.ParameterList.Parameters.FirstOrDefault(p => p.Identifier.ValueText == name)?.Type,
                    BlockSyntax block => block.Statements.OfType<LocalDeclarationStatementSyntax>()
                        .Where(statement => statement.SpanStart < identifier.SpanStart)
                        .Select(statement => statement.Declaration)
                        .FirstOrDefault(declaration => declaration.Variables.Any(v => v.Identifier.ValueText == name))?.Type,
                    TypeDeclarationSyntax type => type.Members.OfType<FieldDeclarationSyntax>()
                        .Select(field => field.Declaration)
                        .FirstOrDefault(declaration => declaration.Variables.Any(v => v.Identifier.ValueText == name))?.Type
                        ?? type.Members.OfType<PropertyDeclarationSyntax>().FirstOrDefault(p => p.Identifier.ValueText == name)?.Type,
                    _ => null,
                };

                if (declared is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic
                    && generic.Identifier.ValueText is "DbSet" or "IQueryable")
                {
                    return SqlCallSyntax.GetSimpleTypeName(generic.TypeArgumentList.Arguments[0]);
                }

                if (declared != null)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
