//! FastMCP Bridge & Documentation Generator Agent in Rust Native.

use eco_core::{ClaudeClient, Config, Result};
use serde::{Deserialize, Serialize};
use tracing::info;
use anyhow::Result as AnyhowResult;
use std::collections::HashMap;
use std::fmt;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BridgeResult {
    pub package_name: String,
    pub server_filename: String,
    pub server_source_code: String,
    pub readme_markdown: String,
    pub thinking_trace: Option<String>,
}

pub struct DocBridgeAgent {
    claude: ClaudeClient,
}

impl DocBridgeAgent {
    pub fn new(config: Config) -> Self {
        Self {
            claude: ClaudeClient::new(config),
        }
    }

    pub async fn generate_mcp_bridge(
        &self,
        package_name: &str,
        api_signatures: &str,
        thinking_budget: u32,
    ) -> Result<BridgeResult> {
        info!(
            "DocBridgeAgent synthesizing FastMCP server for {}",
            package_name
        );

        let prompt = format!(
            "Package: {}\n\
            API Signatures:\n{}\n\n\
            Task: Generate a production-ready FastMCP 2.0 / rmcp server module exposing these APIs safely.",
            package_name, api_signatures
        );

        let response = self
            .claude
            .generate_with_thinking(&prompt, None, Some(thinking_budget))
            .await?;

        Ok(BridgeResult {
            package_name: package_name.to_string(),
            server_filename: format!("{}_mcp.rs", package_name.replace('-', "_")),
            server_source_code: response.content,
            readme_markdown: format!(
                "# FastMCP Server for `{}`\n\nGenerated autonomously by EcoSupport Native.",
                package_name
            ),
            thinking_trace: response.thinking,
        })
    }
}
// opencode zen Bridge Agent — Rust native agent using opencode zen free models
//
// References `.omo/agents.toml` for model definitions and `.omo/config.toml` for
// routing rules and default agent configuration.

#[derive(Debug, Clone)]
pub struct OpencodeZenConfig {
    pub default_agent: String,
    pub routing: HashMap<String, String>,
    pub fallback_chain: Vec<String>,
}

impl OpencodeZenConfig {
    pub fn from_toml() -> AnyhowResult<Self> {
        let _agents_content = std::fs::read_to_string("./.omo/agents.toml")
            .map_err(|_| anyhow::anyhow!("Failed to read .omo/agents.toml"))?;
        let config_content = std::fs::read_to_string("./.omo/config.toml")
            .map_err(|_| anyhow::anyhow!("Failed to read .omo/config.toml"))?;

        fn parse_toml_section(content: &str, section: &str) -> HashMap<String, String> {
            let mut result = HashMap::new();
            for line in content.lines() {
                let trimmed = line.trim();
                if trimmed.starts_with(section) && trimmed.contains('=') {
                    let parts: Vec<&str> = trimmed.splitn(2, '=').collect();
                    if parts.len() == 2 {
                        let key = parts[0]
                            .trim()
                            .trim_matches('"')
                            .trim_matches('[')
                            .trim_matches(']');
                        let val = parts[1].trim().trim_matches('"');
                        result.insert(key.to_string(), val.to_string());
                    }
                }
            }
            result
        }

        let routing = parse_toml_section(&config_content, "[omp.routing]");
        let default_agent = routing
            .get("default_agent")
            .cloned()
            .unwrap_or_else(|| "nemotron-3-ultra-free".to_string());
        let fallback_chain = vec![
            "nemotron-3-ultra-free".to_string(),
            "deepseek-v3-free".to_string(),
            "ollama-nemotron3-8b".to_string(),
        ];

        Ok(OpencodeZenConfig {
            default_agent,
            routing,
            fallback_chain,
        })
    }
}

fn resolve_agent_for_task(task_type: &str, config: &OpencodeZenConfig) -> String {
    config
        .routing
        .get(task_type)
        .cloned()
        .unwrap_or_else(|| config.default_agent.clone())
}

fn get_fallback_agent(config: &OpencodeZenConfig, attempt: usize) -> String {
    let idx = attempt.min(config.fallback_chain.len().saturating_sub(1));
    config.fallback_chain[idx].clone()
}

#[derive(Debug, Clone)]
pub enum OpencodeZenError {
    NoProviderAvailable,
    ModelNotFound(String),
    RoutingError(String),
    ConfigReadError(String),
}

impl fmt::Display for OpencodeZenError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            OpencodeZenError::NoProviderAvailable => write!(f, "No opencode zen provider available"),
            OpencodeZenError::ModelNotFound(model) => write!(f, "Model not found: {}", model),
            OpencodeZenError::RoutingError(task) => write!(f, "No agent routed for task: {}", task),
            OpencodeZenError::ConfigReadError(msg) => write!(f, "Config error: {}", msg),
        }
    }
}

impl std::error::Error for OpencodeZenError {}

pub struct OpencodeZenBridgeAgent {
    config: OpencodeZenConfig,
}

impl OpencodeZenBridgeAgent {
    pub fn new() -> AnyhowResult<Self> {
        let config = OpencodeZenConfig::from_toml()?;
        Ok(OpencodeZenBridgeAgent { config })
    }

    pub fn route_task(&self, task_type: &str) -> String {
        resolve_agent_for_task(task_type, &self.config)
    }

    pub fn fallback_agent(&self, attempt: usize) -> String {
        get_fallback_agent(&self.config, attempt)
    }

    pub async fn generate_with_model(
        &self,
        task_type: &str,
        prompt: &str,
        thinking_budget: u32,
    ) -> AnyhowResult<String> {
        let agent_name = self.route_task(task_type);
        tracing::info!(
            "OpencodeZenBridgeAgent routing task '{}' to model: {}",
            task_type,
            agent_name
        );

        // Try primary agent first, then fallbacks
        for (i, model_name) in self.config.fallback_chain.iter().enumerate() {
            let result = self.try_generate_with_model(model_name, prompt, thinking_budget).await;
            if result.is_ok() {
                return result;
            }
            tracing::info!(
                "Model {} failed, trying fallback {}",
                model_name,
                self.fallback_agent(i)
            );
        }

        Err(anyhow::anyhow!(
            "All opencode zen models failed for task type: {}",
            task_type
        ))
    }

    async fn try_generate_with_model(
        &self,
        model_name: &str,
        prompt: &str,
        thinking_budget: u32,
    ) -> AnyhowResult<String> {
        tracing::info!(
            "OpencodeZenBridgeAgent using model '{}' with thinking budget {}",
            model_name,
            thinking_budget
        );
        // Simulate - would call opencode-zen API here
        Ok(format!(
            "Generated response using model {} for prompt: {}",
            model_name,
            prompt.lines().next().unwrap_or("empty")
        ))
    }
}
