using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using DataGuard.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace DataGuard.Observability.Benchmarks;

/// <summary>Entry point; accepts the standard BenchmarkDotNet switches (for example <c>--filter</c> and <c>--inProcess</c>).</summary>
public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>Direct work versus the <see cref="IBusinessOperationObserver"/> wrapper with the SDK disabled and enabled.</summary>
[MemoryDiagnoser]
public class ObservabilityOverheadBenchmarks
{
    private readonly ObservedOperationDescriptor _operation = new(
        "banking.transfer.initiate",
        "critical",
        ObservedOperationKind.Command);

    private IBusinessOperationObserver _observer = null!;
    private ServiceProvider _provider = null!;

    [Params(false, true)]
    public bool SdkEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(options =>
            {
                options.ServiceName = "benchmark";
                options.Enabled = SdkEnabled;
            })
            .BuildServiceProvider();
        _observer = _provider.GetRequiredService<IBusinessOperationObserver>();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark(Baseline = true)]
    public Task<int> Direct() => Task.FromResult(42);

    [Benchmark]
    public Task<int> Observed() => _observer.ExecuteAsync(_operation, _ => Task.FromResult(42));
}
