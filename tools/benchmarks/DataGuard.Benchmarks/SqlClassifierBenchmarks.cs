using BenchmarkDotNet.Attributes;
using DataGuard.SqlClassification;

/// <summary>Measures deterministic local classification over fixed SQL-call corpus sizes.</summary>
[MemoryDiagnoser]
public class SqlClassifierBenchmarks
{
    internal const string StatementCorpus = "SELECT X;";
    private static readonly Uri DocumentUri = new("file:///benchmark/SqlCorpus.cs");
    private string source = string.Empty;

    /// <summary>Gets or sets the fixed number of independently classified SQL calls.</summary>
    [Params(1, 100, 1_000)]
    public int CallCount { get; set; }

    /// <summary>Creates a deterministic corpus with one recognized statement per call.</summary>
    [GlobalSetup]
    public void CreateCorpus()
    {
        var calls = Enumerable.Repeat(StatementCorpus, CallCount);
        source = string.Join(Environment.NewLine, calls);
        if (source.Length > 65_536)
        {
            throw new InvalidOperationException("The classifier corpus exceeds its source-length contract.");
        }
    }

    /// <summary>Measures only the classifier after the corpus has been constructed.</summary>
    [Benchmark]
    public IReadOnlyList<SqlClassification> Classify() =>
        SqlClassifier.Classify(source, DocumentUri, "benchmark-v1");
}
