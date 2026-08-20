# Agent Configuration Architecture

## Overview

Two distinct layers exist and MUST NOT be conflated:

1. **Product runtime (`eco-agents` crate)** — the Rust agent swarm shipped by this
   repository. It is configured programmatically via `eco_core::Config` and talks
   to Anthropic Claude through `ClaudeClient`. It does NOT read `.omo/` files and
   does NOT route to opencode-zen.
2. **Developer workflow (OMP)** — the interactive coding harness used by humans
   working on this repo. Its configuration lives in `~/.omp/agent/config.yml`
   (auto-loaded by OMP 17.x) and is documented in `.omp/readme.md`.

## OMP Developer Workflow

The pipeline is pinned to two models:

| Role | Model |
|---|---|
| Worker: dev/test/source exploration/research, all subagents | `deepseek/deepseek-v4-pro:high` |
| Solver: solution design, plan, verify | `openai-codex/gpt-5.6-terra:high` |
| Internal title/memory (`tiny`) | `deepseek/deepseek-v4-flash:low` |

Key settings in `~/.omp/agent/config.yml`:

- `modelRoles` — role → provider-pinned selector (table above).
- `retry.fallbackChains` — same-provider fallback only: DeepSeek Pro → Flash;
  GPT Terra → Sol. If the whole chain fails, the request errors out; no
  cross-provider task handoff.
- `modelProviderOrder: [deepseek, openai-codex]`.
- `tools.approvalMode: write`; `advisor.enabled: false`; `prewalk.enabled: false`.
- `task.maxConcurrency: 3`, `task.batch: true`.

Handoff between the two models uses `.omp/handoffs/CURRENT.md` (overwritten at each
milestone). The mandatory template is defined in `~/.omp/agent/AGENTS.md`.

## Rust Integration (`eco-agents`)

`crates/eco-agents/src/bridge.rs` exposes:

- `BridgeResult` — package name, generated server filename, server source,
  README markdown, optional thinking trace.
- `DocBridgeAgent` — generates a production-ready FastMCP 2.0 / rmcp server from
  API signatures using `ClaudeClient`.

```rust
use eco_agents::{BridgeResult, DocBridgeAgent};
use eco_core::Config;

let agent = DocBridgeAgent::new(config);
let result: BridgeResult = agent
    .generate_mcp_bridge("my-package", &api_signatures, 2000)
    .await?;
```

## Verified Commands

```bash
omp --version
omp config list --json
omp models find deepseek-v4-pro --json
omp models find gpt-5.6-terra --json
cargo check --workspace
cargo test -p eco-agents
```

## Security

- No API keys or tokens are stored in YAML or committed files; OMP resolves
  credentials through its own credential store.
- Never write `.env` values, keys, tokens, database sessions, or transcripts into
  prompts or handoff files.
