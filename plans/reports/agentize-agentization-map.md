# Agentization Map — EcoSupport

Based on scout analysis of the polyglot Rust + Python codebase.

| Capability | Function/Entry Point | Inputs | Outputs | Side Effects | Auth Needed | Agent Value | CLI Value |
|---|---|---|---|---|---|---|---|
| **scan_niche_ecosystem** | `NicheScanner::scan_category()` / `eco-support scan` | category: C-FFI|C-RTOS|C-WASM|C-EMBEDDED|RUST-CRATES|GO-MODULES, limit: u32 | ScanResult { niches: Vec<NicheInfo>, metadata } | Network (GitHub API, crates.io, PyPI), File I/O (cache) | GitHub token (optional, for rate limits) | **H** — core discovery workflow | **H** — primary CLI command |
| **diagnose_repo_bottleneck** | `eco-support.mcp.tools.ecosystem_tools.handle_diagnose_repo_bottleneck()` | repo: owner/name, issue_ref: #number or title/body | BottleneckDiagnosis { root_causes, recommendations, evidence } | Network (GitHub API), LLM calls (Claude) | Anthropic API key | **H** — deep analysis workflow | **H** — CLI triage command |
| **triage_issue** | `TriageAgent::triage_issue()` / `eco-support triage` | repo, issue_id, title?, body?, thinking_budget | TriageResult { severity, labels, assignees, analysis, patch_plan } | Network (GitHub API), LLM calls (Claude) | Anthropic API key, GitHub token | **H** — agent-automated triage | **H** — CLI command |
| **synthesize_mcp_bridge** | `DocBridgeAgent::generate_mcp_bridge()` / `eco-support synthesize-mcp` | package_name, api_summary, thinking_budget | MCPBridgeCode { server_code, tools, resources, prompts } | LLM calls (Claude), File I/O (output) | Anthropic API key | **H** — code generation workflow | **M** — CLI command |
| **audit_mcp_security** | `MCPSecurityAuditor::audit_tool_source()` / `eco-support audit-mcp` | path: string (file or directory) | AuditReport { findings: Vec<Finding>, severity, remediation } | File I/O, LLM calls (Claude) | Anthropic API key | **H** — security workflow | **M** — CLI command |
| **run_mcp_server** | `EcoMcpServer::run_stdio_loop()` / `eco-support mcp-serve` | transport: stdio|sse, host?, port? | Running MCP server (stdio or HTTP) | Network (if SSE), Process spawn | Anthropic API key | **M** — deployment | **H** — CLI command |
| **calculate_eci** | `CriticalityCalculator::compute_eci()` / `evaluate()` | metrics: EciMetrics, category: Category | EciScore { score: f64, tier: Tier, breakdown } | Pure computation | None | **M** — reusable scoring | **L** — internal |
| **classify_tier** | `CriticalityCalculator::classify_tier(score)` | score: f64 | Tier enum | Pure computation | None | **L** — helper | **L** — internal |
| **generate_patch** | `PatchSynthesizerAgent::synthesize_patch()` | repo, problem, code_context, thinking_budget | Patch { diff, explanation, tests } | LLM calls (Claude) | Anthropic API key | **H** — automated fix | **M** — CLI (via triage) |
| **check_claude_live** | `ClaudeClient::is_live()` | — | bool | Network (Anthropic API) | Anthropic API key | **L** — health check | **L** — internal |
| **load_config** | `Config::from_env()` | — | Config { anthropic_key, github_token, ... } | File I/O (.env), Env vars | Anthropic API key, GitHub token | **L** — bootstrap | **L** — internal |

## Design Decisions (Agent-Centric Rules)

### Workflows Over Endpoint Mirrors
- **scan_niche_ecosystem** → CLI command + MCP tool (single actionable workflow)
- **triage_issue** → CLI command + MCP tool (combines fetch + analyze + label)
- **synthesize_mcp_bridge** → CLI command + MCP tool (generate complete server)
- **audit_mcp_security** → CLI command + MCP tool (security scanning)

### Context Optimization
- All tools return concise summary + structured data
- `--detailed` / `format=detailed` flag for full output
- Scan results paginated (default limit 10)

### Actionable Errors
- Auth errors: include `auth_setup_url` and `required_scope`
- Rate limit: include `retry_after_seconds` and `quota_remaining`
- LLM errors: include `model` and `thinking_budget_used`

### Human-Readable Identifiers
- Categories as strings: "C-FFI", "RUST-CRATES" not enum integers
- Tiers as strings: "CRITICAL", "HIGH", "MEDIUM", "LOW"
- Tool names: snake_case verb-noun

### Idempotency & Dry-Run
- `scan_niche_ecosystem`: cached results, `--force-refresh` flag
- `synthesize_mcp_bridge`: `--dry-run` outputs to stdout
- `audit_mcp_security`: read-only, no mutations

## Capability Cuts

| Capability | Reason |
|---|---|
| `calculate_eci` | Internal computation, expose via scan results only |
| `classify_tier` | Internal helper, not standalone |
| `check_claude_live` | Health check only, not user-facing |
| `load_config` | Internal bootstrap, not a tool |
| `run_mcp_server` | Deployment concern, not a data/query tool → CLI only |

## Final Tool/Command Set (v1)

### MCP Tools (read-only safe)
1. `scan_niche_ecosystem` — discover niches
2. `diagnose_repo_bottleneck` — deep repo analysis
3. `triage_issue` — automated issue triage
4. `synthesize_mcp_bridge` — generate MCP server code
5. `audit_mcp_security` — security audit MCP servers

### CLI Commands (all capabilities including mutating)
1. `eco-support scan` — scan niches
2. `eco-support triage` — triage issue (mutating: can label/assign)
3. `eco-support synthesize-mcp` — generate bridge code
4. `eco-support audit-mcp` — security audit
5. `eco-support mcp-serve` — run MCP server (stdio/SSE)
6. `eco-support config` — manage credentials (doctor, login, logout)
7. `eco-support doctor` — health check all deps