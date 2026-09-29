# Phase 2 — VS extension run-lifecycle and trust-gate fixes (TDD)

Plan: `plans/260929-0952-vs-hardening-redteam-tdd-followup/phase-02-vs-extension-run-lifecycle-and-trust-gate-fixes.md`
Branch: `feat/vs-extension-hardening` (uncommitted; only `src/DataGuard.VisualStudio/` and `tests/DataGuard.VisualStudio.Tests/` touched; `packages.lock.json` untouched — pre-existing 9 `M` entries left as found; no git add/reset/commit/stash run).
Status: **completed** (all gates below green).

## 1. Tests Before (RED)

Baseline before any change: `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` → Passed 95, Skipped 1 (pre-existing `Navigate_WhenFileMissing_WritesOutputMessage`, needs live shell).

Stubs added so the test project compiled, each mirroring *current* behaviour so failures were runtime RED, not compile errors: `ProcessTerminator.ClassifyAfterKillAttempt` (taskkill-or-exited → Terminated), `CliRunTimeoutHandler.ShouldPublishAfterTimeout` (AlreadyExited only), `ProgressReadResult.IdeSafeAcknowledged/SawAnyProgressEvent` (never set), `PublishGate.Decide` (rejection line → CliTooOld else Publish), `ProgressPump` whose `Enqueue` blocked inline on the flush (`#pragma VSTHRD002`, deleted in GREEN), `ProgressStreamReader(RuleInventory, Action<string>)` wrapping the old awaited path, `CliProcessRegistry.IsReserved/RequestCancelPending/ShouldStartAfterConsent` (false/false/true), `RuleInventory.ClearInventoryIfReserved` (clear-then-reserve), `SolutionTrustGate.ComputeConsentKey(dir, sln, …)` ignoring the .sln, `ForgetConsent`/`ITrustConsentStore.Remove` → false, `ExitCodeExplainer.Explain(…, sarifExists)` ignoring the flag, `SolutionLifetimeWatcher.DescribeSolutionMismatch` → null.

Command (build clean, 0 warnings, then):
```
dotnet test tests/DataGuard.VisualStudio.Tests -c Release --no-build
→ Failed: 26, Passed: 120, Skipped: 1 (+1 more RED after fixing the pump test's discriminator, see below) = 27 RED
```
RED test names:
- `ProcessTerminatorClassificationTests.ClassifyAfterKillAttempt_OnlySuccessfulKillIsTerminated(false, true → AlreadyExited)`
- `ProcessTerminatorClassificationTests.ShouldPublishAfterTimeout_…` × 2 (Terminated + normal exit + SARIF rows)
- `ProgressStreamReaderTests.SpoofedFileNameContainingRejectionText_DoesNotFlagOldCli`
- `ProgressStreamReaderTests.IdeSafeActiveAsFirstLine_SetsAcknowledged`
- `ProgressStreamReaderTests.Verdict_WithoutAcknowledgement_IsHandshakeMissing_NotPublish`
- `ProgressStreamReaderTests.Verdict_OldCliStream_IsCliTooOld` (exit-2 row → HandshakeMissing)
- `ProgressStreamReaderTests.Verdict_RejectionLineButProgressEvents_IsHandshakeMissing`
- `ProgressStreamReaderTests.BaselineApplied_RendersAsWarningLine_NotRedacted`
- `CliArgumentBuilderTests.IsIdeSafeUnsupportedMessage_DetectsOnlyRejectionOfTheFlag` × 2 (spoofed `[DG1290] C:\x\Unrecognized…csproj` row; `error: unknown option '--ide-safe'` row flipped to false — intended change, starts-with rule)
- `CliArgumentBuilderTests.ExitCodeExplainer_SummaryWithoutSarif_ReportsWriteFailure`
- `CliArgumentBuilderTests.NoDotnetToolGuidance_AnywhereInExtensionSources` (walks up from the test assembly to `DataGuard.sln`, greps `*.cs`, `*.md`, `*.vsct` under `src/DataGuard.VisualStudio` for `dotnet tool (install|update)`)
- `SolutionTrustGateTests.ComputeConsentKey_DiffersPerSolutionFileInSameDirectory`
- `SolutionTrustGateTests.ForgetConsent_RemovesKeyAndReportsWhetherOneExisted`
- `SolutionTrustGateTests.BuildPromptText_SaysConsentIsPerSolutionFile`
- `CliProcessRegistryTests.IsReserved_TrueAfterReserve_BeforeStartAndRegister`
- `CliProcessRegistryTests.RequestCancelPending_WhileReservedWithoutProcess_PreventsStart`
- `CliProcessRegistryTests.CancelPending_IsResetByReleaseAndComplete`
- `CliProcessRegistryTests.ClearInventoryIfReserved_WhenSlotBusy_KeepsInventoryAndReturnsFalse`
- `CliProcessRegistryTests.ClearInventoryIfReserved_WhenSlotFree_ReservesAndClears`
- `ProgressPumpTests.Reader_WithUiFlushDelayedFourSeconds_StillCompletesParseAndKeepsSummary` (first version passed under the stub because the blocking happened synchronously inside the `ReadAsync` call; fixed by starting the read via `Task.Run` and bounding with `WhenAny(2 s)` — then RED)
- `ProgressPumpTests.Pump_WhenCapacityExceeded_DropsOldestAndReportsCount` (first version deadlocked the test host under the blocking stub; producer moved to `Task.Run` + 5 s bound = "Enqueue must never block")
- `ProgressPumpTests.Pump_WhenFlushThrows_ContinuesWithNextBatch` (final version waits on a "first flush attempted" signal instead of a 200 ms gap, so the two lines cannot coalesce into the throwing batch on a slow runner)
- `SolutionLifetimeWatcherTests.DescribeSolutionMismatch_DifferentOrClosed_ReturnsDiscardMessage` × 3

Characterization pins written first and already green (kept as regression guards): `ProcessTerminatorClassificationTests.DescribeTimeout_Characterization_NamesEachOutcome`; `ProgressStreamReaderTests.Characterization_PhaseStartedAndSummary_AreFormattedAndCounted`, `…_RejectionLineAtStart_FlagsIdeSafeUnsupported`, `…_IdeSafeAndBaselineStderrLines_AreEchoedNeverDropped`, `IdeSafeActiveNotExactFirstLine_IsNotAcknowledged` × 3, `Verdict_Acknowledged_IsPublish`; `CliProcessRegistryTests.TryReserve_Twice_SecondReturnsFalse`, `RequestCancelPending_WithoutReservation_IsIgnored`, `StartAndRegister_UsesInjectedStarter_AndTracksActiveProcess`; `ProgressPumpTests.Pump_FlushesInBatchesOfAtMostThirtyTwoLines_PreservingOrder`; `SolutionLifetimeWatcherTests.DescribeSolutionMismatch_SameDirectory_ReturnsNull` × 3; `SolutionTrustGateTests.ComputeConsentKey_ToleratesMissingSolutionFile`; existing explainer/trust-gate/reader tests updated to the new signatures (5-arg `Explain`, 3-arg `ComputeConsentKey`, `Action<string>` reader ctor) — no existing assertion weakened except the one intended `unknown option` row.

## 2. Files changed / created

Created (all ≤ 200 lines):
- `src/DataGuard.VisualStudio/ProgressPump.cs` (144) — bounded queue (drop-oldest + counted marker line), ≤ 32 lines / ≤ 50 ms per batch, one flush call per batch, flush errors logged and skipped.
- `src/DataGuard.VisualStudio/SolutionLifetimeWatcher.cs` (146) — `IVsSolutionEvents` (advise/unadvise cookie, `OnBeforeCloseSolution` → package callback) + pure `DescribeSolutionMismatch`/`IsSameSolutionDirectory`.
- `src/DataGuard.VisualStudio/CliRunModels.cs` (71) — `CliRunOutcome` (moved), `SolutionInfo`, `CliRunContext`.
- `src/DataGuard.VisualStudio/TrustConsentStores.cs` (94) — `SettingsStoreTrustConsentStore`/`InMemoryTrustConsentStore` split out of `SolutionTrustGate.cs` (which had reached 215 lines).
- `src/DataGuard.VisualStudio/DataGuardPackage.Consent.cs` (107) — `ReadSolutionInfoAsync`, `EnsureConsentAsync`, `ForgetSolutionConsentAsync`.
- `src/DataGuard.VisualStudio/DataGuardPackage.Lifetime.cs` (72) — `OnBeforeCloseSolution`, `CancelValidationAsync`.
- Tests: `ProcessTerminatorClassificationTests.cs` (47), `CliProcessRegistryTests.cs` (117), `ProgressPumpTests.cs` (147), `ProgressStreamReaderTests.cs` (146), `SolutionLifetimeWatcherTests.cs` (35), `CliRunSessionLiveTests.cs` (200).

Partials after the split: `DataGuardPackage.cs` 191 (was 203), `.Commands.cs` 135 (was 209), `.Publishing.cs` 150, `.Consent.cs` 107, `.Lifetime.cs` 72.

Modified: `ProcessTerminator.cs`, `CliRunTimeoutHandler.cs`, `CliRunSession.cs` (191), `ProgressStreamReader.cs`, `ProgressReadModels.cs` (+`PublishVerdict`, `PublishGate`), `ProgressLineParser.cs` (`BaselineApplied`, `IsProgressEvent`), `CliArgumentBuilder.cs` (starts-with rejection, `IdeSafeActiveLine`, `CreateProcess`), `CliProcessRegistry.cs`, `RuleInventory.cs` (`ClearInventoryIfReserved`), `SolutionTrustGate.cs`, `ExitCodeExplainer.cs`, `DataGuardPackage.cs/.Commands.cs/.Publishing.cs`, `Commands/Menus.vsct` (0x0105 "Forget Solution Consent"), `DataGuardOptionsPage.cs` (description), `overview.md` (Requirements section), `ErrorListPresenter.cs` (doc comment only), `OutputPaneWriter.cs` (**not in the phase's enumerated list but inside the ownership boundary**: added `WriteLinesAsync` = one main-thread hop per batch, and the banner line "Errors: previous results are kept until new ones load"), tests `CliArgumentBuilderTests.cs`, `SolutionTrustGateTests.cs`.

## 3. Design decisions

- **Termination**: `StopProcess` = HasExited → AlreadyExited; taskkill ok → Terminated; else HasExited → AlreadyExited; else `Kill()` → `ClassifyAfterKillAttempt(killSucceeded, hasExited)` (only a successful Kill is Terminated).
- **Timeout publish**: `ShouldPublishAfterTimeout(termination, hasExited, exitCode, sarifExists)` = AlreadyExited, or Terminated ∧ exited ∧ `IsNormalCliExitCode` (0–4) ∧ SARIF exists. Live test confirmed `taskkill /F` leaves a normal exit code, so SARIF existence is the effective gate in the Terminated case; a partial SARIF (killed mid-write) fails `SarifErrorListPublisher.Load` → error line + **previous Error List kept** (Publish is skipped when `loaded.Error != null`). `RunAsync` now takes `sarifPath`.
- **Handshake/verdict**: `IdeSafeAcknowledged` = first non-whitespace stderr line exactly `ide-safe: active` (ordinal, no trim). `IdeSafeUnsupported` stays a raw observation (line *starts with* `Unrecognized command or argument '--ide-safe'`, case-insensitive); `PublishGate.Decide`: acknowledged → Publish; `IdeSafeUnsupported ∧ !SawAnyProgressEvent ∧ exit == 1` → CliTooOld; else HandshakeMissing ("[DataGuard] CLI did not confirm ide-safe mode; results discarded.", status "DataGuard: Results discarded"). The `unknown option` wording was dropped (System.CommandLine never emits it).
- **Pump/threads**: `ProgressStreamReader` is synchronous per line and runs under `Task.Run`; it emits into `ProgressPump.Enqueue`; the pump loop (`Task.Run`) calls `OutputPaneWriter.WriteLinesAsync` (one `SwitchToMainThreadAsync` per batch). `CliRunSession` awaits parse completion (exit + 3 s, unchanged) and the pump flush separately (bounded 5 s, warning logged if exceeded), so `HasSummary`/`IdeSafeAcknowledged` never depend on UI latency. Process start: `await TaskScheduler.Default.SwitchTo(alwaysYield: true)` — plain `await TaskScheduler.Default` does not yield when the caller is already a pool thread (proved by the live test under xUnit), so the always-yield form guarantees the property regardless of caller. Package hops (`CreateRunDirectory`, consent hash) use `await TaskScheduler.Default` (caller is the UI thread there). No `System.Threading.Channels`/Dataflow (not in the VSIX whitelist); hand-rolled `Queue` + `SemaphoreSlim`.
- **Consent key**: `SHA256(UPPER(fullpath(dir)) | UPPER(fullpath(sln)) | configHash)`; empty/null .sln (Open Folder) → empty segment, no throw; `Path.GetFullPath` failures fall back to the raw string. **Every existing user is re-prompted once after upgrade** (all keys change). `ForgetConsent` removes only the key for the *current* config hash (stale keys for older config versions stay, harmless). Prompt now says "this solution file" and names the Forget command.
- **Run slot**: `RuleInventory.ClearInventoryIfReserved` reserves first, clears second. `RequestCancelPending` applies only when reserved with no active process; `ShouldStartAfterConsent` checked twice — after the modal before `RecordConsent`, and again at the top of `ExecuteRunAsync` immediately before the process would start (covers a Cancel that lands after consent or on the already-consented no-modal path; "Cancel was requested before <command> started; nothing was executed."); flag reset by `TryReserve`/`ReleaseReservation`/`Complete`.
- **Solution lifetime**: `OnBeforeCloseSolution` (main thread) → `StopProcess` + `TryMarkCancelled` + Output line + `ErrorListPresenter.Clear()`; publish re-reads `GetSolutionInfo` on the main thread and discards on mismatch. Error List is now only replaced by `Publish` (which clears immediately before adding) or cleared on solution close; the run-start clear and its comment block were removed. Cancelled/timed-out/exit-3/no-SARIF paths say previous results were kept.
- **Explainer**: `Explain(command, exitCode, hasSummary, warningCount, sarifExists)`; summary ∧ no SARIF ∧ exit 0/1 (both commands — verified with the bundled CLI extracted from the built .vsix against an empty directory: exit 0, `ide-safe: active` first stderr line, `v.sarif` written (375 bytes, `runs` array) with zero findings, so a clean run always has a SARIF and this rule cannot fire on the happy path) → "[ERROR] The CLI reported a summary but failed to write results (see [DataGuard CLI] lines)"; exit 3 → "…; previous Error List items were preserved." Tuple `switch (command, exitCode)` (pre-existing, compiled to `ValueTuple`) replaced with plain branching to honour the "no ValueTuple" constraint literally.
- **Strings**: all `dotnet tool` guidance replaced by the mandated reinstall/custom-path/SHA-256 sentence (`ReinstallGuidance` const); options description and `overview.md` rewritten (bundled CLI first after the custom path, matching `CliLocator`).
- `BaselineApplied` → `[DataGuard] [WARN] baseline: n violations suppressed by <path>`; `ide-safe:`/`baseline:` text lines were already echoed (redacted, `[DataGuard CLI] ` prefix) — pinned by test.

## 4. Tests After + regression gate

Tests After: `CliRunSessionLiveTests` (cmd.exe fakes written to `%TEMP%`, `>&2 echo` used because `echo … 1>&2` appends a trailing space that breaks the exact handshake): `Timeout_WithoutSarif_TerminatesTreeAndDiscards` (Terminated, tree dead), `Timeout_WhenProcessExitsDuringKill_AlreadyExitedPathPublishes` (injected stopper waits for exit and classifies with `killSucceeded:false` → publishes, exit 0, acknowledged), `Timeout_TerminatedAfterSarifWasWritten_StillPublishes` (real taskkill; asserts normal exit code), `HandshakeMissing_ResultsAreDiscarded`, `OldCli_RejectionLineAndExitOne_IsCliTooOld`, `ProcessStarter_RunsOffTheCallingSynchronizationContext` (marker `SynchronizationContext`; starter observes null context + pool thread). `SolutionLifetimeWatcherTests` were written in the Before step (pure helper).

Gate:
- `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` → **Passed 152, Failed 0, Skipped 1** (was 95/0/1), ~5 s.
- `dotnet build src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -c Release` → 0 Warning(s), 0 Error(s); test project build → 0 Warning(s), 0 Error(s).
- `dotnet format whitespace DataGuard.sln --verify-no-changes` → exit 0 (after `dotnet format whitespace DataGuard.sln --include src/DataGuard.VisualStudio/ tests/DataGuard.VisualStudio.Tests/` normalized the CRLF my python patch script had introduced; nothing outside the two owned folders was changed by the format run).
- MSBuild VSIX packaging (`MSBuild.exe src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -restore -t:Build -p:Configuration=Release -p:CreateVsixContainer=true -p:DeployExtension=false -m:1`): exit 0, 0 warnings, produced `src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix` (50,481,198 bytes on the final re-run after the last two edits); zip contains `cli/dataguard.exe`, `DataGuard.VisualStudio.dll`, `DataGuard.VisualStudio.pkgdef`, `extension.vsixmanifest`, `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll` (same list `ci.yml` asserts). Note for CI/local use under Git Bash: `/restore`-style switches get path-converted; use `-restore` or `MSYS_NO_PATHCONV=1`. `packages.lock.json` files unchanged by the publish step (still the 9 pre-existing `M` entries).

## 5. Deviations / skips

- `OutputPaneWriter.cs` edited (not in the enumerated list; within `src/DataGuard.VisualStudio/`). `TrustConsentStores.cs` and `CliRunModels.cs` created beyond the listed files to keep every code file ≤ 200 lines.
- `IsIdeSafeUnsupportedMessage` no longer accepts `unknown option '--ide-safe'` (theory row flipped) — spec says starts-with the System.CommandLine text.
- "Summary but no SARIF" message applies to exit 0 as well as 1 and to `assess` too (spec named validate/1 only) — same failure mode.
- Cancelled-run Output wording changed from "Validation cancelled by user. No diagnostics were produced." to "<command> was stopped before completion. No diagnostics were produced; previous Error List results were kept." (solution-close also lands here; the Cancel command still prints its own "cancelled by user" line first).
- `ProgressLineParser.cs` is 198 lines (≤ 200, not new). `CliRunSessionLiveTests.cs` exactly 200.
- No Phase 1 CLI change was needed. `scripts/assert-vsix.ps1` (Phase 4) not run here — not owned; CI step mirrored manually (see §4).

## 6. Unresolved questions

1. Should `ForgetConsent` also purge keys recorded for earlier `.dataguard.yml` versions of the same solution (would need the store to enumerate keys, or a per-solution prefix scheme)? Currently only the current-config key is removed.
2. The one-time re-prompt for every existing user after this upgrade (key now includes the .sln path) — acceptable for the release notes, or should Phase 4 call it out explicitly?
3. `OnBeforeCloseSolution` runs `StopProcess` synchronously on the UI thread (taskkill wait ≤ 5 s worst case). Acceptable, or prefer fire-and-forget with the publish gate as the only guard?
4. Terminated-after-SARIF publish trusts `taskkill`'s exit code being in 0–4 (observed: normal); if a future CLI ever exits 5+ for a legitimate outcome, extend `IsNormalCliExitCode`.

## Review fixes (VS)

Executor: fullstack-developer · 2026-09-29 · branch `feat/vs-extension-hardening` (HEAD c37a2ce, uncommitted). Scope: M2 and M4 (close path) from `plans/reports/code-reviewer-260929-1412-vs-hardening-followup.md`. Ownership respected: `src/DataGuard.VisualStudio/`, `tests/DataGuard.VisualStudio.Tests/` only; no docs, no lock files, no commit.

### RED

Tests written first; the RED state was a compile break on the new contracts (9 errors: CS1739/CS1061/CS7036/CS0117), plus one theory row whose expectation flips: `Terminated, hasExited, exitCode -1, sarif` was `false` (exit code outside 0-4) and is now `true` (the exit code no longer participates).

- `CliArgumentBuilderTests.ExitCodeExplainer_TerminatedAtTimeout_DoesNotInterpretTaskkillExitCode` - exit code 1 (taskkill), with and without a summary, and `assess`/0/warnings: all yield the exact new string; without the flag exit 1 still reads "found errors".
- `ProcessTerminatorClassificationTests.ShouldPublishAfterTimeout_PublishesAlreadyExitedOrTerminatedWithSarif` - rewritten to the 3-parameter decision (`termination, hasExited, sarifExists`); `Failed` rows stay `false` even with SARIF and exit.
- `ProcessTerminatorClassificationTests.TerminatedWithSarif_PublishesAndExplainsTerminationInsteadOfExitCode` - exit code 1 + SARIF present -> publish, and `Explain(..., terminatedAtTimeout: true)` equals the new wording.
- `CliRunSessionLiveTests.Timeout_TerminatedAfterSarifWasWritten_StillPublishes` - line 92 swapped one-for-one: `IsNormalCliExitCode(exitCode)` -> `outcome.TerminatedAtTimeout.Should().BeTrue(...)` (real `taskkill /T /F`; file stays exactly 200 lines).
- `SolutionLifetimeWatcherTests.RequestStopAsync_ReturnsBeforeTheStopCompletes_ThenRecordsCancellation` - injected stopper blocks on a `ManualResetEventSlim`; the returned task is asserted **not completed** while the stopper blocks (a synchronous implementation fails this after 5 s), then the event is released and `registry.WasCancelled(process)` becomes true. Registry populated with the injected no-op starter (`StartAndRegister(process, _ => { })`).
- `SolutionLifetimeWatcherTests.RequestStopAsync_AlreadyExited_DoesNotRecordCancellation` - an `AlreadyExited` stop must not mark the run cancelled (its results still publish).

### GREEN - files

- `src/DataGuard.VisualStudio/CliRunTimeoutHandler.cs` - `ShouldPublishAfterTimeout(termination, hasExited, sarifExists)` = `AlreadyExited || (Terminated && hasExited && sarifExists)`. `hasExited` is kept because `TryTaskKill` returns on taskkill's own exit, not the target's, and `RunAsync` reads `process.ExitCode` afterwards. `Failed` remains a discard (unchanged; the lead's "OR sarif exists" was applied to the Terminated branch only). Doc comment no longer states the false "normal exit code" premise.
- `src/DataGuard.VisualStudio/ExitCodeExplainer.cs` - new `TerminatedAtTimeoutExplanation` const; `Explain(..., bool terminatedAtTimeout = false)` returns it first, before every exit-code branch. `IsNormalCliExitCode` removed (dead after the clause drop; the live test was its only other caller).
- `src/DataGuard.VisualStudio/CliRunModels.cs` - `CliRunOutcome.TerminatedAtTimeout` (documented: the exit code is then taskkill's).
- `src/DataGuard.VisualStudio/CliRunSession.cs` - sets the flag right after the timeout publish decision (`termination == Terminated`); `TryGetExitCode` removed (unused). 181 lines.
- `src/DataGuard.VisualStudio/DataGuardPackage.Publishing.cs` - passes the flag to `Explain`, always prints the explanation for a terminated run, and logs `TimedOutExitCode` (-1) instead of taskkill's code for that run.
- `src/DataGuard.VisualStudio/SolutionLifetimeWatcher.cs` - pure `RequestStopAsync(process, stop, markCancelled)`: `Task.Run` -> stop -> mark -> outcome. Added `using Task = System.Threading.Tasks.Task;` (the `Microsoft.VisualStudio.Shell` import otherwise makes `Task.Run` ambiguous). 169 lines.
- `src/DataGuard.VisualStudio/DataGuardPackage.Lifetime.cs` - `OnBeforeCloseSolution` keeps `ThrowIfNotOnUIThread` and the synchronous `errorListPresenter.Clear()`; the stop is now `JoinableTaskFactory.RunAsync(async () => { await SolutionLifetimeWatcher.RequestStopAsync(process, ProcessTerminator.StopProcess, this.processRegistry.TryMarkCancelled); await output.WriteAsync(...); }).FileAndForget("DataGuard/SolutionClosing")` - the first await is the pool task, so the UI thread returns immediately; a late result is discarded by the publish gate's solution re-check.

### Exact new explainer string (for docs)

```
[WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List.
```

Emitted as `[DataGuard] [WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List.` in the Output pane, after the existing `[DataGuard] validate timed out after N seconds and its process tree was terminated.` line and the `completed in N ms with exit code 1` line. The CHANGELOG sentence "terminated only after exiting with a normal CLI exit code" must be reworded to "terminated after its SARIF output had already been written" (docs agent).

### Gate

- `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` -> **Passed 155, Failed 0, Skipped 1** (was 152/0/1), ~5 s; the live taskkill test passes with the new flag.
- `dotnet build src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -c Release` (full, including the bundled-CLI publish) -> 0 Warning(s), 0 Error(s). Note: a first attempt failed on another agent's in-flight `src/DataGuard.Cli/SafeWritablePath.cs` (CS1734, not owned here); the retry after their fix is the recorded result. The extension-only compile (`-p:CreateVsixContainer=false`) was 0/0 throughout.
- MSBuild VSIX packaging (`MSBuild.exe ... -p:Configuration=Release -p:CreateVsixContainer=true -p:DeployExtension=false -restore -m:1`) -> exit 0, `src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix` (50,483,090 bytes).
- `dotnet format whitespace DataGuard.sln --verify-no-changes` -> exit 0. All touched files LF; every touched code file <= 200 lines (largest: live test 200, session 181). No `record`/`ValueTuple`/`TextManager.Interop` introduced.

### Out of scope, recorded

- M4 second half (`Process.Start` inside the registry lock, `CliProcessRegistry.StartAndRegister`): **not touched**. It is not a two-line change - it needs start outside the lock, register inside, then a re-check of `cancelPending` with a conditional stop, plus a registry test for the Cancel-during-start race. Follow-up for the lead.
- `ReportOutcomeAsync` still prints `completed in N ms with exit code 1` before the new explanation for a terminated run; the explanation immediately disclaims the code. Left as is to keep the change minimal.
