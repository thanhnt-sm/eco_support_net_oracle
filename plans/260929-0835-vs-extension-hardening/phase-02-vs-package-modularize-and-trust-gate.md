# Phase 02 — VS package modularization, trust gate, and fixes

## Context
- Predict report (all personas); VS Code parity scout (trust gate at `extension.ts:352-355`, machine-scoped `cliPath`, no auto-install).
- `src/DataGuard.VisualStudio/DataGuardPackage.cs` (1 629 lines), `DataGuardLogger.cs` (501), tests in `tests/DataGuard.VisualStudio.Tests/`.
- Build constraint: CreatePkgDef reflects over the package assembly; earlier crashes from `ValueTuple` and `TextManager.Interop` (commits 63afdc8, 1b4f869, 824b559).

## Overview
Priority: Critical · Status: done

## Requirements
Functional:
1. Trust gate: before any CLI run for a solution, require consent stored per `(solution dir, SHA-256 of .dataguard.yml or "absent")` in the VS user settings store (`ShellSettingsManager` → `WritableSettingsStore`, collection `DataGuard\TrustedSolutions`). First run → modal `VsShellUtilities.ShowMessageBox` (Yes/No) describing: solution path, config present?, that the run is ide-safe (no code loading / no DB connection), and that CLI reads repo files. Build-triggered runs never prompt: if no consent → skip with Output-pane line.
2. Always pass `--ide-safe`. If the CLI exits 1 (System.CommandLine unknown-option rejection) and stderr mentions `--ide-safe`, report "CLI too old for ide-safe mode" and do **not** rerun without it.
3. Remove `TryAutoInstallCliAsync`; replace with actionable message.
4. Custom CLI path must be rooted; no solution-relative resolution.
5. `Navigate` → `errorListProvider.Navigate(task, VSConstants.LOGVIEWID_Code)` after `File.Exists` check.
6. SARIF publisher: per-result try/skip (count skipped), 2 000-task cap with banner line, `Uri.UnescapeDataString` on relative URIs, `SuspendRefresh/ResumeRefresh` around bulk add.
7. Redact every `ex.Message` write; widen `Redact` regex (`user id=…;password=` forms, `pwd`, JWT `eyJ…`).
8. `RunAssessmentAsync` clears `_ruleInventory`.
9. Version: `ExtensionVersion.Current` reads `extension.vsixmanifest` next to the assembly (fallback constant `0.2.3`); used by logger banner and `InstalledProductRegistration` text.
10. Exit-code explainer: validate 3 → "validation incomplete — see CLI lines above (rule unavailable for provider, or no contract source)"; validate 1 with no Summary → "CLI failed before producing a summary".
11. Missing `.dataguard.yml` → Output line warning that only source-only rules ran.
Non-functional: every new file ≤ 200 lines; `DataGuardPackage.cs` ≤ 200 lines of wiring + command handlers; internal types have no `ValueTuple`/`TextManager.Interop` in signatures.

## Architecture (new files under `src/DataGuard.VisualStudio/`)
| File | Responsibility (moved from DataGuardPackage.cs) |
|---|---|
| `CliArgumentBuilder.cs` | `Quote`, `BuildValidateArguments`, `BuildAssessArguments`, skip-rules filter |
| `ProcessTerminator.cs` | `StopProcess`, `ProcessStopOutcome`, `DrainAsync` |
| `ProgressLineParser.cs` | `AppendProgressChar`, `TryFormatProgress`, `FormatProgressLine`, `ParsedProgress`, `IsJsonPayload`, `ProgressReadResult` |
| `RuleInventory.cs` | `RuleInventoryItem`, thread-safe list, `BuildRuleInventoryBanner` |
| `SarifErrorListPublisher.cs` | `ResolveSarifArtifactUri`, `ConvertSarifPosition`, `LoadTasks` (pure parse → list), cap + skip counters |
| `SolutionTrustGate.cs` | consent key, settings-store read/write, prompt text |
| `TempDirectoryCleaner.cs` | `CleanStaleTempDirectories`, `SafeDeleteDirectory`, `TryDeleteFile` |
| `ExitCodeExplainer.cs` | `Explain(command, exitCode, hasSummary)` |
| `ExtensionVersion.cs` | manifest-backed version |
| `CliRunSession.cs` | the per-run orchestration formerly inside `RunCliAsync` (start, timeout, drain, cancel decisions) |
| `DataGuardPackage.cs` | InitializeAsync, commands, Error List provider ownership, output pane, thin delegation |

## Related files
Create: files above + `tests/DataGuard.VisualStudio.Tests/{SolutionTrustGateTests,SarifErrorListPublisherTests,CliArgumentBuilderTests}.cs` (the `ExitCodeExplainer` tests live in `CliArgumentBuilderTests.cs`).
Modify: `DataGuardPackage.cs`, `DataGuardLogger.cs`, `DataGuardOptionsPage.cs` (description text), existing tests (retarget statics to new classes).
Delete: none.

## Implementation steps
1. Extract pure statics first (ArgumentBuilder, ProgressLineParser, RuleInventory, SarifErrorListPublisher.LoadTasks, TempDirectoryCleaner, ExitCodeExplainer, ProcessTerminator) with tests retargeted. Build `dotnet test` (BuildingForTesting).
2. Add `SolutionTrustGate`, `ExtensionVersion`, `CliRunSession`; rewrite `RunCliAsync` as wiring.
3. Remove auto-install; rooted-only custom path (update the relative-path test to assert empty).
4. Navigate fix, cap, unescape, redact fixes, inventory clear, missing-config warning.
5. Gate: MSBuild `/p:CreateVsixContainer=true` build succeeds; VSIX contains cli/dataguard.exe + analyzers.

## Todo
- [x] step 1  - [x] step 2  - [x] step 3  - [x] step 4  - [x] step 5 gate

## Success criteria
All VS tests green; new tests for gate/publisher/args/explainer; VSIX packaging build passes locally; no `TextManager.Interop` reference; each file ≤ 200 lines (DataGuardPackage.cs ≤ ~220 incl. attributes).

## Risks
- CreatePkgDef TypeLoadException on new internal types → keep signatures to BCL/Shell types only; gate step 5.
- `ShellSettingsManager` requires `Microsoft.VisualStudio.Shell.Settings` namespace (Shell.15.0, already referenced via SDK metapackage).

## Security
Consent hash invalidates when repo changes `.dataguard.yml`; build events never prompt; `--ide-safe` always on; no solution-relative executable resolution.
