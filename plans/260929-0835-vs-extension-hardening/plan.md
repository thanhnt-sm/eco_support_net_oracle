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

## Deferred (tracked, not in this cycle)
- VS findings tree / status-bar parity with VS Code; Init command.
- VSIX Authenticode signing; assess SARIF sanitizer parity; `originalUriBaseIds`.
- Oracle/PostgreSQL `validate` always exits 3 via `ProviderRuleCatalog` Unavailable registrations.
- `Program.cs` (2 430 lines) split by command.
- Independent supply-chain and Core/adapters red-team lenses (agents rate-limited).

## Superseded
- `FIX_DATAGUARDVISUALSTUDIO_BUILD_PLAN.md` (root) — the `using Microsoft.VisualStudio.Shell;` fix is already applied in the working tree; file moved to `plans/260929-0835-vs-extension-hardening/superseded-fix-vs-build-plan.md`.
