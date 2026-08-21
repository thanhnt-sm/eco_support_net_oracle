# Performance Benchmarks (BenchmarkDotNet v0.15.8)

Measured on Apple M1 Max (10 cores), .NET 9.0.12 (Arm64 RyuJIT), macOS Sequoia 15.6.1.

## 1. IncrementalGenerator (`UnvalidatedSqlCallGenerator`)

Syntax-only IDE analyzer run on keystroke:

| Method | Mean | StdDev | Allocated | Gen0 / 1k ops |
|---|---|---|---|---|
| `RunGenerator` | **16.1 μs** (0.016 ms) | 0.075 μs | 16.52 KB | 2.68 |

> **Takeaway**: Sub-millisecond execution (~16 μs) confirms the claim of low overhead on IDE keystrokes with minimal memory allocation.

---

## 2. ConcurrentValidationEngine (`ConcurrentValidationEngine`)

Parallel rule execution with bounded concurrency (MaxDegreeOfParallelism = 4):

| Method | Contract Count | Mean | StdDev | Allocated | Gen0 / 1k ops |
|---|---|---|---|---|---|
| `Validate` | 100 contracts | **213.6 μs** (0.21 ms) | 14.98 μs | 85.5 KB | 13.67 |
| `Validate` | 1,000 contracts | **2.09 ms** | 29.14 μs | 836.5 KB | 132.81 |

> **Takeaway**: Validating 1,000 contracts with multiple rules takes ~2 ms on 4 parallel threads, scaling linearly with contract count.
