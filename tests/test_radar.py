"""Unit tests for Niche Radar and Ecosystem Criticality Index."""

import pytest

from eco_support.radar.health_analyzer import HealthAnalyzer
from eco_support.radar.models import (
    EcosystemCategory,
    RepoHealthMetrics,
    RiskTier,
)
from eco_support.radar.niche_scanner import NicheScanner


def test_eci_critical_fragility_calculation() -> None:
    """Verifies that high-dependency, single-maintainer repos score as critical risk."""
    analyzer = HealthAnalyzer()

    fragile_repo = RepoHealthMetrics(
        repo="vulnerable/cffi-tensor",
        downstream_dependents=5000,
        active_maintainers=1,
        stale_issues_count=45,
        days_since_last_commit=120,
        security_vulnerabilities=2,
        has_mcp_support=False,
        stars=150,
    )

    candidate = analyzer.evaluate_metrics(fragile_repo, EcosystemCategory.C_FFI)

    assert candidate.eci_score > 60.0
    assert candidate.risk_tier in [RiskTier.CRITICAL_EMERGENCY, RiskTier.HIGH_URGENCY]
    assert "Single maintainer" in candidate.summary_diagnosis


def test_eci_stable_repo_calculation() -> None:
    """Verifies that multi-maintainer, active repos score as stable/moderate."""
    analyzer = HealthAnalyzer()

    healthy_repo = RepoHealthMetrics(
        repo="active/popular-repo",
        downstream_dependents=100,
        active_maintainers=10,
        stale_issues_count=2,
        days_since_last_commit=1,
        security_vulnerabilities=0,
        has_mcp_support=True,
        stars=10000,
    )

    candidate = analyzer.evaluate_metrics(healthy_repo, EcosystemCategory.GENERAL_NICHE)

    assert candidate.eci_score < 40.0
    assert candidate.risk_tier in [RiskTier.STABLE, RiskTier.MODERATE]


@pytest.mark.asyncio
async def test_niche_scanner_scan_category() -> None:
    """Tests batch scanning across a niche category."""
    scanner = NicheScanner()
    report = await scanner.scan_category(EcosystemCategory.C_FFI, limit=3)

    assert report is not None
    assert report.category == EcosystemCategory.C_FFI
    assert len(report.top_candidates) <= 3
    assert len(report.top_candidates) > 0
