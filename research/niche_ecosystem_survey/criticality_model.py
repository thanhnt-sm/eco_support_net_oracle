"""
EcoSupport Research: Ecosystem Criticality Index (ECI) Algorithm.

Standalone quantitative scoring module used by the research unit to rank
open-source projects based on system fragility rather than star count.
"""

from __future__ import annotations

import math
from dataclasses import dataclass


@dataclass
class EcosystemMetrics:
    repo_name: str
    downstream_deps: int
    stale_issues: int
    active_maintainers: int
    security_vulnerabilities: int
    has_mcp_support: bool
    days_since_last_commit: int
    stars: int = 0


class CriticalityModel:
    """Computes the Ecosystem Criticality Index (ECI) for an open-source library."""

    def __init__(
        self,
        weight_deps: float = 0.35,
        weight_burnout: float = 0.25,
        weight_security: float = 0.25,
        weight_mcp_gap: float = 0.15,
    ) -> None:
        self.w_deps = weight_deps
        self.w_burnout = weight_burnout
        self.w_security = weight_security
        self.w_mcp = weight_mcp_gap

    def calculate_score(self, m: EcosystemMetrics) -> float:
        """
        Calculate normalized ECI score in range [0.0, 100.0].

        Higher score = higher priority for autonomous eco-support.
        """
        # 1. Dependency impact component (log-scaled)
        # 100k deps yields ~5.0 log factor
        dep_factor = min(10.0, math.log10(max(1, m.downstream_deps) + 1) * 2.0)
        norm_deps = (dep_factor / 10.0) * 100.0

        # 2. Maintainer burnout & stale issue factor
        effective_maintainers = max(1, m.active_maintainers)
        staleness_ratio = m.stale_issues / effective_maintainers
        staleness_penalty = min(100.0, staleness_ratio * 2.5 + (m.days_since_last_commit / 3.0))

        # 3. Security vulnerability risk factor
        security_score = min(100.0, m.security_vulnerabilities * 25.0)

        # 4. MCP gap factor (100 if no MCP support exists for an AI-adjacent tool)
        mcp_gap_score = 0.0 if m.has_mcp_support else 100.0

        # Weighted composite score
        composite = (
            self.w_deps * norm_deps
            + self.w_burnout * staleness_penalty
            + self.w_security * security_score
            + self.w_mcp * mcp_gap_score
        )

        return round(min(100.0, max(0.0, composite)), 2)

    def rank_ecosystem(self, dataset: list[EcosystemMetrics]) -> list[dict[str, any]]:
        """Rank a batch of repositories by urgency."""
        results = []
        for item in dataset:
            score = self.calculate_score(item)
            results.append(
                {
                    "repo": item.repo_name,
                    "eci_score": score,
                    "downstream_deps": item.downstream_deps,
                    "maintainers": item.active_maintainers,
                    "stale_issues": item.stale_issues,
                    "priority_tier": self._classify_tier(score),
                }
            )
        return sorted(results, key=lambda x: x["eci_score"], reverse=True)

    @staticmethod
    def _classify_tier(score: float) -> str:
        if score >= 75.0:
            return "TIER_1_CRITICAL_EMERGENCY"
        if score >= 50.0:
            return "TIER_2_HIGH_URGENCY"
        if score >= 30.0:
            return "TIER_3_MODERATE"
        return "TIER_4_STABLE"


if __name__ == "__main__":
    # Test execution with sample representative niche packages
    sample_data = [
        EcosystemMetrics(
            repo_name="esoteric-simd/cffi-tensor-align",
            downstream_deps=4200,
            stale_issues=34,
            active_maintainers=1,
            security_vulnerabilities=2,
            has_mcp_support=False,
            days_since_last_commit=145,
            stars=210,
        ),
        EcosystemMetrics(
            repo_name="geospatial/raster-fast-io",
            downstream_deps=1200,
            stale_issues=12,
            active_maintainers=1,
            security_vulnerabilities=0,
            has_mcp_support=False,
            days_since_last_commit=60,
            stars=150,
        ),
        EcosystemMetrics(
            repo_name="mainstream/super-popular-cli",
            downstream_deps=500,
            stale_issues=5,
            active_maintainers=15,
            security_vulnerabilities=0,
            has_mcp_support=True,
            days_since_last_commit=2,
            stars=35000,
        ),
    ]

    model = CriticalityModel()
    ranked = model.rank_ecosystem(sample_data)
    print("--- ECOSYSTEM CRITICALITY INDEX (ECI) RESEARCH RANKING ---")
    for r in ranked:
        print(f"[{r['priority_tier']}] {r['repo']} -> ECI: {r['eci_score']}")
