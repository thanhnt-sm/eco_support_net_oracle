#!/usr/bin/env bash
# DataGuard demo — contract validation in action, no database required.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "=========================================================="
echo " DataGuard — contract validation for Entity <-> SP/Raw SQL"
echo " (dotnet tool: dataguard validate, no database needed)"
echo "=========================================================="
echo

echo "[1/3] Building sample (manual ground-truth attributes) ..."
dotnet build samples/DataGuard.Sample/DataGuard.Sample.csproj -c Release --nologo -v q

SAMPLE_DLL="$ROOT/samples/DataGuard.Sample/bin/Release/net9.0/DataGuard.Sample.dll"

echo
echo "[2/3] Validating offline against manual attributes ..."
echo "      (expected: DG006 naming + DG005 nullability findings)"
echo
dotnet run --project "$ROOT/src/DataGuard.Cli/DataGuard.Cli.csproj" --no-build -- validate \
  --offline --assembly "$SAMPLE_DLL" --provider sqlserver --verbose || true

echo
echo "[3/3] Snapshot/CI drift gate (offline, no snapshot yet):"
dotnet run --project "$ROOT/src/DataGuard.Cli/DataGuard.Cli.csproj" --no-build -- \
  validate --offline --assembly "$SAMPLE_DLL" --provider sqlserver --format json > /dev/null 2>&1 \
  && echo "      validate exited 0" || echo "      validate exited non-zero"

echo
echo "Done. Full-mode flow: dataguard snapshot refresh --connection ... --provider ..."
