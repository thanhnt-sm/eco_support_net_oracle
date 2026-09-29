# Phase 04 — Docs, changelog, plan hygiene

## Overview
Priority: Medium · Status: done

## Requirements
- README "IDE support" bullet for Visual Studio: mention per-solution consent + ide-safe mode, no auto-install.
- `docs/USAGE.md` (or `docs/cli.md`): document `--ide-safe` semantics and exit 2 rejections.
- `SECURITY.md` security posture: add "IDE hosts run the CLI in `--ide-safe` mode; Visual Studio requires per-solution consent".
- `docs/project-changelog.md` if present (else `docs/05-operations` changelog equivalent — check): entry for this change set.
- Move root `FIX_DATAGUARDVISUALSTUDIO_BUILD_PLAN.md` into this plan dir as `superseded-fix-vs-build-plan.md`.
- Run `./scripts/verify_docs_sync.sh` (presence check).

## Todo
- [x] README  - [x] cli/usage doc  - [x] SECURITY.md  - [x] changelog  - [x] move plan file  - [x] verify script

## Success criteria
Docs describe actual shipped behaviour (verified against code), links intact.
