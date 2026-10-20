> **Source review 2026-10-18:** current workflow/scripts at `ca68a3159425495f771da866188df75a8237ab4b` were inspected. The 2026-10-16 research/run observations below are historical evidence, not freshly queried. Implementation in this baseline does not establish a successful hosted run or alert resolution.

# Scorecard SAST readiness (2026-10-16)

This local proposal addresses pending SAST observations; it does not claim that alert #52 is resolved or that a hosted score improved. The historical 2026-10-05 score is not current evidence. No repository protection, required checks, Marketplace pause, or CodeQL configuration changes are included.

## Repeated race observed after PR #66

Read-only research supplied during reconciliation records PR #66 (`93e485f`) merged at 2026-10-16 11:23:51 UTC into `edf7314b6779a002f321d27305f9d2959d3281cb`. [Scorecard run 38893501686, job 116948377331](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38893501686/job/116948377331) sampled at 11:24:06 and emitted SAST 9 with **26/28**, with SARIF processing complete at 11:24:29. PR CI `38893479991` and main CI `38893501684` still had CodeQL running at 11:25:24. A short 60-second registration grace could still expire before CodeQL publishes its service suite. Reconciliation therefore waits for missing-suite registration within the full bounded budget; it does not infer success from main CI or from absence. This second occurrence supports the timing defect; it does not prove every missing historical scan was caused by timing. The September 12 alert history predates PR #65. Earlier 25/28 observations are historical, not the latest reported result.

The patch was reconciled locally against that exact main SHA; the workflow was unchanged by PR #66. Synthetic regressions model incomplete final-head evidence. These observations do not establish a successful post-fix hosted run or resolve alert #52.

## Contract and source mapping

The pinned [action README](https://github.com/ossf/scorecard-action/blob/fa7117db83c52d787ae5a89f350999701505d78e/README.md) restricts publishing analysis steps. `.github/workflows/scorecard.yml` therefore keeps custom Python in read-only jobs. Only `analysis` has OIDC write permission; only `upload-sarif` has security-events write permission. Neither runs repository scripts. The original push, schedule, branch-protection and manual triggers remain; jobs execute only for `refs/heads/main`. Non-main manual dispatches are intentionally skipped. No `workflow_run` trigger is used.

1. `scripts/scorecard_readiness.py` captures main's tip, anchors the last 30 commits to it, resolves associated merged PRs in the target base repository (including forks), and polls their final head SHAs. Duplicate heads are polled once; repeated commit associations retain sample weight. Direct commits and ineligible associations are explicit exclusions; an entirely ineligible sample is non-success (`not_applicable`). Multiple distinct eligible PR associations are ambiguous and fail rather than guessing.
2. Readiness observes exact-head check suites from `github-advanced-security` and `github-code-scanning`, ignoring `github-actions`. This is service-suite evidence, not attestation of a particular workflow or query set. Every returned recognized suite must be completed/success. Suite IDs do not establish rerun-attempt order, so an older suite that is pending or failed cannot be masked by a higher-ID success. Historical failed suites can conservatively prevent readiness. Failed, cancelled, skipped and neutral conclusions are non-success. Pending heads continue waiting even when another head failed.
3. Analysis runs after readiness even when readiness fails. Original JSON and SARIF reports are retained without filtering. Custom validation executes in `verify-report`, with read-only permissions and the immutable artifact ID from this exact run.
4. `scripts/verify_scorecard_report.py` checks bounded report structure, repository identity, full analyzed SHA and any SARIF provenance. It does not implement the entire SARIF schema. JSON `repo.commit` is the repository revision; `scorecard.commit` and SARIF `Scorecard.CommitSHA` describe the tool. Invalid metadata or a failed analysis job prevents upload. Original artifacts may still be retained after analysis failure, but are not authorized for code-scanning upload. Valid reports with missing/mismatched readiness SHA retain diagnostic metadata but verification fails and SARIF upload is blocked. Checkout and upload SHA/ref agree.

Readiness has a 900-second total budget, registration grace within that same 900-second budget, 30-second polling interval, at most 1,000 requests, five pages per collection, 100 items per page and 2 MiB per response. Each request is limited to at most 15 seconds and the remaining budget. Only GET to fixed HTTPS `api.github.com` is permitted; redirects and server-supplied pagination URLs are not followed. API/auth/rate errors, malformed responses and exhausted bounds fail explicitly. Token values and response bodies are never included in reports. The workflow's 20-minute job timeout is an outer bound. Readiness JSON and scan reports have seven-day artifact retention.

## Deliberate differences and limits

[Scorecard v5.6.0 sampling](https://github.com/ossf/scorecard/blob/v5.6.0/clients/githubrepo/graphql.go) uses 30 commits and the first associated PR; this preflight rejects ambiguous multiple associations. [SAST evaluation](https://github.com/ossf/scorecard/blob/v5.6.0/checks/raw/sast.go) accepts success or neutral from additional SAST apps and can accept any qualifying suite within its bounded query. Our all-returned-suites, CodeQL-only, success-only observation is stricter. REST and GraphQL sampling can differ; this is conservative readiness evidence, not a reproduction of Scorecard's score.

The action scans remote default HEAD independently of checkout. Main can move after observation. Comparing emitted JSON detects drift after analysis; it cannot prevent the action's earlier public REST publication. Do not relabel the scan as the trigger SHA. Per-ref `cancel-in-progress: false` serializes active runs, but pending runs may be replaced; it provides neither FIFO nor every-event processing.

The pinned action's [action.yaml](https://github.com/ossf/scorecard-action/blob/fa7117db83c52d787ae5a89f350999701505d78e/action.yaml) selects a mutable Docker tag. Its source references Scorecard v5.6.0, but source inspection is not proof of the deployed image. The verifier records emitted runtime metadata; that is not digest attestation. A required successful final-head CodeQL merge check would address premature merges, but is a separate owner security decision and is not changed here.

## Local verification and operational follow-up

Run `python3 -m unittest discover -s scripts/tests`, `actionlint .github/workflows/*.yml`, and the unchanged local hook gates. Synthetic tests cover missing/pending/completed evidence, conflicting/rerun suite evidence, repeated heads, fork PRs, ambiguity, bounded transport, report tampering and SHA drift. Workflow contract tests enforce privilege separation and actual-SHA uploads. The new tests call no live repository API and read no real credentials. The existing live alert-state test requires `gh`; when unavailable it is skipped and does not establish alert state.

Hosted Actions behavior, OIDC publication admission, artifact transfer and GitHub code-scanning ingestion remain unverified locally. Retain failed readiness observations rather than dismissing alerts. The bounded native analysis retry below is the only step allowed to use `continue-on-error`; final failure is not suppressed. See [implementation plan](../../plans/2026-10-16-sast52-readiness.md) and [Vietnamese guide](scorecard-readiness.vi.md).

## Runtime acceptance still required

Inspected sources: `.github/workflows/scorecard.yml`, `scripts/scorecard_readiness.py` (`Readiness`, transport), `scripts/verify_scorecard_report.py` (`validate`, `main`), and corresponding `scripts/tests/` tests.

| Unexecuted scenario | Source-derived expected behavior | Evidence required when authorized |
|---|---|---|
| Missing/pending/failed CodeQL suite | Absence is not success; bounded waiting/reporting | Exact-run readiness JSON, final head SHA and suite states |
| Failed readiness but valid analysis metadata | Analysis is still attempted; reports retained; upload requires successful analysis and valid metadata | Job results, immutable artifact ID, original JSON/SARIF |
| Valid metadata with mismatched readiness SHA | Verifier exits 1 but emits valid=true and actual SHA for diagnosis; upload is blocked by the verifier result | Verifier outputs and evidence of skipped SARIF upload |
| Non-main dispatch or cancelled run | Job conditions prevent corresponding execution | Skip/cancellation evidence; no dispatch in this task |
| OIDC/public REST, artifact transfer, code scanning | Configuration intends publication; it does not prove admission/ingestion | Owner-authorized run and artifact/ingestion evidence; no automatic trial |

No permissions, protection or required checks are changed. Requiring final-head CodeQL before merge is an owner decision. Source interpretation needs no new decision; actual service outcomes remain unverified.


## Bounded recovery for upstream failures

Run 39420559904 failed on 2026-10-20 at 12:18:38 UTC: GitHub GraphQL returned an internal error during ListCommits/graphqlHandler.setup (request ID `1008:1F79F2:26CAAEA:7FBA3B8:6AD75C1B`). The pinned action exits before generating reports, explaining the subsequent missing-file artifact error. This supports an upstream request failure, not a proven service-wide outage or a repository security finding.

Analysis has at most two native action attempts within 20 minutes. Only the first scan softens its exit to allow the second; a failed final scan remains failing. Evidence uploads and checkout failures are never softened. Original reports, if any, are uploaded under unique run/attempt/scan names before a clean checkout resets the workspace. This avoids reusing fixed-name `results.json` or SARIF from a failed publication. No shell retry, sleep, third-party action, job env/defaults or new permissions are introduced in the restricted OIDC analysis job.

A mandatory external verifier requires successful scan/publication outcome plus both valid reports; missing artifacts are failures even after a claimed successful scan. SHA alignment must succeed before SARIF upload. Partial API publication may be retried and therefore is not exactly-once; metadata verification happens after the action's public API publication and cannot retract that publication. Persistent authentication, configuration or upstream failures remain visible after exhaustion. No security finding is filtered or changed. Synthetic orchestration/report tests do not establish hosted OIDC admission or recovery during a real GitHub outage. See [recovery plan](../../plans/2026-10-20-scorecard-bounded-recovery.md).
