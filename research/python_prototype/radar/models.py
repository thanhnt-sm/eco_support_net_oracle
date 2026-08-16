"""Pydantic data models for the Niche Ecosystem Radar."""

from __future__ import annotations

from enum import Enum

from pydantic import BaseModel, Field


class EcosystemCategory(str, Enum):
    C_FFI = "c-ffi"
    GEOSPATIAL = "geospatial"
    BIO_ML = "bio-ml"
    MCP_CONNECTORS = "mcp-connectors"
    TYPING_INFRASTRUCTURE = "typing-infrastructure"
    HARDWARE_EDGE = "hardware-edge"
    GENERAL_NICHE = "general-niche"


class RiskTier(str, Enum):
    CRITICAL_EMERGENCY = "TIER_1_CRITICAL_EMERGENCY"
    HIGH_URGENCY = "TIER_2_HIGH_URGENCY"
    MODERATE = "TIER_3_MODERATE"
    STABLE = "TIER_4_STABLE"


class RepoHealthMetrics(BaseModel):
    """Detailed health and fragility telemetry for a repository."""

    repo: str = Field(description="Full repo slug owner/name")
    downstream_dependents: int = Field(
        default=0, description="Estimated count of downstream dependent repos/packages"
    )
    active_maintainers: int = Field(default=1, description="Active committers in the past 180 days")
    stale_issues_count: int = Field(
        default=0, description="Open issues with no activity in > 60 days"
    )
    days_since_last_commit: int = Field(
        default=0, description="Days elapsed since last main branch commit"
    )
    security_vulnerabilities: int = Field(
        default=0, description="Count of open security advisories/CVEs"
    )
    has_mcp_support: bool = Field(
        default=False, description="Whether an official or active MCP server exists"
    )
    stars: int = Field(default=0, description="GitHub stars (for baseline context)")


class NicheCandidate(BaseModel):
    """An analyzed niche open-source candidate prioritized for ecosystem support."""

    repo: str
    category: EcosystemCategory
    eci_score: float = Field(description="Ecosystem Criticality Index score [0.0 - 100.0]")
    risk_tier: RiskTier
    health_metrics: RepoHealthMetrics
    summary_diagnosis: str
    recommended_action: str


class EcosystemReport(BaseModel):
    """Summary report across an entire category or batch scan."""

    category: EcosystemCategory
    scanned_count: int
    critical_count: int
    high_urgency_count: int
    top_candidates: list[NicheCandidate]
    scan_timestamp: str
