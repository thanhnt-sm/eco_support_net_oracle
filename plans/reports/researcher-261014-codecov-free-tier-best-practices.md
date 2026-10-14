---
title: "Codecov Free Tier & GitHub Actions Best Practices (2025-2026)"
type: research-report
date: 2026-10-14
repository: thanhnt-sm/eco_support_net_oracle
status: completed
---

# Research Report: Codecov Free-Tier Integration & Best Practices

## Executive Summary
Codecov provides a comprehensive free tier for public open-source GitHub repositories (`https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle`). As of 2025–2026 (`codecov/codecov-action@v7.1.1`), best practices center on **OIDC tokenless authentication (`use_oidc: true`)**, **commit-SHA pinning**, **multi-flag uploads (`flags` + `carryforward`)**, **virtual `component_management`**, **inline GitHub Checks annotations**, and **consolidated Test Analytics (`report_type: test_results`)** in a single Action without deprecated `codecov/test-results-action`.

For `thanhnt-sm/eco_support_net_oracle`, all configuration must reside at `.github/codecov.yml` to satisfy `scripts/anti_garbage_guard.sh` and `scripts/preflight_agent_check.sh`, and test steps in `.github/workflows/ci.yml` must remain fail-fast (no `continue-on-error: true` on `dotnet test`, which would violate `scripts/check-workflow-policy.py` rule `(f)`).

## Research Methodology
- Sources consulted: 20+ (Codecov official docs, `codecov/codecov-action` repository & `action.yml` v7.1.1 source, .NET Coverlet documentation, repo CI/security scripts).
- Date range of materials: 2024–2026 (`codecov/codecov-action` v5.0.0 through v7.1.1).
- Key search terms: `codecov/codecov-action v5 v7 use_oidc`, `report_type: test_results`, `codecov.yml component_management flags`, `graphs/sunburst.svg`, `.NET coverlet Cobertura JUnit`.

## Key Findings

### 1. Technology Overview & Action Evolution (`v5` → `v7.1.1`)
- **`codecov/codecov-action@v7.1.1`** (commit SHA: `303a32d7a59b442fa8d48b6a1cc6825c09c847a5`) is the latest release:
  - `v5`: Wrapped `codecov-cli`, replaced `file`/`plugin` with `files`/`plugins`, added `use_oidc` and `report_type: test_results | coverage`.
  - `v6`: Upgraded runtime to Node 24.
  - `v7`: Updated PGP key verification (`codecovsecops` Keybase) in wrapper submodule.
- **`codecov/test-results-action` is deprecated**: `codecov/codecov-action` handles both `report_type: coverage` (default) and `report_type: test_results` (JUnit XML).

### 2. Free Features Available for Open-Source Repositories
| Feature | Codecov Free OSS Support | Value for `eco_support_net_oracle` |
|---|---|---|
| **Line & Branch Coverage** | Unlimited public uploads | Aggregates Cobertura XML (`.NET`) and LCOV (`VS Code` extension) |
| **OIDC + Fork Tokenless Auth** | Built-in (`use_oidc: true`) | Zero secret rotation; fork PRs automatically fall back to tokenless upload |
| **Flags & Carryforward** | Unlimited flags | Isolates `dotnet-unit`, `live-db`, and `vscode-ext` coverage |
| **Components (`component_management`)** | Unlimited components | Slices coverage into 6 architectural subsystems in PR comments & badges |
| **PR Comments & GitHub Annotations** | Built-in (`github_checks`) | Inline uncovered-line annotations in PR diffs + rich component/flag tables |
| **Test Analytics (`test_results`)** | Included for OSS | Tracks slow tests, failure rates, and flaky tests from JUnit XML |
| **Dynamic Badges & Graphs** | SVG endpoints | Overall badge, per-flag/component badges, and `sunburst.svg`/`grid.svg`/`icicle.svg` |

### 3. Best Practices for `.NET 9` + `Node 22` + `GitHub Actions`
1. **OIDC Authentication (`use_oidc: true`)**:
   - Grant `id-token: write` and `contents: read` at the job level for jobs uploading to Codecov.
   - Inspecting `codecov/codecov-action/action.yml:242-286` confirms: when `CC_FORK != 'true'`, the action mints a GitHub OIDC token (`audience: https://codecov.io`); when `CC_FORK == 'true'`, it skips OIDC and sets `TOKENLESS=$GITHUB_EVENT_PULL_REQUEST_HEAD_LABEL`.
2. **Never Set `continue-on-error: true` on `dotnet test`**:
   - Instead, keep `dotnet test` fail-fast (required by `scripts/check-workflow-policy.py` rule `(f)`) and guard Codecov steps with `if: ${{ !cancelled() && env.ACT != 'true' }}`.
3. **Zero-Dependency TRX → JUnit XML Conversion**:
   - Rather than adding third-party `JunitXml.TestLogger` NuGet packages (which touches 6 `packages.lock.json` files and triggers `scripts/check_nuget_licences.py`), convert `**/TestResults/*.trx` to JUnit XML using a small Python script (`scripts/trx_to_junit.py`) in CI.
   - For `src/DataGuard.VSCode` (Node 22 per `.nvmrc`), use built-in `node --test --experimental-test-coverage --test-reporter=lcov --test-reporter-destination=coverage/lcov.info --test-reporter=junit --test-reporter-destination=coverage/junit.xml`.
4. **Path Normalization & `.github/codecov.yml` Placement**:
   - Store config at `.github/codecov.yml` (natively discovered by Codecov) so root file guards (`scripts/anti_garbage_guard.sh`, `scripts/preflight_agent_check.sh`) pass without root clutter.
   - Configure `ignore:` to exclude `tests/**`, `samples/**`, `scripts/**`, `**/obj/**`, and generated files (`*.g.cs`).

### 4. Security & OpenSSF Scorecard Considerations
- **40-char SHA Pinning**: `.github/workflows/standards-audit.yml:51-65` enforces `^[^@]+@[0-9a-f]{40}([[:space:]]+#.*)?$`. Must pin `codecov/codecov-action@303a32d7a59b442fa8d48b6a1cc6825c09c847a5 # v7.1.1`.
- **Job-Scoped Permissions**: Keep workflow-level `permissions: contents: read` in `.github/workflows/ci.yml` and add `permissions: { contents: read, id-token: write }` only on jobs that upload to Codecov (`build-and-test`, `live-db-integration`, `vscode-extension`), preserving OpenSSF Scorecard `Token-Permissions` 10/10.

## Comparative Analysis
| Approach | Coverage Scope | Lockfile Impact | Maintenance |
|---|---|---|---|
| **Option A: Minimal .NET Only** | `build-and-test` Cobertura only | None | Lowest, but misses LiveDB, VS Code, and Test Analytics |
| **Option B: Full Free-Tier Suite** | `.NET Unit` + `LiveDB` + `VS Code` + `6 Components` + `Test Analytics` | **None** (uses Python TRX→JUnit + Node 22 native reporters) | Optimal balance of observability and zero dependency bloat |
| **Option C: Full + Bundle Analyzer** | Option B + `@codecov/bundle-analyzer` | Modifies `src/DataGuard.VSCode/package-lock.json` | Overkill for `tsc`-compiled VS Code extension |

## Resources & References
- [Codecov GitHub Action v7.1.1](https://github.com/codecov/codecov-action/releases/tag/v7.1.1)
- [Codecov YAML Reference](https://docs.codecov.com/docs/codecovyml-reference)
- [Codecov Components](https://docs.codecov.com/docs/components)
- [Codecov Flags](https://docs.codecov.com/docs/flags)
- [Codecov Test Analytics](https://docs.codecov.com/docs/test-analytics)
- [Codecov Status Badges & Graphs](https://docs.codecov.com/docs/status-badges)

## Unresolved Questions
- One-time manual prerequisite: Repo owner must enable the repository at `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/new` and install the Codecov GitHub App so PR comments and GitHub Checks annotations can be posted.
