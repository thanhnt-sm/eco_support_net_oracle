"""Autonomous Issue Triage Agent powered by Claude 3.7 Sonnet Extended Thinking."""

from __future__ import annotations

from pydantic import BaseModel

from eco_support.core.client import ClaudeClient, ThinkingResponse
from eco_support.core.telemetry import get_logger

logger = get_logger(__name__)


class TriageResult(BaseModel):
    repo: str
    issue_id: str
    root_cause: str
    suggested_fix_summary: str
    reproduction_code: str | None = None
    thinking_trace: str | None = None
    formatted_maintainer_reply: str


class TriageAgent:
    """Agent that performs deep automated bug triage on niche repositories."""

    def __init__(self, client: ClaudeClient | None = None) -> None:
        self.client = client or ClaudeClient()

    async def triage_issue(
        self,
        repo: str,
        issue_id: str,
        title: str,
        body: str,
        thinking_budget: int = 4096,
    ) -> TriageResult:
        """Executes multi-step reasoning to diagnose a reported bug."""
        logger.info("TriageAgent analyzing %s issue #%s", repo, issue_id)

        prompt = (
            f"Target Repository: {repo}\n"
            f"Issue #{issue_id}: {title}\n\n"
            f"Issue Body & Logs:\n{body}\n\n"
            f"Task:\n"
            f"1. Diagnose root cause at language/FFI/concurrency boundaries.\n"
            f"2. Write a minimal standalone Python/C reproduction snippet.\n"
            f"3. Explain the precise architectural fix without breaking backward compatibility.\n"
            f"4. Format an empathetic, respectful draft reply for the repository maintainer."
        )

        system = (
            "You are the EcoSupport Senior Triage Agent. Your purpose is to reduce open-source "
            "maintainer fatigue by delivering mathematically precise, zero-fluff bug diagnoses."
        )

        response: ThinkingResponse = await self.client.generate_with_thinking(
            prompt=prompt,
            system_prompt=system,
            thinking_budget=thinking_budget,
        )

        reply_content = response.content

        return TriageResult(
            repo=repo,
            issue_id=issue_id,
            root_cause="Analyzed via Claude 3.7 Extended Thinking",
            suggested_fix_summary="Architectural fix and minimal reproduction synthesized.",
            reproduction_code="# See maintainer draft for standalone reproduction",
            thinking_trace=response.thinking,
            formatted_maintainer_reply=reply_content,
        )
