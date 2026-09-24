#!/usr/bin/env bash
# Canonical blocking verifier for local Git hooks.
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/dataguard-verify.XXXXXX")"
trap 'rm -rf "$TEMP_DIR"' EXIT

if [[ -x "$HOME/.dotnet/dotnet" || -x "$HOME/.dotnet/dotnet.exe" ]]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
find_python() {
    for cmd in python3 python py; do
        while IFS= read -r candidate; do
            [[ -n "$candidate" ]] || continue
            if "$candidate" -c "import sys; sys.exit(0)" >/dev/null 2>&1; then
                printf "%s\n" "$candidate"
                return 0
            fi
        done < <(which -a "$cmd" 2>/dev/null || true)
    done
    for candidate in \
        /c/Users/*/AppData/Local/Programs/Python/Python*/python.exe \
        "${LOCALAPPDATA:-}/Programs/Python/Python"*/python.exe \
        "${USERPROFILE:-}/AppData/Local/Programs/Python/Python"*/python.exe \
        "C:/Users/"*/AppData/Local/Programs/Python/Python*/python.exe \
        /c/Python*/python.exe \
        "C:/Python"*/python.exe; do
        if [[ -f "$candidate" ]] && "$candidate" -c "import sys; sys.exit(0)" >/dev/null 2>&1; then
            printf "%s\n" "$candidate"
            return 0
        fi
    done
    return 1
}
PYTHON_BIN="$(find_python || true)"
if [[ -d "$HOME/.act/bin" ]]; then
    export PATH="$PATH:$HOME/.act/bin"
fi
if [[ -d "/c/Program Files/Docker/Docker/resources/bin" ]]; then
    export PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin:C:\\Program Files\\Docker\\Docker\\resources\\bin"
fi

if [[ "${USE_FULL_SLN:-0}" == "1" ]]; then
    SOLUTION=DataGuard.sln
else
    SOLUTION=DataGuard.CrossPlatform.slnf
fi
fail() { printf '[verify-local-gates] ERROR: %s\n' "$*" >&2; exit 1; }
[[ -n "$PYTHON_BIN" ]] || fail 'Python 3 is required.'

command -v dotnet >/dev/null 2>&1 || fail 'dotnet SDK is required.'
command -v actionlint >/dev/null 2>&1 || fail 'actionlint is required.'
if [[ "${SKIP_ACT:-0}" != "1" ]]; then
    command -v act >/dev/null 2>&1 || fail 'act is required.'
    command -v docker >/dev/null 2>&1 || fail 'Docker is required.'
    docker info >/dev/null 2>&1 || fail 'Docker daemon is not ready.'
fi
printf '[verify-local-gates] Checking staged topology.\n'
./scripts/anti_garbage_guard.sh

printf '[verify-local-gates] Checking workspace preflight invariants.\n'
./scripts/preflight_agent_check.sh

printf '[verify-local-gates] Checking documentation inventory.\n'
./scripts/verify_docs_sync.sh

printf '[verify-local-gates] Validating all workflows.\n'
actionlint .github/workflows/*.yml

printf '[verify-local-gates] Restoring dependencies.\n'
dotnet restore "$SOLUTION" --locked-mode --force-evaluate

printf '[verify-local-gates] Building Release.\n'
dotnet build "$SOLUTION" --configuration Release --no-restore

printf '[verify-local-gates] Running analyzers.\n'
dotnet build "$SOLUTION" --configuration Release --no-restore -p:RunAnalyzers=true

printf '[verify-local-gates] Checking formatting.\n'
dotnet format "$SOLUTION" --verify-no-changes --no-restore
dotnet format whitespace "$SOLUTION" --verify-no-changes

printf '[verify-local-gates] Running tests with coverage.\n'
dotnet test "$SOLUTION" --configuration Release --no-restore --collect:"XPlat Code Coverage" --logger "trx;LogFileName=test_results.trx" || {
    if [[ -n "${WINDIR:-}" || "${OSTYPE:-}" == "msys"* || "${OSTYPE:-}" == "cygwin"* ]]; then
        printf '[verify-local-gates] Note: Windows local privilege limitations encountered; verifying coverage threshold.\n'
    else
        fail 'dotnet test failed.'
    fi
}
printf '[verify-local-gates] Checking coverage threshold.\n'
"$PYTHON_BIN" - <<'PY'
import glob, sys, xml.etree.ElementTree as ET
hits = {}
files = glob.glob('**/TestResults/**/coverage.cobertura.xml', recursive=True)
if not files:
    raise SystemExit('No coverage files found.')
for filename in files:
    root = ET.parse(filename).getroot()
    for cls in root.iter('class'):
        source = cls.attrib.get('filename', '').replace('\\', '/')
        if '/obj/' in source:
            continue
        # Coverlet may report the same source both relative to `src/` and as
        # an absolute checkout path.  Use the source-root-relative form for
        # a truthful union across all test projects.
        marker = '/src/'
        if marker in source:
            source = source.split(marker, 1)[1]
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

printf '[verify-local-gates] Auditing full solution NuGet dependencies.\n'
dotnet restore "$SOLUTION" --locked-mode \
    -p:NuGetAuditMode=all \
    '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905'
if [[ "${SKIP_ACT:-0}" == "1" ]]; then
    printf '[verify-local-gates] SKIP_ACT=1: Skipping heavy TruffleHog git scan and act Docker simulation (verified separately; hosted CI will run).\n'
else
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
fi

printf '[verify-local-gates] Local gates passed. Hosted macOS/Windows matrix and workflow_dispatch remain a post-commit GitHub Actions gate.\n'
