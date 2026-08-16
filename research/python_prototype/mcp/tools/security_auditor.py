"""Security Auditor for Model Context Protocol (MCP) servers and tools."""

from __future__ import annotations

import re

from pydantic import BaseModel, Field


class MCPSecurityIssue(BaseModel):
    tool_name: str
    severity: str = Field(description="CRITICAL, HIGH, MEDIUM, LOW")
    vulnerability_type: str
    description: str
    remediation: str


class MCPAuditReport(BaseModel):
    server_target: str
    total_tools_audited: int
    security_score: int = Field(description="Score from 0 (compromised) to 100 (hardened)")
    is_safe_for_deployment: bool
    issues: list[MCPSecurityIssue]


class MCPSecurityAuditor:
    """Performs static analysis and vulnerability checks on FastMCP tool definitions."""

    def audit_tool_source(self, server_name: str, source_code: str) -> MCPAuditReport:
        """Audits Python source code of an MCP server for common attack vectors."""
        issues: list[MCPSecurityIssue] = []

        # 1. Check for command injection vectors
        if (
            "subprocess" in source_code or "os.system" in source_code or "os.popen" in source_code
        ) and "shell=True" in source_code:
            issues.append(
                MCPSecurityIssue(
                    tool_name="execution_tools",
                    severity="CRITICAL",
                    vulnerability_type="Command Injection",
                    description="Direct invocation of subprocess with `shell=True` detected in MCP tool handler.",
                    remediation="Use argument lists without shell invocation and strictly whitelist allowed binaries.",
                )
            )

        # 2. Check for unvetted eval / exec
        if re.search(r"\beval\(", source_code) or re.search(r"\bexec\(", source_code):
            issues.append(
                MCPSecurityIssue(
                    tool_name="dynamic_eval",
                    severity="CRITICAL",
                    vulnerability_type="Arbitrary Code Execution",
                    description="Unsanitized dynamic eval/exec found in tool body.",
                    remediation="Eliminate eval/exec and replace with structured parser or AST evaluator.",
                )
            )

        # 3. Check for SSRF vectors
        if (
            "httpx.get" in source_code or "requests.get" in source_code or "urllib" in source_code
        ) and ("localhost" not in source_code and "allowed_domains" not in source_code):
            issues.append(
                MCPSecurityIssue(
                    tool_name="network_fetch",
                    severity="HIGH",
                    vulnerability_type="Server-Side Request Forgery (SSRF)",
                    description="Outbound HTTP requests executed with raw user URLs without domain whitelisting.",
                    remediation="Enforce strict URL scheme validation and domain allowlists (block 127.0.0.1, 169.254.169.254).",
                )
            )

        # 4. Check for path traversal risks
        if (
            "open(" in source_code
            and ("../" in source_code or "os.path.join" in source_code)
            and "resolve()" not in source_code
        ):
            issues.append(
                MCPSecurityIssue(
                    tool_name="file_system_tools",
                    severity="MEDIUM",
                    vulnerability_type="Path Traversal",
                    description="Potential arbitrary file access without path canonicalization check.",
                    remediation="Use `pathlib.Path.resolve()` and verify that path is strictly within sandboxed directory.",
                )
            )

        # Calculate safety score
        penalty = sum(
            40 if i.severity == "CRITICAL" else 20 if i.severity == "HIGH" else 10 for i in issues
        )
        score = max(0, 100 - penalty)

        return MCPAuditReport(
            server_target=server_name,
            total_tools_audited=max(1, source_code.count("@mcp.tool")),
            security_score=score,
            is_safe_for_deployment=(
                score >= 70 and not any(i.severity == "CRITICAL" for i in issues)
            ),
            issues=issues,
        )
