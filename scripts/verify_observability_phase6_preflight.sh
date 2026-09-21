#!/usr/bin/env bash
# Deterministic, non-mutating Phase 6A gate for the observability reference.
#
# This script deliberately does not call `kubectl apply` or contact a telemetry
# backend. It validates the pinned local artifacts and the repository contract
# that must be true before an owner-approved Phase 6B canary is allowed.

set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/dataguard-observability-phase6.XXXXXX")"
trap 'rm -rf "$TMP_DIR"' EXIT

PASS_COUNT=0
EXPECTED_PASS_COUNT=23

fail() {
  printf '[phase6a] ERROR: %s\n' "$*" >&2
  exit 1
}

pass() {
  PASS_COUNT=$((PASS_COUNT + 1))
  printf '[phase6a] PASS: %s\n' "$*"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command is missing: $1"
}

require_file() {
  [[ -f "$1" ]] || fail "required file is missing: $1"
}

run_captured() {
  local name="$1"
  local output="$2"
  shift 2
  if "$@" >"$output" 2>&1; then
    pass "$name"
    return 0
  fi
  sed -n '1,160p' "$output" >&2 || true
  fail "$name"
}

require_command git
require_command dotnet
require_command docker
require_command kubectl
require_command jq
require_command ruby
require_command rg
require_command python3

if ! docker info >"$TMP_DIR/docker-info" 2>&1; then
  sed -n '1,80p' "$TMP_DIR/docker-info" >&2 || true
  fail 'Docker daemon is not ready'
fi
pass 'toolchain and Docker daemon are available'

required_files=(
  DataGuard.sln
  DataGuard.CrossPlatform.slnf
  src/DataGuard.Observability/DataGuard.Observability.csproj
  src/DataGuard.Observability/packages.lock.json
  src/DataGuard.Observability.AspNetCore/DataGuard.Observability.AspNetCore.csproj
  src/DataGuard.Observability.AspNetCore/packages.lock.json
  src/DataGuard.Observability.Messaging/DataGuard.Observability.Messaging.csproj
  src/DataGuard.Observability.Messaging/packages.lock.json
  docs/observability/collector/agent.yaml
  docs/observability/collector/gateway.yaml
  docs/observability/collector/local-smoke.yaml
  docs/observability/kubernetes/kustomization.yaml
  docs/observability/kubernetes/config/agent.yaml
  docs/observability/kubernetes/config/gateway.yaml
  docs/observability/slo/rules.yaml
  docs/observability/slo/rules.test.yaml
  docs/observability/configuration.schema.json
  docs/observability/phase6-owner-inputs.schema.json
  docs/observability/phase6-owner-inputs.example.json
  scripts/verify_observability_phase6_owner_inputs.py
  scripts/verify_observability_phase6_local_smoke.sh
)
for path in "${required_files[@]}"; do
  require_file "$path"
done
pass 'Phase 6 artifacts are present'

# Version and supply-chain invariants: no floating project package versions and
# the two Kubernetes workloads must use the same immutable Collector digest.
if rg -n 'Version="[^" ]*(\*|[xX])' src/DataGuard.Observability* --glob '*.csproj'; then
  fail 'floating or wildcard NuGet version found in observability projects'
fi
pass 'observability project package versions are pinned'

expected_package_pins=(
  "src/DataGuard.Observability/DataGuard.Observability.csproj|PackageReference Include=\"OpenTelemetry\" Version=\"1.18.0\""
  "src/DataGuard.Observability/DataGuard.Observability.csproj|PackageReference Include=\"OpenTelemetry.Extensions.Hosting\" Version=\"1.18.0\""
  "src/DataGuard.Observability/DataGuard.Observability.csproj|PackageReference Include=\"OpenTelemetry.Exporter.OpenTelemetryProtocol\" Version=\"1.18.0\""
  "src/DataGuard.Observability/DataGuard.Observability.csproj|PackageReference Include=\"OpenTelemetry.Instrumentation.AspNetCore\" Version=\"1.12.0\""
  "src/DataGuard.Observability/DataGuard.Observability.csproj|PackageReference Include=\"OpenTelemetry.Instrumentation.Http\" Version=\"1.12.0\""
  "src/DataGuard.Observability/DataGuard.Observability.csproj|PackageReference Include=\"OpenTelemetry.Instrumentation.Runtime\" Version=\"1.18.0\""
)
for pin in "${expected_package_pins[@]}"; do
  IFS='|' read -r project expected <<<"$pin"
  rg -Fq "$expected" "$project" || fail "selected package pin is missing: $expected"
done
pass 'selected OpenTelemetry package versions match the discovery decision'

COLLECTOR_DIGEST='sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6'
K8S_IMAGE_REFS="$TMP_DIR/k8s-image-refs"
rg -o 'otel/opentelemetry-collector-contrib@sha256:[0-9a-f]{64}' \
  docs/observability/kubernetes --glob '*.yaml' >"$K8S_IMAGE_REFS" || true
[[ "$(wc -l <"$K8S_IMAGE_REFS" | tr -d ' ')" == '2' ]] || \
  fail 'expected exactly two immutable Collector image references in Kubernetes artifacts'
if ! rg -Fq "otel/opentelemetry-collector-contrib@${COLLECTOR_DIGEST}" "$K8S_IMAGE_REFS"; then
  fail 'Kubernetes artifacts do not use the approved Collector digest'
fi
pass 'Kubernetes Collector references are immutable and consistent'

if rg -n 'insecure:[[:space:]]*true|https?://[^$[:space:]]+:[^$@[:space:]]+@' \
  docs/observability/collector docs/observability/kubernetes --glob '*.yaml'; then
  fail 'insecure transport or inline endpoint credentials found in deployment artifacts'
fi
pass 'Collector/Kubernetes artifacts contain no insecure transport or inline credentials'

if ! cmp -s docs/observability/collector/agent.yaml docs/observability/kubernetes/config/agent.yaml; then
  fail 'agent Collector source and Kustomize copy differ'
fi
if ! cmp -s docs/observability/collector/gateway.yaml docs/observability/kubernetes/config/gateway.yaml; then
  fail 'gateway Collector source and Kustomize copy differ'
fi
pass 'Collector source and Kustomize copies are byte-equivalent'

run_captured 'Kustomize local render' "$TMP_DIR/kustomize-rendered" \
  kubectl kustomize docs/observability/kubernetes
rg -q '^kind: DaemonSet$' "$TMP_DIR/kustomize-rendered" || fail 'render is missing the agent DaemonSet'
rg -q '^kind: Deployment$' "$TMP_DIR/kustomize-rendered" || fail 'render is missing the gateway Deployment'
pass 'Kustomize render contains the expected agent and gateway workloads'

YAML_FILES=()
while IFS= read -r -d '' path; do
  YAML_FILES+=("$path")
done < <(find docs/observability/collector docs/observability/kubernetes docs/observability/slo \
  -type f \( -name '*.yaml' -o -name '*.yml' \) -print0 | sort -z)
[[ "${#YAML_FILES[@]}" -gt 0 ]] || fail 'no observability YAML files found for parsing'
run_captured 'Collector/Kubernetes/SLO YAML parse' "$TMP_DIR/yaml-parse" \
  ruby -e 'require "yaml"; ARGV.each { |path| YAML.load_file(path) }' "${YAML_FILES[@]}"
run_captured 'observability JSON Schema parse' "$TMP_DIR/schema-parse" \
  jq empty docs/observability/configuration.schema.json \
  docs/observability/phase6-owner-inputs.schema.json
run_captured 'Phase 6B owner-input template contract' "$TMP_DIR/owner-template" \
  python3 scripts/verify_observability_phase6_owner_inputs.py \
  --template docs/observability/phase6-owner-inputs.example.json

COLLECTOR_IMAGE="otel/opentelemetry-collector-contrib@${COLLECTOR_DIGEST}"
run_captured 'Collector component inventory' "$TMP_DIR/collector-components" \
  docker run --rm "$COLLECTOR_IMAGE" components
for component in health_check k8s_attributes memory_limiter redaction tail_sampling load_balancing otlp_http; do
  rg -q "name:[[:space:]]+${component}$" "$TMP_DIR/collector-components" || \
    fail "Collector component is not present in approved image: ${component}"
done
pass 'approved Collector image contains the required components'

run_captured 'Collector agent configuration validation' "$TMP_DIR/agent-validate" \
  docker run --rm \
    -v "$ROOT/docs/observability/collector/agent.yaml:/etc/otel/agent.yaml:ro" \
    "$COLLECTOR_IMAGE" validate --config=file:/etc/otel/agent.yaml

run_captured 'Collector gateway configuration validation' "$TMP_DIR/gateway-validate" \
  docker run --rm \
    -e LOKI_OTLP_ENDPOINT=https://loki.example.invalid/otlp \
    -e MIMIR_OTLP_ENDPOINT=https://mimir.example.invalid/otlp \
    -e GRAFANA_TENANT_ID=platform \
    -v "$ROOT/docs/observability/collector/gateway.yaml:/etc/otel/gateway.yaml:ro" \
    "$COLLECTOR_IMAGE" validate --config=file:/etc/otel/gateway.yaml

PROM_IMAGE='prom/prometheus@sha256:3c42b892cf723fa54d2f262c37a0e1f80aa8c8ddb1da7b9b0df9455a35a7f893'
run_captured 'Prometheus recording/alert rule syntax' "$TMP_DIR/prom-check" \
  docker run --rm --entrypoint /bin/promtool \
    -v "$ROOT/docs/observability/slo:/rules:ro" \
    "$PROM_IMAGE" check rules /rules/rules.yaml
run_captured 'Prometheus deterministic rule tests' "$TMP_DIR/prom-test" \
  docker run --rm --entrypoint /bin/promtool \
    -v "$ROOT/docs/observability/slo:/rules:ro" \
    "$PROM_IMAGE" test rules /rules/rules.test.yaml

run_captured 'locked .NET restore' "$TMP_DIR/restore" \
  dotnet restore DataGuard.CrossPlatform.slnf --locked-mode
run_captured 'Release build' "$TMP_DIR/build" \
  dotnet build DataGuard.CrossPlatform.slnf --configuration Release --no-restore
run_captured 'full solution tests' "$TMP_DIR/test" \
  dotnet test DataGuard.CrossPlatform.slnf --configuration Release --no-build --no-restore --verbosity minimal

run_captured 'NuGet vulnerability restore audit' "$TMP_DIR/vulnerabilities" \
  dotnet restore DataGuard.sln --locked-mode -p:NuGetAuditMode=all \
  '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905'

# Keep the gate non-mutating even when a kubeconfig exists. The owner must run
# the server-side dry-run in Phase 6B against the approved context.
[[ "$PASS_COUNT" -eq "$EXPECTED_PASS_COUNT" ]] || \
  fail "preflight check count drifted: expected ${EXPECTED_PASS_COUNT}, got ${PASS_COUNT}"
printf '[phase6a] No kubectl apply, backend request, Secret read, or profiler start was performed.\n'
printf '[phase6a] PHASE6A_STATUS=PASS checks=%d\n' "$PASS_COUNT"
printf '[phase6a] PHASE6B_STATUS=OWNER_GATED\n'
printf '[phase6a] MUTATIONS_PERFORMED=0\n'
