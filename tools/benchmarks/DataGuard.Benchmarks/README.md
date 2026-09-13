# DataGuard benchmarks

This isolated BenchmarkDotNet project measures offline, deterministic hot paths only.
It is intentionally not part of `DataGuard.sln`, does not contact a database or the
network at runtime, and writes generated results to the gitignored
`BenchmarkDotNet.Artifacts/` directory.

Run from this directory:

```sh
dotnet restore --locked-mode
test -z "$(git status --porcelain)" && \
DATAGUARD_BENCHMARK_COMMIT="$(git rev-parse HEAD)" \
DATAGUARD_BENCHMARK_SDK="$(dotnet --version)" \
DATAGUARD_BENCHMARK_WORKTREE=clean \
dotnet run --configuration Release --no-restore
```

The default command uses one dry cold-start iteration to verify the artifact pipeline.
Run `dotnet run --configuration Release --no-restore -- --full` on a controlled host
for a statistical BenchmarkDotNet measurement. The scenarios measure the source-only
`ModelSnapshotCSharpParser` separately for supported and malformed source, and the
shared `SqlClassifier` over fixed 1, 100, and 1,000 call corpora. Corpus construction
happens in `GlobalSetup`, so the classifier measurement excludes input generation.
To measure only the classifier corpus, add `-- --full --classifier-only`; to
measure only the sequential/concurrent pipeline pair, add
`-- --full --pipeline-only`; to measure only semantic analyzer enrichment, add
`-- --full --analyzer-only`; to measure only streaming SARIF export, add
`-- --full --sarif-only`; to measure only the incremental generator, add
`-- --full --generator-only`.
The full harness also measures the public validation pipeline at 100 and 1,000
contracts with concurrency disabled and with a degree capped at four. Its setup
normalizes and compares both results, including execution status and dropped-count
metadata, before either mode is timed.
The same full run measures the semantic CI analyzer over 100 and 1,000 recognized
SQL invocations. It builds the source compilation in setup and verifies one DG098
diagnostic per invocation, so the measured operation is analyzer execution rather
than corpus construction or a no-op analyzer path.
The SARIF scenario emits 100 or 1,000 non-empty DG099 findings through the production
streaming file sink and checks that the setup artifact has result records before timing.
The generator scenario builds its C# corpus once, then requires one DG001 diagnostic
per `ExecuteSqlRaw` literal before timing each new generator driver execution.
BenchmarkDotNet records runtime, environment, allocation, and GC statistics in its
raw artifacts. Results are measurements for their recorded host and corpus; they do
not establish a universal latency, a parallel speedup, or a zero-allocation claim.

The harness writes `BenchmarkDotNet.Artifacts/benchmark-metadata.json` before each
run. A claim is valid only after this metadata is complete and the evaluator accepts
it. Commit, SDK, and a clean-worktree declaration are deliberately required by that
evaluator so an untraceable or uncommitted local run cannot be labelled current:

```sh
python3 scripts/validate_benchmark_claim.py BenchmarkDotNet.Artifacts/benchmark-metadata.json
```

For a sequential-versus-concurrent comparison, run the evaluator with `--compare`
and it will reject differences in corpus, runtime, job, or configuration. Use
`--expected-corpus-sha256` or `--required-runtime` in CI to pin an accepted corpus
and runtime. The evaluator checks metadata only; BenchmarkDotNet's raw CSV and logs
remain the evidence for the reported measurements.
