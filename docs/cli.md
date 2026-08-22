> **⚠️ ARCHIVED** — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET). See [README](../../README.md) for current documentation.

# CLI Reference

## Global Options

| Option | Description |
|--------|-------------|
| `--json` | Output as JSON (available on all commands) |
| `--help`, `-h` | Show help |
| `--version`, `-v` | Show version |

## Commands

### `eco-support scan`

Scan niche ecosystems for critical infrastructure.

```bash
eco-support scan [options]
```

**Options:**

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--category <category>` | `-c` | Niche category to scan | `C-FFI` |
| `--limit <number>` | `-l` | Maximum results (1-100) | `10` |
| `--force-refresh` | `-f` | Force refresh cached results | `false` |
| `--detailed` | `-d` | Show detailed output | `false` |
| `--json` | | Output as JSON | `false` |

**Categories:** `C-FFI`, `C-RTOS`, `C-WASM`, `C-EMBEDDED`, `RUST-CRATES`, `GO-MODULES`

**Examples:**
```bash
# Basic scan
eco-support scan --category RUST-CRATES --limit 20

# Detailed scan with JSON output
eco-support scan --category C-FFI --limit 10 --detailed --json

# Force refresh cache
eco-support scan --category GO-MODULES --force-refresh
```

**Exit Codes:**
- `0` — Success
- `1` — Invalid arguments
- `2` — Authentication error
- `3` — Network error
- `4` — Runtime error (subprocess failed)

---

### `eco-support triage`

Deep bug triage for a GitHub issue with automated analysis, labeling, and patch planning.

```bash
eco-support triage [options]
```

**Options:**

| Option | Short | Description | Required |
|--------|-------|-------------|----------|
| `--repo <owner/name>` | `-r` | Repository in owner/name format | Yes |
| `--issue <number>` | `-i` | Issue number | Yes |
| `--title <text>` | `-t` | Issue title (for context) | No |
| `--body <text>` | `-b` | Issue body (for context) | No |
| `--thinking-budget <tokens>` | | Thinking budget (1000-50000) | No (8000) |
| `--json` | | Output as JSON | No |

**Examples:**
```bash
# Triage existing issue
eco-support triage --repo owner/repo --issue 123

# Triage with context
eco-support triage --repo owner/repo --issue 456 --title "Memory leak" --body "Details here" --thinking-budget 12000
```

**Mutating Operations:** This command can apply labels and assign users to the issue via GitHub API.

**Exit Codes:**
- `0` — Success
- `1` — Invalid arguments
- `2` — Authentication error (missing GitHub token)
- `3` — Network error (GitHub API)
- `4` — Runtime error

---

### `eco-support synthesize-mcp`

Generate FastMCP bridge code for a package.

```bash
eco-support synthesize-mcp [options]
```

**Options:**

| Option | Short | Description | Required |
|--------|-------|-------------|----------|
| `--package <name>` | `-p` | Package name | Yes |
| `--api <summary>` | `-a` | API summary/description | Yes |
| `--thinking-budget <tokens>` | | Thinking budget (1000-50000) | No (8000) |
| `--output <dir>` | `-o` | Output directory | No |
| `--dry-run` | | Output to stdout instead of writing files | No |
| `--json` | | Output as JSON | No |

**Examples:**
```bash
# Generate and write to directory
eco-support synthesize-mcp --package my-api --api "REST API with CRUD endpoints" --output ./generated

# Dry run to stdout
eco-support synthesize-mcp --package my-api --api "GraphQL schema with User, Post types" --dry-run
```

**Output:** Generates `mcp-server.ts` with tools, resources, and prompts.

**Exit Codes:**
- `0` — Success
- `1` — Invalid arguments
- `2` — Authentication error
- `3` — Network error
- `4` — Runtime error

---

### `eco-support audit-mcp`

Security audit of MCP server source code.

```bash
eco-support audit-mcp [options]
```

**Options:**

| Option | Short | Description | Required |
|--------|-------|-------------|----------|
| `--path <path>` | `-p` | Path to MCP server source file or directory | Yes |
| `--detailed` | `-d` | Show detailed findings | No |
| `--json` | | Output as JSON | No |

**Examples:**
```bash
# Basic audit
eco-support audit-mcp ./my-mcp-server

# Detailed audit with JSON
eco-support audit-mcp ./my-mcp-server --detailed --json
```

**Findings Categories:** `INJECTION`, `AUTH`, `DATA_EXPOSURE`, `DENIAL_OF_SERVICE`, `SUPPLY_CHAIN`, `MISC`

**Severities:** `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`, `INFO`

**Exit Codes:**
- `0` — Success (audit completed, findings reported in output)
- `1` — Invalid arguments
- `2` — Authentication error
- `3` — Network error
- `4` — Runtime error

---

### `eco-support mcp-serve`

Run EcoSupport MCP server.

```bash
eco-support mcp-serve [options]
```

**Options:**

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--transport <type>` | `-t` | Transport: `stdio`, `sse`, `http` | `stdio` |
| `--host <host>` | `-h` | Host for SSE/HTTP | `127.0.0.1` |
| `--port <number>` | `-p` | Port for SSE/HTTP | `8080` |

**Examples:**
```bash
# Stdio for Claude Desktop
eco-support mcp-serve --transport stdio

# HTTP server for remote access
eco-support mcp-serve --transport http --port 8080

# SSE legacy transport
eco-support mcp-serve --transport sse --host 0.0.0.0 --port 3000
```

**Exit Codes:**
- `0` — Server stopped gracefully
- `1` — Invalid arguments
- `2` — Authentication error
- `3` — Network error (port in use)
- `4` — Runtime error

---

### `eco-support config`

Manage configuration and credentials.

```bash
eco-support config <subcommand> [options]
```

**Subcommands:**

#### `config login`
Store credentials securely in OS keychain.

```bash
eco-support config login [--anthropic-key <key>] [--github-token <token>]
```

Prompts for missing values interactively (hidden input).

#### `config logout`
Remove stored credentials from keychain.

```bash
eco-support config logout
```

#### `config set <key> <value>`
Set configuration value in user config file.

```bash
eco-support config set cacheDir ~/.cache/eco-support
eco-support config set logLevel debug
eco-support config set scanTimeoutMs 180000
eco-support config set claudeModel claude-3-7-sonnet-20250219
eco-support config set thinkingBudgetTokens 12000
```

**Valid Keys:** `cacheDir`, `logLevel`, `scanTimeoutMs`, `claudeModel`, `thinkingBudgetTokens`

#### `config get [key]`
Get configuration value with source.

```bash
# Show all config with sources
eco-support config get

# Show specific key
eco-support config get anthropicApiKey
```

#### `config doctor`
Run configuration health check.

```bash
eco-support config doctor [--json]
```

---

### `eco-support doctor`

Health check all dependencies and configuration.

```bash
eco-support doctor [--json]
```

Checks:
- Anthropic API key configured
- GitHub token configured (optional)
- EcoSupport binary available (Rust/Python)
- Cache directory writable
- Node.js version >= 20
- Keychain accessibility

**Exit Codes:**
- `0` — All critical checks passed
- `1` — Critical failures found

---

## Credential Resolution

EcoSupport resolves credentials in this order (first match wins):

1. **Explicit flags** — `--anthropic-key`, `--github-token` (never logged)
2. **Environment variables** — `ANTHROPIC_API_KEY`, `GITHUB_TOKEN`
3. **Local `.env.local`** — Project-specific, gitignored
4. **Local `.env`** — Project defaults
5. **User config** — `~/.config/eco-support/config.json` (XDG) / `%APPDATA%\eco-support\config.json`
6. **Project config** — `.eco-supportrc.json` / `eco-support.config.json`
7. **OS Keychain** — Via `eco-support config login` (keytar)

Run `eco-support config get` to see which source resolved each credential.

---

## Environment Variables

| Variable | Description |
|----------|-------------|
| `ANTHROPIC_API_KEY` | Anthropic API key (required) |
| `GITHUB_TOKEN` | GitHub personal access token (optional, higher rate limits) |
| `ECOSUPPORT_CACHE_DIR` | Cache directory override |
| `ECOSUPPORT_LOG_LEVEL` | Log level: `debug`, `info`, `warn`, `error` |
| `ECOSUPPORT_SCAN_TIMEOUT_MS` | Scan timeout in milliseconds |
| `ECOSUPPORT_CLAUDE_MODEL` | Claude model to use |
| `ECOSUPPORT_THINKING_BUDGET` | Default thinking budget tokens |
| `NO_COLOR` | Disable colored output |
| `ECOSUPPORT_JSON` | Global JSON output mode |

---

## Configuration Files

### User Config (`~/.config/eco-support/config.json`)
```json
{
  "cacheDir": "/home/user/.cache/eco-support",
  "logLevel": "info",
  "scanTimeoutMs": 120000,
  "claudeModel": "claude-3-7-sonnet-20250219",
  "thinkingBudgetTokens": 8000
}
```

### Project Config (`.eco-supportrc.json`)
```json
{
  "cacheDir": "./.eco-support-cache",
  "logLevel": "debug"
}
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | User error (invalid arguments, missing required flags) |
| `2` | Authentication error (missing/invalid credentials) |
| `3` | Network error (API unreachable, rate limited) |
| `4` | Runtime error (subprocess failed, internal error) |

---

## Examples

### Complete Workflow: Analyze and Fix a Repository

```bash
# 1. Check health
eco-support doctor

# 2. Scan for related niches
eco-support scan --category RUST-CRATES --limit 15

# 3. Triage a specific issue
eco-support triage --repo rust-lang/rust --issue 12345 --thinking-budget 10000

# 4. Generate MCP bridge for the project
eco-support synthesize-mcp --package rust-analyzer --api "LSP server for Rust" --output ./mcp-bridge

# 5. Audit the generated MCP server
eco-support audit-mcp ./mcp-bridge --detailed
```

### CI/CD Integration

```yaml
# .github/workflows/triage.yml
name: Auto Triage
on:
  issues:
    types: [opened]
jobs:
  triage:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: npx @eco-support/cli triage --repo ${{ github.repository }} --issue ${{ github.event.issue.number }} --json
        env:
          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```