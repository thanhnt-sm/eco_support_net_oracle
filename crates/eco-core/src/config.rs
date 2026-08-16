//! Configuration management for EcoSupport.

use serde::{Deserialize, Serialize};
use std::env;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Config {
    pub anthropic_api_key: Option<String>,
    pub anthropic_model: String,
    pub thinking_budget_tokens: u32,
    pub max_tokens: u32,
    pub github_token: Option<String>,
    pub mcp_server_host: String,
    pub mcp_server_port: u16,
    pub log_level: String,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            anthropic_api_key: env::var("ANTHROPIC_API_KEY").ok().filter(|s| !s.is_empty()),
            anthropic_model: env::var("ANTHROPIC_MODEL")
                .unwrap_or_else(|_| "claude-3-7-sonnet-20250219".to_string()),
            thinking_budget_tokens: env::var("THINKING_BUDGET_TOKENS")
                .ok()
                .and_then(|s| s.parse().ok())
                .unwrap_or(4096),
            max_tokens: env::var("MAX_TOKENS")
                .ok()
                .and_then(|s| s.parse().ok())
                .unwrap_or(20000),
            github_token: env::var("GITHUB_TOKEN").ok().filter(|s| !s.is_empty()),
            mcp_server_host: env::var("MCP_SERVER_HOST")
                .unwrap_or_else(|_| "127.0.0.1".to_string()),
            mcp_server_port: env::var("MCP_SERVER_PORT")
                .ok()
                .and_then(|s| s.parse().ok())
                .unwrap_or(8000),
            log_level: env::var("LOG_LEVEL").unwrap_or_else(|_| "info".to_string()),
        }
    }
}

impl Config {
    pub fn from_env() -> Self {
        Self::default()
    }
}
