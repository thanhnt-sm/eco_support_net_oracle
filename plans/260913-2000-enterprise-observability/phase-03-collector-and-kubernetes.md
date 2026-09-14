---
phase: 3
title: "Collector and Kubernetes"
status: completed
priority: P1
effort: "1d"
dependencies: [1, 2]
---

# Phase 3: Collector and Kubernetes

## Overview

Implement agent→gateway overlays after platform ownership and backend protocols are confirmed.

## Requirements

- Agent has OTLP receiver, memory limiter, k8s attributes, batch, retry and bounded queue.
- Gateway adds redaction, trace-aware load balancing/tail sampling and backend-specific exporters.
- TLS/mTLS secret references, RBAC, NetworkPolicy, PDB, HPA and resource limits are mandatory.

## Success Criteria

- [x] Agent and gateway config validate against immutable Collector distribution 0.160.0.
- [x] Queue/retry bounds, TLS references, self-health, RBAC, PDB, HPA and NetworkPolicy artifacts are present.
- [ ] Backend outage, 429, queue-full and restart tests show bounded loss and no business failure (`NOT EXECUTED`: no approved backend/cluster).

The phase is complete for the repository artifact gate and explicitly incomplete for the
deployment/runtime gate. See `reports/phase-03-review.md`.
