# CI/CD, Release and Container Delivery

## Workflow surface

- **[CONFIRMED]** Six workflows exist: `ci.yml`, `build_release.yml`, `release.yml`, `marketplace.yml`, `scorecard.yml`, `standards-audit.yml`.
- **[CONFIRMED]** CI triggers push/PR/manual/scheduled paths, sets read-oriented default permissions, restores locked packages, builds Release, runs analyzer/format/test/coverage gates, security scans, SBOM, benchmark (non-blocking), Docker smoke and CodeQL (`.github/workflows/ci.yml:1-308`).
- **[CONFIRMED]** Release path packs/tests artifacts, repeats vulnerable-package and secret scans, CodeQL, cosign keyless sign/verify, SBOM, marketplace packaging, optional NuGet/OIDC or token publish, attestations and optional GHCR push (`.github/workflows/release.yml:1-607`).
- **[CONFIRMED]** Marketplace workflow publishes VS Code/Visual Studio artifacts with secret names only; values are injected by CI secrets (`.github/workflows/marketplace.yml:1-199`).
- **[CONFIRMED]** Scorecard enables hardened-runner egress audit and SARIF upload; standards audit checks workflow permissions, action SHA pinning and repository security artifacts (`scorecard.yml`; `standards-audit.yml`).
- **[CONFIRMED]** All `uses:` action references inspected are full commit SHA pinned; `actionlint .github/workflows/*.yml` exited 0 with no output.

## Security gates and artifacts

- **[CONFIRMED]** CI package vulnerability step uses `dotnet list package --vulnerable --include-transitive`; TruffleHog uses verified-only mode and exclude paths; Docker history is checked for secrets; CodeQL and SBOM are produced.
- **[CONFIRMED]** Test/coverage/SBOM/SARIF artifacts have workflow retention settings of 7 or 30 days. These are CI artifact settings, not evidence of product telemetry/log retention policy (`ci.yml`, `build_release.yml`, `release.yml`).
- **[UNKNOWN]** No workflow uploads application metrics, traces, structured logs or health snapshots to an observability backend.
- **[UNVERIFIED_EXTERNAL]** No CI run result, vulnerability database response or signing attestation result was available in this read-only snapshot.

## Container

- **[CONFIRMED]** `Dockerfile:1-67` uses pinned .NET SDK/runtime base digests, multi-architecture `TARGETARCH`, locked restore, Release publish and runtime `USER` non-root UID; entrypoint is `DataGuard.Cli.dll`.
- **[CONFIRMED]** `.dockerignore:1-16` excludes git metadata, bin/obj, test/coverage, SBOM/artifacts, scratch and IDE files.
- **[INFERRED_MEDIUM]** Container image build is CLI-only; Host health/LSP/VS surfaces are not shown in this image entrypoint.
- **[UNKNOWN]** Runtime environment variables, Kubernetes/Helm/service definitions, ingress/TLS, sidecars, scrape annotations, resource limits and network policy are absent.

## Delivery blockers for observability planning

- **[CONFIRMED]** Release automation proves artifact security checks but not runtime observability delivery.
- **[UNKNOWN]** No release owner, environment promotion policy, rollback/RTO/RPO or production telemetry approval path is documented.
