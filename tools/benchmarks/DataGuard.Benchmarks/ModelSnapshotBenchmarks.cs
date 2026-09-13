using BenchmarkDotNet.Attributes;
using DataGuard.Core.Sources;

/// <summary>Offline, deterministic benchmark scenarios for the source-only EF snapshot parser.</summary>
[MemoryDiagnoser]
public class ModelSnapshotBenchmarks
{
    internal const string SupportedCorpus = """
        class Snapshot { void Build(ModelBuilder modelBuilder) {
          modelBuilder.Entity<Customer>(entity => { entity.ToTable("CUSTOMERS"); entity.HasKey(item => item.Id); entity.Property(item => item.Name).HasColumnName("FULL_NAME").HasMaxLength(120).IsRequired(); });
        }}
        """;

    internal const string UnsupportedCorpus = "class Snapshot {";

    /// <summary>Measures a supported fluent-source parse without assembly loading, network, or database access.</summary>
    [Benchmark]
    public DesignTimeExtractionResult ParseSupported() => ModelSnapshotCSharpParser.Parse(SupportedCorpus);

    /// <summary>Measures visible syntax-failure handling separately from a supported parse.</summary>
    [Benchmark]
    public DesignTimeExtractionResult ParseUnsupported() => ModelSnapshotCSharpParser.Parse(UnsupportedCorpus);
}
