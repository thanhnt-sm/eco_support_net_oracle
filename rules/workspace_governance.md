# Workspace Governance & Structural Invariants

This document establishes the **immutable laws of the EcoSupport workspace**. Every AI agent, subagent, developer, and automated pipeline **MUST** strictly adhere to these boundaries.

---

## 🏛️ Absolute Separation of Concerns (Directory Rules)

```
eco_support/
├── crates/             # 🦀 PRODUCTION RUST SOURCE CODE ONLY
│   ├── eco-core/       # Core types, Claude 3.7 API client, telemetry
│   ├── eco-radar/      # Ecosystem Criticality Index (ECI) & registry scanners
│   ├── eco-mcp/        # Native rmcp / FastMCP servers & security auditor
│   ├── eco-agents/     # Triage & Patch Synthesizer agent harnesses
│   └── eco-cli/        # High-speed terminal CLI entrypoint
├── research/           # 🔬 STANDALONE DEEP ONLINE RESEARCH SUITE
│   ├── niche_survey/   # Quantitative ecosystem analysis & crawler scripts
│   ├── benchmarks/     # Empirical evaluation datasets & speed tests
│   └── data/           # Raw JSON/CSV seed registries and data artifacts
├── docs/               # 📚 LIVING SCIENTIFIC DOCUMENTATION
│   ├── architecture/   # System Architecture, Tech Evaluations, ADRs
│   └── guides/         # Developer guides, API references
├── plans/              # 📋 EXECUTION BLUEPRINTS & ROADMAPS
├── brainstorm/         # 🧠 RED-TEAMING & STRATEGIC PLANNING
├── grants/             # 🏆 ANTHROPIC CLAUDE FOR OPEN SOURCE DOSSIER
├── scratch/            # 🗑️ THROWAWAY SCRATCHPADS (STRICTLY GITIGNORED)
├── scripts/            # ⚙️ DEV, GIT AUTOMATION & CI/CD UTILITIES
├── .agents/ & rules/   # 🤖 CONSTITUTIONAL AGENT RULES & HARNESSES
└── .githooks/          # 🪝 AUTOMATED PRE-COMMIT & PRE-PUSH VALIDATORS
```

---

## 🚫 Invariant Prohibitions & Violations

1. **NO Mixing Research into Production**:
   - Files in `research/` must NEVER be imported into `crates/`. Production crates must remain self-contained, typed, and dependency-minimal.
2. **NO Unchecked Scratch Code**:
   - Any throwaway test scripts or experimental prototypes MUST be placed in `scratch/`. `scratch/` is ignored by Git and will never be committed.
3. **Living Documentation Law**:
   - Any architectural modification to `crates/` MUST be reflected immediately in `docs/architecture/` and `CLAUDE.md`.
4. **Zero-Unsafe Rust Invariant**:
   - `#![forbid(unsafe_code)]` is enforced across all production crates unless specifically required for FFI interoperability and gated behind explicit module boundaries.
5. **Deterministic Schema Law**:
   - All MCP tool inputs and outputs must be strongly typed with `serde::Serialize` and `serde::Deserialize` with comprehensive field descriptions.
