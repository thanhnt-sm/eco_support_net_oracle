---
phase: 5
title: "Tooling and IDE safety"
status: in-progress
priority: P1
effort: "L"
dependencies: [3]
---

# Phase 5: Tooling and IDE safety

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Repair hook installation and make analyzer/code-fix/IDE behavior truthful. Program.cs stays Terra’s CLI integration surface; native/Husky, Roslyn, VS Code, VS and disjoint tests may split only after Sol grants it.

## Requirements

- Husky/native install writes script content, uses POSIX `/bin/sh` (not `&>`), and sets executable Unix mode rather than `ReadOnly` pretending to chmod.
- Managed-marker install/uninstall preserves user hooks; no wholesale deletion. Backup, force and ownership behavior require tests.
- Generated validation uses documented plain Snapshot validation; `--offline` without Manual assembly is invalid and must never be emitted.
- Code-fix providers advertise only actionable IDs, guard null roots, and have ID → registration → `ApplyChangesOperation` → final-document tests. Never generate unknown type/direction attributes.
- VS Code accepts SARIF locations only within workspace under Phase-4 policy and retains cancellation/process cleanup. VS must remain Run/Cancel-only; Windows is a separate gate.
- V01 is a bounded compatible dev-tool dependency/lock update only after fresh audit; no `npm audit fix --force` or unrelated major upgrade.

## Architecture

Hooks are idempotent managed-marker transformations with explicit ownership/backup. Tooling boundaries are lightweight analyzer, actionable Roslyn provider, and IDE-to-CLI invocation; source and documentation must report those boundaries rather than emulate missing features.

## Related Code Files

- `src/DataGuard.Cli/{Hooks/PreCommitHookInstaller.cs,Program.cs}`
- `src/DataGuard.{CodeFixes/CodeFixProviders.cs,Analyzers/Analyzers.cs}`
- `src/DataGuard.VSCode/src/{extension.ts,security.ts,security.test.ts}` and `package{,-lock}.json`
- `src/DataGuard.VisualStudio/{DataGuardPackage.cs,Commands/DataGuard.vsct}`
- `tests/DataGuard.CodeFixes.Tests/CodeFixProviderTests.cs`, `tests/DataGuard.Analyzers.Tests/{GeneratorExecutionTests,DescriptorArityTests}.cs`
- Findings CI-04/06, TL-01/TL-04–TL-06, V01; contracts `reports/safety-design.md` and `./findings-ledger.md`.

## Implementation Steps

1. Sol approves managed-marker/backup/force/uninstall matrix. Detect ownership before every edit; default preserves foreign content.
2. Fix Husky `hookContent` write. Make native/Husky scripts POSIX sh and executable on Unix; do not remove unowned user content during uninstall.
3. Test temp native/Husky repos: content/shebang, `sh -n`, repeat install, force, backup, managed/unmanaged uninstall, mode/ownership. Assert generated command never contains invalid offline Manual invocation.
4. Reconcile `FixableDiagnosticIds` with registrations; remove unsupported IDs instead of unsafe fixes. Remove dead DG002 prefix misuse while keeping canonical parameter-type semantics; guard null roots.
5. Add behavior-first Roslyn matrix for advertised provider/ID, transformed text, duplicate attributes, marker suppression, FixAll and no-op naming. Remove/defer unused empty-metadata helper only with caller search proof.
6. Apply Phase-4 containment contract to VS Code relative/file URI resolver; test absolute inside/outside, prefix, escape, encoded/malformed URI, link behavior and cancel/child cleanup.
7. Freshly audit VS Code dev dependency graph; if a compatible update is justified, review lock diff then run `npm ci`, `npm test`, build/package smoke. Do not claim runtime hardening.
8. Preserve VS Run/Cancel scope; update `docs/03-components/tooling/{analyzers,code-fixes,cli,vscode-extension,visual-studio-extension}{,.vi}.md` in this batch, and record Windows VSSDK/VSIX lifecycle as Phase 7 gate, never macOS substitution.

## Success Criteria

<!-- RT-04: accepted -->
Hook lifecycle is transactional: same-directory unique temp → write/flush → set and verify Unix mode → atomic replace. Retain original bytes/mode and ownership fingerprint until commit. Failure/cancel before commit leaves live hook unchanged; recovery restores only a verified owned backup, never overwrite user edits made meanwhile. Fault-inject write/flush/chmod/replace/postcondition/cancel/restore. Lefthook configuration receives the same managed-content preservation requirement; no wholesale file delete.

<!-- RT-04: security boundary merged -->
Validate authorized destination roots and complete ancestor chain before hook/config writes, not only file contents. Git worktree `.git` can be a file referencing a real git/common directory: resolve with read-only Git commands and obtain explicit authority before writing shared/external hooks, rather than blindly requiring repository containment or following arbitrary links. Reject linked `.husky`/lefthook targets and unexpected reparse paths; recheck before publish. Tests linked parent/target, worktree layout, outside-target-unchanged and malicious backup paths. Do not silently honor externally redirected core.hooksPath.

<!-- RT-02 RT-03: accepted -->
Both IDEs check execution/acquisition status and exit category before declaring completion. Preserve valid partial SARIF diagnostics but label the run incomplete/failed, not clean or ordinary findings. Windows `ErrorTask.Document` gets solution-root containment, not only VSCode. Test status handling for nonzero with valid SARIF, no SARIF, malformed SARIF and cancellation/timeout; runtime Windows unavailable remains V04 blocked. Characterize existing VSCode machine-scoped cliPath setting rather than creating a cosmetic configuration fix.

- [x] Hook lifecycle/mode/ownership tests pass without user-content deletion.
- [x] Generated hook commands are valid for supported ground-truth modes.
- [x] Every advertised fix has safe observable transformation or is absent.
- [x] External SARIF locations are rejected; cancellation/process behavior remains tested.
- [x] Any V01 update is compatibility-scoped with fresh audit evidence.

## Risk Assessment

Hooks execute user workflows, so preservation outranks cleanup. Roslyn actions edit developer code; avoid guessed metadata. Unix mode and Visual Studio proof must remain platform-separated.
