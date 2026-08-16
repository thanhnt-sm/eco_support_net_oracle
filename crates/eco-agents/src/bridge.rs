//! FastMCP Bridge & Documentation Generator Agent in Rust Native.

use eco_core::{ClaudeClient, Config, Result};
use serde::{Deserialize, Serialize};
use tracing::info;

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
