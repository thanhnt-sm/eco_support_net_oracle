# Phase 5 review

Review date: 2026-09-13. Scope: final local validation, privacy/cardinality contracts,
benchmark evidence and rollout readiness.

## Verdict: CAUTION — repository gate complete, deployment gate blocked by unknowns

The reference slice is buildable and locally validated. This is not a production-readiness
certificate: discovery has no approved Kubernetes cluster, Grafana LGTM backend endpoints,
traffic profile, retention/residency contract or profiler choice. The canary and chaos gates
therefore remain explicitly unexecuted.

| Check | Evidence | Result |
|---|---|---|
| Restore/build | `dotnet restore DataGuard.sln --locked-mode`; `dotnet build DataGuard.sln --configuration Release --no-restore` | PASS; 0 errors/0 warnings |
| Full solution tests | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` | PASS; 726 passed, 0 failed, 0 skipped |
| Observability contract tests | `dotnet test tests/DataGuard.Observability.Tests/DataGuard.Observability.Tests.csproj --configuration Release --no-build --no-restore` | PASS; 37 passed, 0 failed across the final run |
| Internal package pack | `dotnet pack` for core, ASP.NET Core and messaging projects, followed by nuspec/README inspection | PASS; three `0.1.0` packages created and each contains `README.md` |
| Privacy/cardinality | sensitive-data canary, W3C carrier bounds, finite operation dimensions, stable failure result tags | PASS in local tests |
| Package safety | Five-project loop `dotnet list "$project" package --vulnerable --include-transitive` (the exact loop is in `docs/observability/validation.md`) | PASS; no vulnerable packages from configured NuGet source |
| Formatting/static gate | `dotnet format --verify-no-changes --no-restore` on five affected projects | PASS |
| Collector config | pinned contrib image digest validates `agent.yaml` and `gateway.yaml` | PASS; both exit 0 |
| Prometheus rules | Prometheus `v3.13.1` `promtool check rules` + `test rules` | PASS; 19 rules and deterministic fixture |
| YAML syntax | Ruby Psych parse for Collector/Kubernetes/SLO/dashboard artifacts | PASS |
| Wrapper overhead | BenchmarkDotNet four direct/observed × SDK-disabled/enabled cases | PASS; direct 9.008/8.903 ns, observed 285.828/287.835 ns, 480 B per observed call; command exit 0 |
| Kubernetes local render | `kubectl kustomize` plus source/deployment-copy `cmp` | PASS; rendered objects and copies match |
| Kubernetes server-side schema | `kubectl apply --dry-run=server --validate=strict -k docs/observability/kubernetes` | NOT EXECUTED; no API server/approved cluster |
| Backend protocol and metric samples | Loki/Tempo/Mimir/Pyroscope endpoints, labels, retention and tenant contract | NOT EXECUTED; no approved backend |
| Collector outage/429/queue-full chaos | agent/gateway/backend failure injection | NOT EXECUTED; no runtime deployment |
| Continuous profiling overhead/source mapping | native profiler/eBPF canary with symbols | NOT EXECUTED; profiler and workload unknown |
| Canary rollback | non-critical → critical staged rollout exercise | NOT EXECUTED; owner approval and cluster absent |

## Findings carried forward

1. Concrete Kafka/RabbitMQ/gRPC/Redis adapters cannot be safely implemented without discovering
   their actual client packages and retry/streaming semantics. Npgsql `10.0.3` is present in the
   existing validation adapter, but service-level `Npgsql.OpenTelemetry` wiring still needs an
   owner-approved data-source and redaction contract.
2. `CaptureExceptionDetails` is a deliberate non-production escape hatch; production config
   validation/policy must keep it disabled and retain application-level privacy tests.
3. Tail sampling, redaction maturity, backend protocol details and metric names require a
   live exact-distribution/backend integration test before alert activation.
4. The benchmark measures wrapper allocation/latency only; it does not establish a service
   overhead budget or profiler overhead.
5. The first parallel full-suite run exposed a test-isolation race in the activity listener
   assertion; the test now filters the callback to its operation name and passed three isolated
   runs plus the final parallel solution run.
6. A red-team hardening pass found that Activity listeners, exception recording, custom
   result-classifier logging and failure-denominator semantics needed explicit guards. The
   implementation now catches telemetry-only failures, counts failed attempts in the SLI
   denominator, uses canonical result labels, adds regression coverage, and the final suite is
   721/721 (the pre-cardinality-hardening snapshot).
7. A final sampling review corrected the tail-sampling claim: a gateway cannot recover a trace
   removed by SDK head sampling. The implementation now exposes an explicit
   `UseAlwaysOnHeadSamplingForTailSampling` mode, requires `TraceSamplingRatio=1.0`, and keeps
   the 10% default unchanged; the mode remains capacity/owner gated.
8. The SLO red-team pass found `unknown` missing from the bad-event selector. The rules and
   deterministic fixture now include `unknown` while excluding validation/authentication/
   authorization/cancellation outcomes; exact Prometheus validation passes.
9. Configuration red-team also covered non-finite sampling input: `NaN`/`Infinity` are now
   rejected explicitly rather than relying on relational range checks; the regression suite
   remains 37 observability tests and 726 solution tests after the final mTLS, messaging-cardinality and endpoint-credential hardening.
10. The final transport/cardinality review found two actionable gaps: the gateway exporter was
   configured for agent mTLS without a matching receiver TLS contract, and messaging duration
   metrics accepted any bounded-but-unique operation name. The gateway now mounts a server
   certificate plus client CA and the messaging recorder requires a finite explicit operation
   allowlist. Duration histograms also carry the finite result label so latency SLO selectors can
   exclude cancellation and policy outcomes consistently.
11. A Secret/exporter consistency pass found that the Tempo exporter referenced a client keypair
   while the declared backend Secret only guaranteed a CA. The default now uses CA-authenticated
   TLS to Tempo; a client keypair is an explicit owner overlay rather than a hidden startup
   dependency. Exact Collector validation and Kustomize copy checks pass after the correction.
12. The low-traffic alert was red-teamed after review: `absent_over_time()` on a periodically
   scraped counter would not detect a counter that stopped increasing. The rule now uses a
   15-minute `increase()` with a zero-vector fallback; the pinned `promtool check rules` and
   `test rules` commands pass after the correction.
13. The logging contract was tightened after review: core diagnostic failure/classifier events
   now use stable EventIds (`7001`/`7002`) and default exception event types are bounded, with no
   message or stack capture.

## Post-review revalidation

After the final metrics-builder API correction, the affected source was rebuilt and the full
solution gate was rerun: `dotnet build DataGuard.sln --configuration Release --no-restore`
passed with 0 warnings/errors and `dotnet test DataGuard.sln --configuration Release
--no-build --no-restore` passed 726/726. The five affected projects passed
`dotnet format --verify-no-changes --no-restore`; locked restore and vulnerability scans also
passed. The final wrapper benchmark completed four cases with direct 9.008/8.903 ns and
observed 285.828/287.835 ns (480 B). Collector exact-digest validation, Prometheus rule
syntax/unit tests (19 rules), Kustomize render/copy checks, YAML/JSON parsing, actionlint,
whitespace and documentation sync all passed. These are still repository/local gates; no
runtime backend, cluster, profiler or canary evidence was added.

## Re-plan after review

The next safe phase is an owner-gated integration/canary phase: discover client/backend
versions, obtain TLS/tenant/residency/retention contracts, deploy the exact pinned artifacts
in a non-critical namespace, run metric-sample and failure/chaos tests, then exercise the
staged rollback. Until those gates pass, keep telemetry and profiling kill switches under
platform-owner control and do not label this implementation production-ready.
