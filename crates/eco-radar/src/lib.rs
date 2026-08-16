#![forbid(unsafe_code)]
//! # EcoSupport Radar (Rust Native)
//!
//! Niche ecosystem discovery, maintainer burnout metrics, and Ecosystem Criticality Index (ECI) engine.

pub mod calculator;
pub mod models;
pub mod scanner;

pub use calculator::CriticalityCalculator;
pub use models::{EcosystemCategory, EcosystemReport, NicheCandidate, RepoHealthMetrics, RiskTier};
pub use scanner::NicheScanner;
