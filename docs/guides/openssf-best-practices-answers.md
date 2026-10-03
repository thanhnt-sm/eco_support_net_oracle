# OpenSSF Best Practices (CII) Questionnaire Cheat Sheet: DataGuard

> **Target Repository**: `thanhnt-sm/eco_support_net_oracle`  
> **Self-Certification Portal**: [https://bestpractices.dev](https://bestpractices.dev)  
> **Target Tier**: Passing (100% compliant)  
> **Historical License Note**: Dual-licensed under GNU GPL v3.0-only and Commercial since release v0.3.0; earlier pre-v0.3.0 commits historically MIT (see `docs/legal/MIT-v0.1.0-v0.3.0.txt`).

---

## Table of Contents

- [Quick Reference Checklist](#quick-reference-checklist)
- [Maintainer Registration Runbook](#maintainer-registration-runbook)
- [1. Basics](#1-basics)
- [2. Change Control](#2-change-control)
- [3. Reporting](#3-reporting)
- [4. Quality](#4-quality)
- [5. Security](#5-security)
- [6. Analysis](#6-analysis)

## Quick Summary / Fast-Fill Reference Table

Use this table to rapidly fill the web form at [bestpractices.dev](https://bestpractices.dev). For detailed justification text to paste into audit boxes, click the criterion link.

| ID | Criterion | Tab | Status | Direct Evidence Link |
|---|---|---|---|---|
| [`basics_project_website`](#basics_project_website--project-website) | Project Website | Basics | `Met` | `https://github.com/thanhnt-sm/eco_support_net_oracle` |
| [`basics_description`](#basics_description--project-description) | Project Description | Basics | `Met` | [README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md) |
| [`basics_interact`](#basics_interact--how-to-interact-and-contribute) | How to interact/contribute | Basics | `Met` | [CONTRIBUTING.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md) |
| [`basics_contribution`](#basics_contribution--contribution-guidelines) | Contribution guidelines | Basics | `Met` | [CONTRIBUTING.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md) |
| [`basics_contribution_requirements`](#basics_contribution_requirements--contribution-requirements) | Contribution requirements | Basics | `Met` | [CONTRIBUTING.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md) |
| [`basics_license_oss`](#basics_license_oss--open-source-license) | Open Source License | Basics | `Met` | [LICENSE](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/LICENSE) |
| [`basics_license_floss`](#basics_license_floss--floss-license) | FLOSS License | Basics | `Met` | [LICENSE](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/LICENSE) |
| [`basics_license_location`](#basics_license_location--license-location) | License Location | Basics | `Met` | [LICENSE](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/LICENSE) |
| [`basics_documentation`](#basics_documentation--documentation-provided) | Documentation provided | Basics | `Met` | [docs/README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/README.md) |
| [`basics_documentation_basics`](#basics_documentation_basics--basic-install-and-usage-documentation) | Basic install/run docs | Basics | `Met` | [README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md) |
| [`basics_documentation_interface`](#basics_documentation_interface--interface-and-api-documentation) | Interface / API docs | Basics | `Met` | [docs/cli.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/cli.md) |
| [`basics_doc_sites`](#basics_doc_sites--documentation-websites) | Documentation websites | Basics | `Met` | [docs/README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/README.md) |
| [`basics_doc_english`](#basics_doc_english--english-documentation) | English documentation | Basics | `Met` | [README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md) |
| [`basics_doc_community`](#basics_doc_community--community-documentation) | Community documentation | Basics | `Met` | [CONTRIBUTING.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md) |
| [`change_control_repo_public`](#change_control_repo_public--public-source-code-repository) | Public Repository | Change Control | `Met` | `https://github.com/thanhnt-sm/eco_support_net_oracle` |
| [`change_control_repo_track`](#change_control_repo_track--track-individual-changes) | Track changes | Change Control | `Met` | [Commits](https://github.com/thanhnt-sm/eco_support_net_oracle/commits/main) |
| [`change_control_repo_interim`](#change_control_repo_interim--interim-changes-tracked) | Interim changes tracked | Change Control | `Met` | [Pull Requests](https://github.com/thanhnt-sm/eco_support_net_oracle/pulls) |
| [`change_control_repo_distributed`](#change_control_repo_distributed--distributed-version-control) | Distributed VCS | Change Control | `Met` | `https://github.com/thanhnt-sm/eco_support_net_oracle` |
| [`change_control_version_unique`](#change_control_version_unique--unique-version-identifiers) | Unique versions | Change Control | `Met` | [Tags](https://github.com/thanhnt-sm/eco_support_net_oracle/tags) |
| [`change_control_version_semver`](#change_control_version_semver--semantic-versioning) | Semantic Versioning | Change Control | `Met` | [Directory.Build.props](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/Directory.Build.props) |
| [`change_control_release_notes`](#change_control_release_notes--release-notes) | Release Notes | Change Control | `Met` | [CHANGELOG.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CHANGELOG.md) |
| [`change_control_release_notes_vulnerabilities`](#change_control_release_notes_vulnerabilities--release-notes-vulnerabilities) | Vulnerability notes | Change Control | `Met` | [CHANGELOG.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CHANGELOG.md) |
| [`reporting_bugs_mechanism`](#reporting_bugs_mechanism--bug-reporting-mechanism) | Bug reporting mechanism | Reporting | `Met` | [Issues](https://github.com/thanhnt-sm/eco_support_net_oracle/issues) |
| [`reporting_bugs_responses`](#reporting_bugs_responses--bug-response-policy) | Bug response policy | Reporting | `Met` | [CONTRIBUTING.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md) |
| [`reporting_bugs_archive`](#reporting_bugs_archive--public-bug-archive) | Public bug archive | Reporting | `Met` | [Issues Archive](https://github.com/thanhnt-sm/eco_support_net_oracle/issues?q=is%3Aissue) |
| [`reporting_vulnerabilities_mechanism`](#reporting_vulnerabilities_mechanism--vulnerability-reporting-mechanism) | Vulnerability reporting | Reporting | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`reporting_vulnerabilities_response`](#reporting_vulnerabilities_response--vulnerability-response-sla) | Vulnerability response SLA | Reporting | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`reporting_vulnerabilities_archive`](#reporting_vulnerabilities_archive--vulnerability-archive) | Vulnerability archive | Reporting | `Met` | [Security Advisories](https://github.com/thanhnt-sm/eco_support_net_oracle/security/advisories) |
| [`quality_working_build`](#quality_working_build--working-build-system) | Working build system | Quality | `Met` | [DataGuard.CrossPlatform.slnf](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/DataGuard.CrossPlatform.slnf) |
| [`quality_build_common_tools`](#quality_build_common_tools--standard-build-tools) | Standard build tools | Quality | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`quality_build_floss_tools`](#quality_build_floss_tools--floss-build-tools) | FLOSS build tools | Quality | `Met` | [dotnet/sdk](https://github.com/dotnet/sdk) |
| [`quality_automated_tests`](#quality_automated_tests--automated-test-suite) | Automated test suite | Quality | `Met` | [tests/](https://github.com/thanhnt-sm/eco_support_net_oracle/tree/main/tests) |
| [`quality_test_floss_tools`](#quality_test_floss_tools--floss-test-tools) | FLOSS test tools | Quality | `Met` | [xunit/xunit](https://github.com/xunit/xunit) |
| [`quality_test_continuous_integration`](#quality_test_continuous_integration--continuous-integration) | Continuous Integration | Quality | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`quality_warnings`](#quality_warnings--compiler-warnings-enabled) | Compiler warnings enabled | Quality | `Met` | [Directory.Build.props](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/Directory.Build.props) |
| [`quality_warnings_fixed`](#quality_warnings_fixed--compiler-warnings-addressed) | Compiler warnings addressed | Quality | `Met` | [CI Actions](https://github.com/thanhnt-sm/eco_support_net_oracle/actions) |
| [`quality_test_policy`](#quality_test_policy--test-policy-for-changes) | Test policy for changes | Quality | `Met` | [CONTRIBUTING.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md) |
| [`quality_installation_common`](#quality_installation_common--standard-installation) | Standard installation | Quality | `Met` | [README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md) |
| [`quality_installation_standard`](#quality_installation_standard--standard-install-tools) | Standard install tools | Quality | `Met` | [README.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md) |
| [`quality_external_dependencies`](#quality_external_dependencies--external-dependencies-documented) | External dependencies | Quality | `Met` | [docs/legal/THIRD-PARTY-NOTICES.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/legal/THIRD-PARTY-NOTICES.md) |
| [`security_policy`](#security_policy--published-security-policy) | Published security policy | Security | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`security_secure_design`](#security_secure_design--secure-design-principles) | Secure design principles | Security | `Met` | [docs/architecture/](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md) |
| [`security_crypto_published`](#security_crypto_published--standard-cryptographic-algorithms) | Standard crypto | Security | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`security_crypto_floss`](#security_crypto_floss--floss-cryptography) | FLOSS cryptography | Security | `Met` | [dotnet/runtime](https://github.com/dotnet/runtime) |
| [`security_credentials`](#security_credentials--secret-hygiene-and-scanning) | Secret scanning | Security | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`security_dependency_management`](#security_dependency_management--dependency-auditing) | Dependency auditing | Security | `Met` | [.github/dependabot.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/dependabot.yml) |
| [`security_vulnerabilities_fixed`](#security_vulnerabilities_fixed--vulnerabilities-remediated-timely) | Vulnerabilities remediated | Security | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`security_no_leaked_creds`](#security_no_leaked_creds--no-credentials-in-repository) | No credentials in repo | Security | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`security_assurance_case`](#security_assurance_case--security-assurance-case) | Assurance case | Security | `Met` | [docs/architecture/](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md) |
| [`analysis_static`](#analysis_static--static-analysis-performed) | Static analysis | Analysis | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`analysis_static_tools`](#analysis_static_tools--floss-static-analysis-tools) | FLOSS static analysis tools | Analysis | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`analysis_dynamic`](#analysis_dynamic--dynamic-analysis-performed) | Dynamic analysis | Analysis | `Met` | [.github/workflows/ci.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml) |
| [`analysis_dynamic_tools`](#analysis_dynamic_tools--floss-dynamic-analysis-tools) | FLOSS dynamic analysis tools | Analysis | `Met` | [Testcontainers](https://github.com/testcontainers/testcontainers-dotnet) |
| [`analysis_fixed`](#analysis_fixed--static-analysis-findings-addressed) | Static analysis fixed | Analysis | `Met` | [Code Scanning](https://github.com/thanhnt-sm/eco_support_net_oracle/security/code-scanning) |
| [`analysis_memory_safety`](#analysis_memory_safety--memory-safety-analysis) | Memory safety analysis | Analysis | `N/A` | [CLR Garbage Collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/) |
| [`maintained`](#maintained--project-maintained) | Project maintained | Basics | `Met` | `https://github.com/thanhnt-sm/eco_support_net_oracle` |
| [`version_tags`](#version_tags--version-tags-used) | Version tags used | Change Control | `Met` | [Tags](https://github.com/thanhnt-sm/eco_support_net_oracle/tags) |
| [`enhancement_responses`](#enhancement_responses--enhancement-requests-addressed) | Enhancement responses | Reporting | `Met` | [Issues](https://github.com/thanhnt-sm/eco_support_net_oracle/issues) |
| [`test_most`](#test_most--test-coverage-of-functionality) | Test coverage of functionality | Quality | `Met` | [tests/](https://github.com/thanhnt-sm/eco_support_net_oracle/tree/main/tests) |
| [`warnings_strict`](#warnings_strict--strict-warning-modes) | Strict warning modes | Quality | `Met` | [Directory.Build.props](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/Directory.Build.props) |
| [`know_common_errors`](#know_common_errors--common-software-errors-understood) | Common errors understood | Security | `Met` | [docs/architecture/](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md) |
| [`crypto_call`](#crypto_call--standard-crypto-calls-used) | Standard crypto calls | Security | `Met` | [System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography) |
| [`crypto_keylength`](#crypto_keylength--cryptographic-key-length) | Crypto key length | Security | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`crypto_working`](#crypto_working--cryptographic-algorithms-unbroken) | Crypto algorithms unbroken | Security | `Met` | [SECURITY.md](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md) |
| [`crypto_weaknesses`](#crypto_weaknesses--cryptographic-weaknesses-prohibited) | Crypto weaknesses prohibited | Security | `Met` | [.github/codeql-config.yml](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/codeql-config.yml) |
| [`crypto_pfs`](#crypto_pfs--perfect-forward-secrecy) | Perfect forward secrecy | Security | `N/A` | [docs/architecture/](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md) |
| [`crypto_password_storage`](#crypto_password_storage--password-storage) | Password storage | Security | `N/A` | [docs/architecture/](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md) |
| [`crypto_random`](#crypto_random--cryptographically-secure-random) | Cryptographically secure random | Security | `Met` | [RandomNumberGenerator](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator) |
| [`delivery_unsigned`](#delivery_unsigned--authenticity-verification-of-deliveries) | Delivery authenticity | Security | `Met` | [Releases](https://github.com/thanhnt-sm/eco_support_net_oracle/releases) |

---

## Maintainer Registration Runbook

When submitting this questionnaire on [bestpractices.dev](https://bestpractices.dev):

1. **Sign In**: Navigate to `https://www.bestpractices.dev/en/users/auth/github` and authenticate with your GitHub account.
2. **Select Project**: Go to `https://www.bestpractices.dev/en/projects/new`, select repository `thanhnt-sm/eco_support_net_oracle` (assigned Project ID: **`15184`**).
3. **Portal Link**: Active badge page is [https://www.bestpractices.dev/en/projects/15184/passing](https://www.bestpractices.dev/en/projects/15184/passing).
4. **Fill Answers**: All 67 criteria across all 6 tabs are self-certified and active.
5. **Submission Status**: Fully submitted — badge achieved at **100% Passing**.
6. **Activated Badge in README**: Badge markup in `README.md` and `README.vi.md` is active with Project ID `15184`.

---

## 1. Basics

### `basics_project_website` — Project Website
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle`
- **Justification**:
  ```text
  The GitHub repository serves as the central project website, hosting complete documentation, architecture diagrams, user guides, and release packages.
  ```

### `basics_description` — Project Description
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md`
- **Justification**:
  ```text
  README.md describes DataGuard: design-time and CI/CD contract validation detecting schema and parameter drift between .NET entities and SQL Server/Oracle stored procedures and queries.
  ```

### `basics_interact` — How to Interact and Contribute
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md`
- **Justification**:
  ```text
  CONTRIBUTING.md and CONTRIBUTING.vi.md document the interaction process, issue reporting workflow, branch naming conventions, PR requirements, and developer setup.
  ```

### `basics_contribution` — Contribution Guidelines
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md`
- **Justification**:
  ```text
  CONTRIBUTING.md provides development standards, coding rules, test requirements, conventional commit conventions, and step-by-step guidance for submitting pull requests.
  ```

### `basics_contribution_requirements` — Contribution Requirements
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md`
- **Justification**:
  ```text
  All contributions require passing automated CI tests, 0 compiler warnings (TreatWarningsAsErrors=true), adherence to Code of Conduct, and code review before merge.
  ```

### `basics_license_oss` — Open Source License
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/LICENSE`
- **Justification**:
  ```text
  The project is licensed under the GNU General Public License v3.0 (GPL-3.0-only), an open source license recognized by the Open Source Initiative (OSI).
  ```

### `basics_license_floss` — FLOSS License
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/LICENSE`
- **Justification**:
  ```text
  GNU GPL-3.0 is an approved Free and Open Source Software (FLOSS) license certified by both OSI and the Free Software Foundation (FSF).
  ```

### `basics_license_location` — License Location
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/LICENSE`
- **Justification**:
  ```text
  The license is maintained in the standard top-level LICENSE file in the repository root directory.
  ```

### `basics_documentation` — Documentation Provided
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/README.md`
- **Justification**:
  ```text
  Comprehensive bilingual documentation (English and Vietnamese) is maintained under docs/, covering architecture, operations, testing, CLI usage, and component models.
  ```

### `basics_documentation_basics` — Basic Install and Usage Documentation
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md`
- **Justification**:
  ```text
  README.md Quickstart provides concise instructions for installing the CLI, initializing configuration (.dataguard.yml), validating contracts, and generating drift snapshots.
  ```

### `basics_documentation_interface` — Interface and API Documentation
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/cli.md`
- **Justification**:
  ```text
  CLI commands, command-line arguments, JSON output formats, and public contracts in src/DataGuard.Contracts are fully documented in docs/cli.md and component guides.
  ```

### `basics_doc_sites` — Documentation Websites
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/README.md`
- **Justification**:
  ```text
  Documentation is hosted on GitHub and organized through docs/README.md which serves as the index hub for all modules.
  ```

### `basics_doc_english` — English Documentation
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md`
- **Justification**:
  ```text
  All primary documentation, README files, contribution guides, and architectural specifications are written and maintained in English.
  ```

### `basics_doc_community` — Community Documentation
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md`
- **Justification**:
  ```text
  Community engagement guidelines, governance policies, and support mechanisms are detailed in CONTRIBUTING.md, SUPPORT.md, and rules/workspace_governance.md.
  ```

---

## 2. Change Control

### `change_control_repo_public` — Public Source Code Repository
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle`
- **Justification**:
  ```text
  Source code is publicly accessible on GitHub at https://github.com/thanhnt-sm/eco_support_net_oracle.
  ```

### `change_control_repo_track` — Track Individual Changes
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/commits/main`
- **Justification**:
  ```text
  All commits track author, timestamp, commit hash, and structured commit message following conventional commit standards.
  ```

### `change_control_repo_interim` — Interim Changes Tracked
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/pulls`
- **Justification**:
  ```text
  Every proposed change is tracked through pull requests, topic branches, and GitHub Actions test runs before merging into main.
  ```

### `change_control_repo_distributed` — Distributed Version Control
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle`
- **Justification**:
  ```text
  The project uses Git, a distributed version control system.
  ```

### `change_control_version_unique` — Unique Version Identifiers
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/tags`
- **Justification**:
  ```text
  Every release receives a unique Git tag (e.g. v0.2.2) and matching build metadata stamped in Directory.Build.props.
  ```

### `change_control_version_semver` — Semantic Versioning
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/Directory.Build.props`
- **Justification**:
  ```text
  The project follows Semantic Versioning 2.0.0 (MAJOR.MINOR.PATCH) configured across all assemblies via Directory.Build.props.
  ```

### `change_control_release_notes` — Release Notes
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CHANGELOG.md`
- **Justification**:
  ```text
  CHANGELOG.md details added features, changes, fixes, and breaking changes for each release following the Keep a Changelog format.
  ```

### `change_control_release_notes_vulnerabilities` — Release Notes Vulnerabilities
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CHANGELOG.md`
- **Justification**:
  ```text
  Security fixes and vulnerability remediations are explicitly identified in release notes and GitHub release descriptions.
  ```

---

## 3. Reporting

### `reporting_bugs_mechanism` — Bug Reporting Mechanism
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/issues`
- **Justification**:
  ```text
  GitHub Issues with structured issue templates (.github/ISSUE_TEMPLATE/) is used for public bug reports and feature requests.
  ```

### `reporting_bugs_responses` — Bug Response Policy
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md`
- **Justification**:
  ```text
  Project maintainers triage incoming GitHub issues according to response guidelines defined in CONTRIBUTING.md.
  ```

### `reporting_bugs_archive` — Public Bug Archive
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/issues?q=is%3Aissue`
- **Justification**:
  ```text
  All historical issues, bug discussions, and resolution details remain permanently archived and publicly searchable in GitHub Issues.
  ```

### `reporting_vulnerabilities_mechanism` — Vulnerability Reporting Mechanism
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  SECURITY.md establishes GitHub Private Vulnerability Reporting as the official mechanism for confidentially reporting security vulnerabilities.
  ```

### `reporting_vulnerabilities_response` — Vulnerability Response SLA
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  SECURITY.md commits to acknowledging vulnerability reports within 5 business days and providing regular status updates until patched.
  ```

### `reporting_vulnerabilities_archive` — Vulnerability Archive
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/security/advisories`
- **Justification**:
  ```text
  Disclosed security advisories and CVEs are published and archived in the GitHub Security Advisories registry.
  ```

---

## 4. Quality

### `quality_working_build` — Working Build System
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/DataGuard.CrossPlatform.slnf`
- **Justification**:
  ```text
  The project builds cleanly using standard .NET tooling via `dotnet build DataGuard.CrossPlatform.slnf`.
  ```

### `quality_build_common_tools` — Standard Build Tools
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  Builds invoke standard .NET SDK and MSBuild command-line interfaces without proprietary or closed build tooling.
  ```

### `quality_build_floss_tools` — FLOSS Build Tools
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/dotnet/sdk`
- **Justification**:
  ```text
  The build tools (.NET SDK, MSBuild, Roslyn) are open source under permissive FLOSS licenses approved by OSI.
  ```

### `quality_automated_tests` — Automated Test Suite
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/tree/main/tests`
- **Justification**:
  ```text
  Automated tests in tests/ cover core diff logic, CLI execution, Roslyn analyzer rules, golden corpus suites, and packaging integrity.
  ```

### `quality_test_floss_tools` — FLOSS Test Tools
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/xunit/xunit`
- **Justification**:
  ```text
  Tests are executed using xUnit and `dotnet test`, both open source tools licensed under OSI-approved permissive FLOSS licenses.
  ```

### `quality_test_continuous_integration` — Continuous Integration
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  GitHub Actions CI (.github/workflows/ci.yml) automatically runs builds, unit tests, integration tests, and packaging gates on every push and pull request.
  ```

### `quality_warnings` — Compiler Warnings Enabled
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/Directory.Build.props`
- **Justification**:
  ```text
  Directory.Build.props enforces TreatWarningsAsErrors=true, zero compiler warnings policy, nullable reference types, and strict Roslyn analysis rules.
  ```

### `quality_warnings_fixed` — Compiler Warnings Addressed
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/actions`
- **Justification**:
  ```text
  CI builds fail on any compiler warning. All production code and test suites maintain 0 compiler warnings.
  ```

### `quality_test_policy` — Test Policy for Changes
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/CONTRIBUTING.md`
- **Justification**:
  ```text
  CONTRIBUTING.md mandates automated unit and integration tests for every bug fix and feature pull request before merge.
  ```

### `quality_installation_common` — Standard Installation
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md`
- **Justification**:
  ```text
  DataGuard binaries are distributed via GitHub Releases zip/tar archives and installable as standard .NET global tools and IDE extensions.
  ```

### `quality_installation_standard` — Standard Install Tools
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/README.md`
- **Justification**:
  ```text
  Installation requires only standard operating system extraction utilities (tar, unzip) or standard .NET SDK CLI commands.
  ```

### `quality_external_dependencies` — External Dependencies Documented
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/legal/THIRD-PARTY-NOTICES.md`
- **Justification**:
  ```text
  Directory.Build.props, Directory.Build.targets, and docs/legal/THIRD-PARTY-NOTICES.md document external package versions, licenses, and lockfiles.
  ```

---

## 5. Security

### `security_policy` — Published Security Policy
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  SECURITY.md defines supported versions, vulnerability reporting procedures, disclosure SLAs, and security boundary guidelines.
  ```

### `security_secure_design` — Secure Design Principles
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md`
- **Justification**:
  ```text
  DataGuard follows zero-trust credential hygiene, read-only database connections, local snapshot isolation, and design-time AST parsing without dynamic code execution.
  ```

### `security_crypto_published` — Standard Cryptographic Algorithms
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  Cryptographic operations use standard SHA-256 for release checksums and TLS 1.3 for secure database transport. No proprietary cryptography is implemented.
  ```

### `security_crypto_floss` — FLOSS Cryptography
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/dotnet/runtime`
- **Justification**:
  ```text
  Cryptographic primitives rely on System.Security.Cryptography in the .NET runtime, implemented via standard open source libraries (OpenSSL / Windows CNG).
  ```

### `security_credentials` — Secret Hygiene and Scanning
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  TruffleHog secret scanning runs in GitHub Actions CI (.github/workflows/ci.yml) to ensure no credentials, tokens, or private keys enter the codebase.
  ```

### `security_dependency_management` — Dependency Auditing
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/dependabot.yml`
- **Justification**:
  ```text
  NuGetAuditMode=all is enabled in Directory.Build.props to detect vulnerable packages at build time, backed by automated Dependabot security updates.
  ```

### `security_vulnerabilities_fixed` — Vulnerabilities Remediated Timely
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  High and critical severity dependency vulnerabilities are prioritized for patch release within 14 days of upstream disclosure.
  ```

### `security_no_leaked_creds` — No Credentials in Repository
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  Continuous secret scanning with TruffleHog and zero-credential CI design ensure no production passwords, API tokens, or keys are committed.
  ```

### `security_assurance_case` — Security Assurance Case
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md`
- **Justification**:
  ```text
  System architecture documentation models threats, isolation boundaries, credential lifecycle, and offline fallback modes.
  ```

---

## 6. Analysis

### `analysis_static` — Static Analysis Performed
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  GitHub CodeQL job in GitHub Actions (.github/workflows/ci.yml) runs static application security testing (SAST) on every pull request and weekly schedule.
  ```

### `analysis_static_tools` — FLOSS Static Analysis Tools
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  CodeQL is free for open source projects; Roslyn C# analyzers and .NET SDK code analysis are open source under permissive FLOSS licenses.
  ```

### `analysis_dynamic` — Dynamic Analysis Performed
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/workflows/ci.yml`
- **Justification**:
  ```text
  Integration test suites execute against real database engines via Testcontainers in CI, exercising dynamic queries and runtime drift validation.
  ```

### `analysis_dynamic_tools` — FLOSS Dynamic Analysis Tools
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/testcontainers/testcontainers-dotnet`
- **Justification**:
  ```text
  Testcontainers and xUnit are open source under OSI-approved permissive FLOSS licenses (Apache-2.0).
  ```

### `analysis_fixed` — Static Analysis Findings Addressed
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/security/code-scanning`
- **Justification**:
  ```text
  The repository maintains a strict policy of 0 unresolved high or critical CodeQL alerts, enforced as a merge blocker in PR status checks.
  ```

### `analysis_memory_safety` — Memory Safety Analysis
- **Selection**: `N/A`
- **URL/Evidence**: `https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/`
- **Justification**:
  ```text
  DataGuard is written entirely in managed C# running on .NET 9.0. Memory safety, bounds checking, and lifecycle management are enforced by the Common Language Runtime (CLR) garbage collector and type system. Native memory sanitizers (AddressSanitizer, Valgrind) do not apply to managed .NET code.
  ```

---

## 7. Additional OpenSSF Passing Criteria (Self-Certified)

### `maintained` — Project Maintained
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle`
- **Justification**:
  ```text
  Active development with regular commits, releases, and issue triage on GitHub (https://github.com/thanhnt-sm/eco_support_net_oracle).
  ```

### `version_tags` — Version Tags Used
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/tags`
- **Justification**:
  ```text
  All releases are tagged in git with semantic version tags (e.g. v0.2.2): https://github.com/thanhnt-sm/eco_support_net_oracle/tags
  ```

### `enhancement_responses` — Enhancement Requests Addressed
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/issues`
- **Justification**:
  ```text
  Feature requests and enhancements submitted via GitHub Issues (.github/ISSUE_TEMPLATE/feature_request.yml) are triaged and responded to by maintainers according to CONTRIBUTING.md.
  ```

### `test_most` — Test Coverage of Functionality
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/tree/main/tests`
- **Justification**:
  ```text
  Automated test suites under tests/ cover core diff logic, CLI commands, Roslyn analyzers, and database adapters with automated Cobertura code coverage reporting in CI.
  ```

### `warnings_strict` — Strict Warning Modes
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/Directory.Build.props`
- **Justification**:
  ```text
  Directory.Build.props enforces <TreatWarningsAsErrors>true</TreatWarningsAsErrors>, <Nullable>enable</Nullable>, and integrates Microsoft.CodeAnalysis.NetAnalyzers and StyleCop.Analyzers.
  ```

### `know_common_errors` — Common Software Errors Understood
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md`
- **Justification**:
  ```text
  Architecture specifications (docs/architecture/system_architecture.md) enforce input validation, secure token parsing, credential hygiene, and defense against OWASP/CWE top vulnerabilities.
  ```

### `crypto_call` — Standard Crypto Calls Used
- **Selection**: `Met`
- **URL/Evidence**: `https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography`
- **Justification**:
  ```text
  All cryptographic operations use System.Security.Cryptography standard APIs provided by the .NET runtime (backed by OS OpenSSL/CNG); no custom crypto primitives are implemented.
  ```

### `crypto_keylength` — Cryptographic Key Length
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  Cryptographic hashing uses standard SHA-256 (256-bit) via System.Security.Cryptography.SHA256 for snapshot validation and tamper-evident audit logging (SECURITY.md).
  ```
### `crypto_working` — Cryptographic Algorithms Unbroken
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/SECURITY.md`
- **Justification**:
  ```text
  The software uses SHA-256 for all cryptographic integrity and security verification. SHA-1 is used solely where mandated by ECMA-335 for computing .NET strong-name assembly public key tokens (non-security identifier); MD5 is strictly prohibited.
  ```

### `crypto_weaknesses` — Cryptographic Weaknesses Prohibited
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/.github/codeql-config.yml`
- **Justification**:
  ```text
  Default CodeQL C# security rules (.github/codeql-config.yml) and .NET compiler analyzers (CA5350, CA5351) flag insecure cryptographic API usage such as broken hash and cipher algorithms.
  ```

### `crypto_pfs` — Perfect Forward Secrecy
- **Selection**: `N/A`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md`
- **Justification**:
  ```text
  DataGuard is a client-side CLI and static analyzer; it does not implement custom key-exchange protocols or act as a network service. Network transport encryption is delegated to standard ADO.NET database drivers.
  ```

### `crypto_password_storage` — Password Storage
- **Selection**: `N/A`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/architecture/system_architecture.md`
- **Justification**:
  ```text
  DataGuard does not store user passwords or authentication credentials. It connects to database endpoints via caller-provided connection strings or environment variables.
  ```

### `crypto_random` — Cryptographically Secure Random
- **Selection**: `Met`
- **URL/Evidence**: `https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator`
- **Justification**:
  ```text
  Random numbers and nonces, when required, utilize System.Security.Cryptography.RandomNumberGenerator, the standard OS-backed CSPRNG in .NET.
  ```

### `delivery_unsigned` — Authenticity Verification of Deliveries
- **Selection**: `Met`
- **URL/Evidence**: `https://github.com/thanhnt-sm/eco_support_net_oracle/releases`
- **Justification**:
  ```text
  Release assets and binaries are distributed exclusively over HTTPS via GitHub Releases; each release includes verifiable SHA-256 checksums.
  ```
