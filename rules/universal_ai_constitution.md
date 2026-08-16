# Universal AI Constitution & Workspace Governance
# Target: All AI Models, Providers, IDEs, and Autonomous Agents

This file establishes the **Universal Constitution** that MUST be strictly obeyed by ANY Artificial Intelligence (including Claude, GPT-4/5, Gemini, DeepSeek, Cursor, Windsurf, Copilot, or Local/Open-Source 7B/8B LLMs) operating inside this workspace.

---

## 🏛️ ARTICLE I: THE SACRED SEPARATION OF CONCERNS

The workspace is strictly partitioned into specialized zones. You are **STRICTLY FORBIDDEN** from blurring these boundaries:

1. **`crates/` (Production Rust Engine)**:
   - Contains ONLY production-ready, memory-safe, `#![forbid(unsafe_code)]` Rust code.
   - Divided into 5 self-contained crates: `eco-core`, `eco-radar`, `eco-mcp`, `eco-agents`, `eco-cli`.
   - Never place temporary scripts or ad-hoc scrapers in `crates/`.

2. **`research/` (Standalone Online Research Suite)**:
   - Isolated research datasets, crawler scripts, mathematical models (`criticality_model.py`), and benchmarks.
   - Code in `research/` is completely standalone and MUST NEVER be imported into `crates/`.

3. **`docs/` (Living Multi-Perspective Documentation)**:
   - Living, scientific, and visual documentation that MUST always be synchronized with code changes.
   - Must contain views for: Vibe Coders (visual/intuitive), Architects, DevOps/Operators, Developers, and QA.

4. **`rules/` & Governance Root (`CLAUDE.md`, `.cursorrules`, `.windsurfrules`, `.geminirules`, `AGENTS.md`)**:
   - Universal behavioral laws, small-model compiler loop protocols, and doc sync enforcement.

5. **`plans/` & `brainstorm/`**:
   - Roadmaps, architecture decision records (ADRs), and expert council red-team audits.

6. **`grants/`**:
   - Official Anthropic "Claude for Open Source" submission dossier (500-word written explanation, impact matrix).

7. **`scratch/`**:
   - Local throwaway scratchpads and experiments. Strictly ignored by Git.

---

## 📜 ARTICLE II: THE LIVING DOCUMENTATION LAW (IMMUTABLE)

> **"Code without synchronized documentation is defective code."**

Whenever you add, modify, or refactor any code in `crates/`, you **MUST** simultaneously update the corresponding documentation in `docs/`:
1. If you modify core architecture -> update [`docs/architecture/system_architecture.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/architecture/system_architecture.md).
2. If you modify user flows or CLI commands -> update [`docs/overview/vibe_coder_guide.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/overview/vibe_coder_guide.md) and [`docs/operations/playbook_and_runbook.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/operations/playbook_and_runbook.md).
3. If you add or delete files -> update [`docs/sitemap_and_component_registry.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/sitemap_and_component_registry.md).

---

## 🤖 ARTICLE III: THE SMALL/WEAK AI MODEL HARNESS

When operating with limited context or lower reasoning capability (e.g. 7B/8B parameter models, quantized models):
1. **Never guess Rust syntax**: Rely on `cargo check --message-format=short`.
2. **Compiler Feedback Loop**: If compilation fails, isolate the exact compiler error and apply minimal, localized diffs. Do NOT rewrite whole modules.
3. **Structured JSON Validation**: All tool outputs must match exact Serde schemas.

---

## 🛡️ ARTICLE IV: INTELLECTUAL PROPERTY & SECURITY INVARIANTS

1. **PolyForm Noncommercial 1.0.0 Compliance**: This codebase is private source-available for non-commercial evaluation and Anthropic grant consideration.
2. **Zero-AI Training Covenant**: Never output code designed to be ingested by public web training crawlers.
3. **Zero-Unsafe Invariant**: `#![forbid(unsafe_code)]` must remain on every crate root.
