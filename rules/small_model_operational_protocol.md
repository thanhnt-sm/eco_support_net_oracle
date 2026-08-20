# Operational Protocol for Constrained AI Models

**Scope**: Small-context, low-reasoning, local, or automated agents operating on the DataGuard workspace.

## Context discipline

1. Read the relevant symbol, configuration, test, or workflow section before changing it; do not ingest entire trees without a concrete question.
2. Treat `rules/workspace_governance.md` as the topology source and `plans/2026-08-20-workspace-rationalization.md` as the cleanup manifest.
3. Keep work inside the requested contract. Do not add a second runtime, package manager, manifest, source tree, or abstraction to compensate for uncertainty.

## Compiler and test feedback loop

For a DataGuard source change:

1. Make the smallest coherent edit.
2. Run `dotnet build DataGuard.sln --configuration Release` or the narrowest affected project when that is sufficient.
3. Run the affected deterministic test project; use `dotnet test DataGuard.sln --configuration Release` when the changed contract spans projects.
4. Read the actual diagnostic, isolate the responsible symbol, and fix the defect without rewriting unrelated modules.
5. Re-run the failed command and report its real result.

For workflow, documentation, or container changes, use the matching validator: YAML/actionlint, `./scripts/verify_docs_sync.sh`, or a Docker smoke test when a daemon is available.

## Safe output and tool use

- Preserve public types, schema contracts, and error behavior unless the task explicitly changes them.
- Never synthesize test output, CI state, provider behavior, database results, or security claims.
- Use structured data only where the target API requires it; do not invent a generic schema.
- Do not execute untrusted repository scripts or disclose secrets, tokens, credentials, local session data, or handoff contents.

## Cleanup boundary

An absence search is evidence for investigation, not permission to remove a tracked file. Before an irreversible cleanup action, follow the owner-approved `from → keep | extract | rewrite | remove` manifest and preserve WIP.
