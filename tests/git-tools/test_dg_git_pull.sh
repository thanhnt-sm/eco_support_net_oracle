#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DG_GIT_SCRIPT="$REPO_ROOT/tools/git-tools/dg-git"

if [[ ! -f "$DG_GIT_SCRIPT" ]]; then
    echo "Error: dg-git script not found at $DG_GIT_SCRIPT" >&2
    exit 1
fi

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

REMOTE_DIR="$TMP_DIR/remote.git"
PUBLISHER_DIR="$TMP_DIR/publisher"
CONSUMER_DIR="$TMP_DIR/consumer"

# Initialize bare remote repository
git init --bare "$REMOTE_DIR"
git -C "$REMOTE_DIR" symbolic-ref HEAD refs/heads/main

# Initialize publisher clone
git clone "$REMOTE_DIR" "$PUBLISHER_DIR"
cd "$PUBLISHER_DIR"
git checkout -b main 2>/dev/null || git branch -M main
git config user.name "Publisher"
git config user.email "publisher@example.com"
echo "initial" > README.md
git add README.md
git commit -m "initial commit"
git push -u origin main

# Initialize consumer clone
git clone "$REMOTE_DIR" "$CONSUMER_DIR"
cd "$CONSUMER_DIR"
git config user.name "Consumer"
git config user.email "consumer@example.com"

# 1. Test clean fast-forward pull
cd "$PUBLISHER_DIR"
echo "file1 content" > file1.txt
git add file1.txt
git commit -m "publisher commit 1"
git push origin main
PUBLISHER_REV1=$(git rev-parse HEAD)

cd "$CONSUMER_DIR"
unset DG_GIT_DIR || true
bash "$DG_GIT_SCRIPT" pull

if [[ ! -f "$CONSUMER_DIR/file1.txt" ]]; then
    echo "Assertion failed: file1.txt does not exist in consumer after pull" >&2
    exit 1
fi

CONSUMER_REV1=$(git rev-parse HEAD)
ORIGIN_REV1=$(git rev-parse origin/main)

if [[ "$CONSUMER_REV1" != "$ORIGIN_REV1" ]] || [[ "$CONSUMER_REV1" != "$PUBLISHER_REV1" ]]; then
    echo "Assertion failed: consumer HEAD ($CONSUMER_REV1) does not match origin/main ($ORIGIN_REV1) or publisher ($PUBLISHER_REV1)" >&2
    exit 1
fi

# 2. Test dirty working tree rejection
cd "$PUBLISHER_DIR"
echo "file2 content" > file2.txt
git add file2.txt
git commit -m "publisher commit 2"
git push origin main

cd "$CONSUMER_DIR"
echo "uncommitted change" >> file1.txt
STATUS_BEFORE=$(git status --porcelain)
if [[ -z "$STATUS_BEFORE" ]]; then
    echo "Assertion failed: expected consumer to have dirty working tree" >&2
    exit 1
fi
HEAD_BEFORE=$(git rev-parse HEAD)

# dg-git pull MUST fail on dirty tree
if bash "$DG_GIT_SCRIPT" pull; then
    echo "Assertion failed: dg-git pull should have failed on dirty tree" >&2
    exit 1
fi

HEAD_AFTER=$(git rev-parse HEAD)
STATUS_AFTER=$(git status --porcelain)

if [[ "$HEAD_BEFORE" != "$HEAD_AFTER" ]]; then
    echo "Assertion failed: consumer HEAD changed after failed pull ($HEAD_BEFORE != $HEAD_AFTER)" >&2
    exit 1
fi

if [[ "$STATUS_BEFORE" != "$STATUS_AFTER" ]]; then
    echo "Assertion failed: consumer status changed after failed pull" >&2
    exit 1
fi

echo "test_dg_git_pull: all assertions passed successfully."
