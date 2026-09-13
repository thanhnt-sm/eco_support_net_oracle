using System.Text;
using BenchmarkDotNet.Attributes;
using DataGuard.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>Measures generator execution over a prebuilt fixed C# source corpus.</summary>
[MemoryDiagnoser]
public class IncrementalGeneratorBenchmarks
{
    internal const string CorpusContract = "CSharp:Db.ExecuteSqlRaw(SELECT * FROM BENCHMARK):incremental-generator:100,1000";

    private Compilation? compilation;

    /// <summary>Gets the count of SQL calls expected to yield DG001 diagnostics.</summary>
    [Params(100, 1_000)]
    public int InvocationCount { get; set; }

    /// <summary>Builds the corpus once and proves the generator sees each SQL call before timing it.</summary>
    [GlobalSetup]
    public void SetUp()
    {
        compilation = CSharpCompilation.Create(
            "DataGuard.Benchmark.Generator",
            [CSharpSyntaxTree.ParseText(BuildSource(InvocationCount))],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = Run().Diagnostics.Where(diagnostic => diagnostic.Id == DiagnosticIds.UnvalidatedSqlCall).ToArray();
        if (diagnostics.Length != InvocationCount)
        {
            throw new InvalidOperationException($"Expected {InvocationCount} DG001 diagnostics, got {diagnostics.Length}.");
        }
    }

    /// <summary>Measures a new generator driver's execution against the already-built compilation.</summary>
    [Benchmark]
    public GeneratorDriverRunResult Run()
    {
        var driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation ?? throw new InvalidOperationException("Benchmark setup did not run."), out _, out _);
        return driver.GetRunResult();
    }

    private static string BuildSource(int invocationCount)
    {
        var builder = new StringBuilder(
            "public sealed class Db { public void ExecuteSqlRaw(string sql) { } }\n" +
            "public static class Usage { public static void Run(Db db) {\n");
        for (var index = 0; index < invocationCount; index++)
        {
            builder.Append("db.ExecuteSqlRaw(\"SELECT * FROM BENCHMARK\");\n");
        }

        return builder.Append("} }\n").ToString();
    }
}
