//! Ecosystem Criticality Index (ECI) Calculation Engine.

use crate::models::{EcosystemCategory, NicheCandidate, RepoHealthMetrics, RiskTier};

#[derive(Debug, Clone)]
pub struct CriticalityCalculator {
    pub weight_deps: f64,
    pub weight_burnout: f64,
    pub weight_security: f64,
    pub weight_mcp_gap: f64,
}

impl Default for CriticalityCalculator {
    fn default() -> Self {
        Self {
            weight_deps: 0.35,
            weight_burnout: 0.25,
            weight_security: 0.25,
            weight_mcp_gap: 0.15,
        }
    }
}

impl CriticalityCalculator {
    pub fn evaluate(&self, m: &RepoHealthMetrics, category: EcosystemCategory) -> NicheCandidate {
        let eci_score = self.compute_eci(m);
        let risk_tier = self.classify_tier(eci_score);
        let (diagnosis, action) = self.generate_recommendations(m, risk_tier);

        NicheCandidate {
            repo: m.repo.clone(),
            category,
            eci_score,
            risk_tier,
            health_metrics: m.clone(),
            summary_diagnosis: diagnosis,
            recommended_action: action,
        }
    }

    pub fn compute_eci(&self, m: &RepoHealthMetrics) -> f64 {
        // 1. Dependency impact factor (log scale up to 10k deps)
        let dep_log = ((m.downstream_dependents.max(1) as f64) + 1.0).log10() * 2.5;
        let norm_deps = (dep_log.min(10.0) / 10.0) * 100.0;

        // 2. Maintainer burnout and staleness
        let effective_maintainers = (m.active_maintainers.max(1)) as f64;
        let staleness_ratio = (m.stale_issues_count as f64) / effective_maintainers;
        let staleness_penalty =
            ((staleness_ratio * 3.0) + ((m.days_since_last_commit as f64) / 3.0)).min(100.0);

        // 3. Security exposure
        let security_score = ((m.security_vulnerabilities as f64) * 25.0).min(100.0);

        // 4. MCP gap penalty
        let mcp_gap_score = if m.has_mcp_support { 0.0 } else { 100.0 };

        let composite = self.weight_deps * norm_deps
            + self.weight_burnout * staleness_penalty
            + self.weight_security * security_score
            + self.weight_mcp_gap * mcp_gap_score;

        (composite.clamp(0.0, 100.0) * 100.0).round() / 100.0
    }

    pub fn classify_tier(&self, score: f64) -> RiskTier {
        if score >= 70.0 {
            RiskTier::CriticalEmergency
        } else if score >= 45.0 {
            RiskTier::HighUrgency
        } else if score >= 25.0 {
            RiskTier::Moderate
        } else {
            RiskTier::Stable
        }
    }

    fn generate_recommendations(&self, m: &RepoHealthMetrics, tier: RiskTier) -> (String, String) {
        let mut diagnosis_parts = Vec::new();
        if m.active_maintainers <= 1 {
            diagnosis_parts.push(format!(
                "Single maintainer sustaining {} downstream packages",
                m.downstream_dependents
            ));
        }
        if m.stale_issues_count > 15 {
            diagnosis_parts.push(format!(
                "High issue backlog ({} unassigned issues)",
                m.stale_issues_count
            ));
        }
        if !m.has_mcp_support {
            diagnosis_parts
                .push("Missing Model Context Protocol bridge for AI integration".to_string());
        }
        if m.security_vulnerabilities > 0 {
            diagnosis_parts.push(format!(
                "Unpatched security advisories ({} CVEs)",
                m.security_vulnerabilities
            ));
        }

        let diagnosis = if diagnosis_parts.is_empty() {
            "Healthy baseline state.".to_string()
        } else {
            diagnosis_parts.join("; ")
        };

        let action = match tier {
            RiskTier::CriticalEmergency => {
                "Trigger autonomous Claude 3.7 Triage Swarm & Generate High-Priority Patch PR."
            }
            RiskTier::HighUrgency => {
                if !m.has_mcp_support {
                    "Deploy FastMCP Bridge Builder to generate standardized AI connector."
                } else {
                    "Run TriageAgent to deconstruct stale bug threads."
                }
            }
            RiskTier::Moderate => "Monitor weekly on telemetry radar.",
            RiskTier::Stable => "Routine periodic indexing.",
        };

        (diagnosis, action.to_string())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::{EcosystemCategory, RepoHealthMetrics, RiskTier};

    /// Helper: construct a minimal healthy repo fixture.
    fn stable_repo() -> RepoHealthMetrics {
        RepoHealthMetrics {
            repo: "test-org/stable-lib".to_string(),
            downstream_dependents: 10,
            active_maintainers: 5,
            stale_issues_count: 1,
            days_since_last_commit: 7,
            security_vulnerabilities: 0,
            has_mcp_support: true,
            stars: 500,
        }
    }

    /// Helper: construct a maximally distressed repo fixture.
    fn critical_repo() -> RepoHealthMetrics {
        RepoHealthMetrics {
            repo: "abandoned-org/core-lib".to_string(),
            downstream_dependents: 50_000,
            active_maintainers: 1,
            stale_issues_count: 200,
            days_since_last_commit: 730,
            security_vulnerabilities: 4,
            has_mcp_support: false,
            stars: 20,
        }
    }

    #[test]
    fn test_eci_stable_repo_is_low() {
        let calc = CriticalityCalculator::default();
        let score = calc.compute_eci(&stable_repo());
        // Healthy repo: low dep-load, many maintainers, no vulns, has MCP → score < 25
        assert!(
            score < 25.0,
            "Stable repo ECI should be < 25.0, got {score:.2}"
        );
    }

    #[test]
    fn test_eci_critical_repo_is_high() {
        let calc = CriticalityCalculator::default();
        let score = calc.compute_eci(&critical_repo());
        // Critically distressed repo → ECI ≥ 70 (CriticalEmergency tier)
        assert!(
            score >= 70.0,
            "Critical repo ECI should be ≥ 70.0, got {score:.2}"
        );
    }

    #[test]
    fn test_eci_score_clamped_to_100() {
        let calc = CriticalityCalculator::default();
        let score = calc.compute_eci(&critical_repo());
        assert!(
            score <= 100.0,
            "ECI must never exceed 100.0, got {score:.2}"
        );
    }

    #[test]
    fn test_eci_score_never_negative() {
        let calc = CriticalityCalculator::default();
        let score = calc.compute_eci(&stable_repo());
        assert!(score >= 0.0, "ECI must never be negative, got {score:.2}");
    }

    #[test]
    fn test_classify_tier_critical_emergency() {
        let calc = CriticalityCalculator::default();
        assert_eq!(calc.classify_tier(70.0), RiskTier::CriticalEmergency);
        assert_eq!(calc.classify_tier(99.9), RiskTier::CriticalEmergency);
    }

    #[test]
    fn test_classify_tier_high_urgency() {
        let calc = CriticalityCalculator::default();
        assert_eq!(calc.classify_tier(45.0), RiskTier::HighUrgency);
        assert_eq!(calc.classify_tier(69.9), RiskTier::HighUrgency);
    }

    #[test]
    fn test_classify_tier_moderate() {
        let calc = CriticalityCalculator::default();
        assert_eq!(calc.classify_tier(25.0), RiskTier::Moderate);
        assert_eq!(calc.classify_tier(44.9), RiskTier::Moderate);
    }

    #[test]
    fn test_classify_tier_stable() {
        let calc = CriticalityCalculator::default();
        assert_eq!(calc.classify_tier(0.0), RiskTier::Stable);
        assert_eq!(calc.classify_tier(24.9), RiskTier::Stable);
    }

    #[test]
    fn test_mcp_gap_increases_score() {
        let calc = CriticalityCalculator::default();
        let mut with_mcp = stable_repo();
        with_mcp.has_mcp_support = true;
        let mut without_mcp = stable_repo();
        without_mcp.has_mcp_support = false;

        let score_with = calc.compute_eci(&with_mcp);
        let score_without = calc.compute_eci(&without_mcp);
        assert!(
            score_without > score_with,
            "Missing MCP support should increase ECI score: {score_without:.2} > {score_with:.2}"
        );
    }

    #[test]
    fn test_security_vuln_increases_score() {
        let calc = CriticalityCalculator::default();
        let mut no_vuln = stable_repo();
        no_vuln.security_vulnerabilities = 0;
        let mut with_vuln = stable_repo();
        with_vuln.security_vulnerabilities = 2;

        let score_safe = calc.compute_eci(&no_vuln);
        let score_vuln = calc.compute_eci(&with_vuln);
        assert!(
            score_vuln > score_safe,
            "Security vulns should increase ECI: {score_vuln:.2} > {score_safe:.2}"
        );
    }

    #[test]
    fn test_weights_sum_to_one() {
        let calc = CriticalityCalculator::default();
        let total =
            calc.weight_deps + calc.weight_burnout + calc.weight_security + calc.weight_mcp_gap;
        assert!(
            (total - 1.0_f64).abs() < 1e-9,
            "ECI weights must sum to 1.0, got {total}"
        );
    }

    #[test]
    fn test_evaluate_returns_correct_repo_name() {
        let calc = CriticalityCalculator::default();
        let m = critical_repo();
        let candidate = calc.evaluate(&m, EcosystemCategory::CFfi);
        assert_eq!(candidate.repo, "abandoned-org/core-lib");
        assert_eq!(candidate.category, EcosystemCategory::CFfi);
    }

    #[test]
    fn test_evaluate_critical_repo_triggers_emergency_action() {
        let calc = CriticalityCalculator::default();
        let candidate = calc.evaluate(&critical_repo(), EcosystemCategory::GeneralNiche);
        assert_eq!(candidate.risk_tier, RiskTier::CriticalEmergency);
        assert!(
            candidate.recommended_action.contains("Triage Swarm"),
            "Critical repos must recommend the Triage Swarm action"
        );
    }

    #[test]
    fn test_seed_registry_fixtures_evaluation() {
        use std::path::Path;
        let seed_path = Path::new("../../research/data/niche_seed_registry.json");
        let alt_path = Path::new("research/data/niche_seed_registry.json");

        let path = if seed_path.exists() {
            seed_path
        } else if alt_path.exists() {
            alt_path
        } else {
            return; // Skip if run in isolated directory without workspace root
        };

        let content = std::fs::read_to_string(path).expect("Read seed fixture");
        let json: serde_json::Value = serde_json::from_str(&content).expect("Parse JSON");
        let repos = json
            .get("repositories")
            .and_then(|r| r.as_array())
            .expect("Repo array");
        let calc = CriticalityCalculator::default();

        for item in repos {
            let repo_name = item.get("repo").and_then(|r| r.as_str()).unwrap_or("");
            let risk_level = item
                .get("risk_level")
                .and_then(|r| r.as_str())
                .unwrap_or("");
            let m = RepoHealthMetrics {
                repo: repo_name.to_string(),
                downstream_dependents: item
                    .get("downstream_dependents")
                    .and_then(|d| d.as_u64())
                    .unwrap_or(100),
                active_maintainers: item
                    .get("maintainer_count")
                    .and_then(|m| m.as_u64())
                    .unwrap_or(1) as u32,
                stale_issues_count: item
                    .get("stale_issues_count")
                    .and_then(|s| s.as_u64())
                    .unwrap_or(10) as u32,
                days_since_last_commit: 90,
                security_vulnerabilities: if risk_level == "CRITICAL" { 1 } else { 0 },
                has_mcp_support: item
                    .get("has_mcp_server")
                    .and_then(|m| m.as_bool())
                    .unwrap_or(false),
                stars: 200,
            };

            let candidate = calc.evaluate(&m, EcosystemCategory::GeneralNiche);
            if risk_level == "CRITICAL" {
                assert!(
                    candidate.risk_tier == RiskTier::CriticalEmergency
                        || candidate.risk_tier == RiskTier::HighUrgency,
                    "Expected high or critical tier for {}, got {:?}",
                    repo_name,
                    candidate.risk_tier
                );
            }
        }
    }
}
