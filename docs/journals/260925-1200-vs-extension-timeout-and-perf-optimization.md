---
date: 2026-09-25
title: "VS extension timeout configuration and progress stream performance optimization"
status: completed
---

# VS extension timeout configuration and progress stream performance optimization

## Decision

- Configurable timeouts added to Visual Studio Tools -> Options -> DataGuard: `ValidationTimeoutSeconds` (default 300s, range 5–900) and `AssessmentTimeoutSeconds` (default 60s, range 5–900) with backing field clamping in setters and `[DefaultValue]` attributes.
- STA thread safety: `UpdateSolution_Done` wraps options lookup in `JoinableTaskFactory.RunAsync` and awaits `SwitchToMainThreadAsync()` before invoking `package.GetDialogPage()`, preventing RPC/COM deadlocks when build events fire from worker threads. `ExecuteCliCommandAsync` reuses the initial STA `options` reference.
- `Task.Delay` timer leak prevention: Wrapped delay tasks in `CancellationTokenSource`, cancelling tokens immediately upon `Task.WhenAny` resolution. Secondary 120s drain cleanup also utilizes CTS cancellation.
- Progress stream backpressure elimination: In `TryFormatProgress`, suppressed individual `ContractDiscovered` and `RuleExecuted` (violations == 0) lines by returning `true` with `formatted = null`. `FormatProgressLine` returns `ParsedProgress(null, ...)` directly, avoiding fallback redaction and cutting ~43,000 UI thread marshaling hops down to ~20 lines per run.
- Window activation decoupling: Removed per-line `pane.Activate()` from `WriteOutputAsync`. Centralized activation in `ActivateOutputPaneAsync()` called once during command initiation (`WriteCommandBannerAsync`, `ExecuteViewRules`, `ViewLogsAsync`).

## Verification

- `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj`: 50 passed, 0 failed.
- Added 5 test methods covering progress suppression, violation formatting, phase boundaries, summary preservation, and boundary clamping theories.
- Code review via dedicated subagent: Score 9.8/10, 0 critical issues, 0 warnings.
- Documentation synchronized across English and Vietnamese guides in `docs/03-components/tooling/`.

## Remaining

- Long-term: decouple pipe ingestion via `System.Threading.Channels` if multi-GB SARIF streaming is ever required.
