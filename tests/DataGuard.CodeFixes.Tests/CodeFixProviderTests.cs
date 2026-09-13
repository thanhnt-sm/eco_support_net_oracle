// <copyright file="CodeFixProviderTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.CodeFixes.Tests;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Analyzers;
using DataGuard.Analyzers.CodeFixes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

/// <summary>
/// Tests for DataGuard code fix providers.
/// Verifies that each provider can fix the expected diagnostic IDs.
/// </summary>
public class CodeFixProviderTests
{
    [Fact]
    public void AddMaxLengthAttributeFixProvider_FixesLengthExceedsColumn()
    {
        // Arrange
        var provider = new AddMaxLengthAttributeFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.LengthExceedsColumn);
    }

    [Fact]
    public void AddMaxLengthAttributeFixProvider_FixesInferredSizeFallback()
    {
        // Arrange
        var provider = new AddMaxLengthAttributeFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.InferredSizeFallback);
    }

    [Fact]
    public void AddMaxLengthAttributeFixProvider_HasBatchFixer()
    {
        // Arrange
        var provider = new AddMaxLengthAttributeFixProvider();

        // Act
        var fixAllProvider = provider.GetFixAllProvider();

        // Assert
        fixAllProvider.Should().NotBeNull();
        fixAllProvider.Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void SkipContractCheckFixProvider_FixesUnvalidatedSqlCall()
    {
        // Arrange
        var provider = new SkipContractCheckFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.UnvalidatedSqlCall);
    }

    [Fact]
    public void SkipContractCheckFixProvider_HasBatchFixer()
    {
        // Arrange
        var provider = new SkipContractCheckFixProvider();

        // Act
        var fixAllProvider = provider.GetFixAllProvider();

        // Assert
        fixAllProvider.Should().NotBeNull();
    }

    [Fact]
    public void DataGuardCodeFixProvider_FixesUnvalidatedSqlCall()
    {
        // Arrange
        var provider = new DataGuardCodeFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.UnvalidatedSqlCall);
    }

    [Fact]
    public void DataGuardCodeFixProvider_AdvertisesOnlyIdsWithRegisteredActions()
    {
        var provider = new DataGuardCodeFixProvider();

        provider.FixableDiagnosticIds.Should().BeEquivalentTo(
            DiagnosticIds.ParameterMismatch,
            DiagnosticIds.UnvalidatedSqlCall);
        provider.FixableDiagnosticIds.Should().NotContain(DiagnosticIds.DirectionMismatch);
        provider.FixableDiagnosticIds.Should().NotContain(DiagnosticIds.ColumnShapeMismatch);
        provider.FixableDiagnosticIds.Should().NotContain(DiagnosticIds.NullableMismatch);
    }

    [Fact]
    public void DiagnosticIds_UnvalidatedSqlCall_IsDG001()
    {
        // Assert
        DiagnosticIds.UnvalidatedSqlCall.Should().Be("DG001");
    }

    [Fact]
    public void DiagnosticIds_ParameterMismatch_IsDG002()
    {
        // Assert
        DiagnosticIds.ParameterMismatch.Should().Be("DG002");
    }

    [Fact]
    public void DiagnosticIds_LengthExceedsColumn_IsDG007()
    {
        // Assert
        DiagnosticIds.LengthExceedsColumn.Should().Be("DG007");
    }

    [Fact]
    public void DiagnosticIds_InferredSizeFallback_IsDG009()
    {
        // Assert
        DiagnosticIds.InferredSizeFallback.Should().Be("DG009");
    }

    [Fact]
    public void AllCodeFixProviders_ImplementGetFixAllProvider()
    {
        // Arrange
        var providers = new CodeFixProvider[]
        {
            new DataGuardCodeFixProvider(),
            new AddMaxLengthAttributeFixProvider(),
            new SkipContractCheckFixProvider(),
            new NamingConventionFixProvider(),
            new UseOracleCodeFixProvider(),
        };

        // Act & Assert
        foreach (var provider in providers)
        {
            provider.GetFixAllProvider().Should().NotBeNull(
                because: $"provider {provider.GetType().Name} should support FixAll");
        }
    }

    [Fact]
    public void AllCodeFixProviders_HaveNonEmptyFixableDiagnosticIds()
    {
        // Arrange
        var providers = new CodeFixProvider[]
        {
            new DataGuardCodeFixProvider(),
            new AddMaxLengthAttributeFixProvider(),
            new SkipContractCheckFixProvider(),
            new NamingConventionFixProvider(),
            new UseOracleCodeFixProvider(),
        };

        // Act & Assert
        foreach (var provider in providers)
        {
            provider.FixableDiagnosticIds.Should().NotBeEmpty(
                because: $"provider {provider.GetType().Name} should fix at least one diagnostic");
        }
    }

    [Fact]
    public async Task MaxLengthFix_ReplacesExistingBoundOnlyWhenManifestBoundEvidenceIsPresent()
    {
        var document = CreateDocument("using System.ComponentModel.DataAnnotations; class C { [MaxLength(4000)] public string Name { get; set; } = \"\"; }");
        var property = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DataGuardCodeFixProvider.ManifestDigestProperty, new string('a', 64))
            .Add(DataGuardCodeFixProvider.VerifiedMaxLengthProperty, "100");
        var changed = await ApplyOnlyActionAsync(new AddMaxLengthAttributeFixProvider(), document,
            CreateDiagnostic(DiagnosticIds.LengthExceedsColumn, property.GetLocation(), properties));

        (await changed.GetTextAsync()).ToString().Should().Contain("MaxLength(100)").And.NotContain("MaxLength(4000)");
        (await changed.Project.GetCompilationAsync())!.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task MaxLengthFix_WithoutVerifiedLengthDoesNotRegister()
    {
        var document = CreateDocument("class C { public string Name { get; set; } = \"\"; }");
        var property = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var actions = await GetActionsAsync(new AddMaxLengthAttributeFixProvider(), document,
            CreateDiagnostic(DiagnosticIds.InferredSizeFallback, property.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(DataGuardCodeFixProvider.ManifestDigestProperty, new string('a', 64))));

        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ParameterMismatch_WithVerifiedManifestReplacement_AppliesAndCompiles()
    {
        var document = CreateDocument("class C { void M() { Execute(\"EXEC legacy_proc\"); } void Execute(string sql) { } }");
        var literal = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DataGuardCodeFixProvider.ManifestDigestProperty, new string('a', 64))
            .Add(DataGuardCodeFixProvider.VerifiedSqlReplacementProperty, "EXEC current_proc @id");
        var diagnostic = CreateDiagnostic(DiagnosticIds.ParameterMismatch, literal.GetLocation(), properties);

        var changed = await ApplyOnlyActionAsync(new DataGuardCodeFixProvider(), document, diagnostic);

        (await changed.GetTextAsync()).ToString().Should().Contain("EXEC current_proc @id");
        (await changed.Project.GetCompilationAsync())!.GetDiagnostics()
            .Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ParameterMismatch_WithoutVerifiedManifest_DoesNotOfferUnsafeRewrite()
    {
        var document = CreateDocument("class C { void M() { Execute(\"EXEC legacy_proc\"); } void Execute(string sql) { } }");
        var literal = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<LiteralExpressionSyntax>().Single();

        var actions = await GetActionsAsync(new DataGuardCodeFixProvider(), document,
            CreateDiagnostic(DiagnosticIds.ParameterMismatch, literal.GetLocation()));

        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderOptionMismatch_ReplacesUseSqlServerAndCompiles()
    {
        var document = CreateDocument("class C { void M() { new Builder().UseSqlServer(\"x\"); } } class Builder { public Builder UseSqlServer(string value) => this; public Builder UseOracle(string value) => this; }");
        var invocation = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(i => i.Expression.ToString().Contains("UseSqlServer", System.StringComparison.Ordinal));

        var changed = await ApplyOnlyActionAsync(new UseOracleCodeFixProvider(), document,
            CreateDiagnostic(DiagnosticIds.ProviderOptionMismatch, invocation.GetLocation()));

        (await changed.GetTextAsync()).ToString().Should().Contain("UseOracle");
        (await changed.Project.GetCompilationAsync())!.GetDiagnostics()
            .Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task NamingConventionProvider_RegistersConcreteRenameAndColumnActions()
    {
        var document = CreateDocument("class C { public string customer_name { get; set; } = \"\"; }");
        var property = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();

        var actions = await GetActionsAsync(new NamingConventionFixProvider(), document,
            CreateDiagnostic(DiagnosticIds.NamingConvention, property.GetLocation()));

        actions.Select(action => action.Title).Should().BeEquivalentTo(
            "Auto-fix naming convention", "Add [Column] attribute with explicit name");
    }

    [Fact]
    public async Task ParameterMismatch_FixAll_AppliesEveryVerifiedReplacement()
    {
        var document = CreateDocument("class C { void M() { Execute(\"EXEC legacy_one\"); Execute(\"EXEC legacy_two\"); } void Execute(string sql) { } }");
        var literals = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<LiteralExpressionSyntax>().ToArray();
        var diagnostics = literals.Select((literal, index) => CreateDiagnostic(
            DiagnosticIds.ParameterMismatch,
            literal.GetLocation(),
            ImmutableDictionary<string, string?>.Empty
                .Add(DataGuardCodeFixProvider.ManifestDigestProperty, new string('a', 64))
                .Add(DataGuardCodeFixProvider.VerifiedSqlReplacementProperty, $"EXEC current_{index + 1}")))
            .ToImmutableArray();
        var provider = new DataGuardCodeFixProvider();
        var context = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            "DataGuard.UpdateSql",
            new[] { DiagnosticIds.ParameterMismatch },
            new StaticDiagnosticProvider(diagnostics),
            CancellationToken.None);

        var fixAllAction = await provider.GetFixAllProvider().GetFixAsync(context);
        fixAllAction.Should().NotBeNull();
        var operation = (await fixAllAction!.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
        var changed = operation.ChangedSolution.GetDocument(document.Id)!;

        (await changed.GetTextAsync()).ToString().Should().Contain("EXEC current_1").And.Contain("EXEC current_2");
        (await changed.Project.GetCompilationAsync())!.GetDiagnostics()
            .Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task UnvalidatedSqlCall_RegistersCompileValidContractDeclarationActions()
    {
        var document = CreateDocument("class Customer { public int Id { get; set; } void Find(int id) { Execute(\"SELECT * FROM customers\"); } void Execute(string sql) { } }");
        var literal = (await document.GetSyntaxRootAsync())!.DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        var diagnostic = CreateDiagnostic(DiagnosticIds.UnvalidatedSqlCall, literal.GetLocation());
        var provider = new DataGuardCodeFixProvider();

        var actions = await GetActionsAsync(provider, document, diagnostic);

        actions.Select(action => action.Title).Should().Contain(new[]
        {
            "Add [DataContract] attribute",
            "Add [SqlParameter] attributes",
            "Add [SkipContractCheck] attribute",
        });

        foreach (var title in new[] { "Add [DataContract] attribute", "Add [SqlParameter] attributes" })
        {
            var operation = (await actions.Single(action => action.Title == title).GetOperationsAsync(CancellationToken.None))
                .OfType<ApplyChangesOperation>().Single();
            var changed = operation.ChangedSolution.GetDocument(document.Id)!;
            (await changed.Project.GetCompilationAsync())!.GetDiagnostics()
                .Should().NotContain(value => value.Severity == DiagnosticSeverity.Error, title + " must produce compilable C#");
        }
    }

    private static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "CodeFixTest", "CodeFixTest", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(projectId, GetTrustedPlatformReferences());
        workspace.TryApplyChanges(solution).Should().BeTrue();
        return workspace.AddDocument(projectId, "Test.cs", SourceText.From(source));
    }

    private static IEnumerable<MetadataReference> GetTrustedPlatformReferences()
    {
        var runtimeDirectory = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return System.IO.Directory.EnumerateFiles(runtimeDirectory, "*.dll")
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(DataGuard.Contracts.DataContractAttribute).Assembly.Location));
    }

    private static Diagnostic CreateDiagnostic(string id, Location location, ImmutableDictionary<string, string?>? properties = null)
        => Diagnostic.Create(new DiagnosticDescriptor(id, id, id, "Tests", DiagnosticSeverity.Error, true), location,
            properties: properties ?? ImmutableDictionary<string, string?>.Empty);

    private static async Task<IReadOnlyList<CodeAction>> GetActionsAsync(CodeFixProvider provider, Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await provider.RegisterCodeFixesAsync(context);
        return actions;
    }

    private static async Task<Document> ApplyOnlyActionAsync(CodeFixProvider provider, Document document, Diagnostic diagnostic)
    {
        var actions = await GetActionsAsync(provider, document, diagnostic);
        actions.Should().ContainSingle();
        var action = actions.Single();
        var operation = (await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private sealed class StaticDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> diagnostics;

        public StaticDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        {
            this.diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(this.diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(System.Array.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<Diagnostic>>(this.diagnostics);
    }
}
