using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class BenchmarkRunMetadata
{
    internal static string ArtifactDirectory =>
        Path.GetFullPath("../../..", AppContext.BaseDirectory) + Path.DirectorySeparatorChar + "BenchmarkDotNet.Artifacts";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static void Write(string jobName, bool fullRun, IReadOnlyList<Type> benchmarkTypes)
    {
        var artifactDirectory = ArtifactDirectory;
        Directory.CreateDirectory(artifactDirectory);

        var metadata = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            commitSha = Environment.GetEnvironmentVariable("DATAGUARD_BENCHMARK_COMMIT"),
            sdkVersion = Environment.GetEnvironmentVariable("DATAGUARD_BENCHMARK_SDK"),
            worktreeState = Environment.GetEnvironmentVariable("DATAGUARD_BENCHMARK_WORKTREE"),
            runtime = RuntimeInformation.FrameworkDescription,
            operatingSystem = RuntimeInformation.OSDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            gcServer = System.Runtime.GCSettings.IsServerGC,
            benchmarkDotNetVersion = typeof(BenchmarkDotNet.Running.BenchmarkRunner).Assembly.GetName().Version?.ToString(),
            configuration = "Release",
            job = jobName,
            fullRun,
            affinityPolicy = "not-set-by-harness",
            validationMaxDegreeOfParallelism = ValidationPipelineBenchmarks.MaxDegreeOfParallelism,
            corpusSha256 = ComputeCorpusHash(),
            benchmarks = benchmarkTypes.Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal),
            requiredMetrics = new[] { "Mean", "Allocated", "Gen0" },
            rawArtifactDirectory = "BenchmarkDotNet.Artifacts",
        };

        File.WriteAllText(
            Path.Combine(artifactDirectory, "benchmark-metadata.json"),
            JsonSerializer.Serialize(metadata, SerializerOptions));
    }

    private static string ComputeCorpusHash()
    {
        var corpus = string.Join("\n", new[]
        {
            "ModelSnapshotBenchmarks:supported", ModelSnapshotBenchmarks.SupportedCorpus,
            "ModelSnapshotBenchmarks:unsupported", ModelSnapshotBenchmarks.UnsupportedCorpus,
            "SqlClassifierBenchmarks:statement", SqlClassifierBenchmarks.StatementCorpus,
            "SqlClassifierBenchmarks:counts", "1,100,1000",
            "ValidationPipelineBenchmarks:contract", ValidationPipelineBenchmarks.CorpusContract,
            "SemanticAnalyzerBenchmarks:contract", SemanticAnalyzerBenchmarks.CorpusContract,
            "SarifExportBenchmarks:contract", SarifExportBenchmarks.CorpusContract,
            "IncrementalGeneratorBenchmarks:contract", IncrementalGeneratorBenchmarks.CorpusContract,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(corpus))).ToLowerInvariant();
    }
}
