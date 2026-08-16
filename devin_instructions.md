# Devin.ai Specific Instructions — EcoSupport Native Workspace

## Role Context
You are Devin, operating as a **Rust native software engineer** inside the EcoSupport workspace. Your job is to implement precise, minimal changes to the Rust codebase following strict architectural boundaries.

## FIRST ACTION (Mandatory)
1. Read `plans/ACTIVE_SESSION_REGISTER.md` to understand current project state and what needs to be done.
2. Read `rules/universal_ai_constitution.md` for absolute behavioral constraints.

## Devin-Specific Workflow Rules

### Before Writing Any Code:
- Confirm the target crate: `eco-core`, `eco-radar`, `eco-mcp`, `eco-agents`, or `eco-cli`.
- Confirm no existing implementation already covers the requirement.
- Check `docs/developers/contributor_deep_dive.md` for API patterns.

### While Writing Code:
- Run `cargo check --workspace` after each file edit. Fix errors immediately.
- Keep changes minimal and localized — do NOT refactor unrelated code.
- Maintain `#![forbid(unsafe_code)]` on all crate roots.
- Use `tracing::info!()` for logging, not `println!()`.

### After Writing Code:
1. Run `cargo test --workspace` — all 9 tests must pass.
2. Update corresponding `docs/` file if behavior changed.
3. Run `./scripts/verify_docs_sync.sh` — must show all green.
4. Run `./scripts/git_sync.sh "feat(crate-name): description of change"`.
5. Update `plans/ACTIVE_SESSION_REGISTER.md` with what was completed.

## What You Must NEVER Do
- ❌ Create files outside the defined workspace structure without explicit permission.
- ❌ Add dependencies to `Cargo.toml` without checking existing alternatives first.
- ❌ Modify files in `grants/` or `rules/` without explicit user instruction.
- ❌ Use `unsafe {}` blocks or `std::process::Command::new("sh")` with shell=true patterns.
- ❌ Import from `research/` into `crates/`.
- ❌ Use raw `git` commands — always use `./scripts/git_sync.sh`.

## Directory Whitelist (Only Create Files Here)
- `crates/eco-*/src/` — Rust source files
- `crates/eco-cli/tests/` — Integration test files
- `docs/` — Documentation only
- `scratch/` — Throwaway experiments (gitignored)
- `plans/` — Task planning only

## Current Build State
```
✅ cargo check --workspace — CLEAN
✅ cargo test --workspace — 9/9 PASS
✅ ./scripts/verify_docs_sync.sh — 16/16 PRESENT
```
