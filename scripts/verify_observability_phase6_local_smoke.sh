#!/usr/bin/env bash
# Ephemeral local Collector process smoke for Phase 6A evidence.
#
# This is deliberately separate from the non-mutating preflight: it starts one
# disposable local Docker container and sends a synthetic trace to the debug
# exporter. It never contacts an LGTM backend or a Kubernetes API.

set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

IMAGE='otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6'
CONFIG="$ROOT/docs/observability/collector/local-smoke.yaml"
CONTAINER="dataguard-observability-local-smoke-$$"

fail() {
  printf '[phase6-local-smoke] ERROR: %s\n' "$*" >&2
  exit 1
}

command -v docker >/dev/null 2>&1 || fail 'Docker is required'
command -v curl >/dev/null 2>&1 || fail 'curl is required'
command -v python3 >/dev/null 2>&1 || fail 'python3 is required'
[[ -f "$CONFIG" ]] || fail "missing local smoke config: $CONFIG"
docker info >/dev/null 2>&1 || fail 'Docker daemon is not ready'

free_port() {
  python3 -c 'import socket; sock = socket.socket(); sock.bind(("127.0.0.1", 0)); print(sock.getsockname()[1]); sock.close()'
}

OTLP_PORT="$(free_port)"
HEALTH_PORT="$(free_port)"
cleanup() {
  docker stop "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run --rm -d --name "$CONTAINER" \
  -p "127.0.0.1:${OTLP_PORT}:4318" \
  -p "127.0.0.1:${HEALTH_PORT}:13133" \
  -v "$CONFIG:/etc/otel/local-smoke.yaml:ro" \
  "$IMAGE" --config=file:/etc/otel/local-smoke.yaml >/dev/null

ready=0
for _ in $(seq 1 100); do
  if curl --fail --silent "http://127.0.0.1:${HEALTH_PORT}/" >/dev/null; then
    ready=1
    break
  fi
  sleep 0.2
done
if [[ "$ready" != 1 ]]; then
  docker logs "$CONTAINER" >&2 || true
  fail 'Collector health endpoint did not become ready within 20 seconds'
fi

# Synthetic, non-sensitive OTLP/JSON trace. The stable IDs are test fixtures,
# not business identifiers, and the debug exporter is the only destination.
TRACE_JSON='{"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"dataguard-local-smoke"}}]},"scopeSpans":[{"scope":{"name":"dataguard.smoke"},"spans":[{"traceId":"0123456789abcdef0123456789abcdef","spanId":"0123456789abcdef","name":"banking.smoke.operation","kind":1,"startTimeUnixNano":"1710000000000000000","endTimeUnixNano":"1710000000100000000","attributes":[{"key":"banking.operation.name","value":{"stringValue":"banking.smoke.operation"}}]}]}]}]}'
HTTP_STATUS="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
  -H 'Content-Type: application/json' \
  --data-binary "$TRACE_JSON" \
  "http://127.0.0.1:${OTLP_PORT}/v1/traces")"
[[ "$HTTP_STATUS" == 2* ]] || {
  docker logs "$CONTAINER" >&2 || true
  fail "OTLP/HTTP trace export returned status $HTTP_STATUS"
}

export_seen=0
for _ in $(seq 1 150); do
  logs="$(docker logs "$CONTAINER" 2>&1 || true)"
  if printf '%s\n' "$logs" | rg -q 'banking.smoke.operation' &&
     printf '%s\n' "$logs" | rg -q 'dataguard-local-smoke'; then
    export_seen=1
    break
  fi
  sleep 0.2
done
[[ "$export_seen" == 1 ]] || {
  docker logs "$CONTAINER" >&2 || true
  fail 'debug exporter did not show the synthetic trace within 30 seconds'
}

printf '[phase6-local-smoke] PHASE6_LOCAL_SMOKE_STATUS=PASS\n'
printf '[phase6-local-smoke] HEALTH=READY OTLP_HTTP_STATUS=%s DEBUG_EXPORT=SEEN\n' "$HTTP_STATUS"
printf '[phase6-local-smoke] EXTERNAL_MUTATIONS=0 LOCAL_CONTAINER=EPHEMERAL\n'
printf '[phase6-local-smoke] LGTM_BACKEND=NOT_CONFIGURED KUBERNETES_API=NOT_CONTACTED PROFILER=NOT_STARTED\n'
