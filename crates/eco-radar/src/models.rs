//! Data models and schemas for the EcoSupport Niche Ecosystem Radar.

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum EcosystemCategory {
    CFfi,
    Geospatial,
    BioMl,
    McpConnectors,
    TypingInfrastructure,
    HardwareEdge,
    GeneralNiche,
}

impl EcosystemCategory {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::CFfi => "c-ffi",
            Self::Geospatial => "geospatial",
            Self::BioMl => "bio-ml",
            Self::McpConnectors => "mcp-connectors",
            Self::TypingInfrastructure => "typing-infrastructure",
            Self::HardwareEdge => "hardware-edge",
            Self::GeneralNiche => "general-niche",
        }
    }

    pub fn from_str_lenient(s: &str) -> Self {
        match s.to_lowercase().as_str() {
            "c-ffi" | "cffi" | "ffi" => Self::CFfi,
            "geospatial" | "geo" | "raster" => Self::Geospatial,
            "bio-ml" | "bio" => Self::BioMl,
            "mcp-connectors" | "mcp" => Self::McpConnectors,
            "typing-infrastructure" | "typing" => Self::TypingInfrastructure,
            "hardware-edge" | "edge" => Self::HardwareEdge,
            _ => Self::GeneralNiche,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum RiskTier {
    #[serde(rename = "TIER_1_CRITICAL_EMERGENCY")]
    CriticalEmergency,
    #[serde(rename = "TIER_2_HIGH_URGENCY")]
    HighUrgency,
    #[serde(rename = "TIER_3_MODERATE")]
    Moderate,
    #[serde(rename = "TIER_4_STABLE")]
    Stable,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RepoHealthMetrics {
    pub repo: String,
    pub downstream_dependents: u64,
    pub active_maintainers: u32,
    pub stale_issues_count: u32,
    pub days_since_last_commit: u32,
    pub security_vulnerabilities: u32,
    pub has_mcp_support: bool,
    pub stars: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NicheCandidate {
    pub repo: String,
    pub category: EcosystemCategory,
    pub eci_score: f64,
    pub risk_tier: RiskTier,
    pub health_metrics: RepoHealthMetrics,
    pub summary_diagnosis: String,
    pub recommended_action: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EcosystemReport {
    pub category: EcosystemCategory,
    pub scanned_count: usize,
    pub critical_count: usize,
    pub high_urgency_count: usize,
    pub top_candidates: Vec<NicheCandidate>,
    pub scan_timestamp: String,
}
