---
phase: 1
title: "Red Team and Contract Hardening"
status: completed
priority: P1
effort: "1d"
dependencies: []
---

# Phase 1: Red Team and Contract Hardening

## Overview

Adjudicate the five-persona review and close correctness/security gaps in the starter contract.

## Requirements

- Keep `IBusinessOperationObserver` vendor-neutral.
- Validate operation names and finite dimensions.
- Use current exception API (`Activity.AddException`); expected cancellation is not a failure.
- Make exporter behavior asynchronous/fail-open and configuration validation fail-fast only locally.

## Related Code Files

- Modify: `src/DataGuard.Observability/CoreObservability.cs`
- Modify: `src/DataGuard.Observability/DataGuard.Observability.csproj`
- Create: `tests/DataGuard.Observability.Tests/`

## Implementation Steps

1. Add deterministic options and descriptor tests.
2. Add redaction/cardinality contract tests and recursion guard design.
3. Decide whether the package belongs in `DataGuard.sln` after owner review of solution scope.

## Success Criteria

- [x] No critical red-team finding unresolved for the bounded starter; deployment blockers remain explicit.
- [x] Tests cover success, technical failure, expected cancellation contract, invalid configuration and operation allowlist.

## Risk Assessment

The current source is a compact starter, not a full message/database adapter set. Keep adapters separate until real client versions and workloads are discovered.
