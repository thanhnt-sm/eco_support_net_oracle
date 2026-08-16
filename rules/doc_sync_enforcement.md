# Living Documentation & Continuous Synchronization Standard

**Rule ID**: `DOC-SYNC-INVARIANT-001`  
**Enforcement**: Pre-commit Git Hook + Universal AI Agent Operational Check

---

## 🎯 The Core Philosophy: "Documentation is Part of the AST"

In EcoSupport, documentation is not an afterthought; it is an integral, living representation of the codebase. A PR, commit, or AI code generation turn that modifies code behavior without updating documentation is considered a breaking change and will fail automated verification.

---

## 👥 The 5-Perspective Documentation Matrix

Every component in EcoSupport must be documented from **5 distinct perspectives**:

| Perspective | Target Audience | Key Artifact | Requirements |
| :--- | :--- | :--- | :--- |
| **1. Vibe Coder / Founder** | Non-technical creators, intuitive users | [`docs/overview/vibe_coder_guide.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/overview/vibe_coder_guide.md) | Rich Mermaid flowcharts, real-world metaphors, mindmaps, zero jargon, clear visual diagrams. |
| **2. System Architect** | Senior engineers, reviewers | [`docs/architecture/system_architecture.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/architecture/system_architecture.md) | Topology diagrams, data flow pipelines, state machines, latency/memory benchmarks. |
| **3. SRE & Operator** | DevOps, maintainers, operators | [`docs/operations/playbook_and_runbook.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/operations/playbook_and_runbook.md) | Step-by-step runbooks, incident triage drills, env configs, disaster recovery scripts. |
| **4. Developer & Contributor** | Rust / AI engineers | [`docs/developers/contributor_deep_dive.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/developers/contributor_deep_dive.md) | Crate APIs, serialization schemas, Tokio async loops, AST query patterns. |
| **5. QA & Test Engineer** | Reviewers, grant evaluators | [`docs/testing/qa_test_strategy.md`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/docs/testing/qa_test_strategy.md) | Test coverage matrices, fuzzing strategies, simulation fallbacks, regression proofs. |

---

## 🌐 Bilingual Documentation Invariant (`DOC-SYNC-BILINGUAL-002`)

All user-facing documentation, README files, and guides MUST maintain **two synchronized editions**:
1. **English (Default)**: e.g., `README.md`, `docs/overview/vibe_coder_guide.md`
2. **Vietnamese (`.vi.md`)**: e.g., `README.vi.md`, `docs/overview/vibe_coder_guide.vi.md`

Each document must provide a reciprocal language navigation link at the top:
`[English](README.md) | [Tiếng Việt](README.vi.md)`

---

## 🔄 Automated Synchronization Protocol for AI Agents

Whenever any AI model modifies code:
1. **Detect Changes**: Check which crates or tools were altered (`eco-core`, `eco-radar`, `eco-mcp`, `eco-agents`, `eco-cli`).
2. **Synchronize Registry**: Add/update file entries in [`docs/sitemap_and_component_registry.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/docs/sitemap_and_component_registry.md) and [`docs/sitemap_and_component_registry.vi.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/docs/sitemap_and_component_registry.vi.md).
3. **Update Diagrams & Text in Both Languages**: Ensure both the English original and Vietnamese `.vi.md` translations reflect identical technical facts and Mermaid structures.
4. **Validate**: Run `./scripts/verify_docs_sync.sh` before finalizing.

