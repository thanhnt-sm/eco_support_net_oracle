# Agentize Decisions Record

**Invocation**: `--both --auto` (monorepo with CLI + MCP, fully autonomous)

## Output Mode: `--both` (Monorepo)

**Justification**: The codebase has both CLI entry points (`eco-support` command in Python/Rust) and an existing MCP server implementation (FastMCP 2.0 Python + custom Rust MCP server). Both surfaces are valuable:
- CLI: Direct human usage, scripting, CI/CD integration
- MCP: Agent-to-agent integration, LLM tool use, remote deployment

A monorepo with shared `core/` preserves DRY and enables future evolution.

## Architecture: TypeScript Monorepo (pnpm workspaces)

**Justification**: 
- Target is polyglot (Rust + Python) but the CLI/MCP wrapping layer should be in a single language for maintainability
- TypeScript is the standard for MCP SDK (official `@modelcontextprotocol/sdk`) and CLI tooling (`commander`, `cac`)
- The Rust/Python cores are wrapped as subprocesses or via FFI — the wrapper layer orchestrates, doesn't reimplement

## Package Structure

```
.
├── packages/
│   ├── core/          # Shared logic: config, types, subprocess wrappers, error handling
│   ├── cli/           # Thin CLI adapter (commander) over core
│   └── mcp/           # MCP server adapter (MCP SDK) over core
├── docs/
├── scripts/
├── .github/workflows/
├── package.json       # pnpm workspaces
├── tsconfig.base.json
└── README.md
```

## Tool/Command List (from Agentization Map)

### MCP Tools (5 read-only safe tools)
| Tool Name | Core Capability | Mutating? |
|---|---|---|
| `scan_niche_ecosystem` | Scan niche ecosystems | No |
| `diagnose_repo_bottleneck` | Deep repo analysis | No |
| `triage_issue` | Automated issue triage | **Yes** (labels, assigns) |
| `synthesize_mcp_bridge` | Generate MCP server code | No (generates code) |
| `audit_mcp_security` | Security audit MCP servers | No |

### CLI Commands (7 commands)
| Command | Core Capability | Mutating? |
|---|---|---|
| `eco-support scan` | Scan niches | No |
| `eco-support triage` | Triage issue | **Yes** |
| `eco-support synthesize-mcp` | Generate bridge code | No |
| `eco-support audit-mcp` | Security audit | No |
| `eco-support mcp-serve` | Run MCP server | No (starts process) |
| `eco-support config` | Manage credentials | Yes (writes config) |
| `eco-support doctor` | Health check | No |

## Credentials Resolution Chain

Following `references/auth-resolution-chain.md`:

1. **Explicit flags** — `--anthropic-key`, `--github-token` (never logged)
2. **Process env** — `ANTHROPIC_API_KEY`, `GITHUB_TOKEN`
3. **Local .env files** — `.env.local` → `.env` in CWD
4. **User config** — `~/.config/eco-support/config.json` (XDG) / `%APPDATA%\eco-support\config.json`
5. **Project config** — `.eco-supportrc.json` / `eco-support.config.json`
6. **OS keychain** — `keytar` via `eco-support config login`

**MCP stdio**: Same chain as CLI (subprocess inherits env)
**MCP SSE/HTTP**: Bearer token required (`Authorization: Bearer <token>`), validated against stored API keys

## MCP Transports

All three transports implemented in `packages/mcp/`:
- **stdio** — Default for local agent processes (Claude Desktop, etc.)
- **SSE** — Legacy HTTP streaming compatibility
- **Streamable HTTP** — Modern HTTP transport for remote/PaaS deployment

Transport selected via `--transport` flag or `MCP_TRANSPORT` env var.

## Deployment Targets

1. **CLI**: npm package `@eco-support/cli` (or scoped `@thanhnt/eco-support-cli`)
2. **MCP Server**: 
   - Cloudflare Workers (primary) — `wrangler.toml`, Durable Objects for sessions
   - Docker — `Dockerfile` (distroless), `docker-compose.yml`
   - Self-host — Node server + `Procfile` for Fly.io/Railway/Render

## Package Metadata

| Field | Value |
|---|---|
| CLI package name | `@eco-support/cli` |
| MCP package name | `@eco-support/mcp` |
| Core package name | `@eco-support/core` (private, not published) |
| License | Apache-2.0 (matching upstream) |
| Node engines | `>=20.0.0` |
| Publish provenance | `true` |

## Companion Skill

- Skill name: `eco-support`
- Path: `claude/skills/eco-support/`
- Marketplace category: Developer Tools
- Keywords: mcp, cli, ecosystem-analysis, security-audit, code-generation

## Remaining Decisions (deferred to implementation)

- Exact subprocess wrapper strategy (stdin/stdout vs FFI vs HTTP) — decide during Wrap phase
- Cache directory for scan results — `~/.cache/eco-support/` or XDG
- Whether to vendor Rust/Python binaries or require pre-installed — vendor for zero-dep install