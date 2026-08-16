use eco_radar::{CriticalityCalculator, EcosystemCategory, RepoHealthMetrics, RiskTier};

#[test]
fn test_eci_critical_fragility_calculation() {
    let calculator = CriticalityCalculator::default();

    let fragile = RepoHealthMetrics {
        repo: "vulnerable/cffi-tensor".to_string(),
        downstream_dependents: 5000,
        active_maintainers: 1,
        stale_issues_count: 45,
        days_since_last_commit: 120,
        security_vulnerabilities: 2,
        has_mcp_support: false,
        stars: 150,
    };

    let candidate = calculator.evaluate(&fragile, EcosystemCategory::CFfi);

    assert!(candidate.eci_score > 60.0);
    assert!(matches!(
        candidate.risk_tier,
        RiskTier::CriticalEmergency | RiskTier::HighUrgency
    ));
    assert!(candidate.summary_diagnosis.contains("Single maintainer"));
}

#[test]
fn test_eci_stable_repo_calculation() {
    let calculator = CriticalityCalculator::default();

    let healthy = RepoHealthMetrics {
        repo: "active/popular-repo".to_string(),
        downstream_dependents: 100,
        active_maintainers: 10,
        stale_issues_count: 2,
        days_since_last_commit: 1,
        security_vulnerabilities: 0,
        has_mcp_support: true,
        stars: 10000,
    };

    let candidate = calculator.evaluate(&healthy, EcosystemCategory::GeneralNiche);

    assert!(candidate.eci_score < 40.0);
    assert!(matches!(
        candidate.risk_tier,
        RiskTier::Stable | RiskTier::Moderate
    ));
}
