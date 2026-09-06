#!/usr/bin/env bash
# Canonical blocking verifier for local Git hooks.
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/dataguard-verify.XXXXXX")"
trap 'rm -rf "$TEMP_DIR"' EXIT

fail() { printf '[verify-local-gates] ERROR: %s\n' "$*" >&2; exit 1; }

command -v dotnet >/dev/null 2>&1 || fail 'dotnet SDK is required.'
command -v actionlint >/dev/null 2>&1 || fail 'actionlint is required.'
command -v act >/dev/null 2>&1 || fail 'act is required.'
command -v docker >/dev/null 2>&1 || fail 'Docker is required.'
docker info >/dev/null 2>&1 || fail 'Docker daemon is not ready.'
printf '[verify-local-gates] Checking staged topology.\n'
./scripts/anti_garbage_guard.sh

printf '[verify-local-gates] Checking documentation inventory.\n'
./scripts/verify_docs_sync.sh

printf '[verify-local-gates] Validating all workflows.\n'
actionlint .github/workflows/*.yml

printf '[verify-local-gates] Restoring dependencies.\n'
dotnet restore DataGuard.sln --locked-mode

printf '[verify-local-gates] Building Release.\n'
dotnet build DataGuard.sln --configuration Release --no-restore

printf '[verify-local-gates] Running analyzers.\n'
dotnet build DataGuard.sln --configuration Release --no-restore /p:RunAnalyzers=true

printf '[verify-local-gates] Checking formatting.\n'
dotnet format DataGuard.sln --verify-no-changes --no-restore
dotnet format whitespace DataGuard.sln --verify-no-changes

printf '[verify-local-gates] Running tests with coverage.\n'
dotnet test DataGuard.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --logger "trx;LogFileName=test_results.trx"

printf '[verify-local-gates] Checking coverage threshold.\n'
python3 - <<'PY'
import glob, sys, xml.etree.ElementTree as ET
hits = {}
files = glob.glob('**/TestResults/**/coverage.cobertura.xml', recursive=True)
if not files:
    raise SystemExit('No coverage files found.')
for filename in files:
    root = ET.parse(filename).getroot()
    for cls in root.iter('class'):
        source = cls.attrib.get('filename', '')
        for line in cls.iter('line'):
            key = (source, line.attrib['number'])
            hits[key] = hits.get(key, False) or int(line.attrib.get('hits', '0')) > 0
total = len(hits)
covered = sum(hits.values())
rate = 100 * covered / total if total else 0
print(f'Coverage: {rate:.2f}% ({covered}/{total})')
if rate < 60:
    raise SystemExit('Coverage is below 60%.')
PY

AUDIT_JSON="$TEMP_DIR/vuln_check.json"
if ! dotnet list DataGuard.sln package --vulnerable --include-transitive --format json > "$AUDIT_JSON"; then
    fail 'NuGet vulnerability audit command failed.'
fi
python3 - "$AUDIT_JSON" <<'PY'
import json, sys
with open(sys.argv[1]) as f:
    data = json.load(f)
if data.get('problems'):
    raise SystemExit('NuGet audit reported problems.')
for project in data.get('projects', []):
    for framework in project.get('frameworks', []):
        for package in framework.get('topLevelPackages', []) + framework.get('transitivePackages', []):
            if package.get('vulnerabilities'):
                raise SystemExit(f"Vulnerable package: {package.get('id', package.get('name', '?'))}")
PY

printf '[verify-local-gates] Scanning repository history for verified secrets.\n'
if command -v trufflehog >/dev/null 2>&1; then
    trufflehog git "file://$ROOT" --no-update --only-verified --fail
else
    docker run --rm -v "$ROOT:/pwd" ghcr.io/trufflesecurity/trufflehog:3.97.0 git file:///pwd --no-update --only-verified --fail
fi
printf '[verify-local-gates] Running blocking act standards audit job.\n'
act push --pull=false --workflows .github/workflows/standards-audit.yml --platform ubuntu-latest=dataguard-act-runner:node-path --container-architecture linux/amd64
printf '[verify-local-gates] Running blocking act CI job.\n'
act push --pull=false --workflows .github/workflows/ci.yml --job build-and-test --env ACT=true --platform ubuntu-latest=dataguard-act-runner:node-path --container-architecture linux/amd64

printf '[verify-local-gates] Local gates passed. Hosted macOS/Windows matrix and workflow_dispatch remain a post-commit GitHub Actions gate.\n'
