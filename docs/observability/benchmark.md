# Local performance evidence

Benchmark project: `benchmarks/DataGuard.Benchmarks`, BenchmarkDotNet `0.15.8`, .NET SDK
9.0.310/runtime 9.0.12 on Apple M1 Max (macOS Sequoia 15.6.1). Latest run: 2026-09-14. The benchmark uses the
generic `IBusinessOperationObserver` with no exporter endpoint; it compares direct work and
the selected operation wrapper with the SDK disabled/enabled. Run:

```sh
dotnet build benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --configuration Release
dotnet run --project benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj \
  --configuration Release -- --filter '*ObservabilityOverheadBenchmarks*' --inProcess
```

The command executed 4 benchmarks with exit `0`:

| SdkEnabled | Method | Mean | Allocated | Relative to direct |
|---:|---|---:|---:|---:|
| false | Direct | 9.198 ns | 72 B | 1.00x |
| false | Observed | 347.410 ns | 480 B | 37.77x |
| true | Direct | 8.645 ns | 72 B | 1.00x |
| true | Observed | 348.702 ns | 480 B | 40.34x |

The result is a local microbenchmark, not a workload acceptance claim. It excludes network
export, ASP.NET middleware, messaging, database instrumentation, tail sampling, GC pressure
under production traffic and native profiling. BenchmarkDotNet emitted a non-fatal priority
permission warning in the host; measurements still completed. Run a service-level benchmark
with production-like traffic and exporter/collector enabled before setting an overhead budget.

Continuous profiling overhead is `NOT EXECUTED`: no approved Pyroscope profiler/runtime,
symbols, cluster or workload is present. The deployment kill switch must therefore remain off
until a separate profiling canary produces CPU/allocation/lock and privacy evidence.
