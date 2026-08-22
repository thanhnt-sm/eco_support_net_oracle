> **⚠️ ARCHIVED** — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET). See [README](../../README.md) for current documentation.

[English](sitemap_and_component_registry.md) | [Tiếng Việt](sitemap_and_component_registry.vi.md)

# Workspace Sitemap & Living Component Registry

**Document ID**: `REGISTRY-SITEMAP-2026.1`  
**Purpose**: Exhaustive inventory, component descriptions, and navigation sitemap for all files in the EcoSupport workspace.

---

## 🗺️ Master Directory Map & File Inventory

| Path / File | Purpose & Responsibilities | Layer | Primary Language |
| :--- | :--- | :---: | :---: |
| **`Cargo.toml`** | Root Cargo workspace manifest configuring 5 crates, optimization profiles (LTO, strip), and shared dependencies. | Root Config | TOML |
| **`CLAUDE.md`** | Master operational guide for Claude Code, Claude Desktop, and Anthropic API agent loops. | Governance | Markdown |
| **`AGENTS.md`** | Multi-agent swarm specification and constitutional safety guardrails. | Governance | Markdown |
| **`.cursorrules`** | Cursor IDE AI rules and coding standards. | Governance | Markdown |
| **`.windsurfrules`** | Windsurf IDE Cascade rules. | Governance | Markdown |
| **`.geminirules`** | Google Gemini Code Assist rules. | Governance | Markdown |
| **`LICENSE.md`** | PolyForm Noncommercial 1.0.0 license with anti-AI training covenant. | Legal / IP | Markdown |
| **`README.md`** / **`README.vi.md`** | Master project landing overview, architecture & quickstart (Bilingual EN/VI). | Documentation | Markdown |
| **`CONTRIBUTING.md`** / **`CONTRIBUTING.vi.md`** | Contributor guide & maintainer-first principles (Bilingual EN/VI). | Documentation | Markdown |
| **`SECURITY.md`** / **`SECURITY.vi.md`** | Security disclosure policy and response SLAs (Bilingual EN/VI). | Security | Markdown |
| **`robots.txt`** | Web scraper blocking directives for AI crawlers. | Security | Text |
| **`.gitattributes`** | Line ending normalization and secret export-ignore directives. | Git / SCM | SCM |
| **`.gitignore`** | Comprehensive ignore rules for Rust targets, Python venvs, and scratchpads. | Git / SCM | SCM |
| **`rules/universal_ai_constitution.md`** | Universal constitution binding all AI models and providers. | Governance | Markdown |
| **`rules/workspace_governance.md`** | Immutable rules on folder segregation and architectural boundaries. | Governance | Markdown |
| **`rules/doc_sync_enforcement.md`** | Continuous documentation & bilingual synchronization standard. | Governance | Markdown |
| **`.agentrules`** | Universal agent rules for Devin.ai, OpenCode, Oh-My-Pi, and generic AI agents. | Governance | Markdown |
| **`devin_instructions.md`** | Devin.ai-specific detailed workflow rules with step-by-step coding protocol. | Governance | Markdown |
| **`plans/ACTIVE_SESSION_REGISTER.md`** | **Single Source of Truth** cross-session register. Every AI agent reads this first before any action. | Governance | Markdown |
| **`rules/small_model_operational_protocol.md`** | Deterministic compiler-in-the-loop harness for small/weak AI models. | Governance | Markdown |
| **`crates/eco-core/`** | Core configuration, Anthropic Claude 3.7 API client, telemetry, token accounting. | Production Core | Rust |
| **`crates/eco-radar/`** | Ecosystem Criticality Index (ECI) engine, mathematical models, registry scanners. | Production Radar | Rust |
| **`crates/eco-mcp/`** | FastMCP 2.0 / `rmcp` server, tool handlers, and static security auditor. | Production MCP | Rust |
| **`crates/eco-agents/`** | Autonomous triage agent, patch synthesizer, and doc bridge agent. | Production Agents | Rust |
| **`crates/eco-cli/`** | Terminal CLI entrypoint with Clap, Indicatif, and Colored output. | Production CLI | Rust |
| **`crates/eco-cli/tests/`** | Comprehensive integration tests verifying all 5 crates. | QA / Testing | Rust |
| **`docs/overview/vibe_coder_guide.md`** / **`.vi.md`** | Intuitive visual guide for vibe coders (flowcharts, mindmaps, metaphors). | Documentation | Markdown |
| **`docs/architecture/system_architecture.md`** / **`.vi.md`** | Formal system architecture blueprints with 6 Mermaid diagrams. | Documentation | Markdown |
| **`docs/architecture/tech_stack_evaluation.md`** / **`.vi.md`** | Comparative language benchmark (Rust vs Zig vs Go vs C++ vs Mojo). | Documentation | Markdown |
| **`docs/architecture/agent-config.md`** / **`.vi.md`** | opencode zen free model agent configuration, routing rules, Rust integration. | Documentation | Markdown |
| **`docs/operations/playbook_and_runbook.md`** / **`.vi.md`** | SRE runbook, incident troubleshooting matrix, disaster recovery. | Documentation | Markdown |
| **`docs/testing/qa_test_strategy.md`** / **`.vi.md`** | QA testing philosophy, test pyramid, verification commands. | Documentation | Markdown |
| **`docs/developers/contributor_deep_dive.md`** / **`.vi.md`** | Developer manual, crate APIs, adding new MCP tools. | Documentation | Markdown |
| **`docs/sitemap_and_component_registry.md`** / **`.vi.md`** | Master inventory and sitemap document (Bilingual EN/VI). | Documentation | Markdown |
| **`brainstorm/expert_council_redteam.md`** | 5-perspective expert council stress-testing the grant strategy. | Strategy | Markdown |
| **`brainstorm/product_vision_and_niche_strategy.md`** | Product definition, unfair advantages, and roadmap to grant win. | Strategy | Markdown |
| **`grants/written_explanation.md`** | Official 412-word written explanation for Anthropic Ecosystem Impact Track. | Grants | Markdown |
| **`grants/ecosystem_impact_matrix.md`** | Quantitative matrix of targeted niche sectors and compute ROI. | Grants | Markdown |
| **`grants/grant_pitch.md`** | Executive pitch statement for the Anthropic review committee. | Grants | Markdown |
| **`grants/SUBMISSION_CHECKLIST.md`** | Official pre-submission verification checklist for Anthropic Grant application. | Grants | Markdown |
| **`research/niche_ecosystem_survey/`** | 2026 ecosystem vulnerability survey report and standalone mathematical model. | Research | Markdown/Python |
| **`research/python_prototype/`** | Full Python prototype of EcoSupport (core, agents, radar, MCP, CLI) for rapid validation. | Research | Python |
| **`research/python_prototype/core/`** | Config, Anthropic client, telemetry, exceptions — mirrors eco-core crate. | Research | Python |
| **`research/python_prototype/agents/`** | Triage, Patch Synthesizer, Doc Bridge agents — mirrors eco-agents crate. | Research | Python |
| **`research/python_prototype/radar/`** | Niche scanner, health analyzer, data models — mirrors eco-radar crate. | Research | Python |
| **`research/python_prototype/mcp/`** | FastMCP server, security auditor, ecosystem tools — mirrors eco-mcp crate. | Research | Python |
| **`research/python_prototype/cli/`** | CLI entrypoint using Typer/Rich — mirrors eco-cli crate. | Research | Python |
| **`research/benchmarks/`** | Empirical benchmark scripts evaluating Claude 3.7 Extended Thinking. | Research | Python |
| **`research/data/`** | Seed registry JSON dataset of fragile open-source dependencies. | Research | JSON |
| **`scripts/git_sync.sh`** | 1-click automated git staging, formatting, committing, and fast-pushing. | Tooling | Bash |
| **`scripts/git_conflict_resolver.sh`** | Automated git 3-way conflict diagnosis and resolution helper. | Tooling | Bash |
| **`scripts/verify_docs_sync.sh`** | Automated verification script ensuring docs remain in sync with code. | Tooling | Bash |
| **`scripts/anti_garbage_guard.sh`** | **Anti-Garbage Guard** — blocks any Git commit staging files outside the allowed whitelist zones. | Tooling | Bash |
| **`scripts/preflight_agent_check.sh`** | **Pre-Flight Invariant Checker** — instant in-flight health and root cleanliness check for AI agents. | Tooling | Bash |
| **`scripts/demo_scan.sh`** | **Live CLI Demo Script** — runs end-to-end multi-category scanning demo for recording and review. | Tooling | Bash |
| **`.githooks/pre-commit`** | Git hook running `cargo fmt` and `ruff` before any commit. | Tooling | Bash |
| **`.githooks/pre-push`** | Git hook running `cargo check` before any push. | Tooling | Bash |

