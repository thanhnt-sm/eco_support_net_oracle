// <copyright file="UnvalidatedSqlCallGenerator.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers;

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// IDE layer: an incremental generator that reports DG001 at every SQL call site so the developer knows it still
/// needs <c>dataguard check</c>. It is syntax-only: the predicate filters invocations by method name, the transform
/// classifies the call with <see cref="SqlCallSyntax"/> (no symbol binding, no IO) and produces an equatable
/// <see cref="SqlCallModel"/> that holds no syntax node or <see cref="Location"/>, so unchanged call sites are
/// served from the generator cache on the next keystroke.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class UnvalidatedSqlCallGenerator : IIncrementalGenerator
{
    /// <summary>Initializes the syntax pipeline for DG001.</summary>
    /// <param name="context">Generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var calls = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => SqlCallSyntax.IsCandidate(node),
                transform: static (syntaxContext, cancellationToken) => CreateModel(syntaxContext.Node, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName(ModelTrackingName);

        // Only this last step sees the compilation: it maps the model's path back to its syntax tree so the diagnostic
        // has an in-source location (squiggle, #pragma, code fixes). It is a dictionary lookup per call site; the
        // syntax classification above stays cached across keystrokes.
        var trees = context.CompilationProvider.Select(static (compilation, _) => new SyntaxTreeMap(compilation));
        context.RegisterSourceOutput(calls.Combine(trees), static (output, pair) =>
            output.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UnvalidatedSqlCall,
                pair.Left.ToLocation(pair.Right.Find(pair.Left.Path)),
                pair.Left.Method)));
    }

    /// <summary>Tracking name of the cached <see cref="SqlCallModel"/> step (used by incremental-caching tests).</summary>
    internal const string ModelTrackingName = "DataGuard.SqlCallModels";

    /// <summary>Returns whether <paramref name="node"/> is a SQL call site DG001 reports (syntax only).</summary>
    /// <param name="node">Syntax node.</param>
    /// <returns>True when the generator reports DG001 for the node.</returns>
    internal static bool IsPotentialSqlCall(SyntaxNode node) =>
        SqlCallSyntax.IsCandidate(node) && CreateModel(node, CancellationToken.None) is not null;

    /// <summary>Returns whether a <c>// DataGuard:</c> marker comment suppresses diagnostics for <paramref name="node"/>.</summary>
    /// <param name="node">Syntax node.</param>
    /// <returns>True when suppressed.</returns>
    internal static bool HasDataGuardMarkerComment(SyntaxNode node) => SqlCallSyntax.HasDataGuardMarkerComment(node);

    private static SqlCallModel? CreateModel(SyntaxNode node, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)node;
        var call = SqlCallSyntax.Classify(invocation, cancellationToken);
        if (call == null)
        {
            return null;
        }

        var tree = invocation.SyntaxTree;
        var span = invocation.Span;
        return new SqlCallModel(
            tree.FilePath,
            span.Start,
            span.Length,
            call.Method,
            call.Kind,
            call.Sql.Text ?? string.Empty,
            invocation.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>()?.Identifier.ValueText,
            tree.GetLineSpan(span, cancellationToken).Span);
    }
}

/// <summary>
/// Equatable, cache-friendly DG001 model: file path, span, method name, call family, recovered SQL text and the
/// simple name of the containing type. <see cref="LineSpan"/> (a value type) is carried so the diagnostic location
/// can be rebuilt without the syntax tree.
/// </summary>
/// <param name="Path">Syntax tree file path.</param>
/// <param name="Start">Invocation span start.</param>
/// <param name="Length">Invocation span length.</param>
/// <param name="Method">Invoked method name (diagnostic argument).</param>
/// <param name="Kind">SQL call family.</param>
/// <param name="Sql">SQL text recovered syntactically (empty when unknown).</param>
/// <param name="ContainingType">Simple name of the type declaring the call site, if any.</param>
/// <param name="LineSpan">Line/character span of the invocation.</param>
internal sealed record SqlCallModel(
    string Path,
    int Start,
    int Length,
    string Method,
    SqlCallType Kind,
    string Sql,
    string? ContainingType,
    LinePositionSpan LineSpan)
{
    /// <summary>Rebuilds the diagnostic location.</summary>
    /// <param name="tree">The syntax tree at <see cref="Path"/>, if known.</param>
    /// <returns>An in-source location when the tree is known, otherwise a file location with the same span.</returns>
    public Location ToLocation(SyntaxTree? tree) => tree != null && Start + Length <= tree.Length
        ? Location.Create(tree, new TextSpan(Start, Length))
        : Location.Create(Path, new TextSpan(Start, Length), LineSpan);
}

/// <summary>Path → syntax tree map of one compilation, built lazily on first lookup.</summary>
internal sealed class SyntaxTreeMap
{
    private readonly Compilation compilation;
    private Dictionary<string, SyntaxTree>? map;

    public SyntaxTreeMap(Compilation compilation)
    {
        this.compilation = compilation;
    }

    public SyntaxTree? Find(string path)
    {
        var trees = map;
        if (trees == null)
        {
            trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
            foreach (var tree in compilation.SyntaxTrees)
            {
                trees[tree.FilePath] = tree;
            }

            map = trees;
        }

        return trees.TryGetValue(path, out var found) ? found : null;
    }
}
