#!/usr/bin/env bash
# Publish DataGuard.Host to an isolated directory and smoke its loopback contract.
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/dataguard-host-publish.XXXXXX")"
HOST_PID=""

cleanup() {
    if [[ -n "$HOST_PID" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
        kill "$HOST_PID" 2>/dev/null || true
        wait "$HOST_PID" 2>/dev/null || true
    fi
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

fail() { printf '[verify-host-publish] ERROR: %s\n' "$*" >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || fail 'dotnet SDK is required.'
command -v python3 >/dev/null 2>&1 || fail 'python3 is required.'

PUBLISH_DIR="$TEMP_DIR/publish"
SNAPSHOT="$TEMP_DIR/snapshot.json"
BASELINE="$TEMP_DIR/baseline.json"
RID="${DATAGUARD_HOST_RID:-}"
printf '{}' > "$SNAPSHOT"
printf '{}' > "$BASELINE"

printf '[verify-host-publish] Publishing Release host.\n'
if [[ -n "$RID" ]]; then
    dotnet restore "$ROOT/src/DataGuard.Host/DataGuard.Host.csproj" --locked-mode --runtime "$RID"
    dotnet publish "$ROOT/src/DataGuard.Host/DataGuard.Host.csproj" \
        --configuration Release --no-restore --runtime "$RID" --self-contained false --output "$PUBLISH_DIR"
else
    dotnet publish "$ROOT/src/DataGuard.Host/DataGuard.Host.csproj" \
        --configuration Release --no-restore --output "$PUBLISH_DIR"
    RID="framework-dependent"
fi

for artifact in DataGuard.Host.dll DataGuard.Host.deps.json DataGuard.Host.runtimeconfig.json; do
    [[ -f "$PUBLISH_DIR/$artifact" ]] || fail "published artifact missing: $artifact"
done

HOST_SHA256="$(python3 - "$PUBLISH_DIR/DataGuard.Host.dll" <<'PY'
import hashlib, pathlib, sys
print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())
PY
)"
printf '[verify-host-publish] RID=%s SHA-256=%s\n' "$RID" "$HOST_SHA256"

PORT="$(python3 - <<'PY'
import socket
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
    listener.bind(('127.0.0.1', 0))
    print(listener.getsockname()[1])
PY
)"

printf '[verify-host-publish] Starting published host on loopback port %s.\n' "$PORT"
dotnet "$PUBLISH_DIR/DataGuard.Host.dll" \
    --urls "http://127.0.0.1:$PORT" \
    --DataGuardHealth:SnapshotPath "$SNAPSHOT" \
    --DataGuardHealth:BaselinePath "$BASELINE" \
    --DataGuardHealth:MinimumFreeDiskBytes 0 \
    --DataGuardHealth:MaximumManagedMemoryBytes 9223372036854775807 \
    >"$TEMP_DIR/host.log" 2>&1 &
HOST_PID="$!"

python3 - "$PORT" <<'PY'
import sys, time, urllib.error, urllib.request
port = sys.argv[1]
deadline = time.monotonic() + 15
url = f'http://127.0.0.1:{port}/health/ready'
while time.monotonic() < deadline:
    try:
        with urllib.request.urlopen(url, timeout=1) as response:
            body = response.read().decode('utf-8')
            if response.status == 200 and 'startupComplete' in body and 'components' in body:
                sys.exit(0)
    except (urllib.error.URLError, TimeoutError):
        pass
    time.sleep(.1)
raise SystemExit(f'Host did not become ready at {url}')
PY

printf '[verify-host-publish] Published host readiness passed.\n'
