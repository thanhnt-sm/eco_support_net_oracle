---
phase: 11
title: "IDE feature completion"
status: in-progress
priority: P1
effort: "7d"
dependencies: [5, 8, 10, 12]
---

# Phase 11: IDE feature completion

## Overview

Deliver the user-facing VS Code and Visual Studio capabilities currently promised by canonical docs: trusted-workspace local feedback, commands, provider/settings surfaces, safe summaries, results views, and controlled live operations. This phase implements capability rather than deleting claims; it never sends unredacted streams, connects to a database on keystroke, or assumes arbitrary SQL text is a stored procedure.

## Requirements

- Functional: satisfy VS Code claims in `docs/03-components/tooling/vscode-extension.md:3,19-38,76-117,151-213` and `docs/01-overview/feature-showcase.md:222-225`.
- Functional: satisfy Visual Studio claims in `docs/03-components/tooling/visual-studio-extension.md:81-237` and `feature-showcase.md:229-232`, including settings, results, cancellation, and explicit snapshot/baseline flow.
- Security: retain workspace trust, user/machine-only CLI path policy, `shell: false`, SARIF workspace/solution containment, secret redaction, and bounded in-memory/output retention from Phase 5.
- Safety: live validation, snapshot refresh, and baseline creation require explicit user command/confirmation and a resolved config/credential path; assessment is local by default. Online advisory consent is supplied only after Phase 10.

## Architecture

```text
VS Code document changes -> trusted stdio Language Server -> local shared SQL classifier -> DG001 diagnostics
VS Code explicit commands -> RunCoordinator -> CLI (validate / assess / snapshot refresh) -> SARIF -> contained diagnostics/results
Visual Studio commands/options -> safe CLI runner -> redacted bounded output + SARIF -> Error List + Results Tool Window
```

- Consume Phase12's completed `DataGuard.SqlClassification` library/API and golden fixtures from the new `DataGuard.LanguageServer`; it has no DB provider, credential, or network dependency. Phase12 owns creation and generator migration (XR04).
- VS Code owns a global RunCoordinator. CP8 must resolve the documented global-single-run contract versus current per-workspace behavior; implement the selected global contract, including cancellation/cleanup of the replaced run.
- Language Server Protocol delivers local, debounced document-change diagnostics. Full CLI validation remains explicit, never a keystroke side effect.
- VS Code commands: Validate, Cancel, Assess, and Refresh Snapshot/Baseline. Resource settings include provider, auto-validate mode/delay, and snapshot output; CLI path remains machine/user scope.
- Visual Studio gets a `DialogPage`/`ProvideOptionPage`, Show Settings, Results Tool Window, and Refresh Snapshot/Baseline command. Its runner must use a cancellation token source linked to timeout and package disposal.
- `DataGuard.VisualStudio` targets .NET Framework (`net472` in the current project). Verify the availability/behavior of `ProcessStartInfo.ArgumentList` on the actual target before using it. If unavailable, add a tested internal argument builder; do not assume a modern API compiles or quotes safely.

## Related Code Files

- Create: `src/DataGuard.LanguageServer/` project, stdio protocol host, local diagnostic service, and test project/harness.
- Modify: `src/DataGuard.Analyzers/Analyzers.cs` only to consume the shared local classifier after Phase 12 owns its semantic contract.
- Modify: `src/DataGuard.VSCode/src/{extension.ts,security.ts,security.test.ts}`, `src/DataGuard.VSCode/package.json`, and VS Code test fixtures.
- Modify: `src/DataGuard.VisualStudio/{DataGuardPackage.cs,Commands/DataGuard.vsct,DataGuard.VisualStudio.csproj}`; add Windows-capable test seams/project only when it can target the real VSIX framework.
- Modify after proof: the referenced tooling docs and `docs/01-overview/feature-showcase.md` with actual command/settings names and safety limits.

## Implementation Steps

1. Freeze CP8 decisions: global versus per-workspace coordination; namespace/API policy for shared diagnostics; and the user-visible definition of “real time.” Record the decision before extension edits.
2. Consume and verify Phase12's frozen classifier input/output contract; limit this phase to LSP adaptation and shared-golden-fixture conformance. Reuse generator marker suppression; do not expose SQL text or connection data through LSP logs.
3. Build the stdio language server with trusted-local startup only, incremental document updates, debounce/cancellation, and diagnostic clearing. It must never call CLI, load plugins, read a DB, or resolve credentials on change events.
4. Extend VS Code manifest and runner: rich commands/settings, global RunCoordinator, safe config/provider argument assembly, contained SARIF paths, and redacted bounded line streaming plus parsed SARIF count summary.
5. Implement Assess as local-first. Phase 10's consented remote option is visible only after it supplies a completed consent/configuration contract; no extension fallback may turn it on.
6. Add Visual Studio Option Page, VSCT commands, Tool Window model, safe process lifecycle, and SARIF-to-Error List/results mapping. Snapshot refresh shows the exact provider/config and asks for confirmation before process start.
7. Add a net472 compile probe and Windows test seam for process argument construction. Use `ArgumentList` only if this probe and actual VSIX build prove compatibility; otherwise use the reviewed fallback builder.
8. Update EN/VI docs only after implementation proof. “Raw stdout/stderr” means redacted, bounded streaming; it may not be implemented as unredacted raw output.

## Tests Before

- Add pure VS Code tests for URI containment, provider/settings validation, redaction, output caps, and RunCoordinator replacement.
- Add Language Server protocol fixtures for open/change/close, marker suppression, cancellation, and no-CLI/no-network assertions.
- Add Windows-oriented Visual Studio seams for argument construction, solution containment, cancellation/timeout, and Options value resolution; missing Windows infrastructure is an explicit gate, never a passing skip.

## Success Criteria

- [x] Trusted VS Code edits receive debounced local SQL diagnostics without a CLI/DB/network call; the stdio protocol smoke covers initialize, didOpen, didChange replacement, and didClose clearing.
- [ ] Validate, Assess, and confirmed Snapshot/Baseline commands execute the documented safe CLI arguments and report sanitized summaries.
- [ ] A second global run deterministically replaces/cancels the first according to CP8; temp outputs and stale diagnostics are removed.
- [ ] VS Code and VS never render an external SARIF artifact as an editor/Error List document.
- [ ] VS Options, Results Tool Window, Run, Cancel, and confirmed baseline/snapshot commands work in a Windows VSIX smoke test targeting net472.
- [ ] Streams, stack/error text, and command display are redacted and bounded; no test fixture secret reaches either IDE surface.

## Risk Assessment

- LSP and Roslyn can diverge unless the shared classifier has golden fixtures consumed by both hosts.
- “Auto validate on build” and global coordination can create repeated processes; explicit state transitions and end-to-end process tests are required.
- Visual Studio API availability differs from modern .NET; the net472 compile/VSIX gate controls implementation choice.
- A provider setting must not become an implicit authorization to access a database; only explicit commands with validated configuration may run live work.
