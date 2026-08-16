"""Ecosystem Health Analyzer and Criticality Index calculator."""

from __future__ import annotations

import math

from eco_support.radar.models import (
    EcosystemCategory,
    NicheCandidate,
    RepoHealthMetrics,
    RiskTier,
)


class HealthAnalyzer:
    """Evaluates repository vulnerability and computes the Ecosystem Criticality Index (ECI)."""

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

    def evaluate_metrics(self, m: RepoHealthMetrics, category: EcosystemCategory) -> NicheCandidate:
        """Evaluates health telemetry and produces a prioritized NicheCandidate."""
        eci_score = self.compute_eci(m)
        risk_tier = self.classify_tier(eci_score)
        diagnosis, action = self._generate_recommendations(m, category, risk_tier)

        return NicheCandidate(
            repo=m.repo,
            category=category,
            eci_score=eci_score,
            risk_tier=risk_tier,
            health_metrics=m,
            summary_diagnosis=diagnosis,
            recommended_action=action,
        )

    def compute_eci(self, m: RepoHealthMetrics) -> float:
        """Computes the weighted Ecosystem Criticality Index [0.0 - 100.0]."""
        # 1. Normalized Downstream Dependencies (log scale up to 10k deps)
        dep_factor = min(10.0, math.log10(max(1, m.downstream_dependents) + 1) * 2.5)
        norm_deps = (dep_factor / 10.0) * 100.0

        # 2. Maintainer Burnout & Staleness Penalty
        effective_maintainers = max(1, m.active_maintainers)
        staleness_ratio = m.stale_issues_count / effective_maintainers
        staleness_penalty = min(100.0, (staleness_ratio * 3.0) + (m.days_since_last_commit / 3.0))

        # 3. Security Exposure
        security_score = min(100.0, m.security_vulnerabilities * 25.0)

        # 4. MCP Protocol Gap
        mcp_gap_score = 0.0 if m.has_mcp_support else 100.0

        composite = (
            self.w_deps * norm_deps
            + self.w_burnout * staleness_penalty
            + self.w_security * security_score
            + self.w_mcp * mcp_gap_score
        )

        return round(min(100.0, max(0.0, composite)), 2)

    @staticmethod
    def classify_tier(score: float) -> RiskTier:
        """Categorizes score into actionable RiskTiers."""
        if score >= 70.0:
            return RiskTier.CRITICAL_EMERGENCY
        if score >= 45.0:
            return RiskTier.HIGH_URGENCY
        if score >= 25.0:
            return RiskTier.MODERATE
        return RiskTier.STABLE

    @staticmethod
    def _generate_recommendations(
        m: RepoHealthMetrics, category: EcosystemCategory, tier: RiskTier
    ) -> tuple[str, str]:
        """Generates contextual diagnosis and recommended agent action."""
        diagnosis_parts = []
        if m.active_maintainers <= 1:
            diagnosis_parts.append(
                f"Single maintainer sustaining {m.downstream_dependents} downstream packages"
            )
        if m.stale_issues_count > 15:
            diagnosis_parts.append(f"High issue backlog ({m.stale_issues_count} unassigned issues)")
        if not m.has_mcp_support:
            diagnosis_parts.append("Missing Model Context Protocol bridge for AI integration")
        if m.security_vulnerabilities > 0:
            diagnosis_parts.append(
                f"Unpatched security advisories ({m.security_vulnerabilities} CVEs)"
            )

        diagnosis = "; ".join(diagnosis_parts) or "Healthy baseline state."

        if tier == RiskTier.CRITICAL_EMERGENCY:
            action = "Trigger autonomous Claude 3.7 Triage Swarm & Generate High-Priority Patch PR."
        elif tier == RiskTier.HIGH_URGENCY:
            if not m.has_mcp_support:
                action = "Deploy FastMCP Bridge Builder to generate standardized AI connector."
            else:
                action = "Run TriageAgent to deconstruct stale bug threads."
        else:
            action = "Monitor weekly on telemetry radar."

        return diagnosis, action
