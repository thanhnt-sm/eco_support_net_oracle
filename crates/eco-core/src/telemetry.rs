//! Telemetry and token usage tracker for native Claude agent loops.

use serde::{Deserialize, Serialize};
use tracing::info;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TokenUsage {
    pub prompt_tokens: u32,
    pub completion_tokens: u32,
    pub thinking_tokens: u32,
    pub total_tokens: u32,
}

pub fn init_telemetry(log_level: &str) {
    let filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new(log_level));

    let _ = tracing_subscriber::fmt()
        .with_env_filter(filter)
        .with_target(false)
        .with_thread_ids(false)
        .try_init();
}

pub fn log_token_metrics(op: &str, usage: &TokenUsage) {
    info!(
        operation = op,
        prompt_tokens = usage.prompt_tokens,
        completion_tokens = usage.completion_tokens,
        thinking_tokens = usage.thinking_tokens,
        total_tokens = usage.total_tokens,
        "Anthropic Claude 3.7 Token Metrics Logged"
    );
}
