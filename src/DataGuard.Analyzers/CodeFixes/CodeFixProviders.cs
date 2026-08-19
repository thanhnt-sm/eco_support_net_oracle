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
                c => AddSkipContractCheckAttributeAsync(context.Document, root!, c),
                "DataGuard.AddSkipContractCheck"),
            diagnostic);

        // Fix 2: Add expected parameter attributes
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add expected parameter attributes",
                c => AddExpectedParameterAttributesAsync(context.Document, root!, c),
                "DataGuard.AddExpectedParameters"),
            diagnostic);

        // Fix 3: Run full validation in CI (mark as intentional)
        context.RegisterCodeFix(
            CodeAction.Create(
                "Mark as 'validate in CI only' (add comment)",
                c => AddCiOnlyCommentAsync(context.Document, root!, c),
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
                c => AddExpectedSpParameterAsync(context.Document, root!, c),
                "DataGuard.AddExpectedSpParameter"),
            diagnostic);

        // Fix: Update SQL to match expected parameters
        context.RegisterCodeFix(
            CodeAction.Create(
                "Update SQL to match expected parameters",
                c => UpdateSqlToMatchParametersAsync(context.Document, root!, c),
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
                c => FixNamingConventionAsync(context.Document, root!, c),
                "DataGuard.FixNamingConvention"),
            diagnostic);

        // Fix: Add explicit column mapping attribute
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [Column] attribute with explicit name",
                c => AddColumnAttributeAsync(context.Document, root!, c),
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
                c => ConvertToAnsiSqlAsync(context.Document, root!, c),
                "DataGuard.ConvertToAnsiSql"),
            diagnostic);

        // Fix: Replace SQL Server syntax with Oracle equivalent
        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to Oracle syntax",
                c => ConvertToOracleSyntaxAsync(context.Document, root!, c),
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
                c => AddMaxLengthAttributeAsync(context.Document, root!, c),
                "DataGuard.AddMaxLength"),
            diagnostic);

        // Fix: Change column type to CLOB/NCLOB
        context.RegisterCodeFix(
            CodeAction.Create(
                "Change column type to CLOB/NCLOB",
                c => SuggestClobTypeAsync(context.Document, root!, c),
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
                c => AddUseOracleAsync(context.Document, root!, c),
                "DataGuard.AddUseOracle"),
            diagnostic);
    }

    // Implementation helpers
    private async Task<Document> AddSkipContractCheckAttributeAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        var generator = editor.Generator;

        // Find the containing method/class and add attribute
        // Implementation would add [SkipContractCheck] attribute
        return document;
    }

    private async Task<Document> AddExpectedParameterAttributesAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Add [ExpectedSpParameter] attributes for each parameter
        return await Task.FromResult(document);
    }

    private async Task<Document> AddCiOnlyCommentAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        // Add comment: // DataGuard: Validate in CI only
        return document;
    }

    private async Task<Document> AddExpectedSpParameterAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Add [ExpectedSpParameter] attributes
        return await Task.FromResult(document);
    }

    private async Task<Document> UpdateSqlToMatchParametersAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Would update SQL string to match expected parameters
        return await Task.FromResult(document);
    }

    private async Task<Document> FixNamingConventionAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        // Auto-rename identifiers to match naming convention
        return document;
    }

    private async Task<Document> AddColumnAttributeAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Add [Column("explicit_name")] attribute
        return await Task.FromResult(document);
    }

    private async Task<Document> ConvertToAnsiSqlAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Replace Oracle-specific syntax with ANSI SQL equivalents
        return await Task.FromResult(document);
    }

    private async Task<Document> ConvertToOracleSyntaxAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Replace SQL Server syntax with Oracle equivalents
        return await Task.FromResult(document);
    }

    private async Task<Document> AddMaxLengthAttributeAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Add [MaxLength(n)] attribute
        return await Task.FromResult(document);
    }

    private async Task<Document> SuggestClobTypeAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Suggest changing column type to CLOB/NCLOB
        return await Task.FromResult(document);
    }

    private async Task<Document> AddUseOracleAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        // Add .UseOracle() to DbContextOptionsBuilder
        return document;
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

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [MaxLength] attribute with suggested value",
                c => AddMaxLengthAttributeAsync(context.Document, root!, c),
                "DataGuard.AddMaxLength"),
            diagnostic);
    }

    private async Task<Document> AddMaxLengthAttributeAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        // Implementation would add [MaxLength(n)] attribute to property
        return await Task.FromResult(document);
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

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add .UseOracle() to DbContextOptionsBuilder",
                c => AddUseOracleToContextAsync(context.Document, root!, c),
                "DataGuard.AddUseOracle"),
            diagnostic);
    }

    private async Task<Document> AddUseOracleToContextAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        // Find DbContextOptionsBuilder and add .UseOracle()
        return await Task.FromResult(document);
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

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add [SkipContractCheck] attribute (dynamic SQL)",
                c => AddSkipAttributeAsync(context.Document, root!, c),
                "DataGuard.SkipContractCheck"),
            diagnostic);
    }

    private async Task<Document> AddSkipAttributeAsync(Document document, SyntaxNode root, CancellationToken c)
    {
        var editor = await DocumentEditor.CreateAsync(document, c);
        // Add [SkipContractCheck("Dynamic SQL - manual review required")] attribute
        return await Task.FromResult(document);
    }
}