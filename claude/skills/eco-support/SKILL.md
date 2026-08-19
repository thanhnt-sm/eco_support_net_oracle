# EcoSupport Skill

> Autonomous niche ecosystem radar and support engine

## Description

EcoSupport is a polyglot (Rust + Python) system for autonomous niche ecosystem analysis. It can scan software ecosystems, diagnose repository bottlenecks, triage GitHub issues, generate FastMCP bridges, and perform security audits on MCP servers.

**Key capabilities:**
- **Ecosystem Scanning** — Discover and score niche packages by Ecosystem Criticality Index (ECI)
- **Repository Diagnosis** — Deep analysis of technical debt and architectural issues
- **Automated Triage** — Issue severity classification, labeling, and patch planning
- **MCP Bridge Generation** — Generate FastMCP server code from API specifications
- **Security Auditing** — Comprehensive vulnerability scanning of MCP server source

## Trigger Phrases

Install these trigger phrases in Claude to use this skill:

```
eco-support scan
eco-support triage
eco-support synthesize-mcp
eco-support audit-mcp
eco-support mcp-serve
eco-support config
eco-support doctor
```

## Common Workflows

### 1. Scan a Niche Ecosystem

**Trigger:** "Scan the C FFI ecosystem for critical infrastructure"

```bash
eco-support scan --category C-FFI --limit 20
```

Use case: Finding critical C FFI libraries that need maintenance, assessing ECI scores.

### 2. Triage a GitHub Issue

**Trigger:** "Triage issue #123 in owner/repo with severity and patch plan"

```bash
eco-support triage --repo owner/repo --issue 123 --thinking-budget 8000
```

Use case: Automated issue analysis with suggested labels, assignees, and patch plans.

### 3. Generate an MCP Server

**Trigger:** "Generate an MCP server for my REST API with users and posts endpoints"

```bash
eco-support synthesize-mcp --package my-api --api "REST API with users, posts, comments endpoints" --output ./mcp-bridge
```

Use case: Rapid MCP server generation for LLM tool integration.

### 4. Security Audit

**Trigger:** "Audit the MCP server at ./my-server for security vulnerabilities"

```bash
eco-support audit-mcp ./my-mcp-server --detailed
```

Use case: Finding injection, auth, data exposure, DoS, and supply chain risks.

### 5. Run MCP Server

**Trigger:** "Start the EcoSupport MCP server for Claude Desktop integration"

```bash
eco-support mcp-serve --transport stdio
```

Use case: Local MCP server connection for Claude Desktop.

```bash
eco-support mcp-serve --transport http --port 8080
```

Use case: Remote MCP server accessible via HTTP.

### 6. Configure Credentials

**Trigger:** "Configure Anthropic API key and GitHub token"

```bash
eco-support config login
```

Use case: Interactive credential setup with keychain storage.

```bash
eco-support config doctor
```

Use case: Health check all dependencies and configuration.

## Available MCP Tools

When connected via MCP, these tools are available:

| Tool | Description | Mutating |
|------|-------------|----------|
| `scan_niche_ecosystem` | Scan niche ecosystems for critical infrastructure | No |
| `diagnose_repo_bottleneck` | Deep repository bottleneck analysis | No |
| `triage_issue` | Automated issue triage with labels and assignees | **Yes** |
| `synthesize_mcp_bridge` | Generate FastMCP server code | No |
| `audit_mcp_security` | Security audit of MCP server source | No |

## Configuration

### Credentials Required

- **ANTHROPIC_API_KEY** — Required for all operations (Claude 3.7 Sonnet Extended Thinking)
- **GITHUB_TOKEN** — Optional, increases GitHub API rate limits (5000/hr vs 60/hr)

### Credential Resolution (highest priority first)

1. **Explicit flags** — `--anthropic-key`, `--github-token` (never logged)
2. **Environment variables** — `ANTHROPIC_API_KEY`, `GITHUB_TOKEN`
3. **Local `.env.local`** — Project-specific, gitignored
4. **Local `.env`** — Project defaults
5. **User config** — `~/.config/eco-support/config.json` (XDG standard)
6. **Project config** — `.eco-supportrc.json` in CWD
7. **OS Keychain** — Via `eco-support config login` (keytar)

Run `eco-support config get` to see which source resolved each credential.

### Config File

```json
{
  "cacheDir": "/home/user/.cache/eco-support",
  "logLevel": "info",
  "scanTimeoutMs": 120000,
  "claudeModel": "claude-3-7-sonnet-20250219",
  "thinkingBudgetTokens": 8000
}
```

## Quick Start

1. **Install CLI:** `npm install -g @eco-support/cli`
2. **Configure credentials:** `eco-support config login`
3. **Health check:** `eco-support doctor`
4. **Scan:** `eco-support scan --category RUST-CRATES --limit 10`
5. **Triage:** `eco-support triage --repo rust-lang/rust --issue 12345`
6. **Generate MCP:** `eco-support synthesize-mcp --package rust-analyzer --api "LSP server for Rust"`
7. **Audit:** `eco-support audit-mcp ./generated-mcp-server`
8. **Run server:** `eco-support mcp-serve --transport stdio`

## Skill Metadata

- **Category:** Developer Tools
- **Keywords:** mcp, cli, ecosystem-analysis, security-audit, code-generation
- **License:** Apache-2.0
- **Author:** thanhnt-sm
- **Repository:** https://github.com/thanhnt-sm/eco_support_net_oracle
- **Version:** 0.0.1

## Marketplace Notes

This skill is designed for the Claude Plugins Marketplace. It requires:
- `ANTHROPIC_API_KEY` environment variable set
- Internet access for GitHub API and Anthropic API calls
- Node.js >= 20 for CLI integration

The skill communicates with the local `@eco-support/cli` package or a remote MCP server via stdio/SSE/HTTP transports.

## Examples in Conversation

> **User:** "Can you scan the Go modules ecosystem for critical packages?"
> 
> **Assistant:** `eco-support scan --category GO-MODULES --limit 15`
> 
> *Output shows Go packages with ECI scores and tier classifications*

> **User:** "I have a security concern about my MCP server. Can you audit it?"
>
> **Assistant:** `eco-support audit-mcp ./my-server --detailed`
>
> *Output lists findings with severities and remediation recommendations*

> **User:** "I need to triage a GitHub issue. Can you help?"
>
> **Assistant:** `eco-support triage --repo owner/repo --issue 892 --thinking-budget 10000`
>
> *Output includes severity, suggested labels, assignees, and a patch plan*