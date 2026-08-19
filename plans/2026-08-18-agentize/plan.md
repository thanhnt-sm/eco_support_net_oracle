---
title: Agentize Operation - Convert to CLI/MCP
status: in-progress
priority: P1
effort: medium
branch: main
tags: [agentize, cli, mcp, automation]
created: 2026-08-18
invocation_args: "--both --auto"
---

# Agentize Operation Plan

## Invocation Arguments
- `--both` - Generate both CLI (npm) and MCP server (stdio/SSE/Streamable HTTP)
- `--auto` - Auto mode: skip interactive prompts, use defaults

## Phase Checklist

### Phase 1: Scout
- [ ] Analyze current codebase structure and entry points
- [ ] Identify main features/modules to expose as tools
- [ ] Map dependencies and external integrations
- [ ] Document authentication/credential requirements

### Phase 2: Design
- [ ] Define CLI command structure and subcommands
- [ ] Design MCP server tool schema and resources
- [ ] Plan credentials resolution strategy
- [ ] Create docs/architecture overview

### Phase 3: Scaffold
- [ ] Initialize npm package structure (package.json, tsconfig, etc.)
- [ ] Set up MCP server boilerplate (FastMCP or MCP SDK)
- [ ] Configure build pipeline (tsup/esbuild)
- [ ] Add CI/CD workflow for publishing

### Phase 4: Implement CLI
- [ ] Create main CLI entry point with commander/yargs
- [ ] Implement core commands and subcommands
- [ ] Add help text, version, and error handling
- [ ] Integrate with existing codebase modules

### Phase 5: Implement MCP Server
- [ ] Build MCP server with stdio transport
- [ ] Add SSE/Streamable HTTP transport options
- [ ] Implement tool handlers for each exposed feature
- [ ] Add resource providers and prompt templates

### Phase 6: Credentials & Config
- [ ] Implement credential resolution (env, config file, keychain)
- [ ] Add configuration schema validation
- [ ] Create example config files
- [ ] Document credential setup for users

### Phase 7: Testing
- [ ] Write unit tests for CLI commands
- [ ] Write integration tests for MCP tools
- [ ] Test credential flows and error cases
- [ ] Verify npm package builds correctly

### Phase 8: Documentation
- [ ] Generate README with usage examples
- [ ] Create CLI reference documentation
- [ ] Document MCP server setup and tools
- [ ] Add troubleshooting guide

### Phase 9: Package
- [ ] Run full build and verification
- [ ] Publish to npm (if configured)
- [ ] Create GitHub release with changelog
- [ ] Verify installation from fresh environment