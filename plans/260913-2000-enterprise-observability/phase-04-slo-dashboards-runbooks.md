---
phase: 4
title: "SLO, dashboards and runbooks"
status: completed
priority: P1
effort: "1d"
dependencies: [3]
---

# Phase 4: SLO, dashboards and runbooks

## Overview

Define owner-approved journey SLIs, recording rules, multi-window burn alerts, dashboard links and incident runbooks.

## Requirements

- Separate SLA/SLO/SLI/error budget and telemetry pipeline SLO.
- Business rejection/authz/cancellation are not availability failures by default.
- Low traffic uses synthetic heartbeat/minimum-event policy.

## Success Criteria

- [x] PromQL syntax is verified by Prometheus 3.13.1 and a deterministic unit test.
- [x] Alert annotations include runbook/dashboard/trace links and maintenance/absent-data handling.
- [ ] PromQL label names and histogram buckets are verified from deployed metric samples
  (`NOT EXECUTED`: no target backend).

The repository artifact gate is complete; semantic activation remains an owner/integration gate.
See `reports/phase-04-review.md`.
