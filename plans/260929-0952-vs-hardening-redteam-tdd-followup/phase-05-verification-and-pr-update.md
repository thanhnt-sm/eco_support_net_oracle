---
phase: 5
title: "Verification and PR update"
status: in-progress
priority: P1
effort: "1h"
dependencies: [1, 2, 3, 4]
---

# Phase 5: Verification and PR update

## Overview
Prove the follow-up end to end, update PR #24, and hand the remaining owner actions back.

## Requirements
- Functional:
  - Full suites: `dotnet test DataGuard.sln -c Release` (Core ≥ 829 + new, VS ≥ 95 + new, Analyzers 13, CodeFixes 24, GoldenCorpus 28), `npm test` in `src/DataGuard.VSCode` (≥ 82).
  - MSBuild `CreateVsixContainer=true` packaging build + `scripts/assert-vsix.ps1`; `git status` shows no `packages.lock.json` change.
  - End-to-end scenarios against the built CLI (scratch solution): (a) hostile config under `--ide-safe` → first stderr line `ide-safe: active`, suppression line, SARIF produced; (b) `--ide-safe --allow-env-connection` with an unreachable env host → connection attempted and fails fast (proves the carve-out) ; (c) simulated old CLI (a stub `dataguard.exe` printing the rejection line, exit 1) → VS "CLI too old" path, VS Code error notification, nothing published; (d) spoofed `.csproj` name → assess results still published; (e) run that completes at the timeout boundary → published.
  - `code-reviewer` (Fable) pass on the diff; Critical/High fixed before push.
  - Push to `feat/vs-extension-hardening`; PR #24 description updated with the follow-up section and the "CLI ≤ 0.2.2 rejected by IDE hosts" note; `gh pr checks` watched until the VSIX packaging gate reports.
- Non-functional: journal entry in `docs/journals/`; `plans/ACTIVE_SESSION_REGISTER.md` updated.

## Implementation Steps
1. Run the suites and the packaging build; fix regressions.
2. Execute e2e scenarios (a)–(e) from the scratchpad; record outputs in `plans/reports/tester-<ts>-followup-e2e.md`.
3. Dispatch `code-reviewer`; triage; re-run affected tests.
4. Commit by explicit path (no `.claude/`, no lock files, no AI attribution); push; update PR body; record CI results.
5. Owner hand-back list: reserve/publish `DataGuard.Cli` on nuget.org; decide VSIX Authenticode signing; re-run the two rate-limited red-team lenses (supply chain, Core/adapters) after 2026-10-01 07:00 Asia/Bangkok.

## Success Criteria
- [ ] All suites green; packaging gate green locally and on CI
- [ ] Scenarios (a)–(e) recorded with verbatim output
- [ ] PR #24 updated; VSIX packaging job green on windows-latest
- [ ] Journal + session register updated

## Risk Assessment
- Fable subagent quota may reject the reviewer again → fall back to the lead's own review, state it explicitly in the PR.
