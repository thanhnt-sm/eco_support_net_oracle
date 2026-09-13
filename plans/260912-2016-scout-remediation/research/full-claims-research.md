---
type: researcher
date: 2026-09-12
scope: expanded-feature-delivery
---
# Full documented capabilities — implementation research

## Summary and method

Owner selected full capability implementation, overriding prior docs-only design. Read current source/docs with two Terra design agents; main researched primary ASP.NET/VSCode/VisualStudio/OSV/NuGet/Sigstore/BenchmarkDotNet documentation on2026-09-12. This is implementation design, not proof DataGuard features are shipped.

## Findings and selected architecture

### HTTP health host

ASP.NET Core supports AddHealthChecks/MapHealthChecks with separate readiness/liveness checks and endpoint policy. Health endpoints need an actual host; adding packages to CLI does not create routes. [Microsoft health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0)

Select separate DataGuard.Host, sharing Core check contracts; explicit launch, loopback default. /health/live indicates process responsiveness, /health/startup initialization completion, /health/ready declared dependency readiness. Background bounded probes update snapshot state; HTTP requests must not cause per-request credential/DB work. Nonsecret minimal responses; remote bind needs explicit deployment policy/authorization. Current net9 Core/net472 VS means target/version compatibility is CP8 gate, using [official lifecycle policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), not a blanket solution upgrade or latest-page assumption.

### Online advisory and scoring

OSV supports batched package/version queries and NuGet data. Batch response contains IDs/modified values, not all details; per-query pagination must be followed and details fetched separately. [OSV API](https://google.github.io/osv.dev/api/), [querybatch contract](https://google.github.io/osv.dev/post-v1-querybatch/), [data sources](https://google.github.io/osv.dev/data/)

Select OSV provider abstraction initially; no token required for selected public API. Explicit CLI assess remote option +operator network consent; public API AllowRemoteLookups is honored only with configured network/privacy policy. Fixed HTTPS endpoint, no automatic redirects, bounded batch/detail/page/response limits; package ID/version only. Unknown/private package identities not uploaded by default. Offline mode local/cached report retains timestamps/coverage; timeout/429/bad data/stale cache never produces “no CVEs/100% healthy”. Dedup aliases, preserve withdrawn metadata, and report advisory IDs even when CVE alias absent. A health score is versioned internal prioritization, not a security guarantee or CVSS substitute.

### IDE settings and realtime

VSCode exposes configuration and document-change events suitable for debounced local diagnostics. [VSCode API](https://code.visualstudio.com/api/references/vscode-api) Existing Visual Studio SDK supports DialogPage +ProvideOptionPage and persisted options in experimental-instance testing. [Microsoft Options page](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-an-options-page?view=vs-2022)

Select local stdio LSP with shared lightweight classifier; no network/database in keystroke path. Rich commands invoke CLI explicitly; on-build validation opt-in. Safe bounded/redacted stream reader supplies output, counts and Results view. Windows net472 may not expose modern process APIs—verify target APIs rather than copy modern ArgumentList blindly. Caller state/version/generation tokens prevent stale results after cancel or replacement. Host integration tests mandatory, not only manifest tests.

### Build, EF and fixes

Keep local analyzer compiler-safe. Heavy DB/snapshot semantic checks run through DataGuard.Build MSBuild integration, with explicit live input and immutable offline metadata by default. No-build EF requirement now needs static C# fluent snapshot parsing with explicit supported matrix and diagnostics for dynamic/unsupported expressions; previous compiled-only remediation is foundation fallback, not final FC10 delivery. New public facade attributes need namespace/collision/ABI tests. Codefix “implemented” means registered action transforms and compiles with semantic tests; no comment-only substitute for promised parameter/dialect rewrites.

### Integrity and plugins

NuGet defines package-signature requirements and verification tooling. Cryptographic validity and trusted publisher are separate policy checks. [NuGet signed packages](https://learn.microsoft.com/en-us/nuget/reference/signed-packages-reference) Sigstore verification supports identity/issuer-bound signatures and supplied bundles; artifact digest and provenance claims must also be checked. [Sigstore verification](https://docs.sigstore.dev/cosign/verifying/verify/)

Use maintained verifier/tooling with pinned operator trust, not homemade signature parsing. Offline supplied verification bundle +SPDX/in-toto binding; network must be explicit if trust material refresh required. Verify plugin artifact/manifest before load; trusted plugin still arbitrary code, not sandbox. Platform secret stores should be DPAPI/SecretService/Keychain-backed; unavailable backend fails closed. No implementation can promise no secret ever appears in a memory dump while database drivers consume strings; retain this as bounded threat-contract/owner gate.

### Performance evidence

BenchmarkDotNet has memory and platform-specific diagnosers; results depend on selected scenario/platform. [BenchmarkDotNet diagnosers](https://benchmarkdotnet.org/articles/configs/diagnosers.html)

Select reproducible tracked benchmark fixtures with raw artifacts ignored and durable summaries. Freeze2–4x scenarios/baselines/allocation boundaries BEFORE optimize; measure cold and warm paths separately. Memory+file hash cache1h TTL must key content/provider/canonicalizer and never replace fresh live schema acquisition. Missing target remains open, not removed to make green. “Zero allocation” needs a specified warmed hot path and workload, not universal end-to-end promise.

## Trade-offs and safety gates

Feature scope expands, but runtime authority does not. New HTTP host is not auto-deployed; remote advisory is not default egress; editor realtime is not auto-DB; signed plugin is not untrusted execution sandbox. Preserve full66-parent/15RT safety requirements and new FC child dependencies. Adding host/Build/LSP projects requires explicit solution/release/test packaging updates, not just loose code files.

## Next steps / unresolved

Phase8 enumerates individual current-doc claims and freezes contradictory count/namespace/performance/threat contracts. Phases9–14 deliver them. Phase15 audits every claim against executed evidence. No extra owner question is needed to reconfirm full-feature scope; only genuinely contradictory/absolute contracts or unavailable external authority need later decisions.
