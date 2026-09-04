#!/usr/bin/env bash
# DataGuard workspace guard for staged paths.

set -euo pipefail

readonly ROOT_FILE_PATTERN='^(DataGuard\.sln|Directory(\..+)?\.props|Dockerfile|\.dockerignore|\.env\.example|\.release\.env\.example|\.gitignore|\.gitattributes|\.editorconfig|global\.json|NuGet\.config|README(\.vi)?\.md|CONTRIBUTING(\.vi)?\.md|SECURITY(\.vi)?\.md|LICENSE(\.md)?|CLAUDE\.md|AGENTS\.md|\.agentrules|\.cursorrules|\.windsurfrules|\.geminirules|devin_instructions\.md|robots\.txt)$'
readonly DIRECTORY_PATTERN='^(src|tests|samples|docs|plans|research|grants|brainstorm|scripts|tools|rules|benchmarks|\.github|\.githooks|\.agents|claude)/'
readonly LOCAL_CONFIG_PATTERN='^(\.omo/(config|agents)\.toml|\.codegraph/\.gitignore)$'

printf '[workspace-guard] Checking staged paths against DataGuard topology.\n'

staged_files="$(git diff --cached --name-only --diff-filter=ACMR)"
if [[ -z "$staged_files" ]]; then
    printf '[workspace-guard] No staged additions or modifications.\n'
    exit 0
fi

violations=()
while IFS= read -r staged_file; do
    if [[ "$staged_file" =~ $ROOT_FILE_PATTERN ]] ||
       [[ "$staged_file" =~ $DIRECTORY_PATTERN ]] ||
       [[ "$staged_file" =~ $LOCAL_CONFIG_PATTERN ]]; then
        continue
    fi

    violations+=("$staged_file")
done <<< "$staged_files"

if (( ${#violations[@]} == 0 )); then
    printf '[workspace-guard] All staged paths are allowed.\n'
    exit 0
fi

printf '[workspace-guard] Rejected staged paths:\n' >&2
printf '  %s\n' "${violations[@]}" >&2
printf '%s\n' \
    'Use the canonical DataGuard directories from rules/workspace_governance.md.' \
    'For a legacy stack, create an owner-approved cleanup manifest before staging additions.' >&2
exit 1

