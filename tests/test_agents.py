"""Unit tests for EcoSupport agent swarm."""

import pytest

from eco_support.agents.doc_bridge_agent import DocBridgeAgent
from eco_support.agents.patch_synthesizer import PatchSynthesizerAgent
from eco_support.agents.triage_agent import TriageAgent


@pytest.mark.asyncio
async def test_triage_agent_execution() -> None:
    """Verifies that TriageAgent generates structured triage results."""
    agent = TriageAgent()
    result = await agent.triage_issue(
        repo="py-simd/tokenizer",
        issue_id="42",
        title="Segmentation fault under multi-threading",
        body="Calling tokenize() concurrently causes memory corruption at worker thread exit.",
        thinking_budget=2048,
    )

    assert result is not None
    assert result.repo == "py-simd/tokenizer"
    assert result.issue_id == "42"
    assert result.formatted_maintainer_reply != ""


@pytest.mark.asyncio
async def test_patch_synthesizer_agent() -> None:
    """Verifies that PatchSynthesizerAgent generates patch diffs."""
    agent = PatchSynthesizerAgent()
    result = await agent.synthesize_patch(
        repo="esoteric/cffi-tensor",
        problem_statement="Null pointer dereference when shape is empty tuple",
        relevant_code_snippet="int reshape_tensor(Tensor* t) { return t->data[0]; }",
        thinking_budget=4096,
    )

    assert result is not None
    assert result.repo == "esoteric/cffi-tensor"
    assert result.safety_audit_passed
    assert result.git_diff != ""


@pytest.mark.asyncio
async def test_doc_bridge_agent() -> None:
    """Verifies that DocBridgeAgent synthesizes FastMCP server files."""
    agent = DocBridgeAgent()
    result = await agent.generate_mcp_bridge(
        package_name="custom-raster-io",
        api_signatures="read_band(file_path: str, band_id: int) -> list[float]",
        thinking_budget=2048,
    )

    assert result is not None
    assert result.package_name == "custom-raster-io"
    assert "mcp" in result.mcp_server_filename
    assert result.server_source_code != ""
