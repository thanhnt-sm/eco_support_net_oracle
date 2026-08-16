# Operational Protocol for Small, Constrained & Local AI Models

**Target Audience**: 7B/8B Parameter LLMs, Quantized Models, Constrained-Context Runtimes, and Automated Agent Harnesses.

---

## 🎯 The Core Problem & Philosophy
Smaller AI models often suffer from context degradation, syntax hallucination, and loss of instruction adherence over multi-turn interactions. This repository provides a **deterministic compiler-in-the-loop harness** that allows small models to produce code with the same precision and reliability as frontier models (Claude 3.7 Sonnet, Opus, GPT-4.5).

---

## 🛠️ Step-by-Step Execution Protocol for Small Models

### Phase 1: Micro-Context Targeting (Read Only What is Needed)
- **Rule**: Never ingest entire directories into prompt context.
- Target specific crate files: e.g. `crates/eco-core/src/lib.rs`.
- Read only public signatures and type definitions before implementing logic.

### Phase 2: Schema-Driven Structured Output
- When generating MCP tool outputs or radar candidate evaluations, ALWAYS use strict JSON matching the Serde schema:
```json
{
  "repo": "owner/repo",
  "eci_score": 75.4,
  "risk_tier": "TIER_1_CRITICAL_EMERGENCY",
  "reasoning_steps": [
    "Step 1: Analyzed 4200 downstream dependents",
    "Step 2: Identified single maintainer inactivity for 120 days",
    "Step 3: Confirmed missing FastMCP 2.0 connector"
  ]
}
```

### Phase 3: Self-Healing Compiler Feedback Loop
When writing or modifying Rust code, small models must execute the following automated validation pipeline:

```
                  ┌───────────────────────────────┐
                  │   Small AI Generates Code     │
                  └──────────────┬────────────────┘
                                 │
                                 ▼
                  ┌───────────────────────────────┐
                  │    Execute `cargo check`      │
                  └──────────────┬────────────────┘
                                 │
                   ┌─────────────┴─────────────┐
                   │                           │
          [Compile Errors]              [Compile Success]
                   │                           │
                   ▼                           ▼
    ┌─────────────────────────────┐ ┌──────────────────────┐
    │  Extract JSON Error Message │ │ Execute `cargo test` │
    │  & Re-feed to Agent Loop    │ └──────────────────────┘
    └──────────────┬──────────────┘
                   │
                   └─────────► (Auto-Fix & Retry, max 3 loops)
```

1. Run check: `cargo check --workspace --message-format=short`
2. If errors occur:
   - Isolate the line number and compiler diagnostic.
   - Do NOT rewrite unrelated modules.
   - Fix only the specific type mismatch or missing lifetime.
3. Run lint: `cargo clippy --workspace -- -D warnings`
4. Run tests: `cargo test --workspace`

---

## 🔒 Invariant Safety Check for Weak Models
- If uncertain about a lifetime `'a` or async trait signature, use `Box<dyn Error + Send + Sync>` and standard Tokio primitives.
- Never use `unsafe { ... }`.
- Never introduce unvetted external crates to `Cargo.toml`.
