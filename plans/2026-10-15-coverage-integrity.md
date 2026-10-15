# Coverage integrity and executable module tests — 2026-10-15

## Authorization and baseline

User approved a focused implementation draft PR, TDD, independent review, unchanged
hooks and final-head CI. No merge, release, publication, protection change or fabricated
coverage. Base: `cb45266f8641462f0e751a8ce16f369ee0fdf85c`, fetched main and PR62 merge.
Preserve PR62 publication pause and all retained build/test/package/security jobs.
Actual local SDK 9.0.319 / runtime 9.0.21; Node 22 via `/workspace/.setup/activate.sh`.

## Requirement → implementation → executable acceptance

| Requirement | Source/test contract | Evidence |
| --- | --- | --- |
| Explicit reports, no duplicate uploads | `scripts/coverage_manifest.py`, Python negative tests; fresh suite directories, exact count, normalized existing production paths | Failing-first import observed; ten initial regression tests pass |
| Honest scope and line/branch metrics | Per-suite manifests, unioned measured line gate; branch counts per report, no invented aggregate branch union | Baseline/after measurements below |
| CLI subprocess instrumentation | Execute referenced collector-instrumented CLI copy, meaningful config/init/validation subprocess assertions | Confirm CLI command hits in collected XML |
| LSP missing module | Reference executable in test output; lifecycle and diagnostic protocol assertions | Confirm LSP appears with executed lines |
| Windows coverage | Real Windows collector, validated manifest, explicit upload flag | Hosted Windows result required |
| Unexecuted TS production | c8 all-file collection, source maps, exclude test files only, assert source inventory | Production LCOV inventory and line/branch totals |
| Live README status | GitHub Actions badge on EN/VI README | Docs sync |
| Preservation | No hook edits or publication job restoration | Diff, policy tests, unchanged pre-commit/pre-push |

## Validation sequence

1. Fresh baseline at base SHA; retain raw TRX/Cobertura/LCOV outside tracked source.
2. Failing-first report contract tests; implementation, then negative/positive tests.
3. Targeted executable tests; inspect actual collector output before claiming subprocess coverage.
4. Full unit suite, raw SQL/Oracle and mapping regressions, format/analyzers, workflow lint,
   docs sync, dependency/security checks and packaging gates.
5. Independent review, resolve findings, unchanged hooks, draft PR.
6. Exact final-head hosted checks to terminal outcomes, inspect artifacts and available
   Codecov ingestion evidence. Green workflow alone does not prove ingestion.

## Scope and remaining direction

60% measured .NET suite floor remains. 80% product aggregate and 90% critical changed
code are directions, not newly imposed or achieved gates. Live DB and unit flags are
execution suites with overlapping source, not additive product components. Per-report
branch counts cannot be naively summed across overlapping assemblies. Missing runtime
platforms and unexecuted code must remain visible. No claim about the user's unverified
6% Codecov flag. Carryforward can show historical coverage; report current-head evidence
separately. Broad CLI and IDE behavior expansion may require later batches.

## Evidence log

- Baseline in the default temp directory encounters four existing extractor path assertions
  because this environment injects `/tmp/.git`. A first escalated retry retained that mount.
  Rerun with native `/var/tmp` records normal fixture semantics without changing tests.
- Initial Codecov configuration includes broad `src/` unit/live-db flags and carryforward;
  autodiscovery previously consumed wildcard misses, duplicate reports and historical logs.
- Local `gh` authentication is invalid; authorized GitHub connector and Git transport work.
  PR and workflow inspection use connector APIs.

### Local measured results

| Scope | Baseline `cb45266` | After working tree based on `cb45266` |
| --- | --- | --- |
| .NET non-live suite | 2,204 pass / 5 Windows skips | 2,213 pass / same 5 skips |
| Measured C# union lines | 14,851/20,863 = 71.18% | 17,017/20,978 = 81.12% |
| CLI measured lines | 885/3,673 = 24.09% | 2,901/3,673 = 78.98% |
| Language Server lines | Missing from denominator | 104/115 = 90.43% |
| TypeScript production lines | Earlier native wrapper omitted 8/16 files; reported 96.76% is not comparable | 2,127/4,247 = 50.08%; all 16 files |
| TypeScript branches | Old denominator incomplete | 274/349 = 78.51% |

Per-report raw Coverlet branch counts (covered/valid), kept separate because suites overlap:
Core tests 8,691/14,725 → 9,426/14,809; Golden 1,772/14,533 unchanged;
Analyzers 724/1,259 unchanged; CodeFixes 438/1,371 unchanged;
Observability 227/362 unchanged. Raw reports contain generated compiler classes;
normalized upload manifests explicitly list omitted `/obj/` sources and recompute XML
summary metadata. The line gate's existing generated-output exclusion is preserved.

Local files are under `/workspace/.tmp/coverage-batch/` (not committed). The raw baseline
was produced before rebuilding changed tests; after measurements are uncommitted-tree
results until the implementation SHA is recorded in the PR. Final hook and hosted results
must identify the committed head separately.

- Eleven targeted CLI/LSP subprocess tests pass. Running instrumented subprocesses in
  this sandbox initially failed with a Coverlet named-mutex EROFS at process exit;
  native `/var/tmp` runs pass without changing application code, assertions or hooks.
- Initial independent review identified incorrect live DB expected count and stale XML
  totals after generated filtering. Both corrected; a no-match Observability run proves
  collector still emits a report when a filter selects zero tests. Live DB therefore
  expects all five reports, preserving its honest broad suite denominator.
- Added CLI manifest tests for report count, stale outputs, explicit upload file list,
  consistent XML totals and malformed branch counts. Fifteen validator tests pass.
- Retain current 60% gate. The measured .NET union exceeds 80%; TS, Windows and live DB
  are separate and this is not a product-wide aggregate. No 90% claim for all changed code.

### Remaining coverage work

Eight TS modules have zero real execution despite source-inspection tests; add extension-host
behavior tests in a subsequent batch. CLI remains below 80%, and existing test helpers that
launch separate source-tree binaries outside `CliProcessTestRunner` may still be unmeasured.
LSP malformed framing, debounce races, and cancellation deserve more protocol tests.
Windows coverage and live-DB coverage require their hosted OS/database runs. The original
unit/live-db Codecov flags retain carryforward; new Windows flag explicitly requires fresh
coverage. External upload availability stays non-blocking but local report integrity fails
closed. Inspect uploader processing and provider results separately from CI success.

- Independent re-review reran all 15 report tests and found no remaining static blockers.
  Full Python suite: 134 tests, 133 pass / one existing GitHub-auth-dependent skip.
  actionlint 1.7.12 and workflow policy pass. EN/VI docs sync passes.
- All five push workflows at exact PR62 merge SHA reached success: CI `38731273646`,
  Marketplace Extensions `38731273588`, Build Installers `38731273611`, Standards audit
  `38731273655`, Scorecard `38731273590`. No failure observed at that SHA.
