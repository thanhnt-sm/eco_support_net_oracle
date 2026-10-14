---
title: "Codecov Free-Tier Integration and README Telemetry"
description: "Integrate Codecov.io free OSS features (Multi-Flag Coverage, 6 Architectural Components, OIDC Auth, Test Analytics via TRX-to-JUnit & Node 22 reporters, GitHub Checks Annotations, and bilingual README badges/Sunburst/Grid graphs)"
status: completed
priority: P1
effort: 3h
branch: main
tags: [ci, codecov, coverage, test-analytics, documentation, openssf]
blockedBy: []
blocks: []
created: 2026-10-14
---

# Codecov Free-Tier Integration and README Telemetry

## Overview

Integrate `thanhnt-sm/eco_support_net_oracle` with Codecov (`https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle`) using the agreed **Option B: Full Free-Tier Suite** design from [brainstorm-261014-codecov-integration.md](../reports/brainstorm-261014-codecov-integration.md) and [researcher-261014-codecov-free-tier-best-practices.md](../reports/researcher-261014-codecov-free-tier-best-practices.md).

Key architectural invariants:
- **Zero Lockfile Churn**: Keep `RestoreLockedMode=true` (`packages.lock.json`) and `npm ci` (`package-lock.json`) untouched by converting VSTest `.trx` files to JUnit XML via `scripts/trx_to_junit.py` and using Node 22 built-in `--experimental-test-coverage` + `--test-reporter=lcov` + `--test-reporter=junit`.
- **Supply-Chain & OpenSSF Compliance**: Pin `codecov/codecov-action@303a32d7a59b442fa8d48b6a1cc6825c09c847a5 # v7.1.1` (40-char SHA per `.github/workflows/standards-audit.yml`) and use job-scoped OIDC permissions (`id-token: write`, `contents: read`) with `use_oidc: true`.
- **Policy Gate Compliance**: Place configuration at `.github/codecov.yml` (satisfying `scripts/anti_garbage_guard.sh` and `scripts/preflight_agent_check.sh`) and keep `dotnet test` steps strictly fail-fast (satisfying `scripts/check-workflow-policy.py` rule `(f)`).

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Codecov Config and TRX-to-JUnit Converter](./phase-01-codecov-config-and-trx-to-junit-converter.md) | Completed |
| 2 | [CI Workflow Multi-Flag and Test Analytics](./phase-02-ci-workflow-multi-flag-and-test-analytics.md) | Completed |
| 3 | [Bilingual README Telemetry and Gate Verification](./phase-03-bilingual-readme-telemetry-and-gate-verification.md) | Completed |

## Dependencies

- Phase 2 depends on Phase 1 (`.github/codecov.yml` and `scripts/trx_to_junit.py`).
- Phase 3 depends on Phase 1 & Phase 2 (component IDs and flag names defined in `.github/codecov.yml` and uploaded by `.github/workflows/ci.yml`).

## Success Criteria

- [x] `.github/codecov.yml` configures 3 flags (`dotnet-unit`, `live-db`, `vscode-ext`), 6 components (`core_engine`, `db_adapters`, `roslyn_analyzers`, `cli_and_host`, `observability`, `ide_extensions`), project blocking status (`auto`, `1%` threshold), patch informational status (`80%`), PR comment layout, and GitHub Checks annotations.
- [x] `scripts/trx_to_junit.py` converts VSTest `.trx` files to JUnit XML and passes unit tests in `scripts/tests/test_trx_to_junit.py`.
- [x] `.github/workflows/ci.yml` uploads coverage and test results across `build-and-test`, `live-db-integration`, and `vscode-extension` using `codecov/codecov-action@303a32d7a59b442fa8d48b6a1cc6825c09c847a5 # v7.1.1` with `use_oidc: true` and `if: ${{ !cancelled() && env.ACT != 'true' }}`.
- [x] `README.md` and `README.vi.md` include the header Codecov badge, Component & Flag telemetry matrix, and Sunburst/Grid SVG coverage graphs.
- [x] All repository verification scripts (`anti_garbage_guard.sh`, `preflight_agent_check.sh`, `verify_docs_sync.sh`, `check-workflow-policy.py`, and `scripts/tests`) pass with exit code 0.
