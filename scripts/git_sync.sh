#!/usr/bin/env bash
# ==============================================================================
# EcoSupport Git Sync & Fast Push Automation Tool
# Handles rapid pushing, auto-stashing, conflict prevention, and branch tracking.
# ==============================================================================

set -e

GREEN="\033[0;32m"
CYAN="\033[0;36m"
YELLOW="\033[1;33m"
RED="\033[0;31m"
NC="\033[0m"

echo -e "${CYAN}🌿 [EcoSupport Git Sync] Starting automated workspace sync...${NC}"

# Check if inside git repo
if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo -e "${RED}❌ Not a git repository. Initializing...${NC}"
    git init
    git branch -M main
fi

# Configure hooks directory if not set
git config core.hooksPath .githooks

# Commit message
COMMIT_MSG="${1:-"chore(sync): automated workspace synchronization [$(date -u +'%Y-%m-%dT%H:%M:%SZ')]"}"

# Check git status
CHANGES=$(git status --porcelain)

if [ -n "$CHANGES" ]; then
    echo -e "${YELLOW}📦 Staging changes...${NC}"
    git add -A
    
    echo -e "${CYAN}💾 Committing: '${COMMIT_MSG}'${NC}"
    git commit -m "$COMMIT_MSG" || true
else
    echo -e "${GREEN}✨ Working tree clean. No local changes to commit.${NC}"
fi

# Fetch and Rebase if remote exists
REMOTE_EXISTS=$(git remote 2>/dev/null || true)
if [ -n "$REMOTE_EXISTS" ]; then
    CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
    echo -e "${CYAN}🔄 Pulling latest with rebase from remote '${REMOTE_EXISTS}' on branch '${CURRENT_BRANCH}'...${NC}"
    git pull --rebase "$REMOTE_EXISTS" "$CURRENT_BRANCH" || {
        echo -e "${RED}⚠️ Merge/Rebase conflict detected! Launching conflict resolver...${NC}"
        ./scripts/git_conflict_resolver.sh
        exit 1
    }
    
    echo -e "${GREEN}🚀 Pushing to remote...${NC}"
    git push -u "$REMOTE_EXISTS" "$CURRENT_BRANCH"
    echo -e "${GREEN}✅ Sync completed successfully!${NC}"
else
    echo -e "${YELLOW}ℹ️ No remote configured yet. Local commit is safely stored in git history.${NC}"
    echo -e "${CYAN}To link a remote repository, run: git remote add origin <your-repo-url>${NC}"
fi
