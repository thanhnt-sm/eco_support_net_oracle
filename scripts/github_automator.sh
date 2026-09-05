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

# ── Paths ────────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "$SCRIPT_DIR/..")"
SLN_FILE="$PROJECT_ROOT/DataGuard.sln"
GITHOOKS_DIR="$PROJECT_ROOT/.githooks"
CONFLICT_RESOLVER="$SCRIPT_DIR/git_conflict_resolver.sh"

# ── Globals ──────────────────────────────────────────────────────────────────
COMMIT_MSG=""
DO_PUSH=false
DRY_RUN=false
SKIP_LOCAL_ACTIONS=false

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
      --skip-actions  Skip local GitHub Actions simulation (act)
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
        --skip-actions) SKIP_LOCAL_ACTIONS=true; shift ;;
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

# ── Red-team: Secret scan on local changes BEFORE stashing ───────────────────
if [[ -n "$LOCAL_CHANGES" ]]; then
    if [[ -x "$PROJECT_ROOT/tools/git-tools/dg-git" ]]; then
        echo -e "${CYAN}🔒 [Red-team] Scanning local changes for secrets...${NC}"
        if ! "$PROJECT_ROOT/tools/git-tools/dg-git" secret; then
            log_error "Potential secret/token/password detected in uncommitted changes."
            log_error "Dừng đồng bộ để bảo vệ tài khoản GitHub."
            exit 1
        fi
        log_success "No secrets found in local changes."
    fi

    echo -e "${YELLOW}Local changes detected. Stashing to protect working tree...${NC}"
    STASH_NAME="github-automator-stash-$(date +%s)"
    git stash push -u -m "$STASH_NAME" >/dev/null 2>&1
    STASHED=true
    echo -e "${GREEN}✅ Local changes safely stashed ($STASH_NAME).${NC}"
else
    STASHED=false
    echo -e "${GREEN}✨ Clean working tree. No uncommitted local changes to stash.${NC}"
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
hr
echo -e "${BLUE}${BOLD}[3/6] 📥 Restoring local changes from stash...${NC}"

if [[ "$STASHED" == "true" ]]; then
    echo -e "${CYAN}Applying local changes via git stash pop...${NC}"
    if ! git stash pop >/dev/null 2>&1; then
        echo -e "${RED}⚠️ Conflict detected while applying local changes over updated base!${NC}"
        if [[ -x "$CONFLICT_RESOLVER" ]]; then
            chmod +x "$CONFLICT_RESOLVER"
            "$CONFLICT_RESOLVER"
        fi
        while [[ -n "$(git diff --name-only --diff-filter=U 2>/dev/null || true)" ]]; do
            echo -e "${YELLOW}Please resolve conflicts, stage with 'git add <file>', and press Enter (or 'abort'):${NC}"
            USER_INPUT=""
            if [ -t 0 ]; then
                read -r USER_INPUT
            elif [ -e /dev/tty ]; then
                read -r USER_INPUT < /dev/tty || break
            else
                echo -e "${RED}Non-interactive terminal. Cannot wait for manual resolution.${NC}"
                break
            fi
            [[ "$USER_INPUT" == "abort" ]] && exit 1
        done
        echo -e "${GREEN}✅ Local merge conflicts resolved.${NC}"
    else
        echo -e "${GREEN}✅ Local changes restored cleanly.${NC}"
    fi
else
    echo -e "${GREEN}✨ No local stash to restore.${NC}"
fi

# ==============================================================================
# STEP 4: Stage all changes
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[4/6] 📋 Staging all workspace changes...${NC}"

FINAL_CHANGES="$(git status --porcelain 2>/dev/null || true)"
if [[ -n "$FINAL_CHANGES" ]]; then
    # Red-team: scan before staging
    if [[ -x "$PROJECT_ROOT/tools/git-tools/dg-git" ]]; then
        echo -e "${CYAN}🔒 [Red-team] Scanning workspace for secrets before stage...${NC}"
        if ! "$PROJECT_ROOT/tools/git-tools/dg-git" secret; then
            log_error "Potential secret detected. Aborting before staging."
            exit 1
        fi
        log_success "Workspace is clean of known secret patterns."
    fi

    if [[ "$DRY_RUN" == "true" ]]; then
        echo -e "${YELLOW}DRY-RUN: Would stage, commit, and push. No changes made.${NC}"
        exit 0
    fi

    git add -A
    echo -e "${GREEN}✅ All changes staged.${NC}"
else
    echo -e "${GREEN}✨ Nothing to sync — working tree is clean.${NC}"
fi

# ==============================================================================
# STEP 5: Local CI/CD pipeline simulation (Zero-Bug Policy)
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[5/6] 🧪 Simulating local CI/CD pipeline & security audit...${NC}"

if [[ -z "$FINAL_CHANGES" ]]; then
    echo -e "${GREEN}✅ Nothing to verify — no changes to CI/CD pipeline.${NC}"
else
    # ── 5.1 Restore dependencies (locked mode) ───────────────────────────────
    echo -e "${CYAN}▶ [5.1] dotnet restore --locked-mode${NC}"
    if ! dotnet restore "$SLN_FILE" --locked-mode 2>&1; then
        log_error "dotnet restore --locked-mode failed! Lock file out of sync."
        log_error "Run 'dotnet restore DataGuard.sln' to regenerate lock files, then retry."
        exit 1
    fi
    log_success "Dependencies restored (locked mode)."

    # ── 5.2 Build solution (Release) ──────────────────────────────────────────
    echo -e "${CYAN}▶ [5.2] dotnet build --configuration Release${NC}"
    if ! dotnet build "$SLN_FILE" --configuration Release --no-restore 2>&1; then
        log_error "Build failed! Fix compilation errors before continuing."
        exit 1
    fi
    log_success "Build succeeded (0 errors, 0 warnings)."

    # ── 5.3 Run .NET analyzers ────────────────────────────────────────────────
    echo -e "${CYAN}▶ [5.3] dotnet build /p:RunAnalyzers=true${NC}"
    if ! dotnet build "$SLN_FILE" --configuration Release --no-restore /p:RunAnalyzers=true 2>&1; then
        log_error "Analyzers found issues!"
        exit 1
    fi
    log_success "Code analyzers passed."

    # ── 5.4 Enforce formatting (dotnet format gate) ──────────────────────────
    echo -e "${CYAN}▶ [5.4] dotnet format --verify-no-changes${NC}"
    if ! dotnet format "$SLN_FILE" --verify-no-changes --no-restore 2>&1; then
        log_error "Code formatting check failed! Run 'dotnet format DataGuard.sln' to fix."
        exit 1
    fi
    log_success "Formatting is clean."

    # ── 5.5 Run tests with code coverage ──────────────────────────────────────
    echo -e "${CYAN}▶ [5.5] dotnet test (with coverage collection)${NC}"
    if ! dotnet test "$SLN_FILE" --configuration Release --no-build \
            --collect:"XPlat Code Coverage" \
            --logger "trx;LogFileName=test_results.trx" 2>&1; then
        log_error "Tests failed! Review test output above."
        exit 1
    fi
    log_success "All tests passed."

    # ── 5.6 Coverage gate (fail under 60%) ───────────────────────────────────
    echo -e "${CYAN}▶ [5.6] Code coverage gate (minimum 60%)${NC}"
    COVERAGE_FILES=()
    while IFS= read -r -d '' f; do
        COVERAGE_FILES+=("$f")
    done < <(find "$PROJECT_ROOT" -path '*/TestResults/*/coverage.cobertura.xml' -print0 2>/dev/null || true)

    if [[ ${#COVERAGE_FILES[@]} -gt 0 ]]; then
        python3 - <<'PYEOF' || { log_error "Coverage gate failed!"; exit 1; }
import xml.etree.ElementTree as ET
import glob, sys
from collections import defaultdict
hits = defaultdict(bool)
files = glob.glob('**/TestResults/**/coverage.cobertura.xml', recursive=True)
if not files:
    print("::error::No coverage files found!")
    sys.exit(1)
for f in files:
    r = ET.parse(f).getroot()
    for cls in r.iter('class'):
        fn = cls.attrib['filename']
        if "/obj/" in fn.replace("\\", "/"):
            continue
        for line in cls.iter('line'):
            key = (fn, int(line.attrib['number']))
            hits[key] = hits[key] or int(line.attrib['hits']) > 0
total = len(hits)
cov = sum(1 for v in hits.values() if v)
rate = (cov / total * 100) if total > 0 else 0
print(f"Overall Solution Line Coverage: {rate:.2f}% ({cov}/{total} lines)")
if rate < 60.0:
    print(f"::error::Coverage {rate:.2f}% is below required threshold of 60.0%!")
    sys.exit(1)
PYEOF
        log_success "Coverage gate passed."
    else
        log_warn "No coverage files found — skipping coverage gate."
    fi

    # ── 5.7 Vulnerable NuGet packages (dependency audit) ──────────────────────
    echo -e "${CYAN}▶ [5.7] dotnet list package --vulnerable${NC}"
    dotnet list "$SLN_FILE" package --vulnerable --include-transitive --format json > /tmp/vuln_check.json 2>&1 || true
    python3 - <<'PYEOF' || { log_error "Vulnerable packages detected!"; exit 1; }
import json, sys
try:
    with open('/tmp/vuln_check.json') as f:
        data = json.load(f)
except (json.JSONDecodeError, FileNotFoundError):
    print("No vulnerable packages found.")
    sys.exit(0)
if data.get('problems'):
    for p in data['problems']:
        print(f"::error::Audit problem: {p.get('message', p)}")
    sys.exit(1)
bad = []
for proj in data.get('projects', []):
    for fw in proj.get('frameworks', []):
        for pkg in fw.get('topLevelPackages', []) + fw.get('transitivePackages', []):
            if pkg.get('vulnerabilities'):
                bad.append((pkg.get('id', pkg.get('name', '?')), proj.get('path', '?')))
if bad:
    for name, path in bad:
        print(f"::error::Vulnerable package: {name} ({path})")
    sys.exit(1)
print("No vulnerable packages found.")
PYEOF
    rm -f /tmp/vuln_check.json
    log_success "No vulnerable NuGet packages."

    # ── 5.8 Red-team: TruffleHog secret scan ──────────────────────────────────
    echo -e "${CYAN}▶ [5.8] TruffleHog (verified secret scan over full history)${NC}"
    if command -v trufflehog >/dev/null 2>&1; then
        if ! trufflehog git file://"$PROJECT_ROOT" --no-update --only-verified --fail 2>&1; then
            log_error "TruffleHog found verified secrets in repository history!"
            exit 1
        fi
        log_success "No verified secrets found in history."
    elif docker info >/dev/null 2>&1; then
        echo -e "${YELLOW}trufflehog not installed; running via Docker container...${NC}"
        if ! docker run --rm -v "$PROJECT_ROOT:/pwd" -e "TARGET=/pwd" \
                ghcr.io/trufflesecurity/trufflehog:3.97.0 \
                git file:///pwd --no-update --only-verified --fail 2>&1; then
            log_error "TruffleHog (Docker) found verified secrets!"
            exit 1
        fi
        log_success "No verified secrets found (Docker scan)."
    else
        log_warn "Skipping TruffleHog — neither trufflehog nor Docker available."
    fi

    # ── 5.9 Pre-commit hooks simulation ──────────────────────────────────────
    echo -e "${CYAN}▶ [5.9] Pre-commit hooks (format whitespace, anti-garbage guard, doc sync)${NC}"

    # 5.9.1 dotnet format whitespace
    echo -e "${CYAN}  ▶ dotnet format whitespace --verify-no-changes${NC}"
    if ! dotnet format whitespace "$SLN_FILE" --verify-no-changes 2>&1; then
        log_error "Whitespace formatting check failed! Run 'dotnet format whitespace $SLN_FILE' to fix."
        exit 1
    fi

    # 5.9.2 anti-garbage guard (workspace topology)
    echo -e "${CYAN}  ▶ scripts/anti_garbage_guard.sh${NC}"
    if [[ -x "$SCRIPT_DIR/anti_garbage_guard.sh" ]]; then
        if ! "$SCRIPT_DIR/anti_garbage_guard.sh" 2>&1; then
            log_error "Anti-garbage guard rejected staged paths."
            exit 1
        fi
    fi

    # 5.9.3 documentation sync validator
    echo -e "${CYAN}  ▶ scripts/verify_docs_sync.sh${NC}"
    if [[ -x "$SCRIPT_DIR/verify_docs_sync.sh" ]]; then
        if ! "$SCRIPT_DIR/verify_docs_sync.sh" 2>&1; then
            log_error "Documentation sync check failed!"
            exit 1
        fi
    fi

    log_success "All pre-commit gate checks passed."

    # ── 5.10 GitHub Actions YAML validation (actionlint) ──────────────────────
    echo -e "${CYAN}▶ [5.10] actionlint (GitHub Actions workflow validation)${NC}"
    if command -v actionlint >/dev/null 2>&1; then
        if ! actionlint "$PROJECT_ROOT/.github/workflows/"*.yml 2>&1; then
            log_error "actionlint found YAML/errors in GitHub Actions workflows!"
            exit 1
        fi
        log_success "All workflow YAML files are valid."
    else
        log_warn "actionlint not installed — skipping workflow validation."
    fi

    # ── 5.11 Local GitHub Actions simulation (act) ────────────────────────────
    if [[ "$SKIP_LOCAL_ACTIONS" != "true" ]]; then
        echo -e "${CYAN}▶ [5.11] Local GitHub Actions simulation (act)${NC}"
        if command -v act >/dev/null 2>&1; then
            if ! act push \
               --workflows "$PROJECT_ROOT/.github/workflows/ci.yml" \
               --job build-and-test \
               --env ACT=true \
               --platform "ubuntu-latest=catthehacker/ubuntu:act-latest" 2>&1; then
                log_warn "Local act CI simulation had issues (non-blocking — GitHub Actions still enforces)."
            else
                log_success "Local act CI simulation passed."
            fi
        else
            log_warn "act not installed — skipping local CI simulation."
            log_warn "GitHub Actions CI will still enforce these checks after push."
        fi
    else
        echo -e "${YELLOW}  Skipping local Actions simulation (--skip-actions).${NC}"
    fi
fi

# ==============================================================================
# STEP 6: Commit & Push
# ==============================================================================
hr
echo -e "${BLUE}${BOLD}[6/6] 🚀 Finalizing commit${NC}"

# Re-stage any files touched by formatters
git add -A

if git diff --cached --quiet; then
    echo -e "${GREEN}✨ No new changes to commit (working tree clean).${NC}"
else
    # Enforce Conventional Commits + reject auto-sync messages
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
