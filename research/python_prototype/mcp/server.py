"""FastMCP 2.0 Server entry point for EcoSupport."""

from __future__ import annotations

from typing import Any

from fastmcp import FastMCP

from eco_support.core.telemetry import get_logger
from eco_support.mcp.tools.ecosystem_tools import (
    handle_diagnose_repo_bottleneck,
    handle_scan_niche_ecosystem,
    handle_synthesize_mcp_bridge,
)
from eco_support.mcp.tools.security_auditor import MCPSecurityAuditor

logger = get_logger(__name__)


def create_mcp_server() -> FastMCP:
    """Instantiates and registers all EcoSupport FastMCP tools."""
    mcp = FastMCP(
        name="EcoSupport MCP Server",
        instructions="Autonomous Ecosystem Radar and Diagnostic Support Server for Niche Open Source Foundations.",
    )

    @mcp.tool(
        name="scan_niche_ecosystem",
        description="Scans and identifies fragile, single-maintainer open source repositories with high downstream impact.",
    )
    async def scan_niche_ecosystem(category: str = "c-ffi", limit: int = 5) -> dict[str, Any]:
        """
        Scans a specific category of niche repositories.
        Args:
            category: c-ffi, geospatial, bio-ml, mcp-connectors, typing-infrastructure, general-niche
            limit: Maximum candidates to retrieve (1 to 20)
        """
        return await handle_scan_niche_ecosystem(category, limit)

    @mcp.tool(
        name="diagnose_repo_bottleneck",
        description="Leverages Claude 3.7 Sonnet with Extended Thinking to deeply diagnose complex bugs in niche libraries.",
    )
    async def diagnose_repo_bottleneck(
        repo: str, issue_title: str, issue_description: str, thinking_budget: int = 4096
    ) -> dict[str, Any]:
        """
        Performs multi-step root cause analysis on a bug report.
        Args:
            repo: Repository slug in owner/name format
            issue_title: Title of the issue
            issue_description: Stack trace or bug description
            thinking_budget: Thinking tokens allocated (default 4096)
        """
        return await handle_diagnose_repo_bottleneck(
            repo, issue_title, issue_description, thinking_budget
        )

    @mcp.tool(
        name="synthesize_mcp_bridge",
        description="Automatically synthesizes a compliant, secure FastMCP 2.0 server for any legacy or niche Python package.",
    )
    async def synthesize_mcp_bridge(
        package_name: str, module_summary: str, intended_audience: str = "AI Agents"
    ) -> dict[str, Any]:
        """
        Generates FastMCP server code for a package.
        Args:
            package_name: Name of the Python library
            module_summary: Functions, classes, and workflows to expose
            intended_audience: Target consumers of the MCP tool
        """
        return await handle_synthesize_mcp_bridge(package_name, module_summary, intended_audience)

    @mcp.tool(
        name="audit_mcp_security",
        description="Audits an MCP server Python source code for SSRF, injection, and path traversal vulnerabilities.",
    )
    def audit_mcp_security(server_name: str, source_code: str) -> dict[str, Any]:
        """
        Statically audits MCP tool definitions for security compliance.
        Args:
            server_name: Identifier for the server
            source_code: Python code of the FastMCP server
        """
        auditor = MCPSecurityAuditor()
        report = auditor.audit_tool_source(server_name, source_code)
        return report.model_dump()

    return mcp


def run_mcp_server(transport: str = "stdio", host: str = "127.0.0.1", port: int = 8000) -> None:
    """Launches the FastMCP server with the specified transport."""
    server = create_mcp_server()
    logger.info("Starting EcoSupport FastMCP server via transport: %s", transport)
    if transport == "stdio":
        server.run(transport="stdio")
    else:
        server.run(transport="sse", host=host, port=port)
