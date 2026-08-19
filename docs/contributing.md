# Contributing

## Development Setup

### Prerequisites
- Node.js >= 20
- pnpm >= 9
- Rust toolchain (for native binary) — `curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh`
- Python 3.10+ (for prototype)

### Install
```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
pnpm install
```

### Build
```bash
# Build all packages
pnpm run build

# Build specific package
pnpm run build:core
pnpm run build:cli
pnpm run build:mcp
```

### Develop
```bash
# CLI with auto-reload
pnpm --filter @eco-support/cli run dev

# MCP server with auto-reload
pnpm --filter @eco-support/mcp run dev

# Run CLI commands during development
pnpm --filter @eco-support/cli run dev -- scan --category C-FFI --limit 5
```

### Test
```bash
# All tests
pnpm run test

# Specific package
pnpm run test:core
pnpm run test:cli
pnpm run test:mcp

# Watch mode
pnpm run test:watch
```

### Lint & Typecheck
```bash
pnpm run lint
pnpm run typecheck
```

## Repository Structure

```
.
├── packages/
│   ├── core/          # Shared logic (published privately)
│   │   ├── src/
│   │   │   ├── index.ts       # Main exports
│   │   │   ├── types.ts       # Shared interfaces
│   │   │   ├── config.ts      # Credential resolution
│   │   │   ├── errors.ts      # Error hierarchy
│   │   │   └── subprocess.ts  # Process wrapper
│   │   └── package.json
│   ├── cli/           # CLI adapter (published to npm)
│   │   ├── src/
│   │   │   ├── index.ts       # Entry point
│   │   │   └── commands/      # One file per command
│   │   └── package.json
│   └── mcp/           # MCP server (published to npm)
│       ├── src/
│       │   ├── index.ts       # Server entry
│       │   └── tools/         # Tool implementations
│       └── package.json
├── docs/              # Documentation
├── scripts/           # Utility scripts
├── .github/workflows/ # CI/CD
├── package.json       # Root workspace
├── tsconfig.base.json # Base TypeScript config
└── README.md
```

## Making Changes

### 1. Core Changes
Modify `packages/core/src/`:
- Types: `types.ts`
- Config: `config.ts`
- Errors: `errors.ts`
- Subprocess: `subprocess.ts`

Run `pnpm run build:core` and `pnpm run test:core` after changes.

### 2. CLI Changes
Modify `packages/cli/src/commands/`:
- One file per command
- Follow existing patterns for binary discovery, config loading, output formatting

Register new commands in `packages/cli/src/index.ts`.

### 3. MCP Changes
Modify `packages/mcp/src/tools/index.ts`:
- Add tool registration with Zod schema
- Implement handler calling core subprocess
- Format output as text + JSON

### 4. Documentation
Update relevant files in `docs/`:
- `cli.md` — CLI reference
- `mcp.md` — MCP server docs
- `architecture.md` — Architecture decisions

## Commit Convention

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Types:**
- `feat` — New feature
- `fix` — Bug fix
- `docs` — Documentation
- `refactor` — Code restructuring
- `test` — Tests
- `chore` — Maintenance
- `ci` — CI/CD changes

**Scopes:** `core`, `cli`, `mcp`, `docs`, `ci`, `deps`

**Examples:**
```
feat(cli): add --format option to scan command
fix(mcp): handle SSE transport disconnect gracefully
docs(cli): update triage command examples
refactor(core): simplify credential resolution chain
test(core): add config resolution tests
```

## Pull Request Process

1. **Create feature branch** from `main`
2. **Make changes** with tests
3. **Run full check**: `pnpm run lint && pnpm run typecheck && pnpm run test`
4. **Update docs** if user-facing changes
5. **Open PR** with description of changes
6. **CI must pass** (lint, typecheck, test, build)
7. **Review** by maintainers
8. **Merge** via squash merge

## Release Process

Releases are automated via GitHub Actions on tag push:

```bash
# Create release tag
git tag v0.1.0
git push origin v0.1.0
```

This triggers:
1. Build all packages
2. Run tests
3. Publish `@eco-support/cli` and `@eco-support/mcp` to npm (with provenance)
4. Build and push Docker image to GHCR
5. Deploy MCP server to Cloudflare Workers (on main branch)
6. Create GitHub Release with generated notes

### Versioning

Follows [SemVer](https://semver.org/):
- `MAJOR` — Breaking changes
- `MINOR` — New features (backward compatible)
- `PATCH` — Bug fixes (backward compatible)

Pre-releases: `v0.1.0-alpha.1`, `v0.1.0-beta.2`, `v0.1.0-rc.1`

## Adding Dependencies

### Core Package
```bash
pnpm --filter @eco-support/core add <package>
pnpm --filter @eco-support/core add -D <dev-package>
```

### CLI Package
```bash
pnpm --filter @eco-support/cli add <package>
```

### MCP Package
```bash
pnpm --filter @eco-support/mcp add <package>
```

**Guidelines:**
- Prefer minimal dependencies
- Use `zod` for validation (already in core)
- Use `commander` for CLI (already in cli)
- Use `@modelcontextprotocol/sdk` for MCP (already in mcp)

## Testing Guidelines

### Unit Tests (Core)
- Test config resolution with various env/file combinations
- Test error creation and serialization
- Test subprocess wrapper with mock commands

### Integration Tests (CLI)
- Test command parsing and validation
- Test binary discovery logic
- Test output formatting (JSON vs pretty)

### MCP Tests
- Test tool registration and schemas
- Test each transport boots correctly
- Test auth validation on HTTP transport
- Test tool execution round-trip

### Test Commands
```bash
# Run with coverage
pnpm run test -- --coverage

# Run specific test file
pnpm run test -- packages/core/src/config.test.ts

# Update snapshots
pnpm run test -- -u
```

## Code Style

- **TypeScript:** Strict mode, no `any`, explicit return types on public APIs
- **Formatting:** Prettier (configured in root)
- **Linting:** ESLint with TypeScript rules
- **Imports:** Sorted, grouped (external → internal → relative)
- **Errors:** Always use `EcoSupportError` hierarchy
- **Async:** `Promise.withResolvers()` over `new Promise()`
- **No dynamic imports** for known modules

## Debugging

### CLI
```bash
# Verbose output
DEBUG=* eco-support scan --category C-FFI

# Direct binary
./packages/cli/dist/index.js scan --category C-FFI
```

### MCP Server
```bash
# Stdio with logging
MCP_TRANSPORT=stdio node packages/mcp/dist/index.js 2>&1 | head -50

# HTTP with curl
curl -X POST http://localhost:8080/mcp \
  -H "Authorization: Bearer test" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"tools/list","id":1}'
```

### Subprocess Issues
```bash
# Test binary directly
which eco-support
eco-support scan --category C-FFI --limit 1 --json

# Check Rust build
cd /path/to/rust/workspace && cargo build --release
```

## Getting Help

- **Issues:** GitHub Issues for bugs/features
- **Discussions:** GitHub Discussions for questions
- **Security:** Email security@thanhnt.vn for vulnerabilities

## License

By contributing, you agree that your contributions will be licensed under Apache-2.0 (code) and PolyForm Noncommercial 1.0.0 (non-commercial restrictions).