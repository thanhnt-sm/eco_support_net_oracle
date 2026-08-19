# EcoSupport

> Autonomous niche ecosystem radar and support engine for open source foundations.

[![CI](https://github.com/thanhnt-sm/eco_support_net_oracle/workflows/CI/badge.svg)](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/workflows/ci.yml)
[![Release](https://github.com/thanhnt-sm/eco_support_net_oracle/workflows/Release/badge.svg)](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/workflows/release.yml)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![npm](https://img.shields.io/npm/v/@eco-support/cli)](https://www.npmjs.com/package/@eco-support/cli)
[![npm](https://img.shields.io/npm/v/@eco-support/mcp)](https://www.npmjs.com/package/@eco-support/mcp)

## Overview

EcoSupport analyzes niche software ecosystems (C FFI, RTOS, WebAssembly, Embedded, Rust Crates, Go Modules) to identify critical infrastructure, assess supply chain risk, and automate maintenance workflows.

**Key Capabilities:**
- 🔍 **Ecosystem Scanning** — Discover and score niche packages by Ecosystem Criticality Index (ECI)
- 🔬 **Repository Diagnosis** — Deep bottleneck analysis of GitHub repositories
- 🏷️ **Automated Triage** — Issue severity classification, labeling, and patch planning
- 🔧 **MCP Bridge Generation** — Generate FastMCP servers from API specifications
- 🔒 **Security Auditing** — Comprehensive MCP server vulnerability scanning

## Installation

### CLI (npm)
```bash
npm install -g @eco-support/cli
# or
pnpm add -g @eco-support/cli
```

### MCP Server (Docker)
```bash
docker pull ghcr.io/thanhnt-sm/eco_support_net_oracle/eco-support-mcp:latest
docker run -p 8080:8080 \
  -e ANTHROPIC_API_KEY=your_key \
  -e GITHUB_TOKEN=your_token \
  ghcr.io/thanhnt-sm/eco_support_net_oracle/eco-support-mcp:latest
```

### MCP Server (Cloudflare Workers)
See [docs/mcp.md](docs/mcp.md#cloudflare-workers-deployment)

## Quick Start

### 1. Configure Credentials
```bash
eco-support config login
# Or set environment variables:
export ANTHROPIC_API_KEY=your_anthropic_key
export GITHUB_TOKEN=your_github_token
```

### 2. Run Health Check
```bash
eco-support doctor
```

### 3. Scan Niche Ecosystems
```bash
# Scan C FFI libraries
eco-support scan --category C-FFI --limit 20

# Scan Rust crates with detailed output
eco-support scan --category RUST-CRATES --limit 10 --detailed
```

### 4. Triage a GitHub Issue
```bash
eco-support triage --repo owner/repo --issue 123 --thinking-budget 8000
```

### 5. Generate MCP Bridge
```bash
eco-support synthesize-mcp --package my-package --api "REST API with users, posts, comments endpoints"
```

### 6. Security Audit MCP Server
```bash
eco-support audit-mcp ./path/to/mcp-server --detailed
```

### 7. Run MCP Server
```bash
# Local stdio (for Claude Desktop)
eco-support mcp-serve --transport stdio

# HTTP server (for remote agents)
eco-support mcp-serve --transport http --port 8080
```

## MCP Tools

When connected via MCP, the following tools are available:

| Tool | Description | Mutating |
|------|-------------|----------|
| `scan_niche_ecosystem` | Scan niche ecosystems for critical infrastructure | No |
| `diagnose_repo_bottleneck` | Deep repository bottleneck analysis | No |
| `triage_issue` | Automated issue triage with patch planning | **Yes** |
| `synthesize_mcp_bridge` | Generate FastMCP server code | No |
| `audit_mcp_security` | Security audit of MCP server source | No |

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    @eco-support/core                      │
│  Config, Types, Errors, Subprocess wrappers              │
└──────────────────────┬──────────────────────────────────┘
                       │
        ┌──────────────┴──────────────┐
        ▼                             ▼
┌───────────────┐             ┌───────────────┐
│ @eco-support/ │             │ @eco-support/ │
│     cli       │             │     mcp       │
│  (commander)  │             │  (MCP SDK)    │
└───────┬───────┘             └───────┬───────┘
        │                             │
        ▼                             ▼
┌───────────────┐             ┌───────────────┐
│ Rust/Python   │             │ HTTP/stdio/   │
│ Subprocesses  │             │ SSE Transport │
└───────────────┘             └───────────────┘
```

## Documentation

- [CLI Reference](docs/cli.md) — All commands and options
- [MCP Server](docs/mcp.md) — Tools, transports, deployment
- [Architecture](docs/architecture.md) — Internal design
- [Contributing](docs/contributing.md) — Development workflow

## Credentials

EcoSupport uses a layered credential resolution (highest priority first):

1. **Explicit flags** — `--anthropic-key`, `--github-token`
2. **Environment variables** — `ANTHROPIC_API_KEY`, `GITHUB_TOKEN`
3. **Local .env files** — `.env.local`, `.env`
4. **User config** — `~/.config/eco-support/config.json`
5. **Project config** — `.eco-supportrc.json`
6. **OS Keychain** — `eco-support config login`

Run `eco-support config get` to see resolved values with sources.

## License

Apache-2.0 — see [LICENSE](LICENSE) for details.

PolyForm Noncommercial 1.0.0 — see [LICENSE.noncommercial](LICENSE.noncommercial) for non-commercial use restrictions.