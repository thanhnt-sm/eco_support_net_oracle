# Plan: Visual Studio extension hardening & CLI ide-safe contract

Created: 2026-09-29 · Source: `plans/reports/predict-260929-0835-vs-extension-hardening.md` (Verdict STOP → CAUTION)
Branch: `feat/vs-extension-hardening` (from `main`)

## Goal

Close the Critical/High findings from the predict/red-team pass on DataGuard for Visual Studio: repo-controlled config can load code or force credentials with no trust gate; silent global tool install; broken Error List navigation; unbounded/fragile SARIF publishing; no CI verification of the VSIX; 1 629-line package class.

## Phases

| # | Phase | Status | Owner |
|---|---|---|---|
| 1 | [CLI `--ide-safe` mode](phase-01-cli-ide-safe-mode.md) | done (pending independent review) | main (Fable) |
| 2 | [VS package modularization + trust gate + fixes](phase-02-vs-package-modularize-and-trust-gate.md) | done (pending independent review) | main (Fable) |
| 3 | [CI VSIX packaging gate](phase-03-ci-vsix-packaging-gate.md) | done (pending independent review) | main (Fable) |
| 4 | [Docs, changelog, plan hygiene](phase-04-docs-and-changelog.md) | done (pending independent review) | main (Fable) |

Execution note: Fable weekly quota was exhausted for subagents mid-session (HTTP 429). User constraint: Fable only. Phases executed in the main session; independent code-review re-run is deferred to quota reset (2026-10-01 07:00 Asia/Bangkok).

## Key dependencies
- Phase 2 passes `--ide-safe` → requires Phase 1 merged first (bundled CLI is built from the same tree, so same commit is fine).
- Phase 2 gate: real MSBuild `CreateVsixContainer=true` build must succeed (CreatePkgDef reflection) — no `TextManager.Interop`, no `ValueTuple` in type signatures.
- Phase 3 depends on Phase 2 file layout (asserts VSIX entries only).

## Added during execution
- VS Code extension also passes `--ide-safe` (`src/DataGuard.VSCode/src/command-args.ts`), so the security docs' claim holds for both hosts.
- Root-cause fix for the recurring `packages.lock.json` drift (nested `dotnet publish -r win-x64` in the VSIX build) via `NuGetLockFilePath`.

## Code review outcome (`plans/reports/code-reviewer-260929-0854-vs-extension-hardening.md`)
No Critical/High. Fixed in-cycle: consent store fail-safe + in-memory fallback (#1), Error List clear-at-run-start policy (#2), release.yml now rewrites `ExtensionVersion.Fallback` (#3), VSTHRD003 rationale corrected (#4), fully-qualified custom CLI path (#7), timeout and CLI-too-old runs now written to the run log (#9; the exit-130 status text on the publish path is parity with the original and only reachable if the CLI itself exits 130), old-CLI detector anchored on the quoted flag (#10b), null-presenter message (#11), phase-01 exit-code note (#12).
Deferred from review: pre-existing cancel race (#5), orphan process after failed termination (#6), unlocked bundled-CLI restore → commit a win-x64 lock file (#8), TruncatedCount counts unparsed results (#11), test gaps (registry transitions, live CliRunSession, consent-store exceptions, end-to-end `--ide-safe` handler wiring, extra URI edge cases).

## Red Team Review

### Session — 2026-09-29 (post-implementation, 3 lenses: Security Adversary, Assumption Destroyer, Failure Mode Analyst)
**Findings:** 14 after dedupe (14 accepted, 0 rejected) · **Severity breakdown:** 0 Critical, 5 High, 9 Medium
Reports: `plans/reports/redteam-260929-0929-plan-{security-adversary,assumption-destroyer,failure-mode}.md`. All findings carry file:line evidence; TypeSafe validity ≥ 0.63 on every item. Applied to: follow-up plan `plans/260929-0952-vs-hardening-redteam-tdd-followup/` (tests-first).

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| 1 | VS Code `--ide-safe` strips the user's SecretStorage credential; suppression line hidden | High | Accept | Follow-up P1/P3 |
| 2 | VS Code has no old-CLI detection; released CLIs reject the flag → "found findings" | High | Accept | Follow-up P3 |
| 3 | Timeout race: self-exit during taskkill classified as Terminated → completed SARIF deleted | High | Accept | Follow-up P2 |
| 4 | Messages point at unclaimed nuget.org ID `DataGuard.Cli` (squatting) | High | Accept | Follow-up P2/P3/P4 |
| 5 | Old-CLI detector spoofable via echoed file name; no positive ide-safe handshake | High | Accept | Follow-up P1/P2/P3 |
| 6 | Consent keyed by directory only; no Forget-consent command | Medium | Accept | Follow-up P2 |
| 7 | Release workflow skips VSIX assertions and VS tests; fork PRs upload unsigned VSIX | Medium | Accept | Follow-up P4 |
| 8 | Exit 1+Summary without SARIF misexplained; exit 3 wipes Error List | Medium | Accept | Follow-up P2 |
| 9 | Docs overclaim VS Code ide-safe; snapshot/baseline/verify-shape use repo connection | Medium | Accept | Follow-up P3/P4 |
| 10 | Env vars stay live after policy; baseline can silently zero findings | Medium | Accept | Follow-up P1 |
| 11 | Un-timed super-linear regex; unclamped parallelism from repo config | Medium | Accept | Follow-up P1 |
| 12 | UI-thread `Process.Start`; 3 s drain grace drops Summary | Medium | Accept | Follow-up P2 |
| 13 | Inventory cleared before reservation; Cancel during consent modal misreports | Medium | Accept | Follow-up P2 |
| 14 | Results published into whichever solution is open; assess fails under reparse-point TEMP | Medium | Accept | Follow-up P1/P2 |

### Verification Results (validate pass, Standard tier)
- Claims checked: 26 · Verified: 23 · Failed: 3 · Unverified: 0
- Failures: phase-02 lists `tests/DataGuard.VisualStudio.Tests/ExitCodeExplainerTests.cs` (tests live in `CliArgumentBuilderTests.cs`); phase-02 Req 2 says an old CLI "exits 2" (System.CommandLine exits 1 — `Program.cs` has no `TreatUnmatchedTokensAsErrors` override); phase-03 says `timeout-minutes: 30` (`ci.yml:134` is 40). None affects shipped behaviour; corrected in the follow-up plan.

## Deferred (tracked, not in this cycle)
- VS findings tree / status-bar parity with VS Code; Init command.
- VSIX Authenticode signing; assess SARIF sanitizer parity; `originalUriBaseIds`.
- Oracle/PostgreSQL `validate` always exits 3 via `ProviderRuleCatalog` Unavailable registrations.
- `Program.cs` (2 430 lines) split by command.
- Independent supply-chain and Core/adapters red-team lenses (agents rate-limited).

## Superseded
- `FIX_DATAGUARDVISUALSTUDIO_BUILD_PLAN.md` (root) — the `using Microsoft.VisualStudio.Shell;` fix is already applied in the working tree; file moved to `plans/260929-0835-vs-extension-hardening/superseded-fix-vs-build-plan.md`.
