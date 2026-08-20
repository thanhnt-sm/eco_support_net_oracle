# DataGuard Documentation Synchronization

**Rule ID**: `DOC-SYNC-DATAGUARD-001`
**Authority**: `rules/workspace_governance.md` defines topology; `plans/2026-08-20-workspace-rationalization.md` defines the cleanup transition.

## Scope

Documentation is a product contract, not a duplicate of source code. Update it in the same change when a DataGuard modification changes a public API, CLI behavior, validation rule, supported database/provider, build/release process, configuration, security posture, or operational procedure.

Do not edit documentation merely to preserve obsolete product identity, stale inventories, or historical toolchains. Correctness is more important than document count.

## Placement and language

- Product and developer documentation belongs in `docs/`; current plans and ADRs belong in `plans/`.
- Research, grant material, and strategic notes remain in `research/`, `grants/`, and `brainstorm/`; they are not product source.
- When a user-facing document has an established English/Vietnamese pair, update both from the same verified facts. Internal implementation notes may use one language when no paired document exists.
- Do not create a new translation, sitemap entry, diagram, or document solely to satisfy a checklist. Add it only when it represents a maintained product contract.

## Change protocol

1. Identify the observable contract changed in `src/`, `tests/`, CI/release, container configuration, or public documentation.
2. Update the narrowest affected document and remove stale commands, product names, links, and claims in the same change.
3. Update `docs/sitemap_and_component_registry*.md` only when its replacement/entry is current and semantically correct; do not perpetuate a stale registry.
4. Run the command or scenario documented whenever practical. Mark an unavailable verification explicitly rather than asserting success.
5. Run `./scripts/verify_docs_sync.sh` after documentation/rule changes. Its current result proves required files exist, not that their content is semantically current.

## Prohibitions

- Do not claim a build, test, deployment, command, integration, or feature exists without evidence.
- Do not retain documentation that directs users to a removed runtime or unsupported package manager.
- Do not delete research, legal text, plans, or user-owned work as part of a documentation-only change.

