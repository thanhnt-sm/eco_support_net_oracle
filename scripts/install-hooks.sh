#!/usr/bin/env bash
# Enables the repository's git hooks (.githooks/: pre-commit, commit-msg, pre-push) for this clone.
# Run once per clone/worktree:  ./scripts/install-hooks.sh
# Idempotent; prints the effective value so it can be checked (`git config core.hooksPath` -> .githooks).
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

if [[ ! -d .githooks ]]; then
    printf '[install-hooks] .githooks/ not found in %s\n' "$root" >&2
    exit 1
fi

chmod +x .githooks/* 2>/dev/null || true
git config core.hooksPath .githooks

actual="$(git config --get core.hooksPath)"
if [[ "$actual" != ".githooks" ]]; then
    printf '[install-hooks] core.hooksPath is %s, expected .githooks\n' "${actual:-<unset>}" >&2
    exit 1
fi
printf '[install-hooks] core.hooksPath=%s (hooks: %s)\n' "$actual" "$(cd .githooks && ls | tr '\n' ' ')"
