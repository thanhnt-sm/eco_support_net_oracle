# Performance measurements

The recorded host measurements are from BenchmarkDotNet 0.15.8 on Apple
M1 Max (10 physical/logical cores), macOS Sequoia 15.6.1, .NET SDK/runtime
9.0.310/9.0.12, Arm64 RyuJIT, Concurrent Workstation GC, and
`InProcessEmitToolchain`. They are host-specific measurements, not product-wide
latency, zero-allocation, or parallel-speedup guarantees.

Run the isolated harness from the repository root:

```sh
dotnet restore tools/benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --locked-mode
test -z "$(git status --porcelain)" && \\
DATAGUARD_BENCHMARK_COMMIT="$(git rev-parse HEAD)" DATAGUARD_BENCHMARK_SDK="$(dotnet --version)" DATAGUARD_BENCHMARK_WORKTREE=clean \\
dotnet run --project tools/benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --configuration Release -- --full
python3 tools/benchmarks/DataGuard.Benchmarks/scripts/validate_benchmark_claim.py \\
  tools/benchmarks/DataGuard.Benchmarks/BenchmarkDotNet.Artifacts/benchmark-metadata.json
```

The raw CSV, HTML, and Markdown reports are written below the gitignored
`BenchmarkDotNet.Artifacts/` directory next to the benchmark project. The
evaluator refuses a current label when the worktree is not clean or when commit,
SDK/runtime, corpus hash, job, or allocation metadata is absent. Full methodology, source hashes, commit, and
confidence intervals are recorded in the remediation
[execution evidence](../plans/260912-2016-scout-remediation/reports/execution-evidence.md).

Pull requests and scheduled CI runs also execute the isolated classifier smoke in
the non-blocking `Benchmark (non-blocking)` job. That job records the checked-out
commit and SDK, validates `benchmark-metadata.json`, and uploads raw artifacts for
review; it does not turn a smoke result into a speedup or zero-allocation claim.

## ModelSnapshot parser

| Method | Mean | 99.9% CI | Allocated |
|---|---:|---:|---:|
| Supported snapshot | 27.449 μs | 27.145–27.754 μs | 15.95 KB |
| Malformed snapshot | 1.688 μs | 1.681–1.695 μs | 2.91 KB |

## Local SQL classifier

The fixed `SELECT X;` corpus is generated during benchmark setup and is bounded
below the classifier's 65,536-character input limit. Measurements therefore
exclude corpus construction and do not measure oversize-input rejection.

| Calls | Mean | 99.9% CI | Allocated |
|---:|---:|---:|---:|
| 1 | 162.852 ns | 160.088–165.616 ns | 424 B |
| 100 | 5.175 μs | 5.142–5.207 μs | 10,744 B |
| 1,000 | 52.609 μs | 52.415–52.803 μs | 97,152 B |

## Open measurements

The incremental-generator harness verifies one DG001 diagnostic per recognized
literal call; the semantic-analyzer harness verifies DG098; and the SARIF harness
verifies non-empty results through the production streaming file sink before timing
their fixed 100/1,000-item corpora. Their only runs are dirty-worktree dry smokes
and cannot be labelled current. The pipeline harness has fixed 100/1,000-contract
cases and an output-equivalence gate, but its 2026-09-13 dirty-worktree run made
concurrency 7.95× slower at 100 contracts and 2.47× slower at 1,000; it does not
meet the 2–4× target. A clean committed run, declared CPU-bound corpus, and owner
decision on that failed target are required before any claim.
