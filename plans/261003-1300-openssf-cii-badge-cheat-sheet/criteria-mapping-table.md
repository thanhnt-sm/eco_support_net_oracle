# OpenSSF Best Practices (CII) Passing Criteria Mapping Table: DataGuard

Target Repository: `thanhnt-sm/eco_support_net_oracle`  
Canonical Branch: `main`  
OpenSSF Portal: [https://bestpractices.dev](https://bestpractices.dev)

---

## Overview

This document provides the authoritative mapping between all OpenSSF Best Practices Passing-tier criteria and the actual implementation artifacts, configuration files, CI workflows, and operational procedures in the `eco_support_net_oracle` repository.

---

## 1. Basics

| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `basics_project_website` | Project Website | `https://github.com/thanhnt-sm/eco_support_net_oracle` | Met |
| `basics_description` | Project Description | `README.md` ("DataGuard detects drift between your .NET entities and the SQL...") | Met |
| `basics_interact` | How to interact/contribute | `CONTRIBUTING.md`, `CONTRIBUTING.vi.md` | Met |
| `basics_contribution` | Contribution guidelines | `CONTRIBUTING.md` Section: Development Workflow | Met |
| `basics_contribution_requirements` | Contribution requirements | `CONTRIBUTING.md` (Code of Conduct, tests, 0 warnings policy) | Met |
| `basics_license_oss` | Open Source License | `LICENSE` (GPL-3.0-only) | Met |
| `basics_license_floss` | FLOSS License | GNU GPL v3.0 is approved by OSI and FSF | Met |
| `basics_license_location` | License Location | Top-level `LICENSE` file | Met |
| `basics_documentation` | Documentation provided | `docs/`, `README.md`, bilingual documentation | Met |
| `basics_documentation_basics` | Basic install/run docs | `README.md` Quickstart | Met |
| `basics_documentation_interface` | Interface / API docs | `src/DataGuard.Contracts/`, `docs/cli.md` | Met |
| `basics_doc_sites` | Documentation websites | `README.md`, `docs/README.md` | Met |
| `basics_doc_english` | English documentation | English documentation provided across `docs/` and `README.md` | Met |
| `basics_doc_community` | Community documentation | `CONTRIBUTING.md`, `SUPPORT.md`, `CODE_OF_CONDUCT.md` | Met |

---

## 2. Change Control

| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `change_control_repo_public` | Public Repository | GitHub public repository `thanhnt-sm/eco_support_net_oracle` | Met |
| `change_control_repo_track` | Track individual changes | Git commit history with conventional commits | Met |
| `change_control_repo_interim` | Interim changes tracked | Feature branches, pull requests, and commit history | Met |
| `change_control_repo_distributed` | Distributed VCS | Git distributed version control system | Met |
| `change_control_version_unique` | Unique version identifiers | Git tags (`v0.2.2`, etc.) and SemVer versions | Met |
| `change_control_version_semver` | Semantic Versioning | `Directory.Build.props`, `v0.x.y` semantic versioning scheme | Met |
| `change_control_release_notes` | Release Notes | GitHub Releases and `CHANGELOG.md` | Met |
| `change_control_release_notes_vulnerabilities` | Vulnerability notes | Fixed security advisories and CVEs noted in release descriptions | Met |

---

## 3. Reporting

| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `reporting_bugs_mechanism` | Bug reporting mechanism | GitHub Issues (`.github/ISSUE_TEMPLATE/`) | Met |
| `reporting_bugs_responses` | Bug response policy | `CONTRIBUTING.md`, issue triage guidelines | Met |
| `reporting_bugs_archive` | Public bug archive | GitHub Issues closed/open history | Met |
| `reporting_vulnerabilities_mechanism` | Vulnerability reporting | `SECURITY.md` (GitHub Private Security Advisories) | Met |
| `reporting_vulnerabilities_response` | Vulnerability response SLA | `SECURITY.md` ("acknowledge reports within 5 business days") | Met |
| `reporting_vulnerabilities_archive` | Vulnerability archive | GitHub Security Advisories published disclosures | Met |

---

## 4. Quality

| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `quality_working_build` | Working build system | `dotnet build DataGuard.CrossPlatform.slnf` | Met |
| `quality_build_common_tools` | Standard build tools | `dotnet` CLI (.NET SDK) | Met |
| `quality_build_floss_tools` | FLOSS build tools | .NET SDK is open source under permissive FLOSS license | Met |
| `quality_automated_tests` | Automated test suite | xUnit suite in `tests/` (Core, GoldenCorpus, Analyzers) | Met |
| `quality_test_floss_tools` | FLOSS test tools | xUnit and `dotnet test` (permissive FLOSS licenses) | Met |
| `quality_test_continuous_integration` | Continuous Integration | `.github/workflows/ci.yml` (runs on push/PR) | Met |
| `quality_warnings` | Compiler warnings enabled | `TreatWarningsAsErrors=true` in `Directory.Build.props` / 0 warnings policy | Met |
| `quality_warnings_fixed` | Compiler warnings addressed | CI fails on warnings; clean builds enforced | Met |
| `quality_test_policy` | Test policy for changes | `CONTRIBUTING.md` requires automated unit/integration tests for PRs | Met |
| `quality_installation_common` | Standard installation | GitHub Releases binaries, .NET global tool, or VS Code marketplace | Met |
| `quality_installation_standard` | Standard install tools | Standard system utilities (unzip, tar, dotnet) | Met |
| `quality_external_dependencies` | Document external dependencies | `Directory.Packages.props`, NuGet package references | Met |

---

## 5. Security

| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `security_policy` | Published security policy | `SECURITY.md` | Met |
| `security_secure_design` | Secure design principles | Zero-trust credentials, `--ide-safe`, tamper-evident audit logs | Met |
| `security_crypto_published` | Standard crypto algorithms | SHA-256 for checksums, TLS 1.3 for connections, no custom crypto | Met |
| `security_crypto_floss` | FLOSS cryptography | Standard .NET runtime cryptographic libraries (`System.Security.Cryptography`) | Met |
| `security_credentials` | Secret hygiene & scanning | TruffleHog secret scan in `.github/workflows/ci.yml`, no committed credentials | Met |
| `security_dependency_management` | Dependency auditing | `NuGetAuditMode=all`, Dependabot (`.github/dependabot.yml`), package lockfiles | Met |
| `security_vulnerabilities_fixed` | Vulnerabilities fixed timely | Dependabot automated PRs and patch cadence | Met |
| `security_no_leaked_creds` | No credentials in repo | Automated CI secret scanning and preflight hygiene checks | Met |
| `security_assurance_case` | Assurance case / threat model | `docs/architecture/system_architecture.md`, `SECURITY.md` | Met |

---

## 6. Analysis

| ID | Criterion | Repository Asset / Evidence | Disposition |
|---|---|---|---|
| `analysis_static` | Static analysis performed | CodeQL workflow (`.github/workflows/codeql.yml`), Roslyn analyzers (`DataGuard.Analyzers`) | Met |
| `analysis_static_tools` | FLOSS static analysis tools | GitHub CodeQL (free for open source) and Roslyn analyzers | Met |
| `analysis_dynamic` | Dynamic analysis | Automated integration tests, Testcontainers, regression suites | Met |
| `analysis_dynamic_tools` | FLOSS dynamic analysis tools | Testcontainers, xUnit, and .NET test engine | Met |
| `analysis_fixed` | Static analysis issues fixed | Zero critical/high CodeQL alerts policy | Met |
| `analysis_memory_safety` | Memory safety analysis | Managed runtime (.NET 9.0 CLR) guarantees type safety, memory safety, garbage collection, and bounds checking; native memory sanitizers (ASan/Valgrind) are not applicable | N/A |
