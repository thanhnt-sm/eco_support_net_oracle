---
phase: 1
title: "CLI ide-safe handshake and contract hardening"
status: pending
priority: P1
effort: "4h"
dependencies: []
---

# Phase 1: CLI ide-safe handshake and contract hardening

## Overview
Make `--ide-safe` verifiable by hosts and complete: a positive acknowledgement line, an explicit `--allow-env-connection` opt-in for user-supplied credentials, in-process environment scrubbing, a visible baseline-applied warning, regex/parallelism bounds, and write-path parity for `assess`. Closes red-team findings 1 (CLI half), 5, 10, 11, 14 (CLI half).

## Requirements
- Functional:
  - When `--ide-safe` is active, the CLI writes exactly one stderr line `ide-safe: active` **before** any other output (before the first progress event). Hosts require this line before publishing results.
  - New option `--allow-env-connection` (validate only): under `--ide-safe`, keep a connection string that came from `DATAGUARD_CONNECTION_STRING`; config-file `ConnectionString` is still stripped. Without `--ide-safe` the option is a no-op. Suppression line names what was kept: `ide-safe: kept environment connection (--allow-env-connection)`.
  - After `IdeSafePolicy.Apply`, clear `DATAGUARD_CONNECTION_STRING` (unless `--allow-env-connection`), `VAULT_TOKEN`, `VAULT_ADDR`, `AWS_*` secret vars and every `ConnectionStrings__*` variable in-process so downstream re-reads (`CredentialManager`, `ZeroTrustCredentialProvider`, `AutoDetectionEngine`) cannot recover them.
  - When a baseline filters violations, emit progress event `{"Kind":"BaselineApplied","Phase":"Validating rules","Detail":"<relative path>","Data":{"SuppressedCount":n}}` and, without `--progress`, a stderr line `baseline: n violations suppressed by <path>`.
  - Under `--ide-safe` clamp `MaxDegreeOfParallelism` to `min(value, ProcessorCount)`, `MaxViolationQueueSize` to ≤ 100 000, `ValidationTimeoutSeconds` to ≤ 900; report clamps in the suppression line.
  - Set `AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1))` at CLI startup; rewrite `PhantomIdentifierRule.SelectListRegex` to a linear form (`\bSELECT\s+([^\r\n]*?)\s+FROM\b` is still lazy; use `\bSELECT\b\s*(.*?)\s*\bFROM\b` with `RegexOptions.NonBacktracking` on .NET 9) and cap SQL literal length fed to rules (256 KiB, larger literals skipped with a `DG1291`-style note).
  - `assess` SARIF write uses the same policy as `validate`: check only that the final directory resolves inside the requested output directory (no ancestor reparse-point walk).
  - `assess` relativises `ToolError.Path` against the workspace before echoing to stderr.
- Non-functional: no behaviour change without `--ide-safe`; every new branch covered by a test written first.

## Architecture
`IdeSafePolicy.Apply(config, environmentConnectionPresent, allowEnvConnection)` returns the sanitized record plus `Suppressed`; a new `IdeSafeEnvironment.Scrub(allowEnvConnection)` performs the in-process env clearing; `Program.cs` prints the `ide-safe: active` line immediately after parsing, before `ProgressEmitter` construction. `BaselineManager`/`FilterNewViolations` gets a count out-param used for the new event.

## Related Code Files
- Modify: `src/DataGuard.Cli/IdeSafePolicy.cs`, `src/DataGuard.Cli/Program.cs` (validate `:186-500`, assess `:1633-1810`), `src/DataGuard.Core/Reporting/ProgressEvent.cs` (add `BaselineApplied`), `src/DataGuard.Core/Rules/PhantomIdentifierRule.cs:22-40`, `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` (literal cap), `src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs:123`
- Create: `src/DataGuard.Cli/IdeSafeEnvironment.cs`, `tests/DataGuard.Core.Tests/IdeSafeEndToEndTests.cs`, `tests/DataGuard.Core.Tests/PhantomIdentifierRuleRegexTests.cs`

## Implementation Steps (tests first)
1. **Tests Before** — add failing tests:
   - `IdeSafePolicyTests`: `Apply(..., allowEnvConnection: true)` keeps an env-sourced connection and strips a config-sourced one; clamps parallelism/queue/timeout; `Suppressed` lists clamps.
   - `IdeSafeEndToEndTests` (process-level, pattern from `CliExitCodeTests`): run `validate --ide-safe --project <fixture> --progress` with `DATAGUARD_CONNECTION_STRING=Server=127.0.0.1,1;Connect Timeout=1` and a config with `GroundTruthMode: Manual` + `ManualAssemblyPath`; assert first stderr line is `ide-safe: active`, no adapter connection attempt (exit 0 within 5 s), and `--allow-env-connection` changes the suppression line.
   - Baseline test: fixture with one violation and a baseline suppressing it → stderr contains `baseline: 1 violations suppressed`; with `--progress` a `BaselineApplied` event.
   - `PhantomIdentifierRuleRegexTests`: `"SELECT" + new string(' ', 200_000) + "x"` completes < 1 s.
   - Assess write-path test: output under a junction → SARIF written, exit ≠ 4.
   - Assess echo test: tool error path is workspace-relative.
2. **Refactor/implement** the items in Requirements.
3. **Tests After**: `IdeSafeEnvironment.Scrub` unit tests (variables cleared / kept).
4. **Regression Gate**: `dotnet build DataGuard.sln -c Release` (TreatWarningsAsErrors) and `dotnet test tests/DataGuard.Core.Tests -c Release` all green; existing `IdeSafePolicyTests` unchanged in intent.

## Success Criteria
- [ ] `ide-safe: active` is the first stderr line of every `--ide-safe` run; absent otherwise
- [ ] `--allow-env-connection` keeps only env-sourced credentials; config credentials always stripped
- [ ] End-to-end test proves no socket/adapter construction under `--ide-safe` with a live env credential
- [ ] Baseline suppression is visible in text and progress modes
- [ ] Regex DoS fixture finishes < 1 s; parallelism clamps applied and reported
- [ ] `assess --ide-safe` writes SARIF under a junctioned `%TEMP%`

## Risk Assessment
- Adding a progress `Kind` breaks hosts that reject unknown kinds → VS `TryFormatProgress` returns false for unknown kinds and prints `[structured diagnostic redacted]`; Phase 2 adds the handler first, and the order of merge is the same PR.
- `NonBacktracking` unsupported for lookarounds → the rewritten regex must avoid them; test covers the semantics on the golden corpus.
