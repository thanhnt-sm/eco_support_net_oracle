"""Unit tests for FastMCP server instantiation and security auditing."""

from eco_support.mcp.server import create_mcp_server
from eco_support.mcp.tools.security_auditor import MCPSecurityAuditor


def test_mcp_server_initialization() -> None:
    """Verifies that the FastMCP server creates and registers all tools."""
    server = create_mcp_server()
    assert server is not None
    assert server.name == "EcoSupport MCP Server"


def test_mcp_security_auditor_detects_vulnerabilities() -> None:
    """Verifies that the security auditor catches command injection and SSRF patterns."""
    auditor = MCPSecurityAuditor()

    vulnerable_code = """
import subprocess
import requests

@mcp.tool()
def execute_system_command(cmd: str):
    subprocess.run(cmd, shell=True)

@mcp.tool()
def fetch_unvetted_url(url: str):
    return requests.get(url).text
"""

    report = auditor.audit_tool_source("vulnerable_server.py", vulnerable_code)

    assert not report.is_safe_for_deployment
    assert report.security_score < 60
    assert len(report.issues) >= 2

    vuln_types = [i.vulnerability_type for i in report.issues]
    assert "Command Injection" in vuln_types
    assert "Server-Side Request Forgery (SSRF)" in vuln_types


def test_mcp_security_auditor_approves_safe_code() -> None:
    """Verifies that clean FastMCP tool definitions pass audit with high score."""
    auditor = MCPSecurityAuditor()

    clean_code = """
from fastmcp import FastMCP
from pydantic import BaseModel

mcp = FastMCP("safe_tool")

@mcp.tool()
def compute_array_stats(values: list[float]) -> dict:
    return {"mean": sum(values) / len(values), "count": len(values)}
"""

    report = auditor.audit_tool_source("clean_server.py", clean_code)

    assert report.is_safe_for_deployment
    assert report.security_score >= 90
    assert len(report.issues) == 0
