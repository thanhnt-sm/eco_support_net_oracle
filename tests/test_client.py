"""Unit tests for ClaudeClient and Thinking Response management."""

import pytest

from eco_support.core.client import ClaudeClient, ThinkingResponse
from eco_support.core.config import Settings


@pytest.mark.asyncio
async def test_claude_client_simulation_mode() -> None:
    """Tests that ClaudeClient gracefully falls back to deterministic simulation when unkeyed."""
    settings = Settings(ANTHROPIC_API_KEY=None, THINKING_BUDGET_TOKENS=2048)
    client = ClaudeClient(settings=settings)

    assert not client.is_available

    response: ThinkingResponse = await client.generate_with_thinking(
        prompt="Analyze memory bug in C-FFI wrapper",
        thinking_budget=2048,
    )

    assert response is not None
    assert response.thinking is not None
    assert "Simulation" in response.thinking
    assert response.thinking_tokens == 2048
    assert response.content != ""


@pytest.mark.asyncio
async def test_thinking_response_model() -> None:
    """Verifies Pydantic schema validation on ThinkingResponse."""
    resp = ThinkingResponse(
        thinking="Step 1: Check pointers...",
        content="Diagnosis completed.",
        model="claude-3-7-sonnet-20250219",
        prompt_tokens=100,
        completion_tokens=200,
        thinking_tokens=1024,
    )
    assert resp.prompt_tokens == 100
    assert resp.completion_tokens == 200
    assert resp.thinking_tokens == 1024
