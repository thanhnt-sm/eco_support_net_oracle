// <copyright file="VerifierStubAnalyzers.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

// RS1036/RS1038/RS1041/RS2008 govern analyzers that ship to a compiler host. These stubs are instantiated only by
// Microsoft.CodeAnalysis.Testing inside this net9.0 test assembly and never load into csc or an IDE.
#pragma warning disable RS1036, RS1038, RS1041, RS2008

namespace DataGuard.CodeFixes.Tests.Verification;

using System.Collections.Immutable;
using System.Linq;
using DataGuard.Analyzers;
using DataGuard.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

// DG002 (verified replacement), DG006, DG007, DG009 and DG012 are produced by the CLI rules engine against
// database ground truth and handed to the IDE with manifest-bound properties (DG017 round-trips through the real
// ContractValidationAnalyzer, which reports at the SQL argument). These test-only analyzers emit the
// same IDs and properties at a real source location, standing in for that verifier so each code fix provider
// can be exercised end to end through CSharpCodeFixTest (diagnostic -> registered action -> FixedCode).
// Each stub stops reporting once the code is fixed, which is what lets the framework verify convergence.

/// <summary>Shared helpers for the verifier stubs.</summary>
internal static class StubDescriptors
{
    public const string ManifestDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public static DiagnosticDescriptor Create(string id, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        => new(id, id + " (verifier stub)", "{0}", "DataGuard.Tests", severity, isEnabledByDefault: true);
}

/// <summary>
/// Simulates a manifest whose column limit is 100: DG009 for a string property without a length bound,
/// DG007 for a declared bound above 100. Both carry the verified length the fix applies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ManifestLengthStubAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor LengthExceeds = StubDescriptors.Create(DiagnosticIds.LengthExceedsColumn, DiagnosticSeverity.Error);
    private static readonly DiagnosticDescriptor InferredSize = StubDescriptors.Create(DiagnosticIds.InferredSizeFallback);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(LengthExceeds, InferredSize);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.PropertyDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (property.Type is not PredefinedTypeSyntax { Keyword.ValueText: "string" })
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DataGuardCodeFixProvider.ManifestDigestProperty, StubDescriptors.ManifestDigest)
            .Add(DataGuardCodeFixProvider.VerifiedMaxLengthProperty, "100");
        var bound = property.AttributeLists.SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute => attribute.Name.ToString().EndsWith("MaxLength", System.StringComparison.Ordinal));
        if (bound is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InferredSize, property.Identifier.GetLocation(), properties, "size inferred as NVARCHAR2(2000)"));
            return;
        }

        if (bound.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.Value: int length } && length > 100)
        {
            context.ReportDiagnostic(Diagnostic.Create(LengthExceeds, property.Identifier.GetLocation(), properties, $"{length} > 100"));
        }
    }
}

/// <summary>Simulates the DG006 naming rule: a property name containing '_' without an explicit [Column].</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class NamingStubAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = StubDescriptors.Create(DiagnosticIds.NamingConvention);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            nodeContext =>
            {
                var property = (PropertyDeclarationSyntax)nodeContext.Node;
                var hasColumn = property.AttributeLists.SelectMany(list => list.Attributes)
                    .Any(attribute => attribute.Name.ToString().EndsWith("Column", System.StringComparison.Ordinal));
                if (!hasColumn && property.Identifier.ValueText.Contains('_'))
                {
                    nodeContext.ReportDiagnostic(Diagnostic.Create(Rule, property.Identifier.GetLocation(), property.Identifier.ValueText));
                }
            },
            SyntaxKind.PropertyDeclaration);
    }
}

/// <summary>Simulates the DG012 verifier: the target database is Oracle, so any UseSqlServer call is a mismatch.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class ProviderOptionStubAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = StubDescriptors.Create(DiagnosticIds.ProviderOptionMismatch, DiagnosticSeverity.Error);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            nodeContext =>
            {
                var invocation = (InvocationExpressionSyntax)nodeContext.Node;
                if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "UseSqlServer" })
                {
                    nodeContext.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), "Oracle target configured with UseSqlServer"));
                }
            },
            SyntaxKind.InvocationExpression);
    }
}

/// <summary>
/// Simulates the DG002 verifier: the literal "EXEC legacy_proc" is stale and the manifest-bound replacement is
/// "EXEC current_proc @id". Subclasses decide whether the diagnostic carries that manifest evidence.
/// </summary>
internal abstract class LegacyProcStubAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = StubDescriptors.Create(DiagnosticIds.ParameterMismatch, DiagnosticSeverity.Error);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    protected abstract bool IncludeEvidence { get; }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            nodeContext =>
            {
                var literal = (LiteralExpressionSyntax)nodeContext.Node;
                if (literal.Token.Value is not "EXEC legacy_proc")
                {
                    return;
                }

                var properties = ImmutableDictionary<string, string?>.Empty;
                if (this.IncludeEvidence)
                {
                    properties = properties
                        .Add(DataGuardCodeFixProvider.ManifestDigestProperty, StubDescriptors.ManifestDigest)
                        .Add(DataGuardCodeFixProvider.VerifiedSqlReplacementProperty, "EXEC current_proc @id");
                }

                nodeContext.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation(), properties, "legacy_proc signature changed"));
            },
            SyntaxKind.StringLiteralExpression);
    }
}

/// <summary>DG002 with manifest digest and verified replacement SQL.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class VerifiedSqlStubAnalyzer : LegacyProcStubAnalyzer
{
    protected override bool IncludeEvidence => true;
}

/// <summary>DG002 without manifest evidence: the fix must not offer a rewrite.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class UnverifiedSqlStubAnalyzer : LegacyProcStubAnalyzer
{
    protected override bool IncludeEvidence => false;
}
