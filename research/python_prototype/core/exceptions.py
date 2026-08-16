"""Hierarchical exception classes for EcoSupport."""

from __future__ import annotations

from typing import Any


class EcoSupportError(Exception):
    """Base exception for all EcoSupport operations."""

    def __init__(self, message: str, details: dict[str, Any] | None = None) -> None:
        super().__init__(message)
        self.message = message
        self.details = details or {}


class RadarScanError(EcoSupportError):
    """Raised when niche radar scanning or parsing fails."""

    pass


class LLMProviderError(EcoSupportError):
    """Raised when Anthropic API calls or reasoning generation encounters failure."""

    pass


class MCPServerError(EcoSupportError):
    """Raised when FastMCP server lifecycle or tool dispatch fails."""

    pass


class SecurityAuditError(EcoSupportError):
    """Raised when an MCP server fails safety or policy audits."""

    pass
