#!/usr/bin/env bash
# ==============================================================================
# DataGuard Living Documentation Synchronization Validator
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
    "rules/git_workflow.md"
    "rules/doc_sync_enforcement.md"
    "rules/small_model_operational_protocol.md"
    "docs/legal/THIRD-PARTY-NOTICES.md"
    "docs/legal/ADDITIONAL-PERMISSIONS.md"
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

# Licence surfaces (README, docs, grants, package metadata, shipped notice copies) must agree on
# GPL-3.0-only + Commercial. CI runs the same script in the build-and-test job (ci.yml).
LICENSE_GATE=0
python3 scripts/check-license-consistency.py || LICENSE_GATE=1

if [ "$MISSING" -eq 0 ] && [ "$LICENSE_GATE" -eq 0 ]; then
    echo -e "\n${GREEN}✅ All bilingual documentation & rule artifacts are synchronized and present!${NC}"
    exit 0
else
    echo -e "\n${RED}❌ Documentation synchronization check failed: $MISSING files missing, licence gate exit $LICENSE_GATE.${NC}"
    exit 1
fi
