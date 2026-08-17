#!/usr/bin/env bash
# ==============================================================================
# EcoSupport Living Documentation Synchronization Validator
# Verifies that all required bilingual documentation artifacts exist and are indexed.
# ==============================================================================

set -e

GREEN="\033[0;32m"
CYAN="\033[0;36m"
RED="\033[0;31m"
NC="\033[0m"

echo -e "${CYAN}📚 [DocSync Validator] Verifying bilingual documentation completeness...${NC}"

REQUIRED_DOCS=(
    "README.md"
    "README.vi.md"
    "CONTRIBUTING.md"
    "CONTRIBUTING.vi.md"
    "SECURITY.md"
    "SECURITY.vi.md"
    "docs/overview/vibe_coder_guide.md"
    "docs/overview/vibe_coder_guide.vi.md"
    "docs/architecture/system_architecture.md"
    "docs/architecture/system_architecture.vi.md"
    "docs/architecture/tech_stack_evaluation.md"
    "docs/architecture/tech_stack_evaluation.vi.md"
    "docs/architecture/agent-config.md"
    "docs/architecture/agent-config.vi.md"
    "docs/operations/playbook_and_runbook.md"
    "docs/operations/playbook_and_runbook.vi.md"
    "docs/testing/qa_test_strategy.md"
    "docs/testing/qa_test_strategy.vi.md"
    "docs/developers/contributor_deep_dive.md"
    "docs/developers/contributor_deep_dive.vi.md"
    "docs/sitemap_and_component_registry.md"
    "docs/sitemap_and_component_registry.vi.md"
    "rules/universal_ai_constitution.md"
    "rules/workspace_governance.md"
    "rules/doc_sync_enforcement.md"
    "rules/small_model_operational_protocol.md"
    "grants/written_explanation.md"
    "grants/ecosystem_impact_matrix.md"
    "grants/grant_pitch.md"
    "grants/SUBMISSION_CHECKLIST.md"
    "brainstorm/expert_council_redteam.md"
    "brainstorm/product_vision_and_niche_strategy.md"
)

MISSING=0

for doc in "${REQUIRED_DOCS[@]}"; do
    if [ -f "$doc" ]; then
        echo -e "  ${GREEN}✓ Found:${NC} $doc"
    else
        echo -e "  ${RED}✗ MISSING:${NC} $doc"
        MISSING=$((MISSING + 1))
    fi
done

if [ "$MISSING" -eq 0 ]; then
    echo -e "\n${GREEN}✅ All bilingual documentation & rule artifacts are synchronized and present!${NC}"
    exit 0
else
    echo -e "\n${RED}❌ Documentation synchronization check failed: $MISSING files missing.${NC}"
    exit 1
fi
