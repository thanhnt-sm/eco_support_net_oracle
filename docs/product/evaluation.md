# Security, correctness and performance evaluation

Proposed acceptance design, 2026-10-18; no product tests or benchmarks were run for this documentation task. [Sources](research-backlog.md) · [Gates](roadmap.md)

## Trust boundaries

Default analysis should parse without executing repository MSBuild, restore, source generators or EF design-time code. Microsoft documents that builds/restores and project tooling can execute untrusted code. Any build-aware mode needs explicit opt-in and separate process/sandbox limits for network, credentials, filesystem, time and memory; this is a proposed requirement, not proof of existing isolation.

Prefer snapshots. Metadata connectors should use fixed owned catalog queries and least privilege, never application SQL/procedure/migration execution or EXPLAIN ANALYZE by default. Restricted metadata visibility can hide objects; classify insufficient visibility as unknown rather than missing. Test source paths, XML/imports, SQL/comments, dependencies, tool output and caches as hostile input: traversal/symlinks, XXE, parser bombs, injection, stale caches and redaction. Use synthetic secrets and disposable resources only.

## Proposed finding contract

Versioned JSON plus SARIF 2.1.0 mapping: schemaVersion, engine/rule versions, stable fingerprint, severity separate from confidence, outcome `violation|satisfied|unknown|not_applicable`, analysisStatus `complete|partial|cancelled|failed`, expected/observed/evidence/assumptions, source spans, reason codes, coverage/skips and input/config/schema hashes. Unknown is not satisfied; partial or failed analysis cannot imply full validation. Normalize results for deterministic full/incremental comparison.

Humans need explanations, source navigation, previews and suppressions with reason/owner/expiry. Initial agent tools should be only `scan`, `get_result`, `explain`, `get_schema`, `propose_patch`; no arbitrary shell, execute-SQL or apply-migration tool. Untrusted prose never grants authority. Future patch application needs explicit approval, exact patch hash, input compare-and-swap, idempotency, audit and post-checks. Pin MCP version; validate scope/audience and forbid token passthrough. These adapters are planned, not implemented here.

## Correctness and adversarial gates

QA owns independent held-out labeled corpora by rule/provider/dialect. Report precision, recall, sample denominators, confidence intervals and unknown/coverage rates, including unsupported inputs rather than silently dropping them. Targets: exploratory advisory pilot ≥95% precision/≥80% recall; blocking enterprise ≥99%/≥95% with reviewed sample adequacy. No result has been measured against these targets here.

Target 100% normalized full/incremental equivalence and determinism on declared fixtures only. Include malformed SQL/projects, stale snapshots, permission-denied/empty metadata, process/frame failures, cancellation, cache poisoning and unsafe edit proposals. No-execution/no-DB-write/no-egress assertions describe observed sandbox tests, never universal guarantees. Compare humans and agents on the same controlled mutation tasks: time to correct fix, correctness, unsafe changes, evidence fidelity and unknown comprehension.

## Proposed budgets, pending baseline

Fixed proposed host: 4 vCPU, 8 GiB RAM, SSD; record OS/runtime, CPU model, source/lock hashes, fixture seed, warm-up, repetitions and variance. Separate startup, extraction, rules, report and editor latency. No budget below is a measured result or SLA.

| Workload | Proposed target |
|---|---|
| Incremental file after 300 ms debounce | p95 ≤500 ms |
| Cold 100k LOC + 1,000 SQL + 5,000 objects | p95 ≤30 s, RSS ≤1 GiB |
| Warm 1% change of same corpus | ≤5 s |
| Large 1M LOC, exploratory research | ≤180 s, RSS ≤2 GiB |
| Cancellation | acknowledgment ≤1 s; child termination ≤3 s |
| Metadata | 5 s/query; 30 s total, configurable |
| Resource ceiling | 5 MiB/file; 5 min total; emit partial/resource_limit |

SA-EXP-004 first records baseline/after distributions on identical synthetic workloads; owners revise budgets with user task evidence. A benchmark harness alone cannot support performance claims.

## Enterprise and banking gates

M6 should verify offline operation and default absence of source/telemetry egress, redacted previewable support bundles, secret-store use with no secrets in args/log/cache, retention, SBOM/checksums/signatures/provenance, pinned tooling, reproducibility, vulnerability disclosure and EOL policies. Each requires an owner, configuration, test and known exception in the pilot operating profile. SSO/RBAC/tenant isolation applies only if a future hosted control plane is justified; do not add IAM requirements to an offline CLI without a need.

Banking evaluation additionally needs independent penetration testing, customer/jurisdiction-specific legal review, data classification, air-gap constraints, AI policy, IdP/log retention and third-party risk review. None constitutes “bank certified.” No production systems, real credentials or external services are used to prove these properties in local evaluation.
