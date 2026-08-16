#!/usr/bin/env bash
# ==============================================================================
# EcoSupport Anti-Garbage Guard
# Prevents AI agents from creating files/folders outside allowed locations.
# Integrated into Git Pre-commit Hook.
# ==============================================================================

set -e

RED="\033[0;31m"
YELLOW="\033[0;33m"
GREEN="\033[0;32m"
CYAN="\033[0;36m"
NC="\033[0m"

echo -e "${CYAN}🛡️  [AntiGarbage Guard] Scanning staged files for workspace violations...${NC}"

# ==============================================================================
# WHITELIST: Allowed directories and file patterns
# Any staged file that does NOT match these patterns will be REJECTED.
# ==============================================================================

ALLOWED_PATTERNS=(
    "^Cargo\\.toml$"
    "^Cargo\\.lock$"
    "^CLAUDE\\.md$"
    "^AGENTS\\.md$"
    "^CONTRIBUTING(\\..+)?\\.md$"
    "^README(\\..+)?\\.md$"
    "^SECURITY(\\..+)?\\.md$"
    "^LICENSE$"
    "^LICENSE(\\..+)?\\.md$"
    "^\\.gitignore$"
    "^\\.gitattributes$"
    "^\\.cursorrules$"
    "^\\.windsurfrules$"
    "^\\.geminirules$"
    "^\\.agentrules$"
    "^robots\\.txt$"
    "^devin_instructions\\.md$"
    "^pyproject\\.toml$"
    "^\\.env\\.example$"
    "^\\.github/"
    "^\\.githooks/"
    "^crates/"
    "^docs/"
    "^rules/"
    "^plans/"
    "^brainstorm/"
    "^research/"
    "^grants/"
    "^scripts/"
    "^src/"
    "^tests/"
    "^\\.agents/"
)

VIOLATIONS=0
VIOLATION_LIST=()

# Get all staged files (excluding deletions — deleting files should never be blocked)
STAGED_FILES=$(git diff --cached --name-status 2>/dev/null | awk '$1 != "D" {print $2}' || true)

if [ -z "$STAGED_FILES" ]; then
    echo -e "${GREEN}✅ No staged files to check.${NC}"
    exit 0
fi

while IFS= read -r staged_file; do
    ALLOWED=false
    for pattern in "${ALLOWED_PATTERNS[@]}"; do
        if echo "$staged_file" | grep -qE "$pattern"; then
            ALLOWED=true
            break
        fi
    done

    if [ "$ALLOWED" = false ]; then
        echo -e "  ${RED}❌ REJECTED:${NC} '$staged_file' is outside allowed workspace zones."
        VIOLATION_LIST+=("$staged_file")
        VIOLATIONS=$((VIOLATIONS + 1))
    fi
done <<< "$STAGED_FILES"

if [ "$VIOLATIONS" -gt 0 ]; then
    echo ""
    echo -e "${RED}🚫 WORKSPACE VIOLATION DETECTED: $VIOLATIONS file(s) staged outside allowed zones!${NC}"
    echo ""
    echo -e "${YELLOW}RESOLUTION: Move these files to the correct location:${NC}"
    echo -e "  • Temp / throwaway files  → ${CYAN}scratch/${NC} (gitignored)"
    echo -e "  • Rust source code        → ${CYAN}crates/eco-<name>/src/${NC}"
    echo -e "  • Documentation           → ${CYAN}docs/<category>/${NC}"
    echo -e "  • Task plans              → ${CYAN}plans/${NC}"
    echo -e "  • Strategy / analysis     → ${CYAN}brainstorm/${NC}"
    echo -e "  • Research scripts/data   → ${CYAN}research/${NC}"
    echo ""
    echo -e "${RED}This commit has been BLOCKED. Fix the violations and try again.${NC}"
    exit 1
else
    echo -e "${GREEN}✅ [AntiGarbage Guard] All staged files are within allowed workspace zones.${NC}"
fi
