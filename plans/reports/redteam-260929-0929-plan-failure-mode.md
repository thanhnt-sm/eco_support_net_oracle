# Red-team: Failure Mode Analyst / Flow Tracer — VS extension hardening (branch `feat/vs-extension-hardening`, HEAD b47b36b)

Scope: `plans/260929-0835-vs-extension-hardening/{plan,phase-01..03}.md` vs shipped code under `src/DataGuard.VisualStudio/`, `src/DataGuard.Cli/Program.cs`, `.github/workflows/{ci,release}.yml`. Every hop below was read from the working tree at HEAD; line numbers are from `cat -n`.

Known findings from the prior review (cancel race, orphan after failed termination, consent-store exceptions, Error List clear policy) are only referenced where new evidence extends them; the numbered findings are new.

## Finding 1: Timeout-vs-exit race discards a completed run and deletes its SARIF (`AlreadyExited` is effectively unreachable)
- **Severity:** High
- **Location:** Phase 2, Architecture rows `ProcessTerminator.cs` / `CliRunSession.cs`; plan.md "Deferred from review (#5)" covers only the Cancel variant
- **Flaw:** `ProcessTerminator.StopProcess` classifies "the CLI exited on its own while taskkill was running" as `Terminated`. The timeout handler then treats a successfully finished run as killed.
- **Failure scenario:** `ValidationTimeoutSeconds` is 300; the CLI writes `validation.sarif` at t=299.9 s and begins exiting. At t=300 the timer fires. `HasExited` is still false, `taskkill` is spawned, the CLI exits before taskkill finds it (taskkill reports the process as not found, non-zero → `false`), `process.HasExited` is now true → `Terminated`. The `&& !exitTask.IsCompleted` re-check at `CliRunSession.cs:66` runs *before* `StopProcess` and cannot close this window: `exitTask` is a threadpool `WaitForExit()` (`:63`) whose completion lags the real exit, and the race is between `ProcessTerminator.cs:30` and the taskkill return. VS prints "validate timed out after 300 seconds and its process tree was terminated", logs exit code -1, publishes nothing, and deletes the temp dir containing a complete SARIF. User re-runs, pays another 5 minutes.
- **Evidence (traced path):**
  - `CliRunSession.cs:66-68` — timer wins → `CliRunTimeoutHandler.HandleAsync`
  - `CliRunTimeoutHandler.cs:35` → `ProcessTerminator.StopProcess`
  - `ProcessTerminator.cs:30-33` `HasExited` false → `:35` `TryTaskKill` → `:96` `killer.ExitCode == 0` false → `:36-39` `if (taskkillSucceeded || process.HasExited) return Terminated;` — the only `AlreadyExited` return is `:32`, before the kill attempt
  - `CliRunTimeoutHandler.cs:37` + `:67-68` prints "process tree was terminated"; `:60` returns `Terminated`
  - `CliRunSession.cs:69-72` `termination != AlreadyExited` → returns outcome with `ProceedToPublish=false`
  - `DataGuardPackage.Commands.cs:142-146` logs `TimedOutExitCode`, returns → `:164-169` finally → `:168 TempDirectoryCleaner.SafeDeleteDirectory(temporaryDirectory)` deletes the finished SARIF
  - Same classifier feeds the Cancel path: `DataGuardPackage.cs:186-187` → `CliProcessRegistry.cs:78` (`Terminated && owns`) — this is the known cancel race; the timeout variant and the dead `AlreadyExited` branch at `CliRunTimeoutHandler.cs:44-45, :69-70` are new
- **Suggested fix:** In `StopProcess`: `if (taskkillSucceeded) return Terminated; if (process.HasExited) return AlreadyExited;` (exit-on-its-own is not a termination). Additionally, in the timeout path, if `File.Exists(sarifPath)` and the process exited with a normal code, publish instead of discarding.
- **Test that would catch it:** Extract `ClassifyAfterKillAttempt(bool killSucceeded, bool hasExited)`; assert `(false, true) == AlreadyExited` — currently returns `Terminated`.

## Finding 2: `process.Start()` runs on the UI thread, inside the registry lock
- **Severity:** Medium
- **Location:** Implementation gap not covered by any phase (Phase 2 moved the code into `CliRunSession` unchanged; `main:src/DataGuard.VisualStudio/DataGuardPackage.cs:866` had the same shape)
- **Flaw:** Nothing between the mandatory `SwitchToMainThreadAsync` in `RunCliAsync` and `Process.Start()` moves off the UI thread. `CreateProcess` of the bundled self-contained single-file `dataguard.exe` (`DataGuard.VisualStudio.csproj:96`: `--self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true`) blocks devenv's message loop; on first launch after install (unsigned — plan.md defers Authenticode signing) AV/SmartScreen image scanning makes this seconds, not milliseconds.
- **Failure scenario:** User installs the VSIX, opens a solution, clicks Run Validation, answers Yes to the consent dialog → VS goes "Not Responding" for the duration of the AV scan of the bundle. Also on the UI thread in the same handler: `File.Exists` on the config, `Directory.CreateDirectory` for the run dir, and `File.ReadAllBytes(.dataguard.yml)` for the consent hash (network-share solutions).
- **Evidence (traced path):**
  - `DataGuardPackage.Commands.cs:49` `SwitchToMainThreadAsync` → `:87` `File.Exists(configPath)` → `:88` `EnsureConsentAsync` → `:180` `SolutionTrustGate.ComputeConsentKey` → `SolutionTrustGate.cs:50` `File.ReadAllBytes(configPath)` (UI thread)
  - `:97` `SwitchToMainThreadAsync` again → `:101` `TempDirectoryCleaner.CreateRunDirectory()` (`TempDirectoryCleaner.cs:20-21`, UI thread)
  - `:132-133` `new CliRunSession(...).RunAsync(...)` — no `TaskScheduler.Default` / `Task.Run` in between
  - `CliRunSession.cs:58` `this.registry.StartAndRegister(process)` → `CliProcessRegistry.cs:46-48` `lock (this.gate) { process.Start(); ... }`
  - `DataGuardPackage.cs:125-126` confirms the command handler starts on the UI thread (`JoinableTaskFactory.RunAsync(handler)` from an `OleMenuCommand` callback)
- **Suggested fix:** `await TaskScheduler.Default;` before `StartAndRegister` (the session's later awaits already rejoin the main thread via JTF); keep the lock only for the field writes: start outside the lock, then register under the lock, or hold the lock but start on a background thread.
- **Test that would catch it:** Session test with a fake starter delegate asserting `!ThreadHelper.CheckAccess()` at the moment `Start` is invoked.

## Finding 3: `ruleInventory.Clear()` executes before the reservation check, wiping the live run's inventory
- **Severity:** Medium
- **Location:** Phase 2, Requirement 8 ("`RunAssessmentAsync` clears `_ruleInventory`") — the clear was placed before the single-run guard
- **Flaw:** Both entry points clear the package-wide `RuleInventory` unconditionally, then `RunCliAsync` rejects the second command because a run is already reserved. The rejected command has already destroyed the running session's rule list.
- **Failure scenario:** Validation is running (2 min in, 40 `RuleExecuted` events collected). User clicks Assess, or a build finishes with "Run Validation on Build" enabled. Output pane says "A DataGuard command is already running" — but when the running validation reaches its Summary, the rule inventory banner lists only the rules executed after the click, and the same truncated list goes to the diagnostic log.
- **Evidence (traced path):**
  - `DataGuardPackage.Commands.cs:25-29` `RunValidationAsync` → `:27` `this.ruleInventory.Clear()` → `:28` `RunCliAsync` → `:65-69` `TryReserve()` false → return (clear already happened). Same for `RunAssessmentAsync` `:33`.
  - Build path: `BuildEventsHandler.cs:34-41` → `Commands.cs:37-45` `RunValidationOnBuildAsync` → `:43` `RunValidationAsync(fromBuild: true)` → `:27` clear
  - The running session shares the same instance: `Commands.cs:132` `new CliRunSession(this.processRegistry, this.ruleInventory, ...)` → `CliRunSession.cs:62` `new ProgressStreamReader(this.inventory, ...)` → `ProgressStreamReader.cs:74` `inventory.Add`, `:91` `inventory.BuildBanner()` at Summary
- **Suggested fix:** Move `ruleInventory.Clear()` after a successful `TryReserve` (inside the `try` in `RunCliAsync`), or give each `CliRunSession` its own `RuleInventory`.
- **Test that would catch it:** Reserve the registry, add an item to the inventory, call `RunValidationAsync(false)`; assert the inventory still contains the item.

## Finding 4: Results are published into whichever solution is open when the CLI finishes
- **Severity:** Medium
- **Location:** Phase 2, Requirement 1 (trust gate keyed by solution) and the review-cycle "Error List clear-at-run-start policy (#2)"; no phase subscribes to solution lifetime
- **Flaw:** The solution directory is captured once at start; nothing observes solution close/open. A run can last up to 900 s (options clamp 5–900). Package `Dispose` (which does stop the process) runs at devenv shutdown, not at solution close.
- **Failure scenario:** User starts validation on Solution A (large, 6 min), closes A, opens Solution B, builds → Output says "A DataGuard command is already running for this solution" (about A). A's run completes → Error List of B fills with A's diagnostics (absolute paths under A; `File.Exists` passes so Navigate opens A's files inside B's session). They persist until the next consented run for B starts, because Clear only runs at run start.
- **Evidence (traced path):**
  - `DataGuardPackage.Commands.cs:57-64` `GetSolutionInfo` captured once; `:111` used as `WorkingDirectory`
  - No `IVsSolutionEvents` / `AdviseSolutionEvents` / `OnAfterCloseSolution` anywhere under `src/DataGuard.VisualStudio/*.cs` (grep returned nothing)
  - `DataGuardPackage.cs:91-101` `Dispose` is the only place the active process is stopped outside Cancel/timeout
  - `Commands.cs:156` `PublishSarifAsync(sarifPath, solutionDirectory)` → `DataGuardPackage.Publishing.cs:29` `Load(sarifJson, solutionDirectory)` filters against the *old* directory (`SarifErrorListPublisher.cs:68`) → `:42` `errorListPresenter.Publish` into the current Error List
  - `ErrorListPresenter.cs:94-110` Navigate only checks `File.Exists(task.Document)`
  - `Commands.cs:65-69` "already running for this solution" is printed for B while A's process holds the slot
- **Suggested fix:** Advise `IVsSolutionEvents`; on `OnBeforeCloseSolution` stop the active process (reuse `StopProcess` + `TryMarkCancelled`) and `errorListPresenter.Clear()`; before `Publish`, re-read the current solution dir and discard if it differs from the captured one.
- **Test that would catch it:** Session completes after the "current solution" provider returns a different directory → `Publish` must not be called and Output must say the run was discarded.

## Finding 5: 3-second post-exit drain grace silently drops the Summary (and `IdeSafeUnsupported`) for a successful run
- **Severity:** Medium
- **Location:** Phase 2, Architecture row `CliRunSession.cs` ("start, timeout, drain, cancel decisions")
- **Flaw:** Every stderr line requires a UI-thread hop before the reader advances. If the backlog of hops after process exit is not flushed within 3 s (+0.5 s), the session closes the streams and substitutes an empty `ProgressReadResult`, losing `HasSummary`, counts, and the old-CLI flag — even though the SARIF is fine and gets published.
- **Failure scenario:** Build-triggered validation fires immediately after a build, when the UI thread is busiest (post-build project reloads, Error List refresh from the compiler). The CLI emits its final burst (`RuleExecuted` × N + `Summary`) and exits; the reader is still queued behind UI work. Result: diagnostics appear in the Error List, but the Output/status bar say "Result summary unavailable" with exit code 0 and no explainer, and the log records 0/0. Conversely a stalled reader also stops draining stderr while the CLI is still running, which is one way to reach the Finding 1 timeout path.
- **Evidence (traced path):**
  - `ProgressStreamReader.cs:37-47` per-line `ProcessLineAsync` → `:86` `await this.writeOutput(...)` → `OutputPaneWriter.cs:34-40` `WriteAsync` → `:37` `SwitchToMainThreadAsync` (and `:38` `GetPaneAsync` → `:92` another switch)
  - `CliRunSession.cs:125-135` `FinishDrainsAsync`: `:127` 3 s race, `:132` `CloseStreams`, `:133` 500 ms, then returns regardless
  - `CliRunSession.cs:78` `outcome.Progress = stderrReadTask.IsCompleted ? await stderrReadTask : new ProgressReadResult();`
  - Consumer: `DataGuardPackage.Publishing.cs:62-65` explainer skipped when exit 0 and no summary; `:68-72` "DataGuard: Result summary unavailable"; `Commands.cs:148` `IdeSafeUnsupported` read from the same (possibly empty) object
- **Suggested fix:** Parse stderr fully on a background thread (Summary/inventory/ide-safe detection are pure), queue Output text and flush it in batches; wait on the *parse* completion (bounded by process exit) rather than on UI flush; only the UI writes get a grace period.
- **Test that would catch it:** Feed a stream ending in a Summary line to `ProgressStreamReader` with a `writeOutput` that delays 4 s on that line; run through `CliRunSession` with a fake exited process; assert `outcome.Progress.HasSummary == true` (currently false).

## Finding 6: `assess` from VS is permanently broken when any ancestor of `%TEMP%` is a reparse point; `validate` is not — and there is no override
- **Severity:** Medium
- **Location:** Phase 1 (CLI half) has no requirement for write-path policy parity; Phase 2, Architecture row `TempDirectoryCleaner.cs` hardcodes the temp root
- **Flaw:** The CLI's `IsSafeWritablePath` walks every ancestor directory and refuses if any is a link/reparse point. `WriteSarifAssessment` treats that as fatal; the validate SARIF writer has no such check, and `summary.json` treats it as non-fatal. VS always writes under `Path.GetTempPath()` with no configurable location.
- **Failure scenario:** Corporate laptop with FSLogix / relocated user profile (`C:\Users\<user>` is a junction), or `%TEMP%` redirected via junction. Run Validation works and shows diagnostics. Assess Workspace: Output shows "[DataGuard CLI] Assessment failed: Refusing to write SARIF through a symbolic link or invalid path.", "assess completed ... with exit code 4", "Validation produced no SARIF diagnostics." The user has no setting to change and the message points at a link they cannot remove.
- **Evidence (traced path):**
  - `DataGuardPackage.Commands.cs:101-102` `CreateRunDirectory()` → `TempDirectoryCleaner.cs:20` `Path.Combine(Path.GetTempPath(), "DataGuard", guid)`; `:115` `BuildAssessArguments(..., sarifPath)`
  - `Program.cs:1845-1848` `if (!IsSafeWritablePath(outputPath)) throw new InvalidOperationException(...)` → `Program.cs:28-73` walks `current = current.Parent` to the root checking `LinkTarget`/`ReparsePoint`
  - `Program.cs:1799-1802` generic catch → stderr "Assessment failed: ..." + `ExitCode = 4`; no SARIF written
  - Validate path: `Program.cs:430-434` `FileSarifSink` → `DiagnosticEmitter.cs:321-331` (`WriteStreamingAsync` / `ContractExportWriter.WriteAtomicallyAsync`) — no ancestor link check; `Program.cs:440-460` `summary.json` refusal swallowed as non-fatal
  - VS side: `DataGuardPackage.Publishing.cs:17-20` "produced no SARIF diagnostics"
- **Suggested fix:** Apply one policy to both writers (check the final path component only, or check that the resolved directory is the intended one), and/or let VS pick a run directory it controls (e.g., under the extension's `LocalAppData` folder) with an Options override.
- **Test that would catch it:** Create a junction to a temp dir; run `assess --format sarif --output <junction>\x.sarif --ide-safe`; expect a SARIF file and exit code ≠ 4 (currently exits 4 with no file).

## Finding 7: Cancel during the consent modal (reserved-but-not-started window) reports "already completed; processing diagnostics"
- **Severity:** Medium
- **Location:** Phase 2, Requirement 1 (modal prompt) — the registry's `commandReserved` state is invisible to Cancel
- **Flaw:** The reservation is taken before the modal (correct — a second Run is rejected), but `CancelValidationAsync` only knows about `Active`. With the modal open (it pumps messages, so the Cancel menu is reachable) or in the window between reservation and `StartAndRegister`, Cancel prints a message describing a state that does not exist, and cannot abort the pending run: after the user clicks Yes the CLI still starts.
- **Failure scenario:** User clicks Run, sees the consent dialog, decides against it and hits Tools > DataGuard > Cancel (or the keyboard binding) instead of "No". Output: "The command already completed; processing diagnostics." No process exists. User clicks Yes reflexively → run starts and consent is now persisted for this solution.
- **Evidence (traced path):**
  - `DataGuardPackage.Commands.cs:65` `TryReserve()` → `:88` `EnsureConsentAsync` → `:193-199` `VsShellUtilities.ShowMessageBox` (modal, message-pumping) → `:206` `RecordConsent` → `:133` `RunAsync` → `CliRunSession.cs:58` `StartAndRegister`
  - `DataGuardPackage.cs:181-183` `processToStop = this.processRegistry.Active` → null → `outcome = AlreadyExited` → `:196-197` "The command already completed; processing diagnostics."
  - `CliProcessRegistry.cs:16-18, :21-33` — `commandReserved` exists but no reader exposes it; `Active` (`:54-63`) returns only `activeProcess`
- **Suggested fix:** Expose `IsReserved`; in Cancel, when reserved-but-not-active, print "No DataGuard process is running; a run is waiting for consent" and set a `cancelRequested` flag that `RunCliAsync` checks after the modal returns (release reservation, do not record consent).
- **Test that would catch it:** Registry in reserved state → cancel decision must yield a "pending, nothing to stop" message, not `AlreadyExited`.

## Deepenings of prior findings (not counted as new)
- **Consent store `Record` failure:** `SolutionTrustGate.cs:135-150` logs to the file logger only; the store is chosen once at `DataGuardPackage.cs:73` (`CreateConsentStore`, `:129-143`) with no retry → a transient settings-store failure at package load means the in-memory store for the whole session (build-triggered runs skip via `Commands.cs:186-190` until a manual run in that devenv session, every session), and a persistent `Record` failure means the modal on every run with no Output-pane line explaining why.
- **Orphan resurrects the deleted temp dir:** after `Failed` termination, `Commands.cs:168` deletes the run dir while the CLI is still alive; the CLI's writers recreate it (`DiagnosticEmitter.cs:338-339` `Directory.CreateDirectory`, `Program.cs:97-98`) and leave a SARIF + `summary.json` in `%TEMP%\DataGuard\<guid>` until the next devenv start (sweep only runs from `DataGuardPackage.cs:65-69`).

## Traced and cleared (for adjudication)
- **`--ide-safe` code-loading contract:** `Program.cs:224` passes `efProjectPath` (rejected under ide-safe), not `--project`, to `ResolveEfSnapshotSource` (`:1934-1975`); `:300-303` only calls `EfModelSource.ExtractFromModelSnapshotAsync` (syntax/JSON-based, `EfModelSource.cs:216`) when a snapshot path exists; assembly loading lives in `ExtractFromTrustedCompiledModelSnapshotAsync` (`:613-638`) which is not reachable from validate; `ProjectCSharpSqlSource.cs:114-118` builds metadata references from already-loaded AppDomain assemblies, not repo `bin/`; `BuildContractsAsync` (`:2196-2260`) reaches `ManualContractSource` only when `GroundTruthMode == Manual`, which `IdeSafePolicy.Apply` forces to Snapshot. No MSBuild workspace in Cli/Core (only `DataGuard.Build` references `Microsoft.Build.Utilities.Core`).
- **summary.json secrets:** `ConnectionDiscovery.cs:105-111, :149` masks credentials before storing `ConnectionInfo`.
- **release.yml dirty tree / ci.yml equality assert:** `release.yml:392-406` rewrites manifest + `ExtensionVersion.cs` in the job's working tree only; no step commits or tags; `ci.yml:3-8` has no tag trigger, so the equality assert (`ci.yml:175-177`) never sees the rewritten tree.
- **Old global CLI + `CustomCliPath`:** `Commands.cs:148-154` reports "CLI too old" with the two remedies; not silent.
- **`NuGetLockFilePath` with cached `obj/`:** `ci.yml:36-42` caches only `~/.nuget/packages` in `build-and-test`; the VSIX job (`:131-189`) has no cache and passes `RestoreLockedMode=false` (`csproj:96`), so a stale `obj/cli-publish.packages.lock.json` would be overwritten, not enforced.
- **Partial SARIF read:** VS reads only after exit + drain (`CliRunSession.cs:75`, `Commands.cs:156`); the CLI renames within the same directory (`DiagnosticEmitter.cs:341-343` temp file, `ContractExport.cs:237` `File.Move(..., overwrite: true)`).
- **`CloseStreams` while `ProcessLineAsync` is mid-`SwitchToMainThreadAsync`:** `CliRunSession.cs:132` closes the reader; the stream reader resumes after `writeOutput` returns (`ProgressStreamReader.cs:86`), the next `reader.ReadAsync` at `:37` throws `ObjectDisposedException`, caught at `:50-53`, `:55-58` flushes the partial line, method returns normally; `drains` does not fault. (The data loss is the grace-window substitution in Finding 5, not an exception.)
- **`Complete(process)` after `Dispose`:** `CliProcessRegistry.cs:89-108` tolerates a detached process; `Dispose` only runs at devenv shutdown.
- **Navigate handlers after `Clear()`:** `ErrorListPresenter.cs:94-110` closures hold `task` and `provider`; whether `TaskProvider.Navigate` tolerates a task no longer in `Tasks` is not verifiable without the VS shell source — not asserted.

**Status:** DONE
