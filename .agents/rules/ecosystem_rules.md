# 🤖 EcoSupport — Ecosystem Rules for Autonomous AI Agents
### Applicable to: Antigravity IDE, Antigravity CLI, Gemini, Claude, Devin.ai, OpenCode, Oh-My-Pi, Oh-My-OpenAgent, Cursor, Windsurf

> **This file is the authoritative rule set for ALL AI agents operating in this workspace.**
> Read this file AND `plans/ACTIVE_SESSION_REGISTER.md` before executing ANY task.

---

## ⚡ PRE-ACTION INVARIANT HOOKS (INTERCEPT BEFORE ACTING)

> **DO NOT WAIT FOR GIT HOOKS OR SCRIPT FAILURES.** Intercept yourself *before* calling tools.

1. **🛑 Pre-Write Path Hook**: Before invoking any file writing tool (`write_to_file`, `replace_file_content`, etc.), verify that the target path matches the whitelist.
   - If writing a new file at root or in an unapproved folder, **ABORT** tool call and redirect to `scratch/` or appropriate subfolder.
2. **🔄 In-Flight Co-Update Hook**: When altering code in `crates/`, you MUST within the **exact same interaction turn** update:
   - Corresponding docs in `docs/` (both English `.md` and Vietnamese `.vi.md`).
   - [`docs/sitemap_and_component_registry.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/docs/sitemap_and_component_registry.md) and [`docs/sitemap_and_component_registry.vi.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/docs/sitemap_and_component_registry.vi.md).
   - NEVER defer documentation to a later turn or commit time.
3. **📋 Pre-Turn & Post-Turn Register Hook**:
   - **Start of turn**: Read [`plans/ACTIVE_SESSION_REGISTER.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/plans/ACTIVE_SESSION_REGISTER.md).
   - **End of turn**: Update [`plans/ACTIVE_SESSION_REGISTER.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/plans/ACTIVE_SESSION_REGISTER.md) with completed tasks and next steps BEFORE delivering final response to user.

---

## 🚨 SESSION BOOTSTRAP PROTOCOL (Mandatory for Every Session)

**Step 1 — Read the session register (ALWAYS FIRST):**
```bash
cat plans/ACTIVE_SESSION_REGISTER.md
```

**Step 2 — Verify build health before touching any code:**
```bash
cargo check --workspace   # Must exit 0 with 0 errors, 0 warnings
cargo test --workspace    # Must exit 0 with all tests passing
```

**Step 3 — After any code change, sync docs and commit safely:**
```bash
cargo check --workspace
./scripts/verify_docs_sync.sh
./scripts/git_sync.sh "type(scope): description"
```

> ⚠️ **NEVER skip Step 1.** Working without session context creates duplicated files, conflicts, and violates workspace governance.


---

## 🗺️ Workspace Layout — Approved File/Folder Map

The following is the **canonical, immutable workspace layout**. AI agents MUST NOT create files or folders outside this structure.

```
eco_support/                   ← ROOT
├── Cargo.toml                 ← Workspace manifest (NEVER edit directly)
├── CLAUDE.md                  ← Claude agent instructions
├── AGENTS.md                  ← Multi-agent swarm specification
├── CONTRIBUTING.md
├── README.md
├── SECURITY.md
├── LICENSE / LICENSE.md       ← PolyForm Noncommercial + Anti-AI Training
├── robots.txt                 ← Block AI crawlers
├── pyproject.toml             ← Python research environment
├── .env.example               ← Environment variable template
│
├── crates/                    ← 🦀 RUST PRODUCTION CODE — ONLY Rust here
│   ├── eco-core/              ← Config, Claude 3.7 client, telemetry
│   ├── eco-radar/             ← ECI calculator, registry scanner
│   ├── eco-mcp/               ← FastMCP 2.0 server & security auditor
│   ├── eco-agents/            ← Triage, patch synthesis, MCP bridge
│   └── eco-cli/               ← CLI binary + integration tests
│
├── docs/                      ← 📚 LIVING DOCS (must sync with crates/)
│   ├── overview/
│   ├── architecture/
│   ├── operations/
│   ├── testing/
│   ├── developers/
│   └── sitemap_and_component_registry.md
│
├── rules/                     ← 🤖 AI GOVERNANCE (do not delete or modify without approval)
├── plans/                     ← 📅 Session tracking — read ACTIVE_SESSION_REGISTER.md first
├── brainstorm/                ← 🧠 Strategy docs (read-only)
├── research/                  ← 🔬 Isolated research (NEVER import into crates/)
├── grants/                    ← 🏆 Anthropic grant application
├── scripts/                   ← ⚙️ Automation tools
├── .github/                   ← GitHub Actions workflows
├── .githooks/                 ← Pre-commit, pre-push hooks
├── .agents/                   ← THIS FILE and other agent rules
└── scratch/                   ← 🗑️ THROWAWAY ONLY (gitignored)
```

**FORBIDDEN: Creating ANY of the following:**
- `tmp/`, `temp/`, `output/`, `out/`, `build/` (other than `target/`)
- Random `.md` files at root level not in the approved list
- New crates outside `crates/`
- Python source files in `crates/`
- Any file with `.bak`, `.orig`, `.tmp` extension

---

## 1. Niche Ecosystem Prioritization Invariants

When scanning and prioritizing open-source repositories to support:
- **Priority Tier 1 (Critical Infrastructure)**: Repositories with > 50 downstream dependencies, single maintainer (or last commit > 90 days), and unresolved high-severity bugs.
- **Priority Tier 2 (MCP Protocol Gaps)**: Essential tools/data sources lacking Model Context Protocol support or having stale MCP schemas (< 2026 specs).
- **Priority Tier 3 (Cross-Language FFI & Niche Compilers)**: Low-level bindings (C/Rust/Wasm to Python/Node) crucial for modern AI pipelines.

---

## 2. Code Quality & Formatting Rules (Rust)

- **Zero Unsafe Code**: `#![forbid(unsafe_code)]` is enforced across all crates. Never add `unsafe` blocks.
- **Edition**: Rust Edition 2021 across all crates.
- **Format**: Always run `cargo fmt` before committing.
- **Lint**: `cargo clippy --workspace --all-targets -- -D warnings` must pass with 0 warnings.
- **Tests**: All test additions go in `crates/eco-cli/tests/` (integration) or `#[cfg(test)]` modules (unit).
- **No Python in crates/**: `crates/` is 100% Rust. Python code belongs in `research/` ONLY. `src/` is a legacy artifact being phased out — do NOT add new files there.

---

## 3. Anthropic Claude 3.7 Thinking Standards

When calling the Claude API for triage, patch synthesis, or security audit tasks:

```rust
// eco-core client call with Extended Thinking
let response = client.messages()
    .model("claude-3-7-sonnet-20250219")
    .max_tokens(20000)
    .thinking(ThinkingConfig {
        r#type: "enabled".to_string(),
        budget_tokens: thinking_budget, // 4096–16384
    })
    .messages(messages)
    .send()
    .await?;
```

- Parse and log reasoning blocks securely for auditability.
- Default thinking budget: 4,096 tokens. Complex tasks: 8,192–16,384 tokens.
- Always use offline simulation mode when `ANTHROPIC_API_KEY` is not set.

---

## 4. Git & Commit Protocol

```bash
# ALWAYS use this — never raw git commit
./scripts/git_sync.sh "type(scope): imperative description"

# Commit types:
# feat(crate-name): new feature
# fix(crate-name): bug fix
# docs(section): documentation update
# chore(governance): rule/config change
# test(crate-name): test additions
```

The `git_sync.sh` script runs the full pre-commit chain:
`cargo fmt` → `anti_garbage_guard` → `verify_docs_sync` → `git commit` → `git push`

---

## 5. Documentation Sync Rules

After **ANY** modification to `crates/`:
1. Update the corresponding section in `docs/`
2. Update `docs/sitemap_and_component_registry.md` if new files were added
3. Run `./scripts/verify_docs_sync.sh` — must pass 16/16 checks

---

## 6. Antigravity IDE / CLI Specific Rules

When operating through **Antigravity IDE** (`agy` CLI, conversation ID available in context):

### 6.1 — File Discipline
- **Temporary files**: Use `scratch/` (gitignored) for any throwaway files during the session. NEVER create temp files at the workspace root.
- **Never create `.gemini/` or `.agy/` folders inside the workspace** — these belong in the global app data directory.
- **Artifact writes**: Plan artifacts (`implementation_plan.md`, `task.md`, `walkthrough.md`) live in the AGY brain directory, NOT in the workspace.

### 6.2 — Session Bootstrap in Antigravity Context
```bash
# Step 1: Read session register (MANDATORY)
cat plans/ACTIVE_SESSION_REGISTER.md

# Step 2: Build health verification
export PATH="$HOME/.cargo/bin:$PATH"
cargo check --workspace   # 0 errors, 0 warnings
cargo test --workspace    # all pass

# Step 3: Pre-task docs sync verification
./scripts/verify_docs_sync.sh   # 16/16
```

### 6.3 — Planning Mode Protocol
- For **complex multi-file changes**, always create an `implementation_plan.md` artifact and wait for user approval before executing.
- For **trivial single-file fixes**, proceed directly.
- After completing work, update `plans/ACTIVE_SESSION_REGISTER.md` and run `./scripts/git_sync.sh`.

### 6.4 — Knowledge Items (KI) Awareness
- Antigravity IDE maintains a KI system at `~/.gemini/antigravity-ide/knowledge/`.
- Before deep-diving into any unknown subsystem (e.g., ECI algorithm, Claude API integration), check if a KI exists.
- After completing significant architectural work, consider suggesting a `/learn` command to persist patterns.

### 6.5 — Recommended Slash Commands
| Situation | Command |
|:---|:---|
| Long-running overnight task (e.g., GitHub API integration) | `/goal` |
| Align on design before big refactor | `/grill-me` |
| Persist a solved setup (e.g., Cargo auth, env config) | `/learn` |
| Schedule periodic ecosystem scans | `/schedule` |

### 6.6 — Tool Priority Order
1. `run_command` → verify build/test correctness
2. `view_file` / `grep_search` → understand code before editing
3. `multi_replace_file_content` → surgical edits (non-contiguous)
4. `replace_file_content` → single contiguous block replacement
5. `write_to_file` (Overwrite=true) → only when rewriting entire file

---

## 7. Safety & Security Invariants

1. **No secrets in source**: API keys, tokens, and credentials MUST use `.env` (gitignored). Never hardcode.
2. **No network calls in unit tests**: Tests must be deterministic and offline. Use `#[cfg(feature = "integration")]` for live API tests.
3. **No `eval()` or dynamic imports** of unpinned packages in any generated code.
4. **No arbitrary shell execution** from agent-generated patches.
5. **Supply chain**: All dependencies must be pinned in `Cargo.lock`. Never add `*` version constraints.
6. **Feature-gating live tests**: Any test that calls GitHub API or Anthropic API MUST be behind `#[cfg(feature = "integration")]` and excluded from `cargo test --workspace` default runs.
