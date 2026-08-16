"""Telemetry, audit logging, and token usage tracker for Claude agents."""

from __future__ import annotations

import logging
import sys
from typing import Any


def get_logger(name: str) -> logging.Logger:
    """Configures and returns a standard structured logger."""
    logger = logging.getLogger(name)
    if not logger.handlers:
        handler = logging.StreamHandler(sys.stdout)
        formatter = logging.Formatter(
            fmt="%(asctime)s [%(levelname)s] [%(name)s] %(message)s",
            datefmt="%Y-%m-%d %H:%M:%S",
        )
        handler.setFormatter(formatter)
        logger.addHandler(handler)
        logger.setLevel(logging.INFO)
    return logger


_logger = get_logger("eco_support.telemetry")


def log_token_usage(
    operation: str,
    prompt_tokens: int,
    completion_tokens: int,
    thinking_tokens: int = 0,
    metadata: dict[str, Any] | None = None,
) -> None:
    """Logs token consumption metrics including Anthropic Extended Thinking budget."""
    total_tokens = prompt_tokens + completion_tokens + thinking_tokens
    _logger.info(
        "Token Usage | Op: %s | Prompt: %d | Completion: %d | Thinking: %d | Total: %d | Meta: %s",
        operation,
        prompt_tokens,
        completion_tokens,
        thinking_tokens,
        total_tokens,
        metadata or {},
    )
