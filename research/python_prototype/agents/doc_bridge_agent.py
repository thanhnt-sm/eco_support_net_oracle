"""Documentation and FastMCP Bridge Synthesizer Agent."""

from __future__ import annotations

from pydantic import BaseModel

from eco_support.core.client import ClaudeClient, ThinkingResponse
from eco_support.core.telemetry import get_logger

logger = get_logger(__name__)


class BridgeResult(BaseModel):
    package_name: str
    mcp_server_filename: str
    server_source_code: str
    readme_markdown: str
    thinking_trace: str | None = None


class DocBridgeAgent:
    """Agent that produces FastMCP connectors and documentation for un-agentic niche libraries."""

    def __init__(self, client: ClaudeClient | None = None) -> None:
        self.client = client or ClaudeClient()

    async def generate_mcp_bridge(
        self,
        package_name: str,
        api_signatures: str,
        thinking_budget: int = 4096,
    ) -> BridgeResult:
        """Generates a complete FastMCP 2.0 server module."""
        logger.info("DocBridgeAgent generating FastMCP server for %s", package_name)

        prompt = (
            f"Package Name: {package_name}\n"
            f"API Signatures / Interface:\n{api_signatures}\n\n"
            f"Task: Generate a standalone FastMCP 2.0 Python server script named `server.py`.\n"
            f"It must expose each core operation as a `@mcp.tool()` with typed arguments and Pydantic validation."
        )

        response: ThinkingResponse = await self.client.generate_with_thinking(
            prompt=prompt,
            thinking_budget=thinking_budget,
        )

        server_code = response.content
        readme = f"# FastMCP Server for `{package_name}`\n\nGenerated autonomously by EcoSupport."

        return BridgeResult(
            package_name=package_name,
            mcp_server_filename=f"{package_name.replace('-', '_')}_mcp.py",
            server_source_code=server_code,
            readme_markdown=readme,
            thinking_trace=response.thinking,
        )
