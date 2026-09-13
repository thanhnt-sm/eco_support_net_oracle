// <copyright file="CodeFixProviders.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.CodeFixes;

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using DataGuard.Analyzers;

/// <summary>
/// Code fix provider for DataGuard diagnostics.
/// Provides quick-fix suggestions in IDE for common contract violations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DataGuardCodeFixProvider))]
[Shared]
public class DataGuardCodeFixProvider : CodeFixProvider
{
    /// <summary>Diagnostic property containing verifier-approved replacement SQL.</summary>
    public const string VerifiedSqlReplacementProperty = "DataGuard.VerifiedSqlReplacement";

    /// <summary>Diagnostic property containing the SHA-256 digest of the authoritative manifest.</summary>
    public const string ManifestDigestProperty = "DataGuard.ManifestDigest";

    /// <summary>Diagnostic property containing a verifier-approved positive maximum length.</summary>
    public const string VerifiedMaxLengthProperty = "DataGuard.VerifiedMaxLength";

    /// <summary>Gets the diagnostic IDs this provider can fix.</summary>
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(
            DiagnosticIds.ParameterMismatch,
            DiagnosticIds.UnvalidatedSqlCall);

    /// <summary>Gets the batch fix-all provider.</summary>
    /// <returns>The batch fix-all provider.</returns>
    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    /// <summary>Registers code fixes for the current diagnostic context.</summary>
    /// <param name="context">The code fix context carrying the diagnostic and document.</param>
    /// <returns>A task that represents the asynchronous registration.</returns>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Register appropriate fix based on diagnostic ID
        switch (diagnostic.Id)
        {
            case DiagnosticIds.UnvalidatedSqlCall:
                this.RegisterUnvalidatedSqlCallFixes(context, diagnostic, root);
                break;
            case DiagnosticIds.ParameterMismatch:
                this.RegisterParameterMismatchFixes(context, diagnostic, root);
                break;
        }
    }

    private static AttributeListSyntax CreateSkipContractCheckAttribute(string reason)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.SkipContractCheck"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(reason)))
                    .WithNameEquals(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName("Reason"))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static AttributeListSyntax CreateDataContractAttribute()
        => SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.DataContract"))));

    private static AttributeListSyntax CreateSqlParameterAttribute()
        => SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
            SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.SqlParameter"))));

    private static AttributeListSyntax CreateExpectedSpParameterAttribute(string name)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.ExpectedSpParameter"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList<AttributeArgumentSyntax>(new[]
                {
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name))),
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(string.Empty))),
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(string.Empty))),
                })));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static AttributeListSyntax CreateColumnAttribute(string name)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.ComponentModel.DataAnnotations.Schema.Column"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static AttributeListSyntax CreateMaxLengthAttribute(int length)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.ComponentModel.DataAnnotations.MaxLength"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(length))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static string ToSnakeCase(string pascalCase)
        => DataGuard.Contracts.NameConventions.ToSnakeCase(pascalCase);

    private static string ToPascalCase(string snakeCase)
        => DataGuard.Contracts.NameConventions.ToPascalCase(snakeCase);

    private void RegisterUnvalidatedSqlCallFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Fix 1: Add [SkipContractCheck] attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [SkipContractCheck] attribute",
                c => this.AddSkipContractCheckAttributeAsync(context.Document, root!, node, c),
                "DataGuard.AddSkipContractCheck"),
            diagnostic);

        if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add [DataContract] attribute",
                    c => this.AddDataContractAttributeAsync(context.Document, root, node, c),
                    "DataGuard.AddDataContract"),
                diagnostic);
        }

        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { ParameterList.Parameters.Count: > 0 })
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add [SqlParameter] attributes",
                    c => AddSqlParameterAttributesAsync(context.Document, root, node, c),
                    "DataGuard.AddSqlParameters"),
                diagnostic);
        }
    }

    private void RegisterParameterMismatchFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        if (!TryGetVerifiedSqlReplacement(diagnostic, out var replacement))
        {
            return;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var literal = FindStringLiteral(root, diagnostic.Location.SourceSpan);
        if (literal == null || literal.Kind() != SyntaxKind.StringLiteralExpression)
        {
            return;
        }

        // A verifier has bound this replacement to a hash-identified offline manifest.
        context.RegisterCodeFix(
            CodeAction.Create(
                "Apply verifier-approved SQL replacement",
                c => this.ReplaceVerifiedSqlAsync(context.Document, root!, node, replacement, c),
                "DataGuard.UpdateSql"),
            diagnostic);
    }

    // Implementation helpers
    private async Task<Document> AddSkipContractCheckAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var target = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (target == null)
        {
            return document;
        }

        editor.AddAttribute(target, CreateSkipContractCheckAttribute("Dynamic SQL - manual review required"));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddDataContractAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null || type.AttributeLists.SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString().EndsWith("DataContract", StringComparison.Ordinal)))
        {
            return document;
        }

        editor.AddAttribute(type, CreateDataContractAttribute());
        return editor.GetChangedDocument();
    }

    private static Task<Document> AddSqlParameterAttributesAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null)
        {
            return Task.FromResult(document);
        }

        var parameters = method.ParameterList.Parameters.Select(parameter =>
        {
            var alreadyDeclared = parameter.AttributeLists.SelectMany(list => list.Attributes)
                .Any(attribute => attribute.Name.ToString().EndsWith("SqlParameter", StringComparison.Ordinal));
            return alreadyDeclared
                ? parameter
                : parameter.WithAttributeLists(parameter.AttributeLists.Add(CreateSqlParameterAttribute()));
        });
        var replacement = method.WithParameterList(method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(parameters)));
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(method, replacement)));
    }

    private static bool TryGetVerifiedSqlReplacement(Diagnostic diagnostic, out string replacement)
    {
        replacement = string.Empty;
        if (!diagnostic.Properties.TryGetValue(ManifestDigestProperty, out var digest)
            || digest == null
            || digest.Length != 64
            || !digest.All(Uri.IsHexDigit))
        {
            return false;
        }

        if (!diagnostic.Properties.TryGetValue(VerifiedSqlReplacementProperty, out var proposed)
            || string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        if (proposed!.Length > 65_536)
        {
            return false;
        }

        replacement = proposed;
        return true;
    }

    private async Task<Document> ReplaceVerifiedSqlAsync(Document document, SyntaxNode root, SyntaxNode node, string replacement, CancellationToken c)
    {
        var literal = FindStringLiteral(root, node.Span);
        if (literal == null || literal.Kind() != SyntaxKind.StringLiteralExpression)
        {
            return document;
        }

        var replacementLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(replacement)).WithTriviaFrom(literal);
        return document.WithSyntaxRoot(root.ReplaceNode(literal, replacementLiteral));
    }

    private static LiteralExpressionSyntax? FindStringLiteral(SyntaxNode root, TextSpan span)
        => root.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.Span.Contains(span) || span.Contains(candidate.Span));
}

/// <summary>
/// Code fix for adding missing MaxLength attribute.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddMaxLengthAttributeFixProvider))]
[Shared]
public class AddMaxLengthAttributeFixProvider : CodeFixProvider
{
    /// <summary>Gets the diagnostic IDs this provider can fix.</summary>
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.LengthExceedsColumn, DiagnosticIds.InferredSizeFallback);

    /// <summary>Gets the batch fix-all provider.</summary>
    /// <returns>The batch fix-all provider.</returns>
    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    /// <summary>Registers code fixes for the current diagnostic context.</summary>
    /// <param name="context">The code fix context carrying the diagnostic and document.</param>
    /// <returns>A task that represents the asynchronous registration.</returns>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        if (!TryGetVerifiedMaxLength(diagnostic, out var length))
        {
            return;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        context.RegisterCodeFix(
            CodeAction.Create(
                "Apply verifier-approved maximum length",
                c => this.ApplyMaxLengthAsync(context.Document, root!, node, length, c),
                "DataGuard.ApplyVerifiedMaxLength"),
            diagnostic);
    }

    private static bool TryGetVerifiedMaxLength(Diagnostic diagnostic, out int length)
    {
        length = 0;
        return diagnostic.Properties.TryGetValue(DataGuardCodeFixProvider.ManifestDigestProperty, out var digest)
            && digest is { Length: 64 } && digest.All(Uri.IsHexDigit)
            && diagnostic.Properties.TryGetValue(DataGuardCodeFixProvider.VerifiedMaxLengthProperty, out var value)
            && int.TryParse(value, out length) && length > 0;
    }

    private async Task<Document> ApplyMaxLengthAsync(Document document, SyntaxNode root, SyntaxNode node, int length, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property is null)
        {
            return document;
        }
        var existing = property.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attribute =>
            attribute.Name.ToString().EndsWith("MaxLength", StringComparison.Ordinal) || attribute.Name.ToString().EndsWith("StringLength", StringComparison.Ordinal));
        var replacement = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.ComponentModel.DataAnnotations.MaxLength"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(length))))));
        if (existing is null)
        {
            editor.AddAttribute(property, SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(replacement)));
        }
        else
        {
            editor.ReplaceNode(existing, replacement.WithTriviaFrom(existing));
        }
        return editor.GetChangedDocument();
    }
}

/// <summary>
/// Code fix for adding [SkipContractCheck] attribute.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SkipContractCheckFixProvider))]
[Shared]
public class SkipContractCheckFixProvider : CodeFixProvider
{
    /// <summary>Gets the diagnostic IDs this provider can fix.</summary>
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.UnvalidatedSqlCall);

    /// <summary>Gets the batch fix-all provider.</summary>
    /// <returns>The batch fix-all provider.</returns>
    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    /// <summary>Registers code fixes for the current diagnostic context.</summary>
    /// <param name="context">The code fix context carrying the diagnostic and document.</param>
    /// <returns>A task that represents the asynchronous registration.</returns>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [SkipContractCheck] attribute (dynamic SQL)",
                c => this.AddSkipAttributeAsync(context.Document, root!, node, c),
                "DataGuard.SkipContractCheck"),
            diagnostic);
    }

    private async Task<Document> AddSkipAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var target = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (target == null)
        {
            return document;
        }

        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.SkipContractCheck"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("Dynamic SQL - manual review required"))))));
        editor.AddAttribute(target, SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr)));
        return editor.GetChangedDocument();
    }
}

/// <summary>
/// Code fix for property names that do not meet the configured naming convention.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NamingConventionFixProvider))]
[Shared]
public sealed class NamingConventionFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.NamingConvention);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var node = root.FindNode(context.Diagnostics.First().Location.SourceSpan);
        var property = node as PropertyDeclarationSyntax
            ?? node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Auto-fix naming convention",
                cancellationToken => RenameAsync(context.Document, property, cancellationToken),
                "DataGuard.FixNamingConvention"),
            context.Diagnostics.First());
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [Column] attribute with explicit name",
                cancellationToken => AddColumnAttributeAsync(context.Document, property, cancellationToken),
                "DataGuard.AddColumnAttribute"),
            context.Diagnostics.First());
    }

    private static async Task<Document> RenameAsync(Document document, PropertyDeclarationSyntax property, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var symbol = semanticModel?.GetDeclaredSymbol(property, cancellationToken);
        if (symbol == null)
        {
            return document;
        }

        var newName = DataGuard.Contracts.NameConventions.ToPascalCase(property.Identifier.ValueText);
        if (newName == property.Identifier.ValueText)
        {
            return document;
        }

        var solution = await Renamer.RenameSymbolAsync(
            document.Project.Solution, symbol, default(SymbolRenameOptions), newName, cancellationToken).ConfigureAwait(false);
        return solution.GetDocument(document.Id) ?? document;
    }

    private static async Task<Document> AddColumnAttributeAsync(Document document, PropertyDeclarationSyntax property, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var name = DataGuard.Contracts.NameConventions.ToSnakeCase(property.Identifier.ValueText);
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.ComponentModel.DataAnnotations.Schema.Column"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name))))));
        editor.AddAttribute(property, SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));
        return editor.GetChangedDocument();
    }
}

/// <summary>
/// Code fix for verified provider option mismatches.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseOracleCodeFixProvider))]
[Shared]
public sealed class UseOracleCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.ProviderOptionMismatch);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return;
        }

        var node = root.FindNode(context.Diagnostics.First().Location.SourceSpan);
        var scope = (SyntaxNode?)node.FirstAncestorOrSelf<MethodDeclarationSyntax>()
            ?? node.FirstAncestorOrSelf<StatementSyntax>();
        var useSqlServer = scope?.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation => invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == "UseSqlServer");
        if (useSqlServer == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Replace .UseSqlServer() with .UseOracle()",
                cancellationToken => ReplaceAsync(context.Document, root, useSqlServer, cancellationToken),
                "DataGuard.AddUseOracle"),
            context.Diagnostics.First());
    }

    private static Task<Document> ReplaceAsync(Document document, SyntaxNode root, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        var replacement = invocation.WithExpression(access.WithName(SyntaxFactory.IdentifierName("UseOracle")));
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement)));
    }
}
