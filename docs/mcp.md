> **⚠️ ARCHIVED** — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET). See [README](../../README.md) for current documentation.

# MCP Server Documentation

## Overview

The EcoSupport MCP (Model Context Protocol) server exposes 5 tools for ecosystem analysis, repository diagnosis, issue triage, MCP bridge generation, and security auditing.

## Available Tools

| Tool | Description | Read-Only | Mutating |
|------|-------------|-----------|----------|
| `scan_niche_ecosystem` | Scan niche ecosystems | ✅ | ❌ |
| `diagnose_repo_bottleneck` | Deep repository analysis | ✅ | ❌ |
| `triage_issue` | Automated issue triage | ❌ | ✅ (labels, assignees) |
| `synthesize_mcp_bridge` | Generate MCP server code | ✅ | ❌ |
| `audit_mcp_security` | Security audit MCP servers | ✅ | ❌ |

## Tool Schemas

### `scan_niche_ecosystem`

Scan niche ecosystems for critical infrastructure.

**Input:**
```json
{
  "category": "C-FFI" | "C-RTOS" | "C-WASM" | "C-EMBEDDED" | "RUST-CRATES" | "GO-MODULES",
  "limit": 10,
  "force_refresh": false,
  "detailed": false
}
```

**Output:** Structured scan results with niches, ECI scores, and tier classifications.

---

### `diagnose_repo_bottleneck`

Deep analysis of a GitHub repository to identify bottlenecks.

**Input:**
```json
{
  "repo": "owner/name",
  "issue_ref": 123 | { "title": "string", "body": "string" },
  "thinking_budget": 8000
}
```

**Output:** Root causes, recommendations, evidence, and severity assessment.

---

### `triage_issue` ⚠️ MUTATING

Automated issue triage with severity, labels, assignees, and patch plan.

**Input:**
```json
{
  "repo": "owner/name",
  "issue_id": 123,
  "title": "optional title",
  "body": "optional body",
  "thinking_budget": 8000
}
```

**Output:** Severity, labels, assignees, analysis, and patch plan.

> **Warning:** This tool can modify GitHub issues (apply labels, assign users). Ensure proper permissions.

---

### `synthesize_mcp_bridge`

Generate a complete FastMCP server bridge for a package/API.

**Input:**
```json
{
  "package_name": "my-package",
  "api_summary": "REST API with users, posts, comments endpoints",
  "thinking_budget": 8000,
  "output_dir": "./generated",
  "dry_run": false
}
```

**Output:** Server code, tool definitions, resources, and prompts.

---

### `audit_mcp_security`

Security audit of MCP server source code.

**Input:**
```json
{
  "path": "./path/to/mcp-server",
  "detailed": false
}
```

**Output:** Findings with severity, category, file location, and remediation.

**Categories:** `INJECTION`, `AUTH`, `DATA_EXPOSURE`, `DENIAL_OF_SERVICE`, `SUPPLY_CHAIN`, `MISC`

**Severities:** `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`, `INFO`

---

## Transports

The MCP server supports three transports:

### 1. stdio (Default)
For local agent processes (Claude Desktop, etc.).

```bash
eco-support mcp-serve --transport stdio
```

**Claude Desktop Config:**
```json
{
  "mcpServers": {
    "eco-support": {
      "command": "eco-support",
      "args": ["mcp-serve", "--transport", "stdio"]
    }
  }
}
```

### 2. SSE (Legacy)
Server-Sent Events for HTTP streaming.

```bash
eco-support mcp-serve --transport sse --host 0.0.0.0 --port 8080
```

### 3. Streamable HTTP (Modern)
Modern HTTP transport with session support.

```bash
eco-support mcp-serve --transport http --host 0.0.0.0 --port 8080
```

**Client Connection:**
```bash
# Requires Authorization header
curl -X POST http://localhost:8080/mcp \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/list","id":1}'
```

---

## Authentication

### stdio Transport
Inherits credentials from the parent process environment. Uses the same resolution chain as CLI:
1. `ANTHROPIC_API_KEY` env var
2. `GITHUB_TOKEN` env var
3. `.env.local`, `.env`
4. User config (`~/.config/eco-support/config.json`)
5. Project config (`.eco-supportrc.json`)
6. OS Keychain (via `eco-support config login`)

### SSE / HTTP Transport
Requires `Authorization: Bearer <token>` header.

The token is validated against the configured Anthropic API key. In production, implement proper token management (JWT, API keys, etc.).

---

## Deployment

### Local Development

```bash
# Build
pnpm run build:mcp

# Run stdio
node packages/mcp/dist/index.js --transport stdio

# Run HTTP
node packages/mcp/dist/index.js --transport http --port 8080
```

### Docker

```bash
# Build
docker build -t eco-support-mcp .

# Run
docker run -p 8080:8080 \
  -e ANTHROPIC_API_KEY=your_key \
  -e GITHUB_TOKEN=your_token \
  eco-support-mcp
```

**Docker Compose:**
```bash
docker-compose up -d
```

### Cloudflare Workers

1. **Configure wrangler.toml:**
   ```toml
   name = "eco-support-mcp"
   main = "dist/index.js"
   compatibility_date = "2024-01-01"
   compatibility_flags = ["nodejs_compat"]
   
   [[durable_objects.bindings]]
   name = "SESSIONS"
   class_name = "McpSession"
   
   [[kv_namespaces]]
   binding = "CACHE"
   id = "your-kv-namespace-id"
   ```

2. **Set secrets:**
   ```bash
   wrangler secret put ANTHROPIC_API_KEY
   wrangler secret put GITHUB_TOKEN
   ```

3. **Deploy:**
   ```bash
   pnpm run build:mcp
   wrangler deploy --env production
   ```

### Fly.io

```bash
# Create fly.toml
fly launch --no-deploy

# Set secrets
fly secrets set ANTHROPIC_API_KEY=your_key GITHUB_TOKEN=your_token

# Deploy
fly deploy
```

### Railway

```bash
# Connect GitHub repo
# Set environment variables in dashboard
# Deploy automatically on push
```

### Render

```yaml
# render.yaml
services:
  - type: web
    name: eco-support-mcp
    env: node
    buildCommand: pnpm install && pnpm run build:mcp
    startCommand: node packages/mcp/dist/index.js --transport http --port $PORT
    envVars:
      - key: ANTHROPIC_API_KEY
        sync: false
      - key: GITHUB_TOKEN
        sync: false
```

---

## Health Check Endpoint

When running with HTTP transport, a health check endpoint is available:

```bash
curl http://localhost:8080/health
# Returns: {"status":"ok","timestamp":"2024-01-01T00:00:00.000Z"}
```

---

## Error Handling

All tools return structured errors:

```json
{
  "error": {
    "code": "TOOL_EXECUTION_ERROR",
    "message": "Scan failed: ...",
    "details": {
      "tool": "scan_niche_ecosystem",
      "category": "C-FFI"
    }
  }
}
```

**Common Error Codes:**
- `AUTH_ERROR` — Missing/invalid credentials
- `RATE_LIMIT` — API rate limit exceeded (includes `retryAfterSeconds`)
- `VALIDATION_ERROR` — Invalid input parameters
- `NOT_FOUND` — Resource not found
- `SUBPROCESS_ERROR` — Underlying binary failed
- `TOOL_EXECUTION_ERROR` — Tool-specific failure

---

## Rate Limits

- **Anthropic API:** Respects `anthropic-rate-limit` headers
- **GitHub API:** Uses `GITHUB_TOKEN` for higher limits (5000/hr vs 60/hr)
- **Scan caching:** Results cached for 24 hours by default (`--force-refresh` to bypass)

---

## Example: Connecting from Claude Desktop

1. Install CLI: `npm install -g @eco-support/cli`
2. Configure credentials: `eco-support config login`
3. Add to `claude_desktop_config.json`:
   ```json
   {
     "mcpServers": {
       "eco-support": {
         "command": "eco-support",
         "args": ["mcp-serve", "--transport", "stdio"]
       }
     }
   }
   ```
4. Restart Claude Desktop
5. Use tools in conversation:
   > "Scan the C-FFI ecosystem for critical libraries"
   > "Triage issue #123 in owner/repo"
   > "Audit the MCP server at ./my-server"

---

## Troubleshooting

### "Command not found: eco-support"
```bash
# Ensure npm global bin is in PATH
echo $PATH | grep npm
# Or use npx
npx @eco-support/cli mcp-serve --transport stdio
```

### "Authentication failed"
```bash
# Check credentials
eco-support config get
# Verify Anthropic API key
eco-support config doctor
```

### "Connection refused" (HTTP transport)
```bash
# Check port
lsof -i :8080
# Ensure host binding
eco-support mcp-serve --transport http --host 0.0.0.0 --port 8080
```

### "Subprocess error: eco-support binary not found"
```bash
# Install Rust toolchain
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
cargo install eco-support

# Or use Python prototype
pip install -e ./research/python_prototype
```