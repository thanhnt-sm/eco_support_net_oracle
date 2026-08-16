"""Anthropic Claude 3.7 Sonnet client wrapper with Extended Thinking support."""

from __future__ import annotations

from typing import Any

import anthropic
from pydantic import BaseModel

from eco_support.core.config import Settings, get_settings
from eco_support.core.exceptions import LLMProviderError
from eco_support.core.telemetry import get_logger, log_token_usage

logger = get_logger(__name__)


class ThinkingResponse(BaseModel):
    """Encapsulates response text along with Claude's extended thinking trace."""

    thinking: str | None = None
    content: str
    model: str
    prompt_tokens: int = 0
    completion_tokens: int = 0
    thinking_tokens: int = 0


class ClaudeClient:
    """Production client for interacting with Anthropic Claude 3.7 Sonnet."""

    def __init__(self, settings: Settings | None = None) -> None:
        self.settings = settings or get_settings()
        self.api_key = self.settings.anthropic_api_key
        self._client: anthropic.AsyncAnthropic | None = None
        if self.api_key:
            self._client = anthropic.AsyncAnthropic(api_key=self.api_key)

    @property
    def is_available(self) -> bool:
        """Returns True if Anthropic API key is configured."""
        return self._client is not None

    async def generate_with_thinking(
        self,
        prompt: str,
        system_prompt: str | None = None,
        thinking_budget: int | None = None,
        max_tokens: int | None = None,
    ) -> ThinkingResponse:
        """
        Executes a call to Claude 3.7 Sonnet with Extended Thinking enabled.

        If API key is absent (e.g. in test or dry-run environments), provides a
        deterministic high-fidelity simulation.
        """
        budget = thinking_budget or self.settings.thinking_budget_tokens
        max_tok = max_tokens or self.settings.max_tokens

        # Fallback simulation if no API key is provided
        if not self._client:
            logger.warning(
                "Anthropic API key not configured. Using deterministic offline simulation mode."
            )
            return self._simulate_thinking_response(prompt, budget)

        try:
            messages = [{"role": "user", "content": prompt}]
            kwargs: dict[str, Any] = {
                "model": self.settings.anthropic_model,
                "max_tokens": max_tok,
                "messages": messages,
                "thinking": {
                    "type": "enabled",
                    "budget_tokens": budget,
                },
            }
            if system_prompt:
                kwargs["system"] = system_prompt

            response = await self._client.messages.create(**kwargs)

            # Extract thinking and text blocks
            thinking_text = None
            content_text = ""

            for block in response.content:
                if getattr(block, "type", None) == "thinking":
                    thinking_text = getattr(block, "thinking", "")
                elif getattr(block, "type", None) == "text":
                    content_text += getattr(block, "text", "")

            prompt_tokens = response.usage.input_tokens if hasattr(response, "usage") else 0
            completion_tokens = response.usage.output_tokens if hasattr(response, "usage") else 0

            log_token_usage(
                operation="generate_with_thinking",
                prompt_tokens=prompt_tokens,
                completion_tokens=completion_tokens,
                thinking_tokens=budget,
                metadata={"model": self.settings.anthropic_model},
            )

            return ThinkingResponse(
                thinking=thinking_text,
                content=content_text,
                model=self.settings.anthropic_model,
                prompt_tokens=prompt_tokens,
                completion_tokens=completion_tokens,
                thinking_tokens=budget,
            )

        except Exception as e:
            logger.error("Failed Anthropic API call: %s", str(e))
            raise LLMProviderError(f"Anthropic API execution failed: {e}") from e

    def _simulate_thinking_response(self, prompt: str, budget: int) -> ThinkingResponse:
        """Simulates high-signal diagnostic reasoning when testing in dry-run mode."""
        simulated_thought = (
            f"[Simulation - Claude 3.7 Thinking Mode ({budget} budget tokens)]\n"
            f"1. Deconstructing user context and stack frame signatures...\n"
            f"2. Performing C/Python boundary AST traversal and pointer lifecycle check...\n"
            f"3. Verifying non-breaking downstream compatibility invariants.\n"
        )
        simulated_output = (
            "### Automated EcoSupport Diagnosis (Simulation)\n\n"
            "**Analysis**: Successfully analyzed requested parameters.\n"
            "- Invariant Verification: PASSED\n"
            "- Proposed Solution: Recommended safe abstraction pattern with typing & test coverage.\n"
        )
        return ThinkingResponse(
            thinking=simulated_thought,
            content=simulated_output,
            model="claude-3-7-sonnet-20250219 (Simulated)",
            prompt_tokens=256,
            completion_tokens=512,
            thinking_tokens=budget,
        )
