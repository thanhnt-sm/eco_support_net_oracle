---
phase: 14
title: "Performance and claim evidence"
status: in-progress
priority: P1
effort: "5d"
dependencies: [2, 3, 8, 12]
---

# Phase 14: Performance and claim evidence

## Overview

Make performance claims reproducible, scoped, and tied to implemented Phase 12 semantics. Deliver a benchmark harness and evidence pipeline for the claims in `docs/PERFORMANCE.md:1-26`, `docs/03-components/tooling/analyzers.md:89,120-125`, `docs/03-components/core/validation.md:146-185`, and `docs/02-architecture/design-philosophy.md:204-209`. A 2–4x result is a predeclared target to measure, not a result presumed achieved; “zero allocation” applies only to a separately defined hot path and cannot be claimed for cold start or Roslyn host setup without evidence.

## Requirements

- Functional: create a reproducible benchmark project and commands without adding a new top-level workspace category; place it under `tools/benchmarks/` per workspace automation topology.
- Functional: benchmark local generator/classifier, analyzer semantic enrichment, sequential public pipeline, Phase 12 dependency-level concurrent pipeline, EF snapshot parsing, SARIF/export only where they carry performance claims.
- Evidence: capture commit, SDK/runtime, OS/CPU, configuration, input corpus hash, BenchmarkDotNet version, mean/percentiles/allocation/GC, and raw result artifact.
- Policy: no benchmark result changes product behavior; no network/DB/live provider path runs in benchmark unless an isolated opt-in integration benchmark is approved separately.

## Architecture

```text
fixed corpus + fixed benchmark config -> BenchmarkDotNet harness -> JSON/CSV/raw artifacts
                                                         -> claim evaluator -> docs evidence/update gate
```

- Add `tools/benchmarks/DataGuard.Benchmarks/` using BenchmarkDotNet and deterministic synthetic/fixture corpus.
- Split measurements into cold start/host setup, generator/classifier hot path, and validation engine hot path. Allocation claims apply only to the named inner operation after setup.
- Use fixed process affinity/GC/job configuration where supported, a declared hardware class, warmup/iteration counts, and an explicit comparison baseline.
- CI uploads artifacts from a dedicated non-blocking benchmark lane initially; performance regression gating begins only after owner accepts a baseline and variance policy.

## Related Code Files

- Create: `tools/benchmarks/DataGuard.Benchmarks/{DataGuard.Benchmarks.csproj,Program.cs,Benchmarks/*.cs,Corpus/*,README.md}` and only required solution/CI references.
- Modify: `DataGuard.sln` only if the benchmark project is intentionally part of standard restore/build; otherwise document an isolated explicit command.
- Modify after evidence: `docs/PERFORMANCE.md`, `docs/03-components/tooling/analyzers.md`, `docs/03-components/core/validation.md`, `docs/02-architecture/design-philosophy.md`, and feature-showcase performance language.
- Reuse Phase 12 shared classifier, ModelSnapshot parser, build fixtures, and concurrent result metadata rather than duplicating benchmark-only behavior.

## Implementation Steps

**XR06 — cache delivery before benchmarks:** implement the advertised memory/file cache in `src/DataGuard.Core/Baseline/` and add `tests/DataGuard.Core.Tests/BaselineCacheTests.cs`. Key by content digest/provider/scope/canonicalizer version; bound memory capacity and serialized file size. Use atomic persistence, injected clock, one-hour TTL, corruption recovery, operator-approved cache path and non-secret entries. Report bounded hit/miss metrics. Fresh live acquisition and drift comparison must never use TTL as proof of no change. Test cold/hit/expired/corrupt/concurrent/cancellation and mandatory live-acquisition bypass before benchmarking these exact paths. FC18 cannot close with a benchmark-only deliverable.

1. Define benchmark scenarios and corpus contracts before collecting numbers: SQL call classifier/generator 1/100/1,000 calls; semantic enrichment; sequential/concurrent dependency levels at 100/1,000 contracts; C# snapshot supported/unsupported inputs.
2. Pin BenchmarkDotNet and job configuration. Record CPU model, logical cores, OS, .NET SDK/runtime, GC/server mode, affinity policy, commit SHA, and corpus checksum in each result.
3. Measure cold start separately from steady-state hot paths. Do not aggregate process startup, Roslyn compilation, and classifier invocation into a “zero allocation” result.
4. Establish comparison rules: same corpus/provider/rule graph, same `MaxDegreeOfParallelism`, deterministic output verification, and no live network/database. Require work conservation: concurrent output must equal sequential output including truncation metadata.
5. Set predeclared targets: parallel target is 2–4x only for a specified CPU-bound corpus/host class; target failure reports evidence and opens an owner decision, not fabricated claim success. Absolute sub-millisecond/zero-allocation claims require an owner-approved scenario and threshold.
6. Add artifact publication and a claim-evaluator script that refuses to label a result current if commit/runtime/corpus metadata are missing. Keep generated BenchmarkDotNet artifacts gitignored; retain reviewed summaries and methodology only.
7. Update docs after a reproducible run. Preserve old numbers as dated historical evidence if they cannot be reproduced; do not rewrite them as current.

## Tests Before

- Unit-test corpus builder determinism and metadata serialization.
- Test that sequential and concurrent benchmark delegates produce equal normalized findings/truncation state before timing them.
- Test claim evaluator rejection for missing commit, altered corpus hash, incompatible runtime, absent allocation metric, or comparison across different job settings.
- Verify the benchmark project restores/builds with the locked dependency graph and does not create tracked artifacts outside permitted paths.

## Success Criteria

- [x] A documented command produces machine-readable results and complete environment/corpus metadata from a fixed commit.
- [ ] Hot-path allocation and cold-start allocation are reported independently; no universal zero-allocation statement survives without matching evidence.
- [ ] The 2–4x comparison is measured against a declared sequential baseline on the same corpus and hardware class; failed target is visible, not rounded into success.
- [ ] Concurrent benchmark output is semantically identical to sequential output, including capped/truncated result signals.
- [ ] Documentation claims cite the exact run metadata or are held behind an owner evidence gate.
- [x] Benchmark output remains generated/ignored (`BenchmarkDotNet.Artifacts/`) and the product build/test path remains free of performance side effects; the benchmark project is isolated from `DataGuard.sln`.

## Risk Assessment

- Benchmark numbers vary materially by CPU, runtime, thermal state, and corpus; fixed metadata and owner-approved variance prevent false portability claims.
- Benchmarking must use the implemented Phase 12 contract and shared fixtures; semantic/code-action behavior is now available, while performance claims remain gated on clean-host evidence.
- A hard CI threshold without stabilization can create flaky delivery gates; start with artifact/evidence validation, then ratchet after baseline approval.
