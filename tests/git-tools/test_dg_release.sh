#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DG_RELEASE_SCRIPT="$REPO_ROOT/tools/git-tools/dg-release"

if [[ ! -f "$DG_RELEASE_SCRIPT" ]]; then
    echo "Error: dg-release script not found at $DG_RELEASE_SCRIPT" >&2
    exit 1
fi

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

REMOTE_DIR="$TMP_DIR/remote.git"
LOCAL_DIR="$TMP_DIR/local"

# 1. Test help flag
bash "$DG_RELEASE_SCRIPT" --help >/dev/null

# 2. Test invalid tag rejection
if bash "$DG_RELEASE_SCRIPT" --tag "1.0.0" --dry-run >/dev/null 2>&1; then
    echo "Assertion failed: tag without v prefix should be rejected" >&2
    exit 1
fi
if bash "$DG_RELEASE_SCRIPT" --tag "v1.0" --dry-run >/dev/null 2>&1; then
    echo "Assertion failed: non-SemVer tag should be rejected" >&2
    exit 1
fi

# 3. Setup isolated test git repo with a mock origin
git init --bare "$REMOTE_DIR" >/dev/null
git -C "$REMOTE_DIR" symbolic-ref HEAD refs/heads/main

git clone "$REMOTE_DIR" "$LOCAL_DIR" >/dev/null 2>&1
cd "$LOCAL_DIR"
git checkout -b main 2>/dev/null || git branch -M main
git config user.name "Release Tester"
git config user.email "tester@example.com"
echo "initial" > file.txt
echo ".release.env" > .gitignore
git add file.txt .gitignore
git commit -m "chore: initial commit" >/dev/null
git push -u origin main >/dev/null 2>&1
git remote set-url origin https://github.com/mock-org/mock-repo.git
export DG_RELEASE_SKIP_FETCH=true
# 4. Test dirty tree guard
echo "dirty change" >> file.txt
if bash "$DG_RELEASE_SCRIPT" --tag v1.0.0 --dry-run >/dev/null 2>&1; then
    echo "Assertion failed: dirty working tree should be rejected" >&2
    exit 1
fi
git checkout -- file.txt

# 5. Test non-main branch guard
git switch -c feature-branch >/dev/null 2>&1
if bash "$DG_RELEASE_SCRIPT" --tag v1.0.0 --dry-run >/dev/null 2>&1; then
    echo "Assertion failed: non-default branch should be rejected" >&2
    exit 1
fi
git switch main >/dev/null 2>&1

# 6. Test configuration file loading (.release.env)
cat <<'EOF' > .release.env
RELEASE_TAG=v2.3.4
PUBLISH_MARKETPLACES=false
DRY_RUN=true
TIMEOUT_SECONDS=1800
EOF

output=$(bash "$DG_RELEASE_SCRIPT")
if ! grep -q "v2.3.4" <<<"$output"; then
    echo "Assertion failed: .release.env tag v2.3.4 was not picked up" >&2
    exit 1
fi
if ! grep -q "DRY-RUN THÀNH CÔNG" <<<"$output"; then
    echo "Assertion failed: dry-run did not succeed with .release.env" >&2
    exit 1
fi

# 7. Test existing local tag guard
git tag -a "v2.3.4" -m "Existing tag"
if bash "$DG_RELEASE_SCRIPT" >/dev/null 2>&1; then
    echo "Assertion failed: duplicate tag should be rejected" >&2
    exit 1
fi

echo "test_dg_release: all assertions passed successfully."
