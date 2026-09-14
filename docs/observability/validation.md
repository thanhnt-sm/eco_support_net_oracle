# Validation evidence

Ngày kiểm tra nền tảng: 2026-09-13; Phase 6 local file sink và endpoint lockdown gần nhất:
2026-09-14 (final product gate).
Các dòng `PASS` dưới đây có lệnh và kết quả đã chạy trong workspace; các dòng `NOT EXECUTED`
là owner/runtime gates, không được suy diễn thành đạt.

| Check | Command/Test | Expected | Actual | PASS/FAIL/NOT EXECUTED | Evidence |
|---|---|---|---|---|---|
| Restore | `dotnet restore DataGuard.sln --locked-mode` | exit 0, lock files reproducible | exit 0 | PASS | solution restore log |
| Solution build | `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 errors/warnings | 0 errors, 0 warnings | PASS | build output |
| Full solution tests | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore --verbosity minimal` | no failures | 739 passed, 0 failed, 0 skipped | PASS | VSTest output 2026-09-14 |
| Observability tests | `dotnet test tests/DataGuard.Observability.Tests/DataGuard.Observability.Tests.csproj --configuration Release --no-build --no-restore` | no failures | 38 passed, 0 failed, 0 skipped | PASS | VSTest output 2026-09-14 |
| Internal package pack | `dotnet pack` for the three observability projects with `--no-build --no-restore`, then inspect `.nupkg` | stable pinned package plus README | `DataGuard.Observability*.0.1.0.nupkg` created; each contains `README.md` and nuspec version `0.1.0` | PASS | temporary pack directory output |
| Core/adapter build | `dotnet build` for `src/DataGuard.Observability`, `.AspNetCore`, `.Messaging` | 0 errors/warnings | exit 0 | PASS | project build output |
| Local file observability | `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ObservabilityFileSinkTests` | archive, redaction, bounded queue and no implicit egress pass | 7 passed, 0 failed | PASS | `ObservabilityFileSinkTests` 2026-09-14 |
| Product pipeline integration | `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~ValidationPipeline_TelemetryWritesLocalDailyArchive` | real validation run creates the local daily archive | 1 passed, 0 failed | PASS | `PublicApiAndPipelineTests` 2026-09-14 |
| Endpoint lockdown | `dotnet test ... --filter FullyQualifiedName~HealthHostBindingTests` plus CoreObservability validation | host routes/exporters opt-in only | default `ExposeEndpoints=false`, `RemoteExportEnabled=false`; tests pass | PASS | `HealthHostOptions`, `CoreObservabilityOptions` |
| Default host smoke | `dotnet run --project src/DataGuard.Host/DataGuard.Host.csproj --configuration Release --no-build` | default product process exits without binding an endpoint | exit 0, no listener/output | PASS | local process smoke 2026-09-14 |
| Package vulnerability scan | `dotnet list <project> package --vulnerable --include-transitive` for each observability project/test/benchmark plus Core, Host and CLI | no known vulnerable packages | none from configured NuGet source | PASS | package audit output 2026-09-14 |
| Formatting/static gate | `dotnet format --verify-no-changes --no-restore` for eight affected projects | no formatting changes | exit 0 for Core, Host, Observability, AspNetCore, Messaging, benchmark and both test projects | PASS | format output (non-fatal workspace-load warnings only) |
| Secret-pattern scan | `rg` high-confidence credential patterns over new source/config, with the declared test canary seed reviewed separately | no real credentials | no real matches; one intentional PAN/bearer canary literal in `SensitiveDataAndCardinalityTests` | PASS | scan output + privacy test |
| Whitespace gate | `rg -n "[ \\t]+$"` over affected text/source artifacts | no trailing whitespace | no matches | PASS | scan output |
| Documentation sync | `./scripts/verify_docs_sync.sh` | required docs/rules present | exit 0 | PASS | script output |
| Optional Collector component inventory | `docker run otel/opentelemetry-collector-contrib:0.160.0 components` | optional reference components present | image digest resolved; required components including filter processor present | PASS (optional reference) | Docker component inventory; not product runtime |
| Collector agent config | pinned image `otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6` with `validate --config=file:/etc/otel/agent.yaml` | exit 0 | exit 0 | PASS | `docs/observability/collector/README.md` |
| Collector gateway config | same pinned image `otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6` with endpoint envs and `validate --config=file:/etc/otel/gateway.yaml` | exit 0 | exit 0 | PASS | `docs/observability/collector/README.md` |
| Collector receiver trust contract | gateway OTLP receiver mTLS paths plus `cmp` against the Kustomize copy | server cert, key and client CA paths configured | copies byte-equivalent | PASS | `gateway.yaml`, Kubernetes Secret contract |
| YAML syntax | Ruby Psych parse Collector/Kubernetes/SLO/dashboard YAML | parse succeeds | exit 0 | PASS | local parser output |
| Configuration and owner-input schemas | `jq empty docs/observability/configuration.schema.json docs/observability/phase6-owner-inputs.schema.json` | valid JSON Schema documents | exit 0 | PASS | local parser output |
| Phase 6B owner-input approved mode | `python3 scripts/verify_observability_phase6_owner_inputs.py --file /path/to/owner-inputs.json` | owner packet is approved and complete | no owner packet supplied; transformed local fixture only | NOT EXECUTED | owner evidence gate |
| Prometheus rule syntax | pinned Prometheus image with `/bin/promtool check rules /rules/rules.yaml` | valid rules | 19 rules parsed, exit 0 | PASS | `promtool` output |
| Prometheus rule unit test | pinned Prometheus image with `/bin/promtool test rules /rules/rules.test.yaml` | ratio, selector and low-traffic fixtures pass | exit 0 | PASS | `promtool` output |
| Wrapper benchmark | `dotnet run --project benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --configuration Release -- --filter '*ObservabilityOverheadBenchmarks*' --inProcess` | command completes | 4 benchmarks, exit 0; direct 9.198/8.645 ns, observed 347.410/348.702 ns, 480 B | PASS | [`benchmark.md`](benchmark.md) and BenchmarkDotNet report 2026-09-14 |
| Kubernetes local render | `kubectl kustomize docs/observability/kubernetes` plus `cmp` source/deployment config copies | rendered objects and config copies match | exit 0; all copies byte-equivalent | PASS | Kustomize v5.8.1 output |
| Phase 6B owner-input template contract | `python3 scripts/verify_observability_phase6_owner_inputs.py --template docs/observability/phase6-owner-inputs.example.json` | template shape is valid but cannot authorize runtime | exit 0; `TEMPLATE_STATUS=PASS`, `RUNTIME_AUTHORIZATION=OWNER_REQUIRED` | PASS | [`phase-06-owner-gated-integration-canary.md`](../../plans/260913-2000-enterprise-observability/phase-06-owner-gated-integration-canary.md) |
| Historical local Collector process smoke | `./scripts/verify_observability_phase6_local_smoke.sh` (3 consecutive runs) | optional reference Collector is healthy and accepts a synthetic trace | all 3 exit 0; no external mutation; not a product runtime check | PASS (optional reference) | historical phase report |
| Historical deterministic preflight | `./scripts/verify_observability_phase6_preflight.sh` | optional artifact, exact-image, package-pin, rule, restore/build/test and owner-template gates pass | exit 0; `PHASE6A_STATUS=PASS checks=23`, `MUTATIONS_PERFORMED=0`; not a product runtime gate | PASS (optional reference) | historical phase report |
| Kubernetes server-side schema | `kubectl apply --dry-run=server --validate=strict -k docs/observability/kubernetes` | schema accepted | local `kubectl config current-context` reports `current-context is not set`; no API server | NOT EXECUTED | owner cluster gate |
| Backend protocol/sample verification | Loki/Tempo/Mimir/Pyroscope endpoint, tenant, retention and metric samples | exact deployed behavior | no approved backend/topology in discovery | NOT EXECUTED | owner backend gate |
| Collector outage/429/queue-full chaos | injected agent/gateway/backend failures | fail-open, bounded loss and recovery | no runtime deployment | NOT EXECUTED | owner chaos gate |
| Sensitive-data backend search | PII/PAN/token canaries through deployed logs/traces/metrics/profile metadata | absent everywhere | only local application-level activity test exists | NOT EXECUTED | deploy integration required |
| Profiling overhead/source mapping | native profiler/eBPF canary with symbols | budget and mapping evidence | profiler/runtime/workload unknown | NOT EXECUTED | [`benchmark.md`](benchmark.md) |
| Canary rollback | staged non-critical to critical rollout and kill switch | rollback exercised | no approved cluster/owner window | NOT EXECUTED | [`rollout.md`](rollout.md) |

## Interpretation

The product gate proves compilation, package restore, local daily archive writing, finite dimensions,
redaction, endpoint lockdown and fail-open behavior. The historical Phase 6A artifacts also prove a
repeatable optional Collector/Kustomize preflight without mutating a cluster. Neither path proves
backend availability, Kubernetes admission, data residency/retention, production overhead, native
profiling or zero telemetry loss. A real owner packet has not been supplied; keep remote adapters in
a deferred/canary posture until the unexecuted owner gates have evidence.

## Exact repeat commands

```sh
./scripts/verify_observability_phase6_preflight.sh

python3 scripts/verify_observability_phase6_owner_inputs.py \
  --file /path/to/owner-inputs.json

./scripts/verify_observability_phase6_local_smoke.sh

for project in \
  src/DataGuard.Core/DataGuard.Core.csproj \
  src/DataGuard.Host/DataGuard.Host.csproj \
  src/DataGuard.Cli/DataGuard.Cli.csproj \
  src/DataGuard.Observability/DataGuard.Observability.csproj \
  src/DataGuard.Observability.AspNetCore/DataGuard.Observability.AspNetCore.csproj \
  src/DataGuard.Observability.Messaging/DataGuard.Observability.Messaging.csproj \
  tests/DataGuard.Observability.Tests/DataGuard.Observability.Tests.csproj \
  benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj; do
  dotnet list "$project" package --vulnerable --include-transitive
done

docker run --rm \
  -v "$PWD/docs/observability/collector/agent.yaml:/etc/otel/agent.yaml:ro" \
  otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6 \
  validate --config=file:/etc/otel/agent.yaml

docker run --rm \
  -e LOKI_OTLP_ENDPOINT=https://loki.example.invalid/otlp \
  -e MIMIR_OTLP_ENDPOINT=https://mimir.example.invalid/otlp \
  -e GRAFANA_TENANT_ID=platform \
  -v "$PWD/docs/observability/collector/gateway.yaml:/etc/otel/gateway.yaml:ro" \
  otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6 \
  validate --config=file:/etc/otel/gateway.yaml

docker run --rm --entrypoint /bin/promtool \
  -v "$PWD/docs/observability/slo:/rules:ro" \
  prom/prometheus@sha256:3c42b892cf723fa54d2f262c37a0e1f80aa8c8ddb1da7b9b0df9455a35a7f893 \
  check rules /rules/rules.yaml

docker run --rm --entrypoint /bin/promtool \
  -v "$PWD/docs/observability/slo:/rules:ro" \
  prom/prometheus@sha256:3c42b892cf723fa54d2f262c37a0e1f80aa8c8ddb1da7b9b0df9455a35a7f893 \
  test rules /rules/rules.test.yaml
```
