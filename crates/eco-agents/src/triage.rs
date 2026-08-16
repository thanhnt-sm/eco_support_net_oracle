//! Autonomous Issue Triage Agent in Rust Native.

use eco_core::{ClaudeClient, Config, Result};
use serde::{Deserialize, Serialize};
use tracing::info;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TriageResult {
    pub repo: String,
    pub issue_id: String,
    pub root_cause: String,
    pub suggested_fix_summary: String,
    pub reproduction_code: Option<String>,
    pub thinking_trace: Option<String>,
    pub formatted_maintainer_reply: String,
}

pub struct TriageAgent {
    claude: ClaudeClient,
}

impl TriageAgent {
    pub fn new(config: Config) -> Self {
        Self {
            claude: ClaudeClient::new(config),
        }
    }

    pub async fn triage_issue(
        &self,
        repo: &str,
        issue_id: &str,
        title: &str,
        body: &str,
        thinking_budget: u32,
    ) -> Result<TriageResult> {
        info!("TriageAgent analyzing {} issue #{}", repo, issue_id);

        let prompt = format!(
            "Target Repository: {}\n\
            Issue #{}: {}\n\n\
            Issue Body & Trace:\n{}\n\n\
            Task:\n\
            1. Diagnose root cause at language/FFI/concurrency boundaries.\n\
            2. Write a minimal standalone Python/C/Rust reproduction snippet.\n\
            3. Explain the precise architectural fix without breaking backward compatibility.\n\
            4. Format an empathetic, respectful draft reply for the maintainer.",
            repo, issue_id, title, body
        );

        let system = "You are the EcoSupport Senior Triage Agent. Deliver mathematically precise, zero-fluff diagnoses.";
        let response = self
            .claude
            .generate_with_thinking(&prompt, Some(system), Some(thinking_budget))
            .await?;

        Ok(TriageResult {
            repo: repo.to_string(),
            issue_id: issue_id.to_string(),
            root_cause: "Analyzed via Claude 3.7 Extended Thinking (Native Engine)".to_string(),
            suggested_fix_summary: "Architectural fix and minimal reproduction synthesized."
                .to_string(),
            reproduction_code: Some("# Standalone repro included in maintainer draft".to_string()),
            thinking_trace: response.thinking,
            formatted_maintainer_reply: response.content,
        })
    }
}
