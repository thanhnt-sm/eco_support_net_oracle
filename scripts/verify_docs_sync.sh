#!/usr/bin/env bash
# ==============================================================================
# DataGuard Living Documentation Synchronization Validator
# Verifies that all required bilingual documentation artifacts exist, that the README rule tables match
# the rule sources (scripts/gen_rule_table.py --check) and that docs/USAGE.md names only declared CLI flags.
# ==============================================================================

set -e

find_python() {
    if [[ -n "${PYTHON_BIN:-}" ]] && "$PYTHON_BIN" -c "import sys; sys.exit(0)" >/dev/null 2>&1; then
        printf "%s\n" "$PYTHON_BIN"
        return 0
    fi
    for cmd in python3 python py; do
        while IFS= read -r candidate; do
            [[ -n "$candidate" ]] || continue
            if "$candidate" -c "import sys; sys.exit(0)" >/dev/null 2>&1; then
                printf "%s\n" "$candidate"
                return 0
            fi
        done < <(which -a "$cmd" 2>/dev/null || true)
    done
    for candidate in \
        /c/Users/*/AppData/Local/Programs/Python/Python*/python.exe \
        "${LOCALAPPDATA:-}/Programs/Python/Python"*/python.exe \
        "${USERPROFILE:-}/AppData/Local/Programs/Python/Python"*/python.exe \
        "C:/Users/"*/AppData/Local/Programs/Python/Python*/python.exe \
        /c/Python*/python.exe \
        "C:/Python"*/python.exe; do
        if [[ -f "$candidate" ]] && "$candidate" -c "import sys; sys.exit(0)" >/dev/null 2>&1; then
            printf "%s\n" "$candidate"
            return 0
        fi
    done
    return 1
}
PYTHON_BIN="$(find_python || echo "python3")"
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
"$PYTHON_BIN" scripts/check-license-consistency.py || LICENSE_GATE=1

# Content check 1: the README.md / README.vi.md rule tables must equal the table generated from
# ProviderRuleCatalog.RuleTitles and the rule sources (regenerate with: python3 scripts/gen_rule_table.py).
echo -e "${CYAN}📋 [DocSync Validator] Checking README rule tables against the rule sources...${NC}"
RULE_TABLE_GATE=0
"$PYTHON_BIN" scripts/gen_rule_table.py --check || RULE_TABLE_GATE=1

# Content check 2: every DataGuard CLI flag mentioned in docs/USAGE.md must be declared by a
# `new Option<...>(...)` in src/DataGuard.Cli (string literal or a `const string ... = "--..."`).
# Lines that invoke another tool (dotnet, code, git, npm, docker, ...) without `dataguard` are skipped;
# --help/--version are System.CommandLine built-ins.
echo -e "${CYAN}🚩 [DocSync Validator] Checking CLI flags in docs/USAGE.md against src/DataGuard.Cli...${NC}"
CLI_FLAG_GATE=0
"$PYTHON_BIN" - <<'PY' || CLI_FLAG_GATE=1
import re
import sys
from pathlib import Path

cli_sources = [p for p in Path("src/DataGuard.Cli").rglob("*.cs") if not {"bin", "obj"} & set(p.parts)]
if not cli_sources:
    print("  ✗ no C# sources under src/DataGuard.Cli")
    sys.exit(1)
text = "\n".join(p.read_text(encoding="utf-8") for p in cli_sources)
constants = dict(re.findall(r'const\s+string\s+(\w+)\s*=\s*"(--[a-z0-9-]+)"', text))
declared = {"--help", "--version"}
for arguments in re.findall(r"new\s+Option<[^>]*>\s*\(([^;{]*)", text):
    declared.update(re.findall(r'"(--[a-z0-9-]+)"', arguments))
    for identifier in re.findall(r"\b(?:\w+\.)?(\w+)\b", arguments):
        if identifier in constants:
            declared.add(constants[identifier])

foreign_tool = re.compile(r"(^|[\s`$(|;])(dotnet|code|git|npm|npx|node|docker|gh|pip3?|python3?|curl|az|kubectl|helm)\s")
dataguard_call = re.compile(r"\bdataguard\s")
unknown = {}
for number, line in enumerate(Path("docs/USAGE.md").read_text(encoding="utf-8").splitlines(), 1):
    if foreign_tool.search(line) and not dataguard_call.search(line):
        continue
    for flag in re.findall(r"(?<![\w-])(--[a-z][a-z0-9-]+)", line):
        if flag not in declared:
            unknown.setdefault(flag, []).append(number)

if unknown:
    for flag, lines in sorted(unknown.items()):
        print(f"  ✗ docs/USAGE.md: unknown CLI flag {flag} (line {', '.join(map(str, lines))})")
    sys.exit(1)
print(f"  ✓ docs/USAGE.md: every CLI flag is declared ({len(declared)} known flags)")
PY

if [ "$MISSING" -eq 0 ] && [ "$LICENSE_GATE" -eq 0 ] && [ "$RULE_TABLE_GATE" -eq 0 ] && [ "$CLI_FLAG_GATE" -eq 0 ]; then
    echo -e "\n${GREEN}✅ All bilingual documentation & rule artifacts are synchronized and present!${NC}"
    exit 0
else
    echo -e "\n${RED}❌ Documentation synchronization check failed: $MISSING files missing, licence gate exit $LICENSE_GATE, rule table gate exit $RULE_TABLE_GATE, CLI flag gate exit $CLI_FLAG_GATE.${NC}"
    exit 1
fi
