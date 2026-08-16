"""Standard MCP tools exposed to Claude for ecosystem discovery and diagnosis."""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field

from eco_support.core.client import ClaudeClient
from eco_support.radar.models import EcosystemCategory
from eco_support.radar.niche_scanner import NicheScanner


class ScanCategoryInput(BaseModel):
    category: EcosystemCategory = Field(
        default=EcosystemCategory.C_FFI,
        description="Niche ecosystem category to scan (e.g. c-ffi, geospatial, bio-ml, mcp-connectors, typing-infrastructure)",
    )
    limit: int = Field(default=5, description="Maximum number of candidates to retrieve")


class DiagnoseRepoInput(BaseModel):
    repo: str = Field(description="Repository slug in format 'owner/repo'")
    issue_title: str = Field(description="Title of the bug or triage issue")
    issue_description: str = Field(description="Full text or stack trace of the issue")
    thinking_budget: int = Field(
        default=4096, description="Tokens allocated for Claude 3.7 reasoning"
    )


class SynthesizeMCPBridgeInput(BaseModel):
    package_name: str = Field(
        description="Name of the open-source package needing an MCP connector"
    )
    module_summary: str = Field(
        description="Summary of core functions and classes to expose as MCP tools"
    )
    intended_audience: str = Field(
        default="AI Agents & Scientific Workflows", description="Target consumer"
    )


async def handle_scan_niche_ecosystem(category: str, limit: int = 5) -> dict[str, Any]:
    """Scans and prioritizes fragile niche open-source repositories."""
    scanner = NicheScanner()
    try:
        cat_enum = EcosystemCategory(category)
    except ValueError:
        cat_enum = EcosystemCategory.GENERAL_NICHE

    report = await scanner.scan_category(cat_enum, limit=limit)
    return report.model_dump()


async def handle_diagnose_repo_bottleneck(
    repo: str, issue_title: str, issue_description: str, thinking_budget: int = 4096
) -> dict[str, Any]:
    """Diagnoses a complex bug in a niche library using Claude 3.7 Extended Thinking."""
    client = ClaudeClient()
    prompt = (
        f"You are the EcoSupport Triage Diagnostician.\n"
        f"Target Repository: {repo}\n"
        f"Issue Title: {issue_title}\n"
        f"Issue Description / Trace:\n{issue_description}\n\n"
        f"Perform an exhaustive root-cause analysis. Step through language boundaries, "
        f"concurrency invariants, and AST dependencies. Provide a minimal reproduction script and concrete fix."
    )
    system = "You are a world-class systems engineer diagnosing subtle bugs in open-source AI infrastructure."

    response = await client.generate_with_thinking(
        prompt=prompt, system_prompt=system, thinking_budget=thinking_budget
    )
    return {
        "repo": repo,
        "thinking_trace": response.thinking,
        "diagnostic_report": response.content,
        "model": response.model,
    }


async def handle_synthesize_mcp_bridge(
    package_name: str, module_summary: str, intended_audience: str = "AI Agents"
) -> dict[str, Any]:
    """Synthesizes a production-ready FastMCP 2.0 server for a legacy/niche library."""
    client = ClaudeClient()
    prompt = (
        f"Synthesize a complete, secure FastMCP 2.0 Python server for the package `{package_name}`.\n"
        f"Module Context: {module_summary}\n"
        f"Audience: {intended_audience}\n\n"
        f"Requirements:\n"
        f"1. Use FastMCP decorator syntax (@mcp.tool(), @mcp.resource()).\n"
        f"2. Strict Pydantic and typing validation.\n"
        f"3. Secure input sanitization (no SSRF, no unvetted filesystem access).\n"
        f"4. Provide a runnable Python script."
    )
    response = await client.generate_with_thinking(prompt=prompt, thinking_budget=4096)
    return {
        "package_name": package_name,
        "generated_mcp_server_code": response.content,
        "thinking_trace": response.thinking,
    }
