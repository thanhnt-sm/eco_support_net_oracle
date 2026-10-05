#!/usr/bin/env bash
# ==============================================================================
# DataGuard Git Sync (stage everything -> commit -> pull --rebase -> push)
#
# DANGEROUS BY DESIGN: `git add -A` stages every change in the working tree, including unrelated
# WIP, and the result is pushed to the current branch. rules/git_workflow.md forbids agents from
# doing this without an explicit user request, so the script refuses to run unless the caller opts
# in with DG_ALLOW_AUTO_PUSH=1 and supplies a Conventional Commit message:
#
#   DG_ALLOW_AUTO_PUSH=1 ./scripts/git_sync.sh "docs: update usage guide"
#
# Prefer staging the files you mean and `tools/git-tools/dg-git commit -m "<message>"` + `git push`.
# ==============================================================================

set -euo pipefail

GREEN="\033[0;32m"
CYAN="\033[0;36m"
YELLOW="\033[1;33m"
RED="\033[0;31m"
NC="\033[0m"

if [[ "${DG_ALLOW_AUTO_PUSH:-0}" != "1" ]]; then
    echo -e "${RED}[git_sync] Refusing to run: this script stages EVERY change (git add -A), commits and pushes.${NC}" >&2
    echo "  This is disabled by default (rules/git_workflow.md: no automatic commit/push, no 'git add -A')." >&2
    echo "  Safer: git add <files> && tools/git-tools/dg-git commit -m \"<type>(<scope>): <subject>\" && git push" >&2
    echo "  To run it anyway, deliberately: DG_ALLOW_AUTO_PUSH=1 $0 \"<type>(<scope>): <subject>\"" >&2
    exit 1
fi

COMMIT_MSG="${1:-}"
if [[ -z "$COMMIT_MSG" ]]; then
    echo -e "${RED}[git_sync] A Conventional Commit message argument is required (no generated messages).${NC}" >&2
    exit 1
fi

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo -e "${RED}[git_sync] Not a git repository.${NC}" >&2
    exit 1
fi

CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
echo -e "${YELLOW}=====================================================================${NC}"
echo -e "${YELLOW}  DG_ALLOW_AUTO_PUSH=1: staging ALL changes, committing and PUSHING   ${NC}"
echo -e "${YELLOW}  branch '${CURRENT_BRANCH}' with message: ${COMMIT_MSG}${NC}"
echo -e "${YELLOW}=====================================================================${NC}"

CHANGES=$(git status --porcelain)
if [ -n "$CHANGES" ]; then
    echo -e "${YELLOW}Staging changes (git add -A):${NC}"
    printf '%s\n' "$CHANGES"
    git add -A
    echo -e "${CYAN}Committing: '${COMMIT_MSG}'${NC}"
    # No `|| true`: a rejected commit (commit-msg / pre-commit hook) stops the sync.
    git commit -m "$COMMIT_MSG"
else
    echo -e "${GREEN}Working tree clean. No local changes to commit.${NC}"
fi

REMOTE="$(git remote | head -n 1 || true)"
if [ -n "$REMOTE" ]; then
    echo -e "${CYAN}Pulling with rebase from '${REMOTE}/${CURRENT_BRANCH}'...${NC}"
    git pull --rebase "$REMOTE" "$CURRENT_BRANCH" || {
        echo -e "${RED}Merge/rebase conflict detected. Launching conflict resolver...${NC}" >&2
        ./scripts/git_conflict_resolver.sh
        exit 1
    }
    echo -e "${GREEN}Pushing to '${REMOTE}/${CURRENT_BRANCH}'...${NC}"
    git push -u "$REMOTE" "$CURRENT_BRANCH"
    echo -e "${GREEN}Sync completed.${NC}"
else
    echo -e "${YELLOW}No remote configured; the commit stays local.${NC}"
fi
