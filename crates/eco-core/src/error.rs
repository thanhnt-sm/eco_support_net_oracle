//! Error types for EcoSupport Core.

use thiserror::Error;

#[derive(Error, Debug)]
pub enum EcoError {
    #[error("Configuration error: {0}")]
    Config(String),

    #[error("Anthropic API provider error: {0}")]
    ApiProvider(String),

    #[error("Model Context Protocol (MCP) error: {0}")]
    Mcp(String),

    #[error("Radar scan failure: {0}")]
    RadarScan(String),

    #[error("Security policy violation: {0}")]
    SecurityViolation(String),

    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),

    #[error("JSON serialization error: {0}")]
    Serialization(#[from] serde_json::Error),
}

pub type Result<T> = std::result::Result<T, EcoError>;
