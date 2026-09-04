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

# Initialize publisher
git clone "$REMOTE_DIR" "$PUBLISHER_DIR"
cd "$PUBLISHER_DIR"
git checkout -b main 2>/dev/null || git branch -M main
git config user.name "Publisher"
git config user.email "publisher@example.com"
echo "initial content" > app.txt
git add app.txt
git commit -m "chore: initial setup"
git push -u origin main

# Initialize consumer
git clone "$REMOTE_DIR" "$CONSUMER_DIR"
cd "$CONSUMER_DIR"
git config user.name "Consumer"
git config user.email "consumer@example.com"

# -------------------------------------------------------------
# Test 1: Bare invocation on clean tree pulls upstream commits
# -------------------------------------------------------------
cd "$PUBLISHER_DIR"
echo "update from publisher" >> publisher_feature.txt
git add publisher_feature.txt
git commit -m "feat(pub): add publisher feature"
git push origin main
PUB_HEAD=$(git rev-parse HEAD)

cd "$CONSUMER_DIR"
unset DG_GIT_DIR || true
# Run bare dg-git (no subcommand, no options)
DG_SKIP_LOCAL_ACTIONS=true bash "$DG_GIT_SCRIPT"

if [[ ! -f "$CONSUMER_DIR/publisher_feature.txt" ]]; then
    echo "Assertion failed: publisher_feature.txt missing in consumer after bare sync" >&2
    exit 1
fi

CONSUMER_HEAD=$(git rev-parse HEAD)
if [[ "$CONSUMER_HEAD" != "$PUB_HEAD" ]]; then
    echo "Assertion failed: consumer HEAD ($CONSUMER_HEAD) does not match publisher ($PUB_HEAD)" >&2
    exit 1
fi

# -------------------------------------------------------------
# Test 2: Bare invocation with local uncommitted changes auto-commits, rebases, and pushes
# -------------------------------------------------------------
# Publisher adds a file
cd "$PUBLISHER_DIR"
echo "publisher update 2" >> pub2.txt
git add pub2.txt
git commit -m "feat(pub): add pub2"
git push origin main

# Consumer adds local changes without committing
cd "$CONSUMER_DIR"
echo "consumer work" >> consumer_work.txt

# Run bare dg-git: should auto-commit, fetch, rebase, and push
DG_SKIP_LOCAL_ACTIONS=true bash "$DG_GIT_SCRIPT"

CONSUMER_HEAD_2=$(git rev-parse HEAD)
# Verify working tree is clean
STATUS=$(git status --porcelain)
if [[ -n "$STATUS" ]]; then
    echo "Assertion failed: consumer working tree should be clean after sync" >&2
    exit 1
fi

# Verify publisher can pull and see consumer's commit
cd "$PUBLISHER_DIR"
git pull --ff-only origin main
if [[ ! -f "$PUBLISHER_DIR/consumer_work.txt" ]]; then
    echo "Assertion failed: consumer_work.txt not pushed to remote" >&2
    exit 1
fi

# -------------------------------------------------------------
# Test 3: Bare invocation merges unmerged local branches into main and pushes
# -------------------------------------------------------------
cd "$CONSUMER_DIR"
git switch -c local-feature
echo "local feature" > local_feature.txt
git add local_feature.txt
git commit -m "feat(local): add local feature"
git switch main

DG_SKIP_LOCAL_ACTIONS=true bash "$DG_GIT_SCRIPT"

if [[ ! -f "$CONSUMER_DIR/local_feature.txt" ]]; then
    echo "Assertion failed: local_feature.txt was not merged into main" >&2
    exit 1
fi
if ! git merge-base --is-ancestor local-feature main; then
    echo "Assertion failed: local-feature commit is not contained in main" >&2
    exit 1
fi
git -C "$PUBLISHER_DIR" pull --ff-only origin main
if [[ ! -f "$PUBLISHER_DIR/local_feature.txt" ]]; then
    echo "Assertion failed: local feature merge was not pushed to remote" >&2
    exit 1
fi

# -------------------------------------------------------------
# Test 4: Secret detection aborts sync without committing
# -------------------------------------------------------------
cd "$CONSUMER_DIR"
printf '%s%s\n' 'AKIA' '1234567890123456' > leaked_key.txt
if DG_SKIP_LOCAL_ACTIONS=true bash "$DG_GIT_SCRIPT"; then
    echo "Assertion failed: bare sync should fail when potential secrets are present" >&2
    exit 1
fi
rm -f leaked_key.txt

echo "test_dg_git_sync: all assertions passed successfully."
