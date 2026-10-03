#!/usr/bin/env bash
set -euo pipefail

FAILED=0

echo "=== Verifying Repository Configuration (Scorecard & CODEOWNERS) ==="

# 1. Verify CODEOWNERS existence
CODEOWNERS_FILE=".github/CODEOWNERS"
if [[ -f "$CODEOWNERS_FILE" ]]; then
    echo "[PASS] $CODEOWNERS_FILE exists"
else
    echo "[FAIL] $CODEOWNERS_FILE does not exist"
    FAILED=1
fi

# 2. Verify CODEOWNERS content for valid default rule (e.g. * @...)
if [[ -f "$CODEOWNERS_FILE" ]]; then
    if grep -E '^\s*\*\s+@' "$CODEOWNERS_FILE" >/dev/null; then
        echo "[PASS] $CODEOWNERS_FILE contains a valid wildcard rule (* @...)"
    else
        echo "[FAIL] $CODEOWNERS_FILE does not contain a wildcard rule (* @...)"
        FAILED=1
    fi
fi

# 3. Verify scorecard.yml existence
SCORECARD_FILE=".github/workflows/scorecard.yml"
if [[ -f "$SCORECARD_FILE" ]]; then
    echo "[PASS] $SCORECARD_FILE exists"
else
    echo "[FAIL] $SCORECARD_FILE does not exist"
    FAILED=1
fi

# 4. Verify scorecard.yml cron schedule trigger
if [[ -f "$SCORECARD_FILE" ]]; then
    if grep -E 'cron:\s*["'\''].+["'\'']' "$SCORECARD_FILE" >/dev/null; then
        echo "[PASS] $SCORECARD_FILE contains a scheduled cron trigger"
    else
        echo "[FAIL] $SCORECARD_FILE is missing a scheduled cron trigger"
        FAILED=1
    fi
fi

# 5. Verify scorecard.yml SARIF upload step
if [[ -f "$SCORECARD_FILE" ]]; then
    if grep -E 'upload-sarif' "$SCORECARD_FILE" >/dev/null; then
        echo "[PASS] $SCORECARD_FILE contains upload-sarif step"
    else
        echo "[FAIL] $SCORECARD_FILE is missing upload-sarif step"
        FAILED=1
    fi
fi

echo "=================================================================="
if [[ $FAILED -eq 0 ]]; then
    echo "Result: ALL CHECKS PASSED"
    exit 0
else
    echo "Result: ONE OR MORE CHECKS FAILED"
    exit 1
fi
