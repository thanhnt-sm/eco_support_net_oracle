> **⚠️ ARCHIVED** — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET). See [README](../../README.md) for current documentation.

[English](contributor_deep_dive.md) | [Tiếng Việt](contributor_deep_dive.vi.md)

# Developer & Contributor Deep-Dive Manual

**Document ID**: `DEV-MANUAL-2026.1`  
**Target Audience**: Rust Engineers, AI Agent Developers, Core Contributors


---

## 🛠️ 1. Crate Architecture & Extensibility

The EcoSupport workspace is structured across 5 focused crates:

```
crates/
├── eco-core/    -> Base configuration, HTTP client, Claude 3.7 Thinking API, Telemetry
├── eco-radar/   -> Ecosystem Criticality Index (ECI) engine, mathematical models, scanners
├── eco-mcp/     -> FastMCP 2.0 / rmcp JSON-RPC protocol parser, server, and static auditor
├── eco-agents/  -> Autonomous subagent swarms (Triage, Patch, Doc Bridge)
└── eco-cli/     -> High-speed CLI binary entrypoint (Clap + Indicatif)
```

---

## 🔌 2. How to Add a New FastMCP Tool

To expose a new native tool to Claude Desktop or Cursor:

1. **Define Input Schema** in [`crates/eco-mcp/src/server.rs`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/crates/eco-mcp/src/server.rs):
```rust
McpTool {
    name: "my_custom_tool".to_string(),
    description: "Performs custom AST validation on niche repos.".to_string(),
    input_schema: json!({
        "type": "object",
        "properties": {
            "target_path": { "type": "string", "description": "Absolute path to inspect" }
        },
        "required": ["target_path"]
    }),
}
```

2. **Implement Tool Handler** inside `handle_tool_call`:
```rust
"my_custom_tool" => {
    let path = args.get("target_path").and_then(|p| p.as_str()).unwrap_or("");
    // Execute async logic
    Ok(json!({ "status": "success", "inspected": path }))
}
```

3. **Add Integration Test** in [`crates/eco-cli/tests/test_rust_mcp.rs`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/crates/eco-cli/tests/test_rust_mcp.rs).

---

## 🦀 3. Rust Invariants & Best Practices

1. **Zero-Unsafe Law**: Always maintain `#![forbid(unsafe_code)]` on crate roots.
2. **Error Propagation**: Use `thiserror::Error` for internal crate errors and `eco_core::Result<T>` across crate boundaries.
3. **Structured Logging**: Use `tracing::info!`, `tracing::warn!`, and `tracing::error!` with structured key-value fields. Avoid raw `println!` in library crates.
