# Comprehensive Research Reference: Codecov Free-Tier (OSS) Integration & TypeSafe Judge / JEV Diagnostics

> **Date:** 2026-10-14  
> **Repository:** `thanhnt-sm/eco_support_net_oracle`  
> **Codecov Target:** `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/new`  
> **Implementation Plan:** [`plans/261014-codecov-integration/plan.md`](../../plans/261014-codecov-integration/plan.md)

---

## Table of Contents

1. [Executive Summary & Scope](#1-executive-summary--scope)
2. [Repository Baseline & Governance Constraints (`ck:scout`)](#2-repository-baseline--governance-constraints-ckscout)
3. [Codecov Free-Tier & GitHub Actions Best Practices 2025–2026 (`ck:research` & `ck:docs-seeker`)](#3-codecov-free-tier--github-actions-best-practices-20252026-ckresearch--ckdocs-seeker)
4. [Agreed Integration Architecture (`ck:brainstorm`)](#4-agreed-integration-architecture-ckbrainstorm)
5. [Expert Council & TypeSafe Evaluations (`ck:advisor` & `xd://typesafe_*`)](#5-expert-council--typesafe-evaluations-ckadvisor--xdtypesafe_)
6. [Deep Dive: TypeSafe System One, JEV Judge & `typesafe-planner.ts` Internals](#6-deep-dive-typesafe-system-one-jev-judge--typesafe-plannerts-internals)
7. [Reference Configurations (`.github/codecov.yml`, `ci.yml`, Badges & Graphs)](#7-reference-configurations-githubcodecovyml-ciyml-badges--graphs)
8. [Internal Artifacts & Official References](#8-internal-artifacts--official-references)

---

## 1. Executive Summary & Scope

This reference document consolidates all research findings, architectural evaluations, and diagnostic insights from the `2026-10-14` engineering session:

1. **Codecov Free Open-Source Tier Integration:** How to enable 100% of applicable free Codecov capabilities (OIDC tokenless uploads, Multi-Flag coverage with Carryforward, 6 Virtual Components, Project/Patch status gates, GitHub Checks inline annotations, JUnit Test Analytics, and Sunburst/Grid SVG visualizations) in `.NET 9` + `Node.js 22` + `Testcontainers` CI without mutating `packages.lock.json` or `package-lock.json`.
2. **TypeSafe System One & `typesafe-planner.ts` Diagnostics:** Complete code-level trace of how the `oh-my-pi (omp)` TypeSafe extension intercepts `ask` and `todo` calls, why `meets_criteria=0.00` is emitted when `deliverables_verdict === "REJECT"`, and how `collectTaskEvidence` and `inferTaskType` evaluate task completion.

---

## 2. Repository Baseline & Governance Constraints (`ck:scout`)

### 2.1. Existing CI Test & Coverage Jobs (`.github/workflows/ci.yml`)

- **`build-and-test` (`:102-150`):** Executes `dotnet test DataGuard.CrossPlatform.slnf --configuration Release --no-build --filter "Category!=LiveDb" --collect:"XPlat Code Coverage" --logger "trx;LogFileName=test_results.trx"`, enforces a **60.0% line-coverage floor** via an inline Python parser, and uploads `**/coverage.cobertura.xml` and `**/TestResults/*.trx` artifacts.
- **`live-db-integration` (`:188`):** Runs Testcontainers integration tests (`Category=LiveDb`) against Oracle, SQL Server, PostgreSQL, and MySQL with `--logger "trx;LogFileName=live_db_results.trx"`.
- **`vscode-extension` (`:200-226`):** Runs TypeScript unit tests on Node.js 22 (`.nvmrc`) via `node --test`.

### 2.2. Five Mandatory Repository Policy Gates

1. **Root File Allowlist (`scripts/anti_garbage_guard.sh` & `scripts/preflight_agent_check.sh`):**
   - Root `codecov.yml` or `.codecov.yml` is rejected by `ROOT_FILE_PATTERN`. Placing the configuration at **`.github/codecov.yml`** is natively supported by Codecov and automatically permitted under `^.github/`.
2. **Effective Test Step Invariant (`scripts/check-workflow-policy.py` rule `(f)`, `:108-115, 226-277`):**
   - Product `dotnet test` steps must NOT have `continue-on-error: true`. Codecov upload steps must instead use `if: ${{ !cancelled() && env.ACT != 'true' }}`.
3. **40-Character Commit SHA Pinning (`.github/workflows/standards-audit.yml:51-65`):**
   - Every `uses:` entry must be pinned to a full 40-char lowercase hex commit SHA (`codecov/codecov-action@303a32d7a59b442fa8d48b6a1cc6825c09c847a5 # v7.1.1`).
4. **Locked Dependencies & GPL-3.0 License Gate (`RestoreLockedMode=true`, `scripts/check_nuget_licences.py`):**
   - Avoid adding new NuGet packages (`JunitXml.TestLogger`) or npm packages (`c8`, `@codecov/bundle-analyzer`). Use Python 3 stdlib (`scripts/trx_to_junit.py`) and Node 22 built-in test reporters (`--experimental-test-coverage --test-reporter=lcov --test-reporter=junit`).
5. **Bilingual Documentation Sync (`scripts/verify_docs_sync.sh`):**
   - `README.md` and `README.vi.md` must stay synchronized without modifying generated `<!-- rule-table:start --> ... <!-- rule-table:end -->` sections.

---

## 3. Codecov Free-Tier & GitHub Actions Best Practices 2025–2026 (`ck:research` & `ck:docs-seeker`)

1. **`codecov/codecov-action@v7.1.1` (`303a32d7a59b442fa8d48b6a1cc6825c09c847a5`):**
   - Consolidates coverage uploads (`report_type: coverage`) and Test Analytics (`report_type: test_results`), deprecating `codecov/test-results-action@v1`.
   - Uses Node 24 runtime (`v6+`) and updated `codecovsecops` Keybase PGP verification (`v7+`).
2. **OIDC Tokenless Authentication (`use_oidc: true`) & Fork Fallback (`action.yml:242-286`):**
   - When `CC_FORK != 'true'`, mints a short-lived GitHub OIDC token (`core.getIDToken('https://codecov.io')`) using job-level `permissions: { contents: read, id-token: write }`.
   - When `CC_FORK == 'true'` (external fork PRs), automatically skips OIDC and sets `TOKENLESS="$GITHUB_EVENT_PULL_REQUEST_HEAD_LABEL"`.

---

## 4. Agreed Integration Architecture (`ck:brainstorm`)

- **Selected Option:** **Option B — Full Free-Tier Suite** (`dotnet-unit`, `live-db`, `vscode-ext` flags + 6 Components + OIDC + Test Analytics + Bilingual README Badges, Component Matrix & Sunburst/Grid SVG Graphs).
- **6 Architectural Components in `.github/codecov.yml`:**
  1. `core_engine`: `src/DataGuard.Core/**`, `src/DataGuard.Contracts/**`, `src/DataGuard.SqlClassification/**`
  2. `db_adapters`: `src/DataGuard.Oracle.Adapter/**`, `src/DataGuard.SqlServer.Adapter/**`, `src/DataGuard.PostgreSql.Adapter/**`, `src/DataGuard.MySql.Adapter/**`
  3. `roslyn_analyzers`: `src/DataGuard.Analyzers/**`, `src/DataGuard.CodeFixes/**`
  4. `cli_and_host`: `src/DataGuard.Cli/**`, `src/DataGuard.Host/**`, `src/DataGuard.Build/**`, `src/DataGuard.LanguageServer/**`
  5. `observability`: `src/DataGuard.Observability*/**`
  6. `ide_extensions`: `src/DataGuard.VSCode/**`, `src/DataGuard.VisualStudio/**`

---

## 5. Expert Council & TypeSafe Evaluations (`ck:advisor` & `xd://typesafe_*`)

- **`xd://typesafe_judge` (Architecture Evaluation):** `scope_completeness = 0.95`, `security_compliance = 0.59`, `recommended_tier = "Option B"` (`confidence = 0.97`).
- **`xd://typesafe_advisor` (Bicameral Consensus):** `verdict = "AUTO_APPROVE"`, `ckVerdict = "proceed"`, `confidence = 0.98`, `jev.verdict = "APPROVE"` (`Risk: Moderate 1.8/3`, JEV recommended option: `Option B`).
- **`ck:advisor` Subagent (`CodecovAdvisorReview`):** Verdict `proceed` (`9.4 / 10` for Option B), identifying 3 concrete refinements:
  1. Append `--collect:"XPlat Code Coverage"` to `live-db-integration` (`ci.yml:188`).
  2. Pass `--enable-source-maps` to `node --test --experimental-test-coverage` in `src/DataGuard.VSCode` so LCOV maps to `src/DataGuard.VSCode/src/**/*.ts`.
  3. Keep `fail_ci_if_error: false` and `if: ${{ !cancelled() && env.ACT != 'true' }}` on all Codecov steps.
- **`xd://typesafe_judge` (`ck:plan` §2e Plan Quality Self-Assessment):** `completeness = 1.70 / 2.00`, `scope_adherence = 0.75`, `testability = 0.95`.

---

## 6. Deep Dive: TypeSafe System One, JEV Judge & `typesafe-planner.ts` Internals

1. **Background Hook Interception (`typesafe-planner.ts:1305, 1461`):**
   - `pi.on("tool_call")` intercepts `ask` and `todo(op="done")` inside the host extension process and invokes `evaluateAdvisorIntent()` and `client.evaluate()` directly without emitting a separate tool call in the transcript.
2. **Why `meets_criteria=0.00` Appears on `REJECT` (`src/cook-expert-judge.ts:253-277, 336, 355-361`):**
   - `runPhaseMacroCheck` sends only 3 `choice` questions (`deliverables_verdict`, `architectural_drift`, `scope_mode`) and does not request `meets_criteria` (`noul`).
   - Because `rawMeetsCriteria` is `undefined`, line `336` assigns `0.0` whenever `deliverables_verdict === "REJECT"`, and line `360` emits the fallback string `"Deliverable verification rejected: implementation is evading work, malicious, or stubs out needed functionality."`
3. **Why Intermediate Tasks Were Rejected vs Final Task Approved (`meets_criteria=1.00`):**
   - Marking `todo(op="done")` before deliverables are written to disk (`changedFiles: none`) or while `plan-cli.cjs create` boilerplate stubs (`TBD.`) exist on disk causes `deliverables_verdict` to return `REJECT`.
   - Writing all complete files to disk before calling `todo(op="done")` produces `Decision: APPROVED (by: judge) | Judge: meets_criteria=1.00 | drift=no_drift | scope=HOLD`.
4. **Untracked Files in `collectTaskEvidence` (`src/evidence-collector.ts:316-364`):**
   - With `options.baseline` (`todo(op="done")`), untracked (`??`) files are synthesized into `gitDiff`. Without `options.baseline` (`xd://typesafe_verify_completion`), only `git diff HEAD` is run, omitting untracked files.

---

## 7. Reference Configurations (`.github/codecov.yml`, `ci.yml`, Badges & Graphs)

See Vietnamese companion guide [`docs/guides/codecov-and-typesafe-research.vi.md`](codecov-and-typesafe-research.vi.md#7-tham-chiếu-cấu-hình-đầy-đủ-githubcodecovyml-trx_to_junitpy-ciyml-readme) and phase plans in [`plans/261014-codecov-integration/`](../../plans/261014-codecov-integration/plan.md) for full YAML, Python, and Markdown snippets.

---

## 8. Internal Artifacts & Official References

- **Research Report (`ck:research`):** [`plans/reports/researcher-261014-codecov-free-tier-best-practices.md`](../../plans/reports/researcher-261014-codecov-free-tier-best-practices.md)
- **Brainstorm Report (`ck:brainstorm`):** [`plans/reports/brainstorm-261014-codecov-integration.md`](../../plans/reports/brainstorm-261014-codecov-integration.md)
- **Implementation Plan (`ck:plan`):** [`plans/261014-codecov-integration/plan.md`](../../plans/261014-codecov-integration/plan.md)
- **Technical Journal (`ck:journal`):** [`docs/journals/261014-codecov-brainstorm-and-plan.md`](../journals/261014-codecov-brainstorm-and-plan.md)
- **Codecov & GitHub UI Configuration Report (`computer` + `gh`):** [`plans/261014-codecov-integration/reports/codecov-ui-configuration.md`](../../plans/261014-codecov-integration/reports/codecov-ui-configuration.md) — Verified `Branch Context = main` and `Global Upload Tokenless = ENABLED` on Codecov UI, configured `CODECOV_TOKEN` in GitHub Actions & Dependabot secrets, and activated the repository on Codecov (`active: true`, `activated: true`, baseline coverage **`64.08%`** across `171` tracked files).
