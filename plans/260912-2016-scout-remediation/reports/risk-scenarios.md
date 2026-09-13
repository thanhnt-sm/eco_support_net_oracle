---
type: risk-scenarios
date: 2026-09-12
---
# Prediction and scenario report

## Context

Design-only risk review for [plan](../plan.md). ck:predict uses five analytical perspectives below; these are design perspectives, not a claim five extra agents were spawned. Four independent Sol red-team agents separately inspect the actual plan/source; their adjudication is in red-team.md.

## Prediction verdict: CAUTION

Independent perspectives:
- Architect: shared result/config/schema contracts first; avoid repeated Program.cs edits and public ABI break.
- Security: trusted EF/plugin execution boundaries, no implicit REF CURSOR calls, secret-safe persistence/reporting mandatory.
- Performance: cap scheduling, input sizes and telemetry memory; no unbounded materialization used to “fix” drops.
- UX: incomplete/no-source/error must be distinguishable from no violations, including IDEs and automation.
- Devil's Advocate: missing product claims usually need docs correction, not a new host/CVE/platform; provisional findings may be nonbugs.

Agreement: preserve evidence, never false-clean, minimal authorized scope, explicit platform gates.

| Conflict | Resolution |
|---|---|
| EF accuracy vs no-build convenience | Explicit trusted compiled snapshot route; source-only unavailable diagnostic, no automatic app factory. |
| Bounded memory vs complete results | Observable incomplete result on unevaluated work; compatibility wrapper cannot hide status. |
| Legacy exit compatibility vs false-green drift | Preserve established exit categories where possible but fix false-green behavior; document migration/explicit legacy mode. |
| Parallel throughput vs shared-file safety | Sole Terra writer, independent Sol reviewers; no source merge races. |
| “All issues” vs absent remote features | Close false claims with docs/evidence, keep future feature requests conditional and external readiness blocked. |

Recommendations: CP1 contract approval, smallest batches, regression before fix, source-compatible AND binary-compatible tests, all source IDs reconciled at CP6.

## Scenario report

Dimensions analyzed: User Types, Input Extremes, Timing, Scale, State Transitions, Environment, Error Cascades, Authorization, Data Integrity, Integration.
Dimensions skipped: Compliance (no legal/certification determination in this task; audit redaction is covered operationally), Business Logic (no pricing/payment workflow here).

| ID | Dimension | Scenario | Severity | Expected behavior |
|---|---|---|---|---|
| S01 | User Types | CLI developer có env connection ngoài dự kiến | High | Resolve rõ mode/source, explicit live policy |
| S02 | User Types | CI chỉ có YAML và không option | High | Không null-overwrite; prerequisite fail visible |
| S03 | User Types | Operator trỏ EF/plugin artifact không trusted | Critical | Không autoexecute; explicit trust gate |
| S04 | Input Extremes | Null/blank/unknown provider hoặc schema | High | Validation error, không fallback guess |
| S05 | Input Extremes | Secret-shaped text trong arbitrary SARIF/audit props | Critical | Redact allowlist cả streaming/buffered |
| S06 | Input Extremes | Unicode/space/reserved/colliding TS identifiers | High | Valid deterministic exported code |
| S07 | Timing | Cancel giữa đọc DB và publish snapshot | High | Propagate token, old file intact |
| S08 | Timing | Concurrent graph dependency chưa xong | High | Không start dependent level |
| S09 | Timing | Timer flush chồng và export timeout | High | One inflight, bounded retry, observed error |
| S10 | Scale | 0/1/N/N+1 violations với cap N | High | Complete khác incomplete, no false clean |
| S11 | Scale | Million-file autodetection tree | High | Exclusion/caps/cancellation; observable partial |
| S12 | Scale | Oversized baseline/SARIF/telemetry batch | High | Bound allocation/output, controlled failure |
| S13 | State Transitions | v1 violation baseline load bằng new reader | High | Explicit legacy semantics, không schema migrate giả |
| S14 | State Transitions | Crash/disk-full trước atomic publish | Critical | Old target recoverable, scoped temp cleanup |
| S15 | State Transitions | Session mới không có old agent IDs/temp logs | High | Read durable ledger/evidence, no fabricated resume |
| S16 | Environment | NonWindows encrypt-at-rest requested | Critical | Fail before write, no false encrypted flag |
| S17 | Environment | Windows VSSDK unavailable ở macOS | High | V04 blocked, không surrogate pass |
| S18 | Environment | Docker/DB absent optional vs required mode | High | Named skip vs fail, no passed earlyreturn |
| S19 | Error Cascades | DB permission denied/partial catalog capture | High | Unavailable/incomplete không empty no-drift |
| S20 | Error Cascades | Telemetry exporter fail liên tục | High | Bound memory/backoff/drop count, no blocking dispose |
| S21 | Error Cascades | EF load dependency mismatch or user code throws | High | Safe surfaced failure, no JSON/empty fallback |
| S22 | Authorization | REF CURSOR cần invocation để lấy shape | Critical | Metadata Unknown; no routine execution |
| S23 | Authorization | SARIF URI outside workspace hoặc symlink escape | Critical | Reject regardless workspace trust |
| S24 | Authorization | Uninstall gặp hook không thuộc DataGuard | Critical | Preserve content; owner/marker validation |
| S25 | Data Integrity | Default literal chứa hai spaces/case-sensitive identifier | Critical | Hash không collapse semantic differences |
| S26 | Data Integrity | Snapshot hash tampered/provider-scope mismatch | High | Validate kind/version/scope; error không no-drift |
| S27 | Data Integrity | Two tables cùng column name khác nullability | High | Qualified mapping/explicit ambiguity |
| S28 | Integration | Legacy compiled consumer constructor/ValidateAsync | High | Compat fixture runs or approved breaking migration |
| S29 | Integration | CLI nonzero incomplete và valid SARIF trong IDE | High | Diagnostics visible, outcome not clean |
| S30 | Integration | Provider rules thiếu entity/RawSql context | High | Report prerequisite unavailable, không claim ran |

## Summary and next steps

30 scenarios: 8 Critical, 22 High, 0 Medium, 0 Low. Severity describes potential scenario impact, not a newly confirmed vulnerability. Phase owners map applicable S IDs to AC IDs before tests; not every cross-product permutation is required, but each scenario requires a disposition and risk-based representative fixture. No scenario or risk review claims production fixes have run.
