---
status: active
title: "Remediate Security & Concurrency Findings from Adversarial Review"
createdAt: "2026-10-11"
mode: "deep"
tdd: true
blockedBy: [2026-10-13-github-jobs-scans-main-integration]
---

# Plan: Remediate Security & Concurrency Findings from Adversarial Review

## Overview
This plan implements the critical and high-priority fixes identified during the adversarial code review. It resolves concurrency races in the Language Server and Validation Engine, exception masking in ASP.NET Core Observability, incorrect exit codes in the CLI, Oracle cursor leaks, SQL Server schema normalization issues, and missing timeouts/concurrency limits in GitHub Workflows. The execution is guided by TDD (Test-Driven Development) and mandates TypeSafe Judge (`jev`) evaluation for all complex concurrency and observability changes.

## Phases

| Phase | Title | Status | Effort |
|---|---|---|---|
| 1 | `phase-01-core-observability` | pending | P1 |
| 2 | `phase-02-entrypoints` | pending | P1 |
| 3 | `phase-03-adapters-workflows` | pending | P2 |

## Quality & Execution Standards (ck:cook --auto --tdd)
- **TDD Requirement:** For each phase, write failing tests (`## Tests Before`) covering the race condition, exception masking, or edge case *before* modifying the implementation.
- **TypeSafe Evaluation:** Use the `xd://typesafe_judge` (jev) tool to structurally evaluate the fixes for `ConcurrentValidationEngine.cs` (task leak), `Program.cs` (LSP dictionary race), and `AspNetCoreObservability.cs` (exception handling). Ensure the judge verifies the absence of deadlocks, resource leaks, and silent failures.
- **Advisor Review:** Consult the advisor for architectural alignment when modifying `CancellationTokenSource` ownership in the Language Server.

## Red Team & Validation Review Summary
- **Red Team Lens:** Adversarial reviewers highlighted the risk of `ObjectDisposedException` in the LSP taking down the entire server. They also emphasized that `ConcurrentValidationEngine`'s bounded channel could deadlock if the producer task isn't properly cancelled on consumer early exit.
- **Validation Pass:** Verified that Oracle PL/SQL strings are properly sanitized (SQL injection mitigated), so the fix only requires an `EXCEPTION WHEN OTHERS` block for cursor management. Verified that ASP.NET Core exceptions are stored in `ActionExecutedContext`, necessitating explicit re-throwing in the observability filter.

## Plan Context
Validation: mode=deep, questions=3-5
Red Team: completed, findings integrated.

## Workflow coordination
The GitHub jobs/scans integration plan owns workflow failure diagnosis and remote acceptance. Reconcile its evidence before executing this plan's workflow timeout/concurrency edits; do not impose 30 minutes on existing 40-minute Live DB jobs without runtime evidence. Source adapter/concurrency phases are not blocked by this workflow-specific coordination.