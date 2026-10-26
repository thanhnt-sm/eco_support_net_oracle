#!/usr/bin/env python3
"""Read-only manual smoke preflight. Exit 2 blocks all later workflow steps."""
import os
import re
import subprocess
import sys

FULL_SHA = re.compile(r'[0-9a-fA-F]{40}')


def blocked(reason):
    # Inputs and git error output may contain arbitrary text; emit stable reasons only.
    print('BLOCKED: ' + reason, file=sys.stderr)
    return 2


def main():
    expected = os.environ.get('VSIX_SMOKE_EXPECTED_SHA', '')
    if not FULL_SHA.fullmatch(expected):
        return blocked('expected-sha-format')
    try:
        result = subprocess.run(['git', 'rev-parse', '--verify', 'HEAD'],
                                capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return blocked('checkout-sha-unavailable')
    if result.returncode != 0:
        return blocked('checkout-sha-unavailable')
    actual = result.stdout.strip()
    if not FULL_SHA.fullmatch(actual):
        return blocked('checkout-sha-unavailable')
    if actual.lower() != expected.lower():
        return blocked('checkout-sha-mismatch')
    print('VSIX smoke checkout verified: ' + actual.lower())
    return 0


if __name__ == '__main__':
    sys.exit(main())
