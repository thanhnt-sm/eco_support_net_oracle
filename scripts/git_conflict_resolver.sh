#!/usr/bin/env bash
# ==============================================================================
# DataGuard Git Conflict Resolution Assistant
# Inspects unmerged files, provides status report, and safe resolution options.
# ==============================================================================

set -e

RED="\033[0;31m"
GREEN="\033[0;32m"
YELLOW="\033[1;33m"
CYAN="\033[0;36m"
NC="\033[0m"

echo -e "${YELLOW}🔍 Checking for Git conflicts in workspace...${NC}"

CONFLICT_FILES=$(git diff --name-only --diff-filter=U 2>/dev/null || true)

if [ -z "$CONFLICT_FILES" ]; then
    echo -e "${GREEN}✅ No unmerged conflict files found.${NC}"
    exit 0
fi

echo -e "${RED}⚠️ Unmerged conflict files detected:${NC}"
echo "$CONFLICT_FILES" | while read -r file; do
    echo -e "   ${RED}• $file${NC}"
done

echo ""
echo -e "${CYAN}Resolution Strategies:${NC}"
echo "1) Auto-resolve favoring OUR local changes:"
echo "   git checkout --ours <filename> && git add <filename>"
echo "2) Auto-resolve favoring THEIRS remote changes:"
echo "   git checkout --theirs <filename> && git add <filename>"
echo "3) Abort rebase/merge and restore previous clean state:"
echo "   git rebase --abort || git merge --abort"
echo ""
