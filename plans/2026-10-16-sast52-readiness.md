# SAST52 local readiness plan / Kế hoạch local

Original base: `5927edd852a71177bf1b068da66972153e3a9af3`. Reconciled base from read-only Git fetch: `edf7314b6779a002f321d27305f9d2959d3281cb` (PR #66 merged). Initial preparation was local-only. On 2026-10-16 the user explicitly authorized committing and pushing this focused branch; PR creation and merge remain manual. No workflow dispatch, alert/settings changes or denied API retry is authorized. Existing maintenance and coverage patches remain isolated. Security review approved the four-job design before implementation; independent source and regression review follows implementation.

| Priority | Evidence / reproducible case | Source and acceptance |
|---|---|---|
| P0 boundary | Publishing analysis rejects custom run steps; remote default HEAD may differ from trigger SHA | Preserve pinned action allowlist; read-only custom jobs; only analysis gets OIDC; only upload gets security-events. Validate JSON repository/SHA and SARIF provenance before upload. Drift fails but valid actual-SHA upload remains possible. |
| P1 timing | A sampled merged PR final head has a pending scan while main CI is green | Anchored 30-commit observation, bounded exact-head CodeQL suite polling, registration grace; failed/missing/API/timeout remain non-success; analysis still runs. |
| P1 reporting | Old successful suite followed by newer failed/pending suite; fork PR; duplicate heads | Synthetic regressions reject conflicting suite evidence including reruns of lower-ID suites, include target-base fork PRs, retain per-commit weight while deduplicating polling. |
| P2 operational | Main moves after readiness; runtime image tag changes; pending concurrency replaced | Document limits, emit actual runtime metadata, retain original reports. No guaranteed score, FIFO or every-event claim. |

Fresh recurrence: PR #66 merged 11:23:51 UTC; Scorecard run `38893501686` sampled 11:24:06 and reported SAST 9, 26/28 while final-head/main CodeQL remained running at 11:25:24. September 12 is historical alert evidence, not caused by PR #65. See linked EN/VI evidence chronology. New reconciliation regressions model 26 successful and two pending PR heads, registration after 60 seconds, and never-registered evidence. The earlier 60-second cutoff is replaced by registration waiting within the existing 900-second total budget, with 30-second polling to remain below the 1,000-request cap for ordinary 30-head samples. No post-fix score or alert-resolution claim.

Implementation mapping: `.github/workflows/scorecard.yml`; `scripts/scorecard_readiness.py` and `scripts/tests/test_scorecard_readiness.py`; `scripts/verify_scorecard_report.py` and its corresponding test; `scripts/tests/test_scorecard_workflow.py`; [EN](../docs/security/scorecard-readiness.md) / [VI](../docs/security/scorecard-readiness.vi.md). Existing optimization guide links this correction without erasing historical scores.

Compatibility: original supported triggers retained; main-only execution explicit. No production C#, Node, CodeQL, diagnostic, Marketplace or coverage behavior changed. Strict success/all-returned-suites CodeQL policy is intentionally narrower than Scorecard's any recognized SAST success-or-neutral policy. No unsupported grammar or diagnostic suppression is involved.

Acceptance: regression-first synthetic failure then green; actionlint all workflows; Python suite; unchanged .NET coverage/build/analyzer/format and docs/audit gates; Node tests. Evidence stays outside the tracked tree. Hosted/Windows/editor-host/OIDC/ingestion checks must be identified as unavailable, never inferred from local tests.

Backlog / Chưa triển khai: owner decision on required final-head CodeQL merge check; owner-approved hosted validation of admission and actual artifact/SARIF behavior; image digest provenance review. These are not authorized remote changes. Full local evidence and exact patch hash accompany the handoff; no alert-resolution claim.
