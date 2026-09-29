---
title: "VS hardening red-team follow-up (TDD)"
description: ""
status: pending
priority: P2
effort: 
branch: feat/vs-extension-hardening
tags: []
blockedBy: []
blocks: []
created: 2026-09-29
---

# VS hardening red-team follow-up (TDD)

## Overview

Tests-first follow-up to `plans/260929-0835-vs-extension-hardening/` (shipped in PR #24). Closes the 14 accepted findings of the 2026-09-29 red-team session (`plans/reports/redteam-260929-0929-plan-{security-adversary,assumption-destroyer,failure-mode}.md`): a positive `ide-safe: active` handshake and `--allow-env-connection` in the CLI; termination-classification, threading, drain, run-slot, solution-lifetime, consent-key and explainer fixes in the Visual Studio extension; credential carve-out, old-CLI detection and a verify-shape confirmation in VS Code; a shared VSIX assert used by CI and release with a fork-PR upload guard; corrected security docs. Every phase starts with failing tests. Mode: `--tdd`.

## Validation Log

### Session 1 — 2026-09-29 (7 questions: 1 red-team disposition + 6 decisions)
| Topic | Decision |
|---|---|
| Red-team dispositions | Apply all 14 accepted findings |
| User credential vs ide-safe (F1) | New CLI flag `--allow-env-connection`; hosts set it only for user secret-storage credentials |
| Baseline under ide-safe (F10) | Keep applying; emit visible `BaselineApplied` warning with count and path |
| Consent key (F6) | Add the `.sln` full path; add Forget-consent command |
| Fork-PR VSIX artifact (F12) | Build on all PRs; upload only for same-repo branches |
| verify-shape gating (F9) | Same modal confirmation as snapshot/baseline |
| NuGet `DataGuard.Cli` guidance (F4) | Remove from both extensions; owner reserves the ID separately |
| Execution | Stop at the plan; run `/ck:cook --tdd` later |

### Verification Results
- Claims checked: 26 · Verified: 23 · Failed: 3 · Unverified: 0 · Tier: Standard (Fact Checker + Contract Verifier, via the three red-team reviewers plus a lead grep pass)
- Failures (all documentation, corrected in Phase 4): phase-02 names `ExitCodeExplainerTests.cs` (tests live in `CliArgumentBuilderTests.cs`); phase-02 Req 2 "exits 2" (System.CommandLine exits 1); phase-03 `timeout-minutes: 30` (`ci.yml:134` is 40).

## Dependencies
- Phase 2 and Phase 3 depend on Phase 1's handshake line and `--allow-env-connection` (same PR; bundled CLI is built from the same tree).
- Phase 4 depends on the string/behaviour changes of Phases 2–3 for accurate docs.
- Owner actions outside the repo: reserve `DataGuard.Cli` on nuget.org; decide VSIX signing.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [CLI ide-safe handshake and contract hardening](./phase-01-cli-ide-safe-handshake-and-contract-hardening.md) | Pending |
| 2 | [VS extension run-lifecycle and trust-gate fixes](./phase-02-vs-extension-run-lifecycle-and-trust-gate-fixes.md) | Pending |
| 3 | [VS Code parity: credential carve-out and old-CLI detection](./phase-03-vs-code-parity-credential-carve-out-and-old-cli-detection.md) | Pending |
| 4 | [Workflows and docs: shared VSIX assert and corrected claims](./phase-04-workflows-and-docs-shared-vsix-assert-and-corrected-claims.md) | Pending |
| 5 | [Verification and PR update](./phase-05-verification-and-pr-update.md) | Pending |
