# Agent Configuration Architecture

## Overview

This document describes the agent configuration system for EcoSupport Native using opencode zen free models. The configuration enables task-routed model selection with automatic fallback to local models.

## Files

### `.omo/agents.toml` — Agent Registry

Defines all available agents (models) with their properties:

| Field | Description |
|-------|-------------|
| `name` | Unique agent identifier |
| `provider` | Provider: `opencode-zen` or `ollama` |
| `model` | Model identifier (e.g., `nvidia/nemotron-3-ultra`) |
| `tags` | Task categories this agent excels at |
| `context_window` | Maximum context tokens |
| `max_output` | Maximum output tokens |
| `temperature` | Sampling temperature |
| `top_p` | Nucleus sampling parameter |
| `endpoint` | API endpoint URL |

**Free Models Configured:**
- `nemotron-3-ultra-free` — NVIDIA Nemotron 3 Ultra (reasoning, planning)
- `llama-3.1-405b-free` — Meta Llama 3.1 405B (coding, implementation)
- `qwen2.5-coder-32b-free` — Qwen 2.5 Coder 32B (quick tasks)
- `deepseek-v3-free` — DeepSeek V3 (general, multilingual)
- `gemini-2.0-flash-free` — Google Gemini 2.0 Flash (fast, multimodal)

**Local Fallback Models (Ollama):**
- `ollama-nemotron3-8b` — Nemotron 3 Ultra 8B local
- `ollama-qwen2.5-coder-32b` — Qwen 2.5 Coder 32B local
- `ollama-llama3.1-70b` — Llama 3.1 70B local

### `.omo/config.toml` — Routing & Limits

Contains routing rules, limits, and i18n settings.

**Routing (`[omp.routing]`):**
Maps task types to agent names:
```
reasoning → nemotron-3-ultra-free
planning → nemotron-3-ultra-free
architecture → nemotron-3-ultra-free
coding → llama-3.1-405b-free
implementation → llama-3.1-405b-free
debug → llama-3.1-405b-free
review → nemotron-3-ultra-free
quick → qwen2.5-coder-32b-free
general → deepseek-v3-free
multilingual → deepseek-v3-free
fast → gemini-2.0-flash-free
```

**Fallback Chain (`[omp.routing.fallback_chain]`):**
```
primary → nemotron-3-ultra-free
secondary → deepseek-v3-free
tertiary → ollama-nemotron3-8b
```

**Limits (`[omp.limits]`):**
Per-task-type agent spawn limits and token budgets.

**i18n (`[omp.i18n]`):**
```
supported_languages = ["vi", "en", "ja", "ko"]
default_language = "vi"
fallback_to_english = true
```

## Rust Integration

### `OpencodeZenBridgeAgent` (in `crates/eco-agents/src/bridge.rs`)

```rust
use eco_agents::{OpencodeZenBridgeAgent, OpencodeZenConfig};

let agent = OpencodeZenBridgeAgent::new()?;
let model = agent.route_task("coding"); // Returns "llama-3.1-405b-free"
let result = agent.generate_with_model("coding", "Write a Rust function...", 2000).await?;
```

**Methods:**
- `new()` — Loads config from `.omo/agents.toml` and `.omo/config.toml`
- `route_task(task_type)` — Returns agent name for task type
- `fallback_agent(attempt)` — Returns fallback agent at index
- `generate_with_model(task_type, prompt, thinking_budget)` — Generates with routed model + fallback chain

### Error Handling

```rust
pub enum OpencodeZenError {
    NoProviderAvailable,
    ModelNotFound(String),
    RoutingError(String),
    ConfigReadError(String),
}
```

## Usage Examples

### CLI
```bash
# Validate config
omp config validate

# List agents
omp agent list

# Route a task
omp agent route coding
```

### Rust
```rust
use eco_agents::OpencodeZenBridgeAgent;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let agent = OpencodeZenBridgeAgent::new()?;
    
    // Route task to best model
    let model = agent.route_task("architecture");
    println!("Architecture task → {}", model);
    
    // Generate with automatic fallback
    let response = agent.generate_with_model(
        "debug",
        "Fix this Rust borrow checker error...",
        4096
    ).await?;
    
    println!("{}", response);
    Ok(())
}
```

## Adding New Models

1. Add `[[agents]]` entry to `.omo/agents.toml`
2. Add routing rule to `[omp.routing]` in `.omo/config.toml`
3. Add limits to `[omp.limits]` if needed
4. Add language tags to `[omp.i18n.model_language_tags]` if multilingual
5. Run `cargo check --workspace`
6. Run `./scripts/verify_docs_sync.sh`

## Security

- `restricted_models` blocks proprietary models (GPT-4, etc.)
- `enable_model_quota` enforces rate limits
- API keys via environment variable `OPENCODE_ZEN_API_KEY`