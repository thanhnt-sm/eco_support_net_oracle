---
phase: 2
title: "VS extension run-lifecycle and trust-gate fixes"
status: completed
priority: P1
effort: "6h"
dependencies: [1]
---

# Phase 2: VS extension run-lifecycle and trust-gate fixes

## Overview
Close red-team findings 3, 4 (VS half), 5 (host half), 6, 8, 12, 13, 14 (VS half) in `src/DataGuard.VisualStudio/`: correct termination classification, require the `ide-safe: active` handshake, tighten old-CLI detection, key consent by solution file, add Forget-consent, fix threading and drain semantics, fix run-slot state, follow solution lifetime, and correct explainer/clear behaviour. Every change starts with a failing unit test against the pure collaborators.

## Requirements
- Functional:
  - `ProcessTerminator.StopProcess`: `taskkill` success → `Terminated`; otherwise `HasExited` → `AlreadyExited`; only a successful `Kill()` → `Terminated`. Extract `ClassifyAfterKillAttempt(bool killSucceeded, bool hasExited)`.
  - Timeout path: if termination is `AlreadyExited`, or the CLI exited with a normal code and `validation.sarif` exists, proceed to publish instead of discarding.
  - `ProgressReadResult` gains `IdeSafeAcknowledged` (line exactly `ide-safe: active`, first non-empty stderr line) and `SawAnyProgressEvent`. Publishing requires `IdeSafeAcknowledged`; "CLI too old" = `!IdeSafeAcknowledged && !SawAnyProgressEvent && exit == 1 && stderr line *starts with* `Unrecognized command or argument '--ide-safe'``. Any other missing-ack case → "CLI did not confirm ide-safe mode; results discarded" (distinct message, no `dotnet tool` guidance).
  - Remove every `dotnet tool install|update -g DataGuard.Cli` string; replacement text: "Reinstall the DataGuard extension (the bundled CLI is missing) or set Tools > Options > DataGuard > General > Custom CLI Executable Path to a dataguard.exe from GitHub Releases (verify its SHA-256)."
  - `SolutionTrustGate.ComputeConsentKey(solutionDirectory, solutionFilePath, configBytes)`: key includes the upper-cased full `.sln` path (second out-param of `GetSolutionInfo`, currently discarded at `DataGuardPackage.Commands.cs:57`). Prompt text says "this solution file".
  - New command `Forget Solution Consent` (Menus.vsct id 0x0105) removing the current key from the settings store; Output line confirms.
  - `RunCliAsync`: `ruleInventory.Clear()` moves after a successful `TryReserve`. `CliProcessRegistry` exposes `IsReserved` and `RequestCancelPending()`; Cancel while reserved-but-not-active prints "A run is waiting for consent; it will not start." and the post-modal check aborts without recording consent.
  - `Process.Start` runs off the UI thread (`await TaskScheduler.Default` before `StartAndRegister`); `File.ReadAllBytes` for the consent hash and `CreateRunDirectory` also run off the UI thread.
  - `ProgressStreamReader` parses on the calling (background) thread and enqueues Output text into a bounded channel flushed by one UI hop per batch; `CliRunSession` awaits parse completion (bounded by process exit + 3 s) separately from the UI flush, so `HasSummary`/`IdeSafeAcknowledged` are never lost to UI latency.
  - Subscribe `IVsSolutionEvents`: `OnBeforeCloseSolution` stops the active process (`StopProcess` + `TryMarkCancelled`) and clears the Error List; before `Publish`, re-read the current solution directory and discard with an Output line if it differs from the captured one.
  - Error List clear moves from run start to immediately before `Publish` (after consent, only when a SARIF exists); the run banner states "previous results are kept until new ones load".
  - `ExitCodeExplainer.Explain` gains `sarifExists`: validate exit 1 + summary + no SARIF → "[ERROR] The CLI reported a summary but failed to write results (see [DataGuard CLI] lines)"; exit 3 → previous Error List items preserved.
- Non-functional: all new files ≤ 200 lines; no `ValueTuple`/`TextManager.Interop`/records in the VSIX assembly; MSBuild `CreateVsixContainer=true` build passes.

## Architecture
`CliRunSession` splits into `CliRunSession` (start/wait/timeout) and `ProgressPump` (background parse + batched UI flush). `SolutionLifetimeWatcher` (new, `IVsSolutionEvents`) owns the advise cookie and calls back into the package. `ErrorListPresenter.Clear` is invoked by the publish step and by the lifetime watcher only.

## Related Code Files
- Modify: `ProcessTerminator.cs`, `CliRunTimeoutHandler.cs`, `CliRunSession.cs`, `ProgressStreamReader.cs`, `ProgressReadModels.cs`, `CliArgumentBuilder.cs`, `CliProcessRegistry.cs`, `SolutionTrustGate.cs`, `DataGuardPackage.cs`, `DataGuardPackage.Commands.cs`, `DataGuardPackage.Publishing.cs`, `ExitCodeExplainer.cs`, `Commands/Menus.vsct`, `DataGuardOptionsPage.cs` (description text)
- Create: `ProgressPump.cs`, `SolutionLifetimeWatcher.cs`, `tests/DataGuard.VisualStudio.Tests/ProcessTerminatorClassificationTests.cs`, `tests/DataGuard.VisualStudio.Tests/CliProcessRegistryTests.cs`, `tests/DataGuard.VisualStudio.Tests/ProgressPumpTests.cs`, `tests/DataGuard.VisualStudio.Tests/CliRunSessionLiveTests.cs` (spawns `cmd.exe`/PowerShell fakes for exit/timeout/handshake)

## Implementation Steps (tests first)
1. **Tests Before** (each must fail on current code):
   - `ClassifyAfterKillAttempt(false, true) == AlreadyExited`.
   - `ProgressStreamReader`: stream `PhaseStarted`, `[DG1290] C:\x\Unrecognized command or argument '--ide-safe'.csproj: …`, `Summary` → `IdeSafeUnsupported == false`; stream starting with `ide-safe: active` → `IdeSafeAcknowledged == true`; a stream without it → publish decision is "discard".
   - Old-CLI stream (single rejection line, exit 1, no events) → "CLI too old" decision; `dotnet tool` absent from every user-facing string (reflection/grep test over resources or a static string catalogue).
   - `ComputeConsentKey(dir, slnA, bytes) != ComputeConsentKey(dir, slnB, bytes)`.
   - `CliProcessRegistry`: `TryReserve` twice → false; `IsReserved` true before `StartAndRegister`; `RequestCancelPending` observed by a `ShouldStartAfterConsent()` helper → false.
   - Inventory: registry reserved + inventory item + `RunValidationAsync` → item survives (extract `ClearInventoryIfReserved` helper).
   - `ProgressPump`: Summary line whose UI write is delayed 4 s → `HasSummary` still true.
   - `ExitCodeExplainer.Explain("validate", 1, hasSummary: true, sarifExists: false)` contains "failed to write results".
   - Publish gate: captured solution dir ≠ current → discard message, no `Publish`.
2. **Implement** requirements; keep `DataGuardPackage*.cs` partials ≤ 200 lines each (move solution-lifetime and consent handling to `DataGuardPackage.Lifetime.cs` if needed).
3. **Tests After**: `SolutionLifetimeWatcher` decision helper tests; `CliRunSessionLiveTests` (timeout → Terminated; self-exit during kill → AlreadyExited path publishes; handshake missing → discard).
4. **Regression Gate**: `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` green; MSBuild packaging build + `scripts/assert-vsix.ps1` (Phase 4) pass locally.

## Success Criteria
- [ ] A run that finishes at the timeout boundary is published, never deleted
- [ ] Results are published only when the CLI acknowledged ide-safe; spoofed file names cannot trigger "CLI too old"
- [ ] No `dotnet tool` guidance anywhere in `src/DataGuard.VisualStudio/` or `src/DataGuard.VSCode/`
- [ ] Consent differs per solution file; Forget-consent command works and is logged
- [ ] Second command during a run neither wipes the inventory nor misreports on Cancel
- [ ] Process start and file hashing off the UI thread (test asserts `!ThreadHelper.CheckAccess()` via injected starter)
- [ ] Closing the solution stops the run and clears the Error List; a run finishing under another solution is discarded
- [ ] VSIX packaging build green

## Risk Assessment
- `IVsSolutionEvents` advise during `InitializeAsync` needs the main thread (already switched) — mirror the build-events pattern and unadvise in `Dispose`.
- Batched UI flush changes Output-pane timing; keep batches ≤ 50 ms or 32 lines so progress still feels live.
