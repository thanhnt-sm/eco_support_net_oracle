#!/usr/bin/env bash
# ==============================================================================
# Asserts that a packages.lock.json resolves every listed package to exactly the
# expected version, in every target-framework section of the file.
#
# Why: the bundled Roslyn analyzer must stay on the compiler line that VS 2022
# 17.12 and the .NET 9.0.1xx SDK run (Microsoft.CodeAnalysis 4.12). A Dependabot
# or manual bump to 4.13+ would be accepted by the floating CI SDK and still be
# refused by the IDE (CS9057, no diagnostics). This check reads the committed
# lock file, so it does not depend on the SDK of the machine that runs it.
#
# Usage:  scripts/assert-roslyn-lock.sh <packages.lock.json> <expected-version> <package-id>...
#   e.g.  scripts/assert-roslyn-lock.sh src/DataGuard.Analyzers/packages.lock.json 4.12.0 Microsoft.CodeAnalysis.CSharp
# Exit 0 = every package resolves to the expected version in every section;
# exit 1 = a mismatch (printed as "FAIL ..."); exit 2 = usage/IO error.
# Called by scripts/verify-analyzer-packaging.sh; unit-tested (negative case) by
# scripts/tests/test_assert_roslyn_lock.py. Needs python3 (stdlib json only).
# ==============================================================================
set -euo pipefail

LOCK_FILE="${1:?usage: $0 <packages.lock.json> <expected-version> <package-id>...}"
EXPECTED="${2:?usage: $0 <packages.lock.json> <expected-version> <package-id>...}"
shift 2
[ "$#" -ge 1 ] || { echo "usage: $0 <packages.lock.json> <expected-version> <package-id>..." >&2; exit 2; }
[ -f "$LOCK_FILE" ] || { echo "FAIL: lock file not found: $LOCK_FILE" >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "FAIL: python3 is required to read $LOCK_FILE" >&2; exit 2; }

# -I: isolated mode, so a planted module next to the lock file cannot be imported.
python3 -I - "$LOCK_FILE" "$EXPECTED" "$@" <<'PY'
import json
import sys

lock_path, expected, *packages = sys.argv[1:]
with open(lock_path, encoding="utf-8") as handle:
    lock = json.load(handle)

sections = lock.get("dependencies") or {}
if not sections:
    print(f"FAIL: {lock_path}: no 'dependencies' sections")
    sys.exit(1)

failures = 0
for package in packages:
    seen = False
    for framework, entries in sections.items():
        entry = (entries or {}).get(package)
        if entry is None:
            continue
        seen = True
        resolved = entry.get("resolved")
        if resolved == expected:
            print(f"  ok   {package} = {resolved} ({framework})")
        else:
            print(f"  FAIL {package}: resolved {resolved!r} in {framework}, expected {expected!r} ({lock_path})")
            failures += 1
    if not seen:
        print(f"  FAIL {package}: not present in any section of {lock_path}")
        failures += 1

sys.exit(1 if failures else 0)
PY
