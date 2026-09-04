[English](playbook_and_runbook.md) | [Tiếng Việt](playbook_and_runbook.vi.md)

# SRE Playbook & Operational Runbook

**Document ID**: `OPS-RUNBOOK-2026.1`  
**Target Audience**: DevOps Engineers, SREs, System Operators, Maintainers  
**Service**: `eco-support-rs` Native Daemon & FastMCP Server


---

## 🚀 1. Standard Operational Procedures (SOP)

### SOP-001: Environment Configuration
Create or update your `.env` file in the workspace root:

```bash
# Anthropic API Key (Required for live Claude 3.7 Thinking)
ANTHROPIC_API_KEY=sk-ant-api03-...

# Model Specification
ANTHROPIC_MODEL=claude-3-7-sonnet-20250219

# Extended Thinking Token Budget (Default: 4096, High: 16384)
THINKING_BUDGET_TOKENS=4096

# GitHub API Token (Optional, avoids API rate limits during radar scans)
GITHUB_TOKEN=ghp_...

# Observability
LOG_LEVEL=info
```

### SOP-002: Secure Multi-Platform Release

Follow the [Secure Release Guide](release_guide.md). The only release entry point is:

```bash
bash tools/git-tools/dg-release --tag v1.2.3 --publish-marketplaces --dry-run
```

The documented production command requires an explicit confirmation and never stores marketplace, NuGet, or GitHub credentials in the repository.

### SOP-003: Service Startup & Daemon Deployment
```bash
# Mode A: Interactive Developer CLI
cargo run -p eco-cli -- scan --category c-ffi --limit 10

# Mode B: Production Release Binary (Standard Stdio for Claude Desktop)
./target/release/eco-support mcp-serve --transport stdio

# Mode C: Headless Background Radar Service
nohup ./target/release/eco-support scan --category general-niche --limit 50 > /var/log/ecosupport_radar.log 2>&1 &
```

---

## 🩺 2. Troubleshooting & Incident Response Matrix

| Incident Code | Symptom | Probable Cause | Immediate Remediation |
| :--- | :--- | :--- | :--- |
| **ERR-API-401** | `API returned 401 Unauthorized` | Missing or expired `ANTHROPIC_API_KEY`. | Verify `.env` file and test key validity via `curl https://api.anthropic.com/v1/messages`. System will automatically degrade to deterministic simulation if key is unset. |
| **ERR-RATE-429** | `GitHub API Rate limit exceeded` | Unauthenticated scanning exceeded 60 reqs/hr. | Set `GITHUB_TOKEN` in `.env` to increase limit to 5,000 reqs/hr. |
| **ERR-MCP-TIMEOUT** | Claude Desktop times out on tool call | Extended Thinking budget set too high (> 32k) on slow network. | Lower `THINKING_BUDGET_TOKENS` to `4096` or inspect network latency to Anthropic endpoint. |
| **ERR-AUDIT-REJECT** | `audit_mcp_security` flags SAFE=False | Source code contains raw `subprocess.run(shell=True)` or unvetted `eval()`. | Replace shell invocation with structured parameter vectors and enforce domain whitelist. |

---

## 🔄 3. Disaster Recovery & Failure Drills

### Scenario A: GitHub API Outage
If GitHub or external package registries experience outages:
1. The **Niche Radar** automatically falls back to the curated local seed registry located at [`research/data/niche_seed_registry.json`](file:///Volumes/Data/101.AI/GitHub/eco_supporrt/research/data/niche_seed_registry.json).
2. Service remains 100% operational in cached diagnostic mode without throwing uncaught panics.

### Scenario B: Anthropic API Outage
1. The **Claude Client** catches HTTP transport failures and falls back to **Offline High-Fidelity Simulation Mode**.
2. Telemetry records the offline fallback event in logs (`WARN: Running deterministic offline simulation harness`).

---

## 💰 4. Token Cost Accounting & Budget Guardrails

To prevent runaway API costs:
- Every reasoning call logs prompt, completion, and thinking tokens via `eco_core::telemetry::log_token_metrics`.
- Hard token cap enforced at `max_tokens = 20000`.
- Thinking budget defaults to `4096` tokens per turn (~$0.015 per triage diagnosis).
