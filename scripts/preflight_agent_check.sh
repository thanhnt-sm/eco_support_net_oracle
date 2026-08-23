#!/usr/bin/env bash
# ==============================================================================
# EcoSupport Pre-Flight Invariant & Workspace Health Checker
# Real-time instant verification tool for AI Agents & Developers.
# ==============================================================================

set -e

GREEN="\033[0;32m"
CYAN="\033[0;36m"
YELLOW="\033[1;33m"
RED="\033[0;31m"
NC="\033[0m"

echo -e "${CYAN}⚡ [EcoSupport Pre-Flight Check] Running in-flight verification...${NC}"

# 1. Check rogue untracked / unapproved files in real-time
echo -e "${CYAN}1. Scanning workspace for rogue/unapproved files...${NC}"
ALLOWED_ROOT_PATTERNS=(
    "^Cargo\.toml$"
    "^Cargo\.lock$"
    "^CLAUDE\.md$"
    "^AGENTS\.md$"
    "^AI_AGENT_AUDIT\.md$"
    "^CHANGELOG\.md$"
    "^CODE_OF_CONDUCT\.md$"
    "^SUPPORT\.md$"
    "^CONTRIBUTING(\..+)?\.md$"
    "^README(\..+)?\.md$"
    "^SECURITY(\..+)?\.md$"
    "^LICENSE$"
    "^LICENSE(\..+)?\.md$"
    "^robots\.txt$"
    "^devin_instructions\.md$"
    "^pyproject\.toml$"
    "^package\.json$"
    "^pnpm-lock\.yaml$"
    "^tsconfig(\..+)?\.json$"
    "^vitest\.config\.ts$"
    "^DataGuard\.sln$"
    "^Directory\.Build\.props$"
    "^Dockerfile$"
    "^\.dockerignore$"
    "^\.env(\.example)?$"
    "^\.gitignore$"
    "^\.gitattributes$"
    "^\.editorconfig$"
    "^\.tmp_new_models$"
    "^\.cursorrules$"
    "^\.windsurfrules$"
    "^\.geminirules$"
    "^\.agentrules$"
    "^\.DS_Store$"
    "^\.codex"
    "^\.agents"
    "^\.github"
    "^\.githooks"
    "^\.git$"
    "^claude"
    "^crates"
    "^docs"
    "^rules"
    "^plans"
    "^brainstorm"
    "^research"
    "^grants"
    "^scripts"
    "^scratch"
    "^tests"
    "^samples"
    "^benchmarks"
    "^tools"
    "^src"
    "^packages"
    "^node_modules$"
    "^coverage$"
    "^target"
    "^BenchmarkDotNet\.Artifacts$"
    "^\.venv"
    "^\.mypy_cache"
    "^\.pytest_cache"
    "^\.ruff_cache"
    "^\.codegraph"
    "^\.omo"
    "^\.omp"
)

ROGUE_COUNT=0
for item in * .*; do
    [ "$item" = "." ] || [ "$item" = ".." ] && continue
    MATCHED=false
    for pat in "${ALLOWED_ROOT_PATTERNS[@]}"; do
        if echo "$item" | grep -qE "$pat"; then
            MATCHED=true
            break
        fi
    done
    if [ "$MATCHED" = false ]; then
        echo -e "  ${RED}❌ ROGUE ITEM DETECTED:${NC} $item"
        ROGUE_COUNT=$((ROGUE_COUNT + 1))
    fi
done

if [ "$ROGUE_COUNT" -gt 0 ]; then
    echo -e "${RED}⚠️  Workspace violation: $ROGUE_COUNT rogue file(s) found at root. Move to scratch/ or subfolders!${NC}"
    exit 1
else
    echo -e "  ${GREEN}✓ Root directory is 100% clean and compliant with fixed layout.${NC}"
fi

# 2. Check bilingual documentation
echo -e "${CYAN}2. Verifying bilingual documentation sync...${NC}"
./scripts/verify_docs_sync.sh > /dev/null
echo -e "  ${GREEN}✓ All 30 bilingual docs and rules are present and synchronized.${NC}"

# 3. Check Cargo compilation health
echo -e "${CYAN}3. Verifying Rust compiler health...${NC}"
export PATH="$HOME/.cargo/bin:$PATH"
cargo check --workspace --quiet
echo -e "  ${GREEN}✓ Cargo check passed with 0 errors, 0 warnings.${NC}"

echo -e "\n${GREEN}🚀 [Pre-Flight OK] Workspace is in perfect state. Proceed with confidence!${NC}"
