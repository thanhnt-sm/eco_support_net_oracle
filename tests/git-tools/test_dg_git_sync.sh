#!/usr/bin/env bash
# Contract tests for `dg-git` (bare invocation and `sync`), matching rules/git_workflow.md:
#   - bare `dg-git` prints usage and exits 1 without touching the repository;
#   - nothing is committed without an explicit -m (no generated chore/auto-sync message);
#   - `sync` on a clean tree fast-forwards from the remote; WIP is stashed and restored, never committed;
#   - generic auto-sync messages are refused; potential secrets abort the sync.
# The commit path of `sync -m` runs scripts/verify_local_gates.sh (dotnet, actionlint, Docker), so it is
# not exercised here. No network, no TTY: run with stdin redirected from /dev/null in CI.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DG_GIT_SCRIPT="$REPO_ROOT/tools/git-tools/dg-git"

if [[ ! -f "$DG_GIT_SCRIPT" ]]; then
    echo "Error: dg-git script not found at $DG_GIT_SCRIPT" >&2
    exit 1
fi

fail() { echo "Assertion failed: $*" >&2; exit 1; }

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

REMOTE_DIR="$TMP_DIR/remote.git"
PUBLISHER_DIR="$TMP_DIR/publisher"
CONSUMER_DIR="$TMP_DIR/consumer"

git init --bare "$REMOTE_DIR"
git -C "$REMOTE_DIR" symbolic-ref HEAD refs/heads/main

git clone "$REMOTE_DIR" "$PUBLISHER_DIR"
cd "$PUBLISHER_DIR"
git checkout -b main 2>/dev/null || git branch -M main
git config user.name "Publisher"
git config user.email "publisher@example.com"
echo "initial content" > app.txt
git add app.txt
git commit -m "chore: initial setup"
git push -u origin main

git clone "$REMOTE_DIR" "$CONSUMER_DIR"
cd "$CONSUMER_DIR"
git config user.name "Consumer"
git config user.email "consumer@example.com"
unset DG_GIT_DIR DG_YES_MODE || true

# -------------------------------------------------------------
# Test 1: bare invocation prints usage, exits 1, changes nothing
# -------------------------------------------------------------
echo "wip" > untracked_wip.txt
HEAD_BEFORE=$(git rev-parse HEAD)
STATUS_BEFORE=$(git status --porcelain)
set +e
BARE_OUTPUT=$(bash "$DG_GIT_SCRIPT" 2>&1)
BARE_RC=$?
set -e
[[ $BARE_RC -eq 1 ]] || fail "bare dg-git exited $BARE_RC, expected 1"
grep -q "Usage: dg-git <command>" <<<"$BARE_OUTPUT" || fail "bare dg-git did not print usage: $BARE_OUTPUT"
[[ "$(git rev-parse HEAD)" == "$HEAD_BEFORE" ]] || fail "bare dg-git moved HEAD"
[[ "$(git status --porcelain)" == "$STATUS_BEFORE" ]] || fail "bare dg-git changed the working tree"
[[ -z "$(git stash list)" ]] || fail "bare dg-git created a stash"
rm -f untracked_wip.txt

# Help documents the safe defaults.
HELP_OUTPUT=$(bash "$DG_GIT_SCRIPT" help)
grep -q "DG_YES_MODE=false" <<<"$HELP_OUTPUT" || fail "help does not document DG_YES_MODE=false"

# -------------------------------------------------------------
# Test 2: `sync` on a clean tree fast-forwards upstream commits
# -------------------------------------------------------------
cd "$PUBLISHER_DIR"
echo "update from publisher" >> publisher_feature.txt
git add publisher_feature.txt
git commit -m "feat(pub): add publisher feature"
git push origin main
PUB_HEAD=$(git rev-parse HEAD)

cd "$CONSUMER_DIR"
bash "$DG_GIT_SCRIPT" sync
[[ -f "$CONSUMER_DIR/publisher_feature.txt" ]] || fail "publisher_feature.txt missing in consumer after sync"
[[ "$(git rev-parse HEAD)" == "$PUB_HEAD" ]] || fail "consumer HEAD does not match publisher after sync"

# -------------------------------------------------------------
# Test 3: staged changes without -m are refused before anything happens
# -------------------------------------------------------------
cd "$PUBLISHER_DIR"
echo "publisher update 2" >> pub2.txt
git add pub2.txt
git commit -m "feat(pub): add pub2"
git push origin main

cd "$CONSUMER_DIR"
echo "consumer staged work" > staged_work.txt
git add staged_work.txt
HEAD_BEFORE=$(git rev-parse HEAD)
if bash "$DG_GIT_SCRIPT" sync; then
    fail "sync with staged changes and no -m should fail"
fi
[[ "$(git rev-parse HEAD)" == "$HEAD_BEFORE" ]] || fail "refused sync moved HEAD"
git diff --cached --name-only | grep -qx "staged_work.txt" || fail "refused sync lost the staged selection"
[[ -z "$(git stash list)" ]] || fail "refused sync left a stash behind"
[[ ! -f "$CONSUMER_DIR/pub2.txt" ]] || fail "refused sync still pulled upstream changes"

# `commit` without -m is refused too.
if bash "$DG_GIT_SCRIPT" commit; then
    fail "dg-git commit without -m should fail"
fi
[[ "$(git rev-parse HEAD)" == "$HEAD_BEFORE" ]] || fail "dg-git commit without -m moved HEAD"

# Generic auto-sync messages are refused even with -m.
for junk in "chore(sync): automated workspace synchronization [2026-10-05T00:00:00Z]" "chore: auto-sync"; do
    if bash "$DG_GIT_SCRIPT" sync -m "$junk"; then
        fail "sync accepted junk message '$junk'"
    fi
done
[[ "$(git rev-parse HEAD)" == "$HEAD_BEFORE" ]] || fail "junk-message sync moved HEAD"
git reset -q staged_work.txt
rm -f staged_work.txt

# -------------------------------------------------------------
# Test 4: unstaged/untracked WIP is stashed, the branch syncs, WIP is restored and NOT committed
# -------------------------------------------------------------
echo "consumer work" > consumer_work.txt
echo "edit" >> app.txt
bash "$DG_GIT_SCRIPT" sync
[[ -f "$CONSUMER_DIR/pub2.txt" ]] || fail "pub2.txt was not pulled by sync with WIP present"
[[ -f "$CONSUMER_DIR/consumer_work.txt" ]] || fail "untracked WIP was not restored"
git diff --name-only | grep -qx "app.txt" || fail "tracked WIP edit was not restored"
[[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]] || fail "sync committed WIP without -m"
[[ -z "$(git stash list)" ]] || fail "sync left its autostash behind"
git -C "$PUBLISHER_DIR" pull -q --ff-only origin main
[[ ! -f "$PUBLISHER_DIR/consumer_work.txt" ]] || fail "uncommitted WIP reached the remote"
git checkout -q -- app.txt
rm -f consumer_work.txt

# -------------------------------------------------------------
# Test 5: explicit `sync` merges already-committed local branches into main and pushes
# (existing behaviour, unchanged; agents must not run it without a user request)
# -------------------------------------------------------------
git switch -q -c local-feature
echo "local feature" > local_feature.txt
git add local_feature.txt
git commit -q -m "feat(local): add local feature"
git switch -q main
bash "$DG_GIT_SCRIPT" sync
[[ -f "$CONSUMER_DIR/local_feature.txt" ]] || fail "local_feature.txt was not merged into main"
git merge-base --is-ancestor local-feature main || fail "local-feature commit is not contained in main"
git -C "$PUBLISHER_DIR" pull -q --ff-only origin main
[[ -f "$PUBLISHER_DIR/local_feature.txt" ]] || fail "local feature merge was not pushed to remote"

# -------------------------------------------------------------
# Test 6: secret detection aborts sync without committing
# -------------------------------------------------------------
HEAD_BEFORE=$(git rev-parse HEAD)
printf '%s%s\n' 'AKIA' '1234567890123456' > leaked_key.txt
if bash "$DG_GIT_SCRIPT" sync; then
    fail "sync should fail when potential secrets are present"
fi
[[ "$(git rev-parse HEAD)" == "$HEAD_BEFORE" ]] || fail "secret-abort sync moved HEAD"
[[ -f leaked_key.txt ]] || fail "secret-abort sync removed the WIP file"
rm -f leaked_key.txt

echo "test_dg_git_sync: all assertions passed successfully."
