using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

var fullRun = args.Contains("--full", StringComparer.Ordinal);
var job = (fullRun ? Job.Default : Job.Dry).WithToolchain(InProcessEmitToolchain.Instance);
var config = DefaultConfig.Instance
    .AddJob(job)
    .WithArtifactsPath(BenchmarkRunMetadata.ArtifactDirectory);
var benchmarkTypes = args.Contains("--classifier-only", StringComparer.Ordinal)
    ? new[] { typeof(SqlClassifierBenchmarks) }
    : args.Contains("--pipeline-only", StringComparer.Ordinal)
        ? new[] { typeof(ValidationPipelineBenchmarks) }
        : args.Contains("--analyzer-only", StringComparer.Ordinal)
            ? new[] { typeof(SemanticAnalyzerBenchmarks) }
            : args.Contains("--sarif-only", StringComparer.Ordinal)
                ? new[] { typeof(SarifExportBenchmarks) }
                : args.Contains("--generator-only", StringComparer.Ordinal)
                    ? new[] { typeof(IncrementalGeneratorBenchmarks) }
        : new[]
        {
            typeof(ModelSnapshotBenchmarks),
            typeof(SqlClassifierBenchmarks),
            typeof(ValidationPipelineBenchmarks),
            typeof(SemanticAnalyzerBenchmarks),
            typeof(SarifExportBenchmarks),
            typeof(IncrementalGeneratorBenchmarks),
        };
BenchmarkRunMetadata.Write(fullRun ? "Default" : "Dry", fullRun, benchmarkTypes);
BenchmarkRunner.Run(
    benchmarkTypes,
    config);
