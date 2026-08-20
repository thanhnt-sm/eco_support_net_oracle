# Devin Adapter — DataGuard Workspace

## Before work

1. Read `AGENTS.md`, `rules/workspace_governance.md`, and the relevant active plan.
2. Identify the exact DataGuard contract, source path, test, configuration, or workflow being changed.
3. Preserve unrelated WIP and do not infer product scope from historical documents.

## While working

- Production implementation belongs in `src/`; DataGuard tests live in the corresponding projects under `tests/`.
- Make minimal, coherent changes. Use symbol-aware navigation for exported symbols when language support is available.
- Update the narrowest affected documentation when public behavior, configuration, build/release, or operational behavior changes.
- Do not create arbitrary root paths, a second runtime, or generated artifacts outside policy-approved ignored locations.

## Verification

- Source change: run the relevant `dotnet build` and deterministic `dotnet test` scope.
- Workflow/container/doc change: run the matching YAML/actionlint, Docker smoke, or documentation validator when available.
- Report only observed results; mark unavailable verification explicitly.

## Cleanup and security

- Do not move or delete tracked source, research, legal text, agent configuration, or local state without an owner-approved `from → keep | extract | rewrite | remove` manifest.
- Do not expose secrets, credentials, tokens, private state, or handoff content.
