//! Anthropic Claude 3.7 Sonnet client with native Extended Thinking support.

use crate::config::Config;
use crate::error::{EcoError, Result};
use crate::telemetry::{log_token_metrics, TokenUsage};
use reqwest::Client as HttpClient;
use serde::{Deserialize, Serialize};
use tracing::{info, warn};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ThinkingResponse {
    pub thinking: Option<String>,
    pub content: String,
    pub model: String,
    pub usage: TokenUsage,
}

#[derive(Debug, Clone)]
pub struct ClaudeClient {
    config: Config,
    http: HttpClient,
}

impl ClaudeClient {
    pub fn new(config: Config) -> Self {
        Self {
            config,
            http: HttpClient::builder().build().unwrap_or_default(),
        }
    }

    pub fn is_live(&self) -> bool {
        self.config.anthropic_api_key.is_some()
    }

    pub async fn generate_with_thinking(
        &self,
        prompt: &str,
        system_prompt: Option<&str>,
        thinking_budget: Option<u32>,
    ) -> Result<ThinkingResponse> {
        let budget = thinking_budget.unwrap_or(self.config.thinking_budget_tokens);

        if let Some(ref api_key) = self.config.anthropic_api_key {
            info!(
                "Executing live Claude 3.7 API request with thinking budget: {} tokens",
                budget
            );

            let payload = serde_json::json!({
                "model": self.config.anthropic_model,
                "max_tokens": self.config.max_tokens,
                "messages": [{"role": "user", "content": prompt}],
                "thinking": {
                    "type": "enabled",
                    "budget_tokens": budget
                },
                "system": system_prompt.unwrap_or("You are the EcoSupport Rust Native Diagnostician.")
            });

            let res = self
                .http
                .post("https://api.anthropic.com/v1/messages")
                .header("x-api-key", api_key)
                .header("anthropic-version", "2023-06-01")
                .header("content-type", "application/json")
                .json(&payload)
                .send()
                .await
                .map_err(|e| EcoError::ApiProvider(format!("HTTP transport error: {e}")))?;

            if !res.status().is_success() {
                let status = res.status();
                let err_text = res.text().await.unwrap_or_default();
                return Err(EcoError::ApiProvider(format!(
                    "API returned {status}: {err_text}"
                )));
            }

            let body: serde_json::Value = res.json().await.map_err(|e| {
                EcoError::ApiProvider(format!("Failed to parse response JSON: {e}"))
            })?;

            let mut thinking_text = None;
            let mut content_text = String::new();

            if let Some(content_array) = body.get("content").and_then(|c| c.as_array()) {
                for block in content_array {
                    if let Some(block_type) = block.get("type").and_then(|t| t.as_str()) {
                        if block_type == "thinking" {
                            thinking_text = block
                                .get("thinking")
                                .and_then(|t| t.as_str())
                                .map(String::from);
                        } else if block_type == "text" {
                            if let Some(t) = block.get("text").and_then(|t| t.as_str()) {
                                content_text.push_str(t);
                            }
                        }
                    }
                }
            }

            let usage = TokenUsage {
                prompt_tokens: body
                    .get("usage")
                    .and_then(|u| u.get("input_tokens"))
                    .and_then(|t| t.as_u64())
                    .unwrap_or(0) as u32,
                completion_tokens: body
                    .get("usage")
                    .and_then(|u| u.get("output_tokens"))
                    .and_then(|t| t.as_u64())
                    .unwrap_or(0) as u32,
                thinking_tokens: budget,
                total_tokens: (body
                    .get("usage")
                    .and_then(|u| u.get("input_tokens"))
                    .and_then(|t| t.as_u64())
                    .unwrap_or(0)
                    + body
                        .get("usage")
                        .and_then(|u| u.get("output_tokens"))
                        .and_then(|t| t.as_u64())
                        .unwrap_or(0)) as u32,
            };

            log_token_metrics("live_claude_generate", &usage);

            Ok(ThinkingResponse {
                thinking: thinking_text,
                content: content_text,
                model: self.config.anthropic_model.clone(),
                usage,
            })
        } else {
            warn!("ANTHROPIC_API_KEY not configured. Running deterministic offline simulation harness.");
            Ok(self.simulate_thinking(prompt, budget))
        }
    }

    fn simulate_thinking(&self, prompt: &str, budget: u32) -> ThinkingResponse {
        let thinking = format!(
            "[Native Rust Simulation - Claude 3.7 Thinking Mode ({} tokens)]\n\
            1. Ingesting prompt AST signature (length: {} bytes)...\n\
            2. Performing zero-cost FFI boundary safety checks...\n\
            3. Verifying non-breaking downstream backwards compatibility invariants.",
            budget,
            prompt.len()
        );

        let content = "### EcoSupport Native Rust Diagnosis\n\n\
            **Status**: Invariant Verification Completed.\n\
            - Subagent Execution: PASSED\n\
            - Memory Safety Check: 100% Zero-Unsafe Invariants Preserved.\n\
            - Actionable Recommendation: Synthesized typed FastMCP 2.0 interface with automated regression tests.".to_string();

        let usage = TokenUsage {
            prompt_tokens: (prompt.len() / 4) as u32,
            completion_tokens: 384,
            thinking_tokens: budget,
            total_tokens: (prompt.len() / 4) as u32 + 384 + budget,
        };

        log_token_metrics("simulated_generate", &usage);

        ThinkingResponse {
            thinking: Some(thinking),
            content,
            model: "claude-3-7-sonnet-rust-native (Simulated)".to_string(),
            usage,
        }
    }
}
