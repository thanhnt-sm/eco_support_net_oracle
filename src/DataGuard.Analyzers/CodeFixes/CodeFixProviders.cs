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
using Microsoft.CodeAnalysis.Formatting;
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
        
        // Fix: Replace Oracle syntax with ANSI SQL
        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to ANSI SQL",
                c => ConvertToAnsiSqlAsync(context.Document, root!, node, c),
                "DataGuard.ConvertToAnsiSql"),
            diagnostic);

        // Fix: Replace SQL Server syntax with Oracle equivalent
        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to Oracle syntax",
                c => ConvertToOracleSyntaxAsync(context.Document, root!, node, c),
                "DataGuard.ConvertToOracle"),
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

        editor.AddAttribute(target, CreateAttribute("SkipContractCheck", "Dynamic SQL - manual review required"));
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddExpectedParameterAttributesAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return document;

        var attributes = method.ParameterList.Parameters.Select(p => CreateAttribute("ExpectedSpParameter", p.Identifier.ValueText));
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

        var attributes = method.ParameterList.Parameters.Select(p => CreateAttribute("ExpectedSpParameter", p.Identifier.ValueText));
        editor.ReplaceNode(method, method.WithAttributeLists(method.AttributeLists.AddRange(attributes)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> UpdateSqlToMatchParametersAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null || invocation.ArgumentList.Arguments.Count == 0) return document;

        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return document;

        var paramList = string.Join(", ", method.ParameterList.Parameters.Select(p => p.Identifier.ValueText));
        var sql = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal($"/* DataGuard: verify SQL parameters match: {paramList} */"));
        var newInvocation = invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(
                invocation.ArgumentList.Arguments.Replace(invocation.ArgumentList.Arguments[0],
                    SyntaxFactory.Argument(sql))));
        editor.ReplaceNode(invocation, newInvocation);
        return editor.GetChangedDocument();
    }

    private async Task<Document> FixNamingConventionAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        // Apply snake_case to PascalCase renaming (identity when already PascalCase).
        var newName = ToPascalCase(property.Identifier.ValueText);
        if (newName == property.Identifier.ValueText) return document;

        var newProperty = property.WithIdentifier(SyntaxFactory.Identifier(newName));
        editor.ReplaceNode(property, newProperty);
        return editor.GetChangedDocument();
    }

    private async Task<Document> AddColumnAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        editor.AddAttribute(property, CreateAttribute("Column", ToSnakeCase(property.Identifier.ValueText)));
        return editor.GetChangedDocument();
    }

    private async Task<Document> ConvertToAnsiSqlAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        return await RewriteSqlLiteralAsync(document, root, node, c, new Dictionary<string, string>
        {
            ["NVL("] = "COALESCE(",
            ["DECODE("] = "CASE WHEN ",
            ["SYSDATE"] = "GETDATE()"
        });
    }

    private async Task<Document> ConvertToOracleSyntaxAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        return await RewriteSqlLiteralAsync(document, root, node, c, new Dictionary<string, string>
        {
            ["ISNULL("] = "NVL(",
            ["GETDATE()"] = "SYSDATE",
            ["TOP "] = "/* TOP -> FETCH FIRST */ "
        });
    }

    private async Task<Document> AddMaxLengthAttributeAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property == null) return document;

        editor.AddAttribute(property, CreateAttribute("MaxLength", "2000"));
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
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return document;

        var useOracle = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, invocation, SyntaxFactory.IdentifierName("UseOracle")));
        editor.ReplaceNode(invocation, useOracle);
        return editor.GetChangedDocument();
    }

    private static AttributeListSyntax CreateAttribute(string name, string argument)
    {
        var attr = SyntaxFactory.Attribute(SyntaxFactory.ParseName(name))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(argument))))));
        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
    }

    private static async Task<Document> RewriteSqlLiteralAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken c, IReadOnlyDictionary<string, string> replacements)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var literal = node.FirstAncestorOrSelf<LiteralExpressionSyntax>();
        if (literal == null || literal.Token.Value is not string sql) return document;

        var rewritten = sql;
        foreach (var (from, to) in replacements)
            rewritten = rewritten.Replace(from, to);

        var newLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(rewritten));
        editor.ReplaceNode(literal, newLiteral);
        return editor.GetChangedDocument();
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