using BenchmarkDotNet.Attributes;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using Microsoft.CodeAnalysis;

/// <summary>Measures streaming SARIF export for a deterministic non-empty finding corpus.</summary>
[MemoryDiagnoser]
public class SarifExportBenchmarks
{
    internal const string CorpusContract = "sarif:DG099-warning-findings:100,1000:streaming-file-sink";

    private IReadOnlyList<ContractViolation> violations = Array.Empty<ContractViolation>();
    private string outputPath = string.Empty;
    private DiagnosticEmitter? emitter;

    /// <summary>Gets the number of non-empty findings emitted into the SARIF log.</summary>
    [Params(100, 1_000)]
    public int ViolationCount { get; set; }

    /// <summary>Creates the corpus and proves the configured sink writes an actual SARIF result set.</summary>
    [GlobalSetup]
    public void SetUp()
    {
        violations = Enumerable.Range(0, ViolationCount)
            .Select(index => new ContractViolation(
                "DG099",
                $"Potential SQL injection pattern in benchmark query {index}",
                DiagnosticSeverity.Warning,
                Properties: new Dictionary<string, object?> { ["table"] = "BENCHMARK" }))
            .ToArray();
        outputPath = Path.Combine(Path.GetTempPath(), $"dataguard-benchmark-{Guid.NewGuid():N}.sarif");
        emitter = new DiagnosticEmitter(sourceRoot: Path.GetTempPath());
        emitter.AddSarifSink(new FileSarifSink(outputPath, streaming: true, sourceRoot: Path.GetTempPath()));

        Emit().GetAwaiter().GetResult();
        var output = File.ReadAllText(outputPath);
        if (!output.Contains("DG099", StringComparison.Ordinal) ||
            !output.Contains("\"results\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SARIF benchmark setup produced an empty or invalid result set.");
        }
    }

    /// <summary>Measures production streaming SARIF emission after corpus construction.</summary>
    [Benchmark]
    public Task Emit() =>
        (emitter ?? throw new InvalidOperationException("Benchmark setup did not run.")).EmitAsync(violations);

    /// <summary>Deletes the generated SARIF artifact after BenchmarkDotNet completes.</summary>
    [GlobalCleanup]
    public void CleanUp()
    {
        if (!string.IsNullOrEmpty(outputPath) && File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }
    }
}
