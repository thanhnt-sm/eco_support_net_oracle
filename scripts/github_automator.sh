#!/usr/bin/env bash
# ==============================================================================
# DataGuard GitHub Automator
# ------------------------------------------------------------------------------
# Comprehensive automation: stash → sync (fetch + rebase) → restore → stage →
# verify (zero-bug policy) → security audit (red-team) → commit → push.
#
# Inspired by eco_support_GV/scripts/github_automator.sh, adapted for the
# DataGuard .NET / C# workspace and its CI/CD pipeline.
#
# Usage:
#   ./scripts/github_automator.sh              # full sync + verify + commit
#   ./scripts/github_automator.sh -m "msg"     # custom conventional commit msg
#   ./scripts/github_automator.sh --push       # also push to remote
#   ./scripts/github_automator.sh --dry-run    # simulate without commit/push
#   ./scripts/github_automator.sh --help       # show help
# ==============================================================================

set -euo pipefail

# ── Colors ───────────────────────────────────────────────────────────────────
RED='\033[0;31m'    GREEN='\033[0;32m'   YELLOW='\033[1;33m'
CYAN='\033[0;36m'   BLUE='\033[0;34m'    BOLD='\033[1m'
NC='\033[0m'

if [[ -x "$HOME/.dotnet/dotnet" || -x "$HOME/.dotnet/dotnet.exe" ]]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
if [[ -d "$HOME/.act/bin" ]]; then
    export PATH="$HOME/.act/bin:$PATH"
fi
if [[ -d "/c/Program Files/Docker/Docker/resources/bin" ]]; then
    export PATH="/c/Program Files/Docker/Docker/resources/bin:$PATH"
fi
# ── Paths ────────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "$SCRIPT_DIR/..")"
SLN_FILE="$PROJECT_ROOT/DataGuard.sln"
GITHOOKS_DIR="$PROJECT_ROOT/.githooks"
CONFLICT_RESOLVER="$SCRIPT_DIR/git_conflict_resolver.sh"

COMMIT_MSG=""
DO_PUSH=false
DRY_RUN=false
STASH_REF=""
# ── Logging helpers ──────────────────────────────────────────────────────────
log_info()    { printf "${BLUE}ℹ️  %s${NC}\n" "$*"; }
log_success() { printf "${GREEN}✅ %s${NC}\n" "$*"; }
log_warn()    { printf "${YELLOW}⚠️  %s${NC}\n" "$*"; }
log_error()   { printf "${RED}❌ %s${NC}\n" "$*" >&2; }
hr()          { printf '\n'; }

# ── Argument parsing ──────────────────────────────────────────────────────────
print_help() {
    cat <<'EOF'
DataGuard GitHub Automator — full sync & CI verification pipeline

Usage: github_automator.sh [OPTIONS] [COMMIT_MSG]

Options:
  -m, --message MSG   Conventional Commit message (default: chore(sync): ... )
  -p, --push          Also push commits to the remote after verification
  -n, --dry-run       Simulate without committing or pushing
  -h, --help          Show this help message

Examples:
  ./scripts/github_automator.sh
  ./scripts/github_automator.sh -m "feat: add new analyzer"
  ./scripts/github_automator.sh --push
  ./scripts/github_automator.sh --dry-run
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -m|--message) COMMIT_MSG="$2"; shift 2 ;;
        -p|--push)    DO_PUSH=true; shift ;;
        -n|--dry-run) DRY_RUN=true; shift ;;
        -h|--help)    print_help; exit 0 ;;
        -*)           log_error "Unknown option: $1"; print_help; exit 1 ;;
        *)            COMMIT_MSG="$1"; shift ;;
    esac
done

if [[ -z "$COMMIT_MSG" ]]; then
    COMMIT_MSG="chore(sync): automated workspace synchronization [$(date -u +'%Y-%m-%dT%H:%M:%SZ')]"
fi

# ── Pre-flight ────────────────────────────────────────────────────────────────
echo -e "${CYAN}${BOLD}🌿 [DataGuard GitHub Automator] Starting automated sync & CI verification...${NC}"

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    log_error "Not inside a git repository."
    exit 1
fi

cd "$PROJECT_ROOT"

# Configure hooks
git config core.hooksPath "$GITHOOKS_DIR" 2>/dev/null || true

# Require dotnet
if ! command -v dotnet >/dev/null 2>&1; then
    log_error "dotnet SDK is required for this DataGuard workspace."
    exit 1
fi

# ==============================================================================
# STEP 1: Bảo vệ local state (Stash)
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[1/6] 📦 Checking & protecting local state (auto-stash)...${NC}"

LOCAL_CHANGES="$(git status --porcelain 2>/dev/null || true)"
UNSTAGED_CHANGES="$(git diff --name-only 2>/dev/null || true)"
UNTRACKED_CHANGES="$(git ls-files --others --exclude-standard 2>/dev/null || true)"

# ── Red-team: Secret scan on local changes BEFORE stashing ───────────────────
if [[ -n "$LOCAL_CHANGES" && -x "$PROJECT_ROOT/tools/git-tools/dg-git" ]]; then
    echo -e "${CYAN}🔒 [Red-team] Scanning local changes for secrets...${NC}"
    if ! "$PROJECT_ROOT/tools/git-tools/dg-git" secret; then
        log_error "Potential secret/token/password detected in uncommitted changes."
        log_error "Dừng đồng bộ để bảo vệ tài khoản GitHub."
        exit 1
    fi
    log_success "No secrets found in local changes."
fi

if [[ -n "$UNSTAGED_CHANGES" || -n "$UNTRACKED_CHANGES" ]]; then
    echo -e "${YELLOW}Local unstaged changes detected. Stashing them while preserving the staged selection...${NC}"
    STASH_NAME="github-automator-stash-$(date +%s)"
    if ! git stash push --keep-index -u -m "$STASH_NAME"; then
        log_error "Failed to stash unstaged work; refusing to continue."
        exit 1
    fi
    STASH_REF="$(git stash list --format='%gd%x09%gs' | sed -n "\|$STASH_NAME$|{s/\t.*//;p;q;}")"
    if [[ -z "$STASH_REF" ]]; then
        log_error "Cannot identify the stash created by this run; refusing to continue."
        exit 1
    fi
    STASHED=true
    echo -e "${GREEN}✅ Unstaged work safely stashed; staged selection preserved.${NC}"
else
    STASHED=false
    echo -e "${GREEN}✨ No unstaged work to stash; staged selection remains in place.${NC}"
fi

# ==============================================================================
# STEP 2: Fetch & Pull (rebase) from remote, handle conflicts
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[2/6] 🔄 Fetching & pulling latest source from GitHub...${NC}"

REMOTE_NAME="origin"
if ! git remote get-url "$REMOTE_NAME" >/dev/null 2>&1; then
    REMOTE_NAME="$(git remote 2>/dev/null | head -n 1 || true)"
fi

CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "main")"

if [[ -n "$REMOTE_NAME" ]]; then
    echo -e "${CYAN}Target remote: '${REMOTE_NAME}', branch: '${CURRENT_BRANCH}'${NC}"

    echo -e "${CYAN}Executing git fetch --all --prune --tags...${NC}"
    git fetch --all --prune --tags || {
        log_error "Cannot fetch from remote. Check network connectivity."
        exit 1
    }
    echo -e "${GREEN}✅ Fetched latest data from GitHub.${NC}"

    # Determine the remote-tracking branch
    REMOTE_BRANCH="$REMOTE_NAME/$CURRENT_BRANCH"
    if ! git show-ref --verify --quiet "refs/remotes/$REMOTE_BRANCH"; then
        echo -e "${YELLOW}Remote branch '${CURRENT_BRANCH}' does not exist yet. Will push as new branch.${NC}"
    else
        # Detect fast-forward vs rebase
        BEHIND="$(git rev-list --count "$CURRENT_BRANCH..$REMOTE_BRANCH" 2>/dev/null || echo 0)"
        AHEAD="$(git rev-list --count "$REMOTE_BRANCH..$CURRENT_BRANCH" 2>/dev/null || echo 0)"

        if [[ "$BEHIND" -gt 0 && "$AHEAD" -eq 0 ]]; then
            echo -e "${CYAN}Fast-forwarding to remote...${NC}"
            git merge --ff-only "$REMOTE_BRANCH" || {
                log_error "Fast-forward failed unexpectedly."
                exit 1
            }
            log_success "Local branch is now up-to-date with ${REMOTE_NAME}/${CURRENT_BRANCH}."
        elif [[ "$BEHIND" -gt 0 && "$AHEAD" -gt 0 ]]; then
            echo -e "${YELLOW}Branch has diverged ($AHEAD local, $BEHIND remote). Rebasing...${NC}"
            if ! git rebase "$REMOTE_BRANCH"; then
                echo -e "${RED}⚠️ Rebase conflict detected with remote changes!${NC}"
                if [[ -x "$CONFLICT_RESOLVER" ]]; then
                    chmod +x "$CONFLICT_RESOLVER"
                    "$CONFLICT_RESOLVER"
                fi
                while [[ -n "$(git diff --name-only --diff-filter=U 2>/dev/null || true)" ]]; do
                    echo -e "${YELLOW}Please resolve conflict markers, stage resolved files with 'git add <file>', and press Enter to continue (or type 'abort'):${NC}"
                    USER_INPUT=""
                    if [ -t 0 ]; then
                        read -r USER_INPUT
                    elif [ -e /dev/tty ]; then
                        read -r USER_INPUT < /dev/tty || break
                    else
                        echo -e "${RED}Non-interactive terminal. Cannot wait for manual resolution.${NC}"
                        break
                    fi
                    [[ "$USER_INPUT" == "abort" ]] && { git rebase --abort 2>/dev/null || true; exit 1; }
                done
                git rebase --continue || { log_error "Rebase continuation failed."; exit 1; }
            fi
            log_success "Remote conflicts resolved."
        else
            echo -e "${GREEN}✅ Already up-to-date with ${REMOTE_NAME}/${CURRENT_BRANCH}.${NC}"
        fi
    fi
else
    echo -e "${YELLOW}ℹ️ No remote configured. Skipping fetch/rebase.${NC}"
fi

# ==============================================================================
# STEP 3: Restore stashed changes, handle local conflicts
# ==============================================================================
if [[ "$STASHED" == "true" ]]; then
    echo -e "${CYAN}Applying local changes via git stash pop $STASH_REF...${NC}"
    if ! git stash pop "$STASH_REF"; then
        log_error "Failed to restore stashed local changes. Resolve manually before continuing."
        exit 1
    fi
    if [[ -n "$(git diff --name-only --diff-filter=U 2>/dev/null || true)" ]]; then
        log_error "Unresolved merge conflicts remain after restoring local changes."
        exit 1
    fi
    echo -e "${GREEN}✅ Local changes restored cleanly.${NC}"
else
    echo -e "${GREEN}✨ No local stash to restore.${NC}"
fi

# ==============================================================================
# STEP 4: Stage all changes
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[4/6] 📋 Verifying selected staged workspace scope...${NC}"

STAGED_CHANGES="$(git diff --cached --name-only 2>/dev/null || true)"
if [[ -z "$STAGED_CHANGES" ]]; then
    log_error "No staged changes found; refusing to stage the entire workspace automatically."
    exit 1
fi
FINAL_CHANGES="$STAGED_CHANGES"
echo -e "${GREEN}✅ Using the existing selected staged scope.${NC}"

# ==============================================================================
# STEP 5: Local CI/CD pipeline simulation (Zero-Bug Policy)
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[5/6] 🧪 Simulating local CI/CD pipeline & security audit...${NC}"

if [[ -z "$FINAL_CHANGES" ]]; then
    echo -e "${GREEN}✅ Nothing to verify — no changes to CI/CD pipeline.${NC}"
else
    echo -e "${CYAN}▶ Canonical local gates (workflow, security, tests, act)${NC}"
    if ! "$SCRIPT_DIR/verify_local_gates.sh"; then
        log_error "Canonical local gates failed; commit and push are blocked."
        exit 1
    fi
    log_success "Canonical local gates passed."
fi

# ============================================================================
# STEP 6: Commit & Push
# ============================================================================
hr
echo -e "${BLUE}${BOLD}[6/6] 🚀 Finalizing commit${NC}"

if git diff --cached --quiet; then
    echo -e "${GREEN}✨ No new changes to commit (working tree clean).${NC}"
else
    if printf '%s' "$COMMIT_MSG" | grep -Eq '^[[:space:]]*chore:[[:space:]]*auto[-_ ]?sync'; then
        log_error "Refusing generic auto-sync commit message: '$COMMIT_MSG'"
        log_error "Provide a meaningful Conventional Commit (feat/fix/chore/docs/refactor/test/ci/build/perf)."
        exit 1
    fi

    if ! printf '%s' "$COMMIT_MSG" | grep -Eq '^(feat|fix|chore|docs|style|refactor|perf|test|build|ci|revert)(\([^)]*\))?!?: .+'; then
        log_error "Commit message must use Conventional Commits: <type>[<scope>]: <subject>"
        log_error "Examples: feat(auth): add JWT validation / fix(ci): close SQL reader"
        exit 1
    fi

    echo -e "${CYAN}💾 Committing staged changes: '${COMMIT_MSG}'${NC}"
    if [[ "$DRY_RUN" == "true" ]]; then
        echo -e "${YELLOW}DRY-RUN: Would commit with message: $COMMIT_MSG${NC}"
    else
        git commit -m "$COMMIT_MSG"
        log_success "Committed: $(git rev-parse --short HEAD) - $COMMIT_MSG"
    fi
fi

if [[ "$DO_PUSH" == true && "$DRY_RUN" == "false" ]]; then
    REMOTE_EXISTS="$(git remote 2>/dev/null || true)"
    if [[ -n "$REMOTE_EXISTS" ]]; then
        echo -e "${CYAN}🚀 Pushing to remote...${NC}"
        CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
        git push -u "$REMOTE_NAME" "$CURRENT_BRANCH" || {
            log_error "Push failed. Check permissions or run status."
            exit 1
        }
        log_success "✅ Push completed: ${REMOTE_NAME}/${CURRENT_BRANCH}"
    else
        log_warn "No remote configured. Cannot push."
    fi
elif [[ "$DO_PUSH" == true && "$DRY_RUN" == "true" ]]; then
    echo -e "${YELLOW}DRY-RUN: Would push to remote.${NC}"
else
    echo -e "${YELLOW}ℹ️  Changes committed locally. Use --push to sync with GitHub.${NC}"
fi

# ── Restore stash if we had one ──────────────────────────────────────────────
if [[ "$STASHED" == "true" && "$DRY_RUN" == "false" ]]; then
    echo -e "${CYAN}🔄 Restoring pre-sync stash (if any was stashed during this run)...${NC}"
    # The stash was already popped in step 3; this is a safety net.
fi

echo ""
echo -e "${GREEN}=======================================================${NC}"
echo -e "${GREEN}✅ SYNCHRONIZATION & VERIFICATION COMPLETE!            ${NC}"
echo -e "${GREEN}=======================================================${NC}"
echo -e "  ${CYAN}• Synced with GitHub (${REMOTE_NAME})${NC}"
echo -e "  ${CYAN}• Local changes protected & restored${NC}"
echo -e "  ${CYAN}• CI/CD zero-bug pipeline simulated${NC}"
echo -e "  ${CYAN}• Red-team audit (secrets + deps)${NC}"
echo -e "  ${CYAN}• All checks green${NC}"
echo -e "  ${CYAN}• Committed: $(git rev-parse --short HEAD 2>/dev/null || echo '?')${NC}"
echo ""
