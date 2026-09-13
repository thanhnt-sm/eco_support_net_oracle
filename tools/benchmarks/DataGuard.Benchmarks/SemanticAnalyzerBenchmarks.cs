using System.Collections.Immutable;
using System.Text;
using BenchmarkDotNet.Attributes;
using DataGuard.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>Measures the CI semantic analyzer after compilation construction has completed.</summary>
[MemoryDiagnoser]
public class SemanticAnalyzerBenchmarks
{
    internal const string CorpusContract = "CSharp:Db.ExecuteSqlRaw(SELECT 1):semantic-analyzer:100,1000";

    private Compilation? compilation;
    private ImmutableArray<DiagnosticAnalyzer> analyzers;

    /// <summary>Gets the fixed number of recognized SQL invocations in the source corpus.</summary>
    [Params(100, 1_000)]
    public int InvocationCount { get; set; }

    /// <summary>Creates and validates the compilation once, excluding parse/compilation setup from the analyzer timing.</summary>
    [GlobalSetup]
    public void SetUp()
    {
        compilation = CSharpCompilation.Create(
            "DataGuard.Benchmark.Semantic",
            [CSharpSyntaxTree.ParseText(BuildSource(InvocationCount))],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ContractValidationAnalyzer());

        var compilationErrors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (compilationErrors.Length > 0)
        {
            throw new InvalidOperationException($"Semantic benchmark corpus does not compile: {compilationErrors[0]}");
        }

        var diagnostics = Analyze().GetAwaiter().GetResult();
        if (diagnostics.Length != InvocationCount)
        {
            throw new InvalidOperationException($"Expected {InvocationCount} semantic diagnostics, got {diagnostics.Length}.");
        }
    }

    /// <summary>Measures semantic operation analysis for the prebuilt fixed source corpus.</summary>
    [Benchmark]
    public Task<ImmutableArray<Diagnostic>> Analyze() =>
        (compilation ?? throw new InvalidOperationException("Benchmark setup did not run."))
        .WithAnalyzers(analyzers)
        .GetAnalyzerDiagnosticsAsync();

    private static string BuildSource(int invocationCount)
    {
        var builder = new StringBuilder(
            "public sealed class Db { public void ExecuteSqlRaw(string sql) { } }\n" +
            "public static class Usage { public static void Run(Db db) {\n");
        for (var index = 0; index < invocationCount; index++)
        {
            builder.Append("db.ExecuteSqlRaw(\"SELECT 1\");\n");
        }

        return builder.Append("} }\n").ToString();
    }
}
