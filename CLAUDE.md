# CLAUDE.md - EcoSupport Project Guidelines (Rust Native Edition)

Welcome to **EcoSupport** — the open-source Autonomous Ecosystem Support Platform built in **Rust** to monitor, protect, and empower high-criticality, low-bandwidth open-source libraries and Model Context Protocol (MCP) connectors.

Targeted for the **Claude for Open Source: Ecosystem Impact Track** (Anthropic).

---

## 🚨 MANDATORY FIRST STEP — READ THIS BEFORE ANYTHING ELSE

**Before making any changes, ALWAYS read the session register:**

```bash
cat plans/ACTIVE_SESSION_REGISTER.md
```

This file is the **Single Source of Truth** for the current project state, task priorities, and workspace layout. Failure to read this file first will result in duplicated work, file conflicts, and violating workspace governance rules.

**Then verify build health:**

```bash
cargo check --workspace   # Must be: 0 errors, 0 warnings
cargo test --workspace    # Must be: all tests pass
```

---

## ⚡ PRE-ACTION INVARIANT HOOKS (DO NOT WAIT FOR GIT/SCRIPT ERRORS)

1. **🛑 Pre-Write Path Hook**: Before creating or moving any file, verify path against approved structure. Never create rogue files at root or random directories. Use `scratch/` for throwaway work.
2. **🔄 In-Flight Co-Update Hook**: In the **exact same turn** you modify code in `crates/`, you MUST update matching docs in `docs/` (both `.md` and `.vi.md`) and `docs/sitemap_and_component_registry.md`.
3. **📋 Real-Time Register Hook**: Before returning your final response, update `plans/ACTIVE_SESSION_REGISTER.md` with your completed work and updated next steps.


---

## 🏗️ Architectural Boundaries & Separation of Concerns

1. **`crates/` (Production Rust Engine)**:
   - Modular Cargo workspace containing strictly typed, memory-safe, high-performance Rust crates:
     - `eco-core`: Configuration, Claude 3.7 API client with Extended Thinking, telemetry.
     - `eco-radar`: Ecosystem Criticality Index (ECI) engine, dependency graph parser.
     - `eco-mcp`: Native FastMCP / `rmcp` protocol servers and static security auditors.
     - `eco-agents`: Autonomous triage and patch synthesis harnesses.
     - `eco-cli`: High-speed terminal interface.
2. **`research/` (Deep Online Research & Analytics Suite)**:
   - Isolated environment for crawler scripts, market surveys, and empirical benchmark datasets.
   - **NEVER import code from `research/` into `crates/`.**
3. **`docs/` (Living Scientific Documentation)**:
   - Architecture Decision Records (ADRs), benchmarks, and technical guides.
   - **Must be updated synchronously whenever `crates/` is modified.**
4. **`plans/` & `brainstorm/`**:
   - Master roadmaps, expert council red-team audits, and strategic notes.
5. **`grants/`**:
   - Official 500-word written explanation, impact matrix, and pitch documents for Anthropic.
6. **`scratch/`**:
   - Temporary throwaway scripts and local scratchpads (strictly gitignored).

> ⚠️ **DO NOT create files or folders outside the structure above.** Violations are blocked by `scripts/anti_garbage_guard.sh` in the pre-commit hook.

---

## ⚡ Development & Verification Commands

```bash
# Set Cargo path
export PATH="$HOME/.cargo/bin:$PATH"

# Build entire Rust workspace
cargo build --workspace

# Run all unit, integration, and MCP test suites
cargo test --workspace

# Strict linting and formatting
cargo clippy --workspace --all-targets -- -D warnings
cargo fmt --check

# Run the CLI
cargo run -p eco-cli -- scan --category c-ffi --limit 5
cargo run -p eco-cli -- triage --repo "owner/repo" --issue 42
cargo run -p eco-cli -- mcp-serve --stdio

# Safe git push (runs full pre-commit chain automatically)
./scripts/git_sync.sh "feat(crate-name): description of change"
```

---

## 🧬 Anthropic & Claude Engineering Standards

1. **Claude 3.7 Sonnet Extended Thinking**:
   - Leverage `thinking={"type": "enabled", "budget_tokens": N}` for deep FFI boundary bug repair and complex triage.
   - Keep thinking budgets configurable per task severity: default 4,096 tokens; complex tasks up to 16,384 tokens.
   - Parse and stream reasoning tokens with zero-overhead telemetry.

2. **Deterministic Compiler-in-the-Loop for Small AI Models**:
   - If an agent generates invalid Rust code, immediately re-feed `cargo check --message-format=short` diagnostics into the prompt loop for automated self-healing.

3. **Model Context Protocol (MCP)**:
   - All tools exposed to Claude must strictly follow FastMCP 2.0 / `rmcp` specs.
   - Tool descriptions must precisely reflect behavior to prevent hallucinated tool calls.

4. **Zero Unsafe Rust**:
   - `#![forbid(unsafe_code)]` across all core modules.

5. **Defensive Programming & Safety**:
   - Never execute untrusted user code or external repository scripts directly without sandboxing.
   - Redact all API keys and tokens from logs via the telemetry module.
   - Strictly follow the principle of least privilege in MCP tool definitions.

---

## 📏 Golden Rules for AI Agents

| Rule | Content |
|:---|:---|
| **RULE 1** | Read `plans/ACTIVE_SESSION_REGISTER.md` FIRST. Always. |
| **RULE 2** | No files/folders outside the approved workspace layout. Temp files → `scratch/`. |
| **RULE 3** | After any `crates/` edit → run `cargo check --workspace`. Fix errors before proceeding. |
| **RULE 4** | After any `crates/` edit → update corresponding `docs/` files. |
| **RULE 5** | Use `./scripts/git_sync.sh "message"` to commit. Never use `git commit` directly. |
| **RULE 6** | `research/` is read-only for Rust code. No imports from `research/` into `crates/`. |
