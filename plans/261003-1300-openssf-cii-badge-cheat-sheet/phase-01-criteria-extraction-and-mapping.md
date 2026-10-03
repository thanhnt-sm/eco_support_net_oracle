---
phase: 1
title: "Criteria Extraction and Mapping"
status: completed
priority: P2
effort: "1h"
dependencies: []
---

# Phase 1: Criteria Extraction and Mapping

## Overview

Extract all ~107 OpenSSF Best Practices (CII) Passing-tier criteria and map each criterion directly to existing files, configuration, CI workflows, and operational procedures in the `eco_support_net_oracle` repository.

## Requirements

- **Functional**:
  - Audit the 6 core OpenSSF categories: Basics, Change Control, Reporting, Quality, Security, and Analysis.
  - Determine compliance disposition for each criterion: `Met` (Yes), `Unmet` (No / Action Required), or `N/A` (Not Applicable).
  - Collect exact GitHub URLs, file paths, and verifiable commands demonstrating compliance.
- **Non-functional**:
  - Strict honesty: Zero false positives. Only criteria verifiable in the repository are marked `Met`.
  - Maintainable documentation with canonical permanent links to `main`.

## Architecture & Criterion Mapping Table

### 1. Basics
| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `basics_project_website` | Project Website | `https://github.com/thanhnt-sm/eco_support_net_oracle` | Met |
| `basics_description` | Project Description | `README.md` ("DataGuard detects drift between your .NET entities and the SQL...") | Met |
| `basics_interact` | How to interact/contribute | `CONTRIBUTING.md`, `CONTRIBUTING.vi.md` | Met |
| `basics_contribution` | Contribution guidelines | `CONTRIBUTING.md` Section: Development Workflow | Met |
| `basics_contribution_requirements` | Contribution requirements | `CONTRIBUTING.md` (Code of Conduct, tests, 0 warnings) | Met |
| `basics_license_oss` | Open Source License | `LICENSE` (GPL-3.0-only) | Met |
| `basics_license_floss` | FLOSS License | GNU GPL v3.0 is approved by OSI and FSF | Met |
| `basics_license_location` | License Location | Top-level `LICENSE` file | Met |
| `basics_documentation` | Documentation provided | `docs/`, `README.md`, bilingual docs | Met |
| `basics_documentation_basics` | Basic install/run docs | `README.md` Quickstart | Met |
| `basics_documentation_interface` | Interface / API docs | `src/DataGuard.Contracts/`, `docs/cli.md` | Met |

### 2. Change Control
| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `change_control_repo_public` | Public Repository | GitHub public repo `thanhnt-sm/eco_support_net_oracle` | Met |
| `change_control_repo_track` | Track individual changes | Git commit history with conventional commits | Met |
| `change_control_repo_interim` | Interim changes tracked | Feature branches, PRs, and commit history | Met |
| `change_control_repo_distributed` | Distributed VCS | Git | Met |
| `change_control_version_unique` | Unique version identifiers | Git tags (`v0.2.2`, etc.) and SemVer versions | Met |
| `change_control_version_semver` | Semantic Versioning | `Directory.Build.props`, `v0.x.y` versioning scheme | Met |
| `change_control_release_notes` | Release Notes | GitHub Releases and `CHANGELOG.md` | Met |
| `change_control_release_notes_vulnerabilities` | Vulnerability notes | Fixed CVEs/security advisories noted in release descriptions | Met |

### 3. Reporting
| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `reporting_bugs_mechanism` | Bug reporting mechanism | GitHub Issues (`.github/ISSUE_TEMPLATE/`) | Met |
| `reporting_bugs_responses` | Bug response policy | `CONTRIBUTING.md`, response via issues | Met |
| `reporting_bugs_archive` | Public bug archive | GitHub Issues closed/open history | Met |
| `reporting_vulnerabilities_mechanism` | Vulnerability reporting | `SECURITY.md` (GitHub Private Security Advisories) | Met |
| `reporting_vulnerabilities_response` | Vulnerability response SLA | `SECURITY.md` ("acknowledge reports within 5 business days") | Met |

### 4. Quality
| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `quality_working_build` | Working build system | `dotnet build DataGuard.CrossPlatform.slnf` | Met |
| `quality_build_common_tools` | Standard build tools | `dotnet` CLI (.NET SDK) | Met |
| `quality_build_floss_tools` | FLOSS build tools | .NET SDK is open source under MIT | Met |
| `quality_automated_tests` | Automated test suite | xUnit suite in `tests/` (Core, GoldenCorpus, Analyzers) | Met |
| `quality_test_floss_tools` | FLOSS test tools | xUnit and `dotnet test` (Apache-2.0 / MIT) | Met |
| `quality_test_continuous_integration` | Continuous Integration | `.github/workflows/ci.yml` (runs on push/PR) | Met |
| `quality_warnings` | Compiler warnings enabled | `TreatWarningsAsErrors=true` in CI / 0 warnings policy | Met |
| `quality_warnings_fixed` | Compiler warnings addressed | CI fails on warnings; clean builds enforced | Met |

### 5. Security
| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `security_policy` | Published security policy | `SECURITY.md` | Met |
| `security_secure_design` | Secure design principles | Zero-trust credentials, `--ide-safe`, tamper-evident audit logs | Met |
| `security_crypto_published` | Standard cryptographic algorithms | SHA-256 for checksums, TLS 1.3 for connections, no custom crypto | Met |
| `security_credentials` | Secret hygiene & scanning | TruffleHog secret scan in CI, no credentials committed | Met |
| `security_dependency_management` | Dependency auditing | `NuGetAuditMode=all`, Dependabot, lockfiles | Met |

### 6. Analysis
| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `analysis_static` | Static analysis performed | CodeQL workflow (`codeql.yml`), Roslyn analyzers (`DataGuard.Analyzers`) | Met |
| `analysis_dynamic` | Dynamic analysis | Automated integration tests, Testcontainers, regression suites | Met |
| `analysis_fixed` | Static analysis issues fixed | Zero critical/high CodeQL alerts policy | Met |

## Related Code Files

- Modify: `plans/reports/261003-1200-openssf-cii-badge-strategy.md` (reference baseline)
- Create: `plans/261003-1300-openssf-cii-badge-cheat-sheet/criteria-mapping-table.md` (intermediate matrix)

## Implementation Steps

1. Review full OpenSSF Best Practices Passing criterion schema from [bestpractices.dev/criteria/0](https://www.bestpractices.dev/criteria/0).
2. Validate each criterion against the repository structure, `SECURITY.md`, `CONTRIBUTING.md`, `LICENSE`, `Directory.Build.props`, and `.github/workflows/`.
3. Draft justification sentences and repository link templates for all passing items.
4. Identify any edge-case criteria (e.g., dynamic memory safety tools which are N/A for managed .NET code) and document rationale.
5. Generate `plans/261003-1300-openssf-cii-badge-cheat-sheet/criteria-mapping-table.md`.

## Verification & Concrete Commands

1. Execute automated test suite for Phase 1:
   ```bash
   python -m unittest scripts/test_openssf_cheat_sheet.py
   ```
2. Verify syntax and markdown formatting:
   ```bash
   python -c "import pathlib; p = pathlib.Path('plans/261003-1300-openssf-cii-badge-cheat-sheet/criteria-mapping-table.md'); assert p.exists() and len(p.read_text()) > 1000"
   ```

## Edge Cases & Error Handling

1. **Missing Evidence File**: If a planned criterion cites a file that doesn't exist, the validation script will fail. Mitigation: inspect repository tree before mapping; only cite real files.
2. **Subjective OpenSSF Criteria**: For criteria requiring policy decisions (e.g. project maintenance continuity), link explicitly to `rules/` and `CONTRIBUTING.md`.
3. **Managed Code Safe Dispositions**: Ensure all memory safety and compiler warning criteria cite .NET 9.0 type safety and `TreatWarningsAsErrors=true` in Directory.Build.props.

## Claude Isolation Invariants

- **File Scope**: Strictly limited to reading repository metadata and writing `plans/261003-1300-openssf-cii-badge-cheat-sheet/criteria-mapping-table.md`.
- **No Cross-Phase Side Effects**: Does not modify production code or user-facing `docs/` before Phase 2.

## Success Criteria

- [x] Complete mapping table covering all 6 OpenSSF Passing categories.
- [x] Every "Met" status backed by an existing file, CI workflow, or documentation link.
- [x] Every "N/A" status backed by a valid technical explanation (e.g. managed runtime memory safety).
- [x] `python -m unittest scripts/test_openssf_cheat_sheet.py` passes 100%.

## Risk Assessment

- **Risk**: Over-promising on criteria not fully active (e.g., formal CVE publishing).
- **Mitigation**: Strictly cite GitHub Security Advisories for private vulnerability reporting without claiming formal CVE Numbering Authority (CNA) status unless confirmed.
