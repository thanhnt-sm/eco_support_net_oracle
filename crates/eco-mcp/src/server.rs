//! Native Rust Model Context Protocol (MCP) Server Implementation.

use crate::auditor::McpSecurityAuditor;
use crate::protocol::*;
use eco_core::{ClaudeClient, Config, Result};
use eco_radar::{EcosystemCategory, NicheScanner};
use serde_json::json;
use std::io::{self, BufRead, Write};
use tracing::info;

pub struct EcoMcpServer {
    pub config: Config,
    pub scanner: NicheScanner,
    pub claude: ClaudeClient,
    pub auditor: McpSecurityAuditor,
}

impl EcoMcpServer {
    pub fn new(config: Config) -> Self {
        Self {
            scanner: NicheScanner::new(config.clone()),
            claude: ClaudeClient::new(config.clone()),
            auditor: McpSecurityAuditor::default(),
            config,
        }
    }

    pub fn list_tools(&self) -> Vec<McpTool> {
        vec![
            McpTool {
                name: "scan_niche_ecosystem".to_string(),
                description: "Scans and prioritizes fragile, single-maintainer open-source repositories with high downstream impact.".to_string(),
                input_schema: json!({
                    "type": "object",
                    "properties": {
                        "category": { "type": "string", "description": "c-ffi, geospatial, bio-ml, mcp-connectors, typing-infrastructure, general-niche" },
                        "limit": { "type": "integer", "description": "Maximum number of repositories to return (1-20)", "default": 5 }
                    }
                }),
            },
            McpTool {
                name: "diagnose_repo_bottleneck".to_string(),
                description: "Diagnoses deep bugs in niche libraries using Claude 3.7 Sonnet Extended Thinking.".to_string(),
                input_schema: json!({
                    "type": "object",
                    "properties": {
                        "repo": { "type": "string", "description": "Repository slug in owner/name format" },
                        "issue_title": { "type": "string", "description": "Title of the issue" },
                        "issue_description": { "type": "string", "description": "Stack trace or problem description" },
                        "thinking_budget": { "type": "integer", "description": "Thinking tokens budget (default: 4096)", "default": 4096 }
                    },
                    "required": ["repo", "issue_title", "issue_description"]
                }),
            },
            McpTool {
                name: "synthesize_mcp_bridge".to_string(),
                description: "Synthesizes a production FastMCP 2.0 server for any legacy or niche Python/C library.".to_string(),
                input_schema: json!({
                    "type": "object",
                    "properties": {
                        "package_name": { "type": "string", "description": "Name of the target package" },
                        "module_summary": { "type": "string", "description": "API signature summary" },
                        "intended_audience": { "type": "string", "description": "Target AI agent consumer" }
                    },
                    "required": ["package_name", "module_summary"]
                }),
            },
            McpTool {
                name: "audit_mcp_security".to_string(),
                description: "Audits an MCP server source code for SSRF, command injection, and path traversal vulnerabilities.".to_string(),
                input_schema: json!({
                    "type": "object",
                    "properties": {
                        "server_name": { "type": "string", "description": "Target server identifier" },
                        "source_code": { "type": "string", "description": "Source code of the MCP server" }
                    },
                    "required": ["server_name", "source_code"]
                }),
            },
        ]
    }

    pub async fn handle_tool_call(
        &self,
        name: &str,
        args: serde_json::Value,
    ) -> Result<serde_json::Value> {
        match name {
            "scan_niche_ecosystem" => {
                let category_str = args
                    .get("category")
                    .and_then(|c| c.as_str())
                    .unwrap_or("c-ffi");
                let limit = args.get("limit").and_then(|l| l.as_u64()).unwrap_or(5) as usize;
                let category = EcosystemCategory::from_str_lenient(category_str);
                let report = self.scanner.scan_category(category, limit).await?;
                Ok(serde_json::to_value(report)?)
            }
            "diagnose_repo_bottleneck" => {
                let repo = args
                    .get("repo")
                    .and_then(|r| r.as_str())
                    .unwrap_or("unknown/repo");
                let title = args
                    .get("issue_title")
                    .and_then(|t| t.as_str())
                    .unwrap_or("Bug Report");
                let desc = args
                    .get("issue_description")
                    .and_then(|d| d.as_str())
                    .unwrap_or("");
                let budget = args
                    .get("thinking_budget")
                    .and_then(|b| b.as_u64())
                    .unwrap_or(4096) as u32;

                let prompt = format!(
                    "Repository: {}\nIssue: {}\nTrace/Description:\n{}\n\nDiagnose root cause and provide fix.",
                    repo, title, desc
                );
                let response = self
                    .claude
                    .generate_with_thinking(&prompt, None, Some(budget))
                    .await?;

                Ok(json!({
                    "repo": repo,
                    "thinking_trace": response.thinking,
                    "diagnostic_report": response.content,
                    "model": response.model,
                }))
            }
            "synthesize_mcp_bridge" => {
                let pkg = args
                    .get("package_name")
                    .and_then(|p| p.as_str())
                    .unwrap_or("custom-pkg");
                let summary = args
                    .get("module_summary")
                    .and_then(|s| s.as_str())
                    .unwrap_or("");
                let prompt = format!(
                    "Synthesize a standalone FastMCP 2.0 Python/Rust server for package `{}`.\nAPI Interface:\n{}",
                    pkg, summary
                );
                let response = self
                    .claude
                    .generate_with_thinking(&prompt, None, Some(4096))
                    .await?;

                Ok(json!({
                    "package_name": pkg,
                    "generated_mcp_server_code": response.content,
                    "thinking_trace": response.thinking
                }))
            }
            "audit_mcp_security" => {
                let server_name = args
                    .get("server_name")
                    .and_then(|s| s.as_str())
                    .unwrap_or("target_server");
                let code = args
                    .get("source_code")
                    .and_then(|c| c.as_str())
                    .unwrap_or("");
                let report = self.auditor.audit_source(server_name, code);
                Ok(serde_json::to_value(report)?)
            }
            _ => Ok(json!({ "error": format!("Unknown tool: {}", name) })),
        }
    }

    pub async fn run_stdio_loop(&self) -> Result<()> {
        info!("Starting EcoSupport MCP Server in STDIO mode");
        let stdin = io::stdin();
        let mut stdout = io::stdout();

        for line in stdin.lock().lines() {
            let line = line?;
            if line.trim().is_empty() {
                continue;
            }

            if let Ok(req) = serde_json::from_str::<JsonRpcRequest>(&line) {
                let res = match req.method.as_str() {
                    "initialize" => JsonRpcResponse {
                        jsonrpc: "2.0".to_string(),
                        id: req.id,
                        result: Some(json!({
                            "protocolVersion": "2024-11-05",
                            "capabilities": { "tools": {} },
                            "serverInfo": { "name": "eco-mcp-rust", "version": "0.1.0" }
                        })),
                        error: None,
                    },
                    "tools/list" => JsonRpcResponse {
                        jsonrpc: "2.0".to_string(),
                        id: req.id,
                        result: Some(json!({ "tools": self.list_tools() })),
                        error: None,
                    },
                    "tools/call" => {
                        let tool_name = req
                            .params
                            .as_ref()
                            .and_then(|p| p.get("name"))
                            .and_then(|n| n.as_str())
                            .unwrap_or("");
                        let tool_args = req
                            .params
                            .as_ref()
                            .and_then(|p| p.get("arguments"))
                            .cloned()
                            .unwrap_or_else(|| json!({}));
                        match self.handle_tool_call(tool_name, tool_args).await {
                            Ok(output) => JsonRpcResponse {
                                jsonrpc: "2.0".to_string(),
                                id: req.id,
                                result: Some(
                                    json!({ "content": [{ "type": "text", "text": output.to_string() }] }),
                                ),
                                error: None,
                            },
                            Err(e) => JsonRpcResponse {
                                jsonrpc: "2.0".to_string(),
                                id: req.id,
                                result: None,
                                error: Some(JsonRpcError {
                                    code: -32603,
                                    message: e.to_string(),
                                    data: None,
                                }),
                            },
                        }
                    }
                    _ => JsonRpcResponse {
                        jsonrpc: "2.0".to_string(),
                        id: req.id,
                        result: None,
                        error: Some(JsonRpcError {
                            code: -32601,
                            message: format!("Method not found: {}", req.method),
                            data: None,
                        }),
                    },
                };

                let out_json = serde_json::to_string(&res)?;
                writeln!(stdout, "{}", out_json)?;
                stdout.flush()?;
            }
        }
        Ok(())
    }
}
