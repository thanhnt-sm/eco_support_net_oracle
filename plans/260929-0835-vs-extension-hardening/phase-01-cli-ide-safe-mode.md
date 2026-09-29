# Phase 01 — CLI `--ide-safe` mode

## Context
- Predict report §Security #1/#2; CLI contract scout (Summary events, exit codes).
- `src/DataGuard.Cli/Program.cs` validate handler `:186-472`, assess handler `:1633-1762`, `ResolveCommandConfiguration` `:1880`, `AcquireContractsAsync` `:2084`, `BuildContractsAsync` `:2160` (ManualContractSource → `Assembly.LoadFrom`).
- `src/DataGuard.Cli/CliConfigurationResolver.cs` (connection precedence).

## Overview
Priority: Critical · Status: done
IDE hosts run `validate`/`assess` against repositories the user may not trust. `--ide-safe` makes the CLI refuse every code-loading and outbound-connection path regardless of what `.dataguard.yml` or the environment says.

## Requirements
Functional:
- New global-ish option `--ide-safe` on `validate` and `assess`.
- validate + ide-safe: `GroundTruthMode` forced to Snapshot; `ManualAssemblyPath` cleared; `ConnectionString` from config file **and** `DATAGUARD_CONNECTION_STRING` ignored; `--connection`, `--offline`, `--assembly`, `--ef-snapshot`, `--ef-project`, `--ef-context` rejected with exit 2 and a clear stderr line; `AuditLogPath`/`TelemetryFileDirectory` cleared (no file writes at repo-chosen paths); one stderr line `ide-safe: suppressed <list>` when anything was suppressed.
- assess + ide-safe: `--allow-network` and `--remote-advisories` rejected (exit 2).
Non-functional: pure, unit-testable policy class ≤ 200 lines; no behaviour change without the flag.

## Architecture
`IdeSafePolicy` (new, `src/DataGuard.Cli/IdeSafePolicy.cs`): `Apply(DataGuardConfiguration, bool environmentConnectionPresent) → (DataGuardConfiguration, IReadOnlyList<string> Suppressed)` and `RejectedValidateOptions(...)`/`RejectedAssessOptions(...)` returning the first offending option name or null. Program.cs calls it right after `ResolveCommandConfiguration`.

## Related files
- Create: `src/DataGuard.Cli/IdeSafePolicy.cs`, `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs`
- Modify: `src/DataGuard.Cli/Program.cs`

## Implementation steps
1. Add `ideSafeOption` (`--ide-safe`, bool) near `progressOption`; add to `validateCommand` and `assessCommand` option lists.
2. Validate handler: read flag; if set, check rejected options → exit 2; after resolve, `IdeSafePolicy.Apply` → replace `config`; print suppression line to stderr.
3. Assess handler: if set and (`allowNetwork` or `remoteProvider != null`) → exit 2.
4. Tests: policy unit tests (suppresses Manual/assembly/connection/audit paths; no-op on clean config; rejects each option).

## Todo
- [x] option wiring  - [x] policy class  - [x] tests  - [x] `dotnet build` + `dotnet test tests/DataGuard.Core.Tests --filter IdeSafe`

## Success criteria
Config with `GroundTruthMode: Manual` + `ManualAssemblyPath` under `--ide-safe` never reaches `ManualContractSource`; `DATAGUARD_CONNECTION_STRING` never reaches an adapter; all rejected options exit 2 before any work.

## Risks
Behaviour drift for non-flag users → policy only runs when flag set; existing CLI tests unchanged.

## Security
This is the CLI half of the trust boundary. It does not protect a user who points VS at an old global CLI that lacks the flag — VS must detect the System.CommandLine parse error on stderr ("Unrecognized command or argument '--ide-safe'", exit 1) and refuse to fall back to an unsafe run (Phase 2).

## Next
Phase 2 passes `--ide-safe` unconditionally.
