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
using DataGuard.Analyzers;

namespace DataGuard.Analyzers.CodeFixes;

/// <summary>
/// Code fix provider for DataGuard diagnostics.
/// Provides quick-fix suggestions in IDE for common contract violations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DataGuardCodeFixProvider)), Shared]
public class DataGuardCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(
            DiagnosticIds.ParameterMismatch,
            DiagnosticIds.DirectionMismatch,
            DiagnosticIds.ColumnShapeMismatch,
            DiagnosticIds.NullableMismatch,
            DiagnosticIds.NamingConvention,
            DiagnosticIds.LengthExceedsColumn,
            DiagnosticIds.ByteLengthOverflow,
            DiagnosticIds.InferredSizeFallback,
            DiagnosticIds.OracleSyntaxInNonOracle,
            DiagnosticIds.NonOracleFunctionInOracle,
            DiagnosticIds.ProviderOptionMismatch,
            DiagnosticIds.SqlServerSyntaxLeak,
            DiagnosticIds.UnmappedTypeUsage,
            DiagnosticIds.UnvalidatedSqlCall);

    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Register appropriate fix based on diagnostic ID
        switch (diagnostic.Id)
        {
            case DiagnosticIds.UnvalidatedSqlCall:
                RegisterUnvalidatedSqlCallFixes(context, diagnostic, root!);
                break;
            case DiagnosticIds.ParameterMismatch:
                RegisterParameterMismatchFixes(context, diagnostic, root!);
                break;
            case DiagnosticIds.NamingConvention:
                RegisterNamingConventionFixes(context, diagnostic, root!);
                break;
            case DiagnosticIds.OracleSyntaxInNonOracle:
            case DiagnosticIds.NonOracleFunctionInOracle:
            case DiagnosticIds.SqlServerSyntaxLeak:
                RegisterDialectFixes(context, diagnostic, root!);
                break;
            case DiagnosticIds.LengthExceedsColumn:
                RegisterLengthFixes(context, diagnostic, root!);
                break;
            case DiagnosticIds.ProviderOptionMismatch:
                RegisterProviderOptionFixes(context, diagnostic, root!);
                break;
        }
    }

    private void RegisterUnvalidatedSqlCallFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        
        // Fix 1: Add [SkipContractCheck] attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [SkipContractCheck] attribute",
                c => AddSkipContractCheckAttributeAsync(context.Document, root!, node, c),
                "DataGuard.AddSkipContractCheck"),
            diagnostic);

        // Fix 2: Add expected parameter attributes
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add expected parameter attributes",
                c => AddExpectedParameterAttributesAsync(context.Document, root!, node, c),
                "DataGuard.AddExpectedParameters"),
            diagnostic);

        // Fix 3: Run full validation in CI (mark as intentional)
        context.RegisterCodeFix(
            CodeAction.Create(
                "Mark as 'validate in CI only' (add comment)",
                c => AddCiOnlyCommentAsync(context.Document, root!, node, c),
                "DataGuard.MarkCiOnly"),
            diagnostic);
    }

    private void RegisterParameterMismatchFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        
        // Fix: Add expected parameter attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [ExpectedSpParameter] attribute",
                c => AddExpectedSpParameterAsync(context.Document, root!, node, c),
                "DataGuard.AddExpectedSpParameter"),
            diagnostic);

        // Fix: Update SQL to match expected parameters
        context.RegisterCodeFix(
            CodeAction.Create(
                "Update SQL to match expected parameters",
                c => UpdateSqlToMatchParametersAsync(context.Document, root!, node, c),
                "DataGuard.UpdateSql"),
            diagnostic);
    }

    private void RegisterNamingConventionFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        
        // Fix: Auto-rename to match convention
        context.RegisterCodeFix(
            CodeAction.Create(
                "Auto-fix naming convention",
                c => FixNamingConventionAsync(context.Document, root!, node, c),
                "DataGuard.FixNamingConvention"),
            diagnostic);

        // Fix: Add explicit column mapping attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [Column] attribute with explicit name",
                c => AddColumnAttributeAsync(context.Document, root!, node, c),
                "DataGuard.AddColumnAttribute"),
            diagnostic);
    }

    private void RegisterDialectFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Fix: Add a manual conversion note (non-destructive; dialect conversion needs a real SQL parser).
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add manual dialect conversion note",
                c => AddDialectCommentAsync(context.Document, root!, node, c),
                "DataGuard.AddDialectNote"),
            diagnostic);
    }

    private void RegisterLengthFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        
        // Fix: Add MaxLength attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [MaxLength] attribute",
                c => AddMaxLengthAttributeAsync(context.Document, root!, node, c),
                "DataGuard.AddMaxLength"),
            diagnostic);

        // Fix: Change column type to CLOB/NCLOB
        context.RegisterCodeFix(
            CodeAction.Create(
                "Change column type to CLOB/NCLOB",
                c => SuggestClobTypeAsync(context.Document, root!, node, c),
                "DataGuard.SuggestClob"),
            diagnostic);
    }

    private void RegisterProviderOptionFixes(CodeFixContext context, Diagnostic diagnostic, SyntaxNode root)
    {
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        
        // Fix: Add UseOracle() to DbContext
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add .UseOracle() to DbContextOptions",
                c => AddUseOracleAsync(context.Document, root!, node, c),
                "DataGuard.AddUseOracle"),
            diagnostic);
    }

    // Implementation helpers
    private async Task<Document> AddSkipContractCheckAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var target = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (target == null) return document;

        editor.AddAttribute(target, CreateSkipContractCheckAttribute("Dynamic SQL - manual review required"));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddExpectedParameterAttributesAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return document;

        var attributes = method.ParameterList.Parameters.Select(p => CreateExpectedSpParameterAttribute(p.Identifier.ValueText));
        editor.ReplaceNode(method, method.WithAttributeLists(method.AttributeLists.AddRange(attributes)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddCiOnlyCommentAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var target = node.FirstAncestorOrSelf<StatementSyntax>();
        if (target == null) return document;

        var comment = SyntaxFactory.Comment("// DataGuard: Validate in CI only");
        editor.ReplaceNode(target, target.WithLeadingTrivia(target.GetLeadingTrivia().Add(comment)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddExpectedSpParameterAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return document;

        var attributes = method.ParameterList.Parameters.Select(p => CreateExpectedSpParameterAttribute(p.Identifier.ValueText));
        editor.ReplaceNode(method, method.WithAttributeLists(method.AttributeLists.AddRange(attributes)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> UpdateSqlToMatchParametersAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return document;

        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return document;

        // Do not rewrite the SQL literal: attach a review note as leading trivia instead.
        var paramList = string.Join(", ", method.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
        var comment = SyntaxFactory.Comment($"// DataGuard: verify SQL parameters match: {paramList}");
        editor.ReplaceNode(invocation, invocation.WithLeadingTrivia(invocation.GetLeadingTrivia().Add(comment)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> FixNamingConventionAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        var semanticModel = await document.GetSemanticModelAsync(c).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var symbol = semanticModel.GetDeclaredSymbol(property, c);
        if (symbol == null) return document;

        // Apply snake_case to PascalCase renaming (identity when already PascalCase).
        var newName = ToPascalCase(property.Identifier.ValueText);
        if (newName == property.Identifier.ValueText) return document;

        var solution = await Renamer.RenameSymbolAsync(
            document.Project.Solution, symbol, new SymbolRenameOptions(), newName, c).ConfigureAwait(false);

        return solution.GetDocument(document.Id) ?? document;
    }

    private async Task<Document> AddColumnAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        editor.AddAttribute(property, CreateColumnAttribute(ToSnakeCase(property.Identifier.ValueText)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddDialectCommentAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var target = (SyntaxNode?)node.FirstAncestorOrSelf<LiteralExpressionSyntax>()
            ?? node.FirstAncestorOrSelf<StatementSyntax>();
        if (target == null) return document;

        var comment = SyntaxFactory.Comment("// DataGuard: convert this SQL to the target dialect manually");
        editor.ReplaceNode(target, target.WithLeadingTrivia(target.GetLeadingTrivia().Add(comment)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddMaxLengthAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        editor.AddAttribute(property, CreateMaxLengthAttribute(2000));
        return editor.GetChangedDocument();
    }

    private async Task<Document> SuggestClobTypeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        var comment = SyntaxFactory.Comment("// DataGuard: consider mapping this property to NCLOB/CLOB");
        editor.ReplaceNode(property, property.WithLeadingTrivia(property.GetLeadingTrivia().Add(comment)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddUseOracleAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);

        // Replace UseSqlServer(...) with UseOracle(...), keeping the existing connection string argument.
        var scope = (SyntaxNode?)node.FirstAncestorOrSelf<MethodDeclarationSyntax>()
            ?? node.FirstAncestorOrSelf<StatementSyntax>();
        if (scope == null) return document;

        var useSqlServer = scope.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.ValueText == "UseSqlServer");
        if (useSqlServer == null) return document;

        var renamed = useSqlServer.WithExpression(
            ((MemberAccessExpressionSyntax)useSqlServer.Expression).WithName(SyntaxFactory.IdentifierName("UseOracle")));
        editor.ReplaceNode(useSqlServer, renamed);
        return editor.GetChangedDocument();
    }

    private static AttributeListSyntax CreateSkipContractCheckAttribute(string reason)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("SkipContractCheck"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(reason)))
                    .WithNameEquals(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName("Reason"))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static AttributeListSyntax CreateExpectedSpParameterAttribute(string name)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("ExpectedSpParameter"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList<AttributeArgumentSyntax>(new[]
                {
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name))),
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(string.Empty))),
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(string.Empty)))
                })));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static AttributeListSyntax CreateColumnAttribute(string name)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("Column"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(name))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static AttributeListSyntax CreateMaxLengthAttribute(int length)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("MaxLength"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(length))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }



    private static string ToSnakeCase(string pascalCase)
    {
        return string.Concat(pascalCase.Select((ch, i) =>
            i > 0 && char.IsUpper(ch) ? "_" + char.ToUpperInvariant(ch) : char.ToUpperInvariant(ch).ToString()));
    }

    private static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_', '-', '.')
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
    }
}

/// <summary>
/// Code fix for adding missing MaxLength attribute.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddMaxLengthAttributeFixProvider)), Shared]
public class AddMaxLengthAttributeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.LengthExceedsColumn, DiagnosticIds.InferredSizeFallback);

    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [MaxLength] attribute with suggested value",
                c => AddMaxLengthAttributeAsync(context.Document, root!, node, c),
                "DataGuard.AddMaxLength"),
            diagnostic);
    }

    private async Task<Document> AddMaxLengthAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("MaxLength"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("2000"))))));
        editor.AddAttribute(property, SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr)));
        return editor.GetChangedDocument();
    }
}

/// <summary>
/// Code fix for adding missing UseOracle() registration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddUseOracleFixProvider)), Shared]
public class AddUseOracleFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.ProviderOptionMismatch);

    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add .UseOracle() to DbContextOptionsBuilder",
                c => AddUseOracleToContextAsync(context.Document, root!, node, c),
                "DataGuard.AddUseOracle"),
            diagnostic);
    }

    private async Task<Document> AddUseOracleToContextAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return document;

        var useOracle = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, invocation, SyntaxFactory.IdentifierName("UseOracle")));
        editor.ReplaceNode(invocation, useOracle);
        return editor.GetChangedDocument();
    }
}

/// <summary>
/// Code fix for adding [SkipContractCheck] attribute.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SkipContractCheckFixProvider)), Shared]
public class SkipContractCheckFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(DiagnosticIds.UnvalidatedSqlCall);

    public sealed override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [SkipContractCheck] attribute (dynamic SQL)",
                c => AddSkipAttributeAsync(context.Document, root!, node, c),
                "DataGuard.SkipContractCheck"),
            diagnostic);
    }

    private async Task<Document> AddSkipAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var target = node.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (target == null) return document;

        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName("SkipContractCheck"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("Dynamic SQL - manual review required"))))));
        editor.AddAttribute(target, SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr)));
        return editor.GetChangedDocument();
    }
}