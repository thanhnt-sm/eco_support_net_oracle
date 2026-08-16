//! Static Security Auditor for Model Context Protocol (MCP) server definitions.

use regex::Regex;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct McpSecurityIssue {
    pub tool_name: String,
    pub severity: String,
    pub vulnerability_type: String,
    pub description: String,
    pub remediation: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct McpAuditReport {
    pub server_target: String,
    pub total_tools_audited: usize,
    pub security_score: u32,
    pub is_safe_for_deployment: bool,
    pub issues: Vec<McpSecurityIssue>,
}

pub struct McpSecurityAuditor {
    cmd_injection_re: Regex,
    dynamic_eval_re: Regex,
    ssrf_re: Regex,
    path_traversal_re: Regex,
}

impl Default for McpSecurityAuditor {
    fn default() -> Self {
        Self {
            cmd_injection_re: Regex::new(
                r#"(subprocess\.run|os\.system|os\.popen|Command::new).*shell\s*=\s*True"#,
            )
            .unwrap(),
            dynamic_eval_re: Regex::new(r#"\b(eval|exec)\s*\("#).unwrap(),
            ssrf_re: Regex::new(r#"(requests\.get|httpx\.get|reqwest::get)\s*\(\s*[a-zA-Z0-9_]+"#)
                .unwrap(),
            path_traversal_re: Regex::new(r#"open\s*\(\s*.*(\.\./|os\.path\.join)"#).unwrap(),
        }
    }
}

impl McpSecurityAuditor {
    pub fn audit_source(&self, server_target: &str, source_code: &str) -> McpAuditReport {
        let mut issues = Vec::new();

        // 1. Command Injection check
        if self.cmd_injection_re.is_match(source_code)
            || (source_code.contains("subprocess") && source_code.contains("shell=True"))
        {
            issues.push(McpSecurityIssue {
                tool_name: "execution_handler".to_string(),
                severity: "CRITICAL".to_string(),
                vulnerability_type: "Command Injection".to_string(),
                description: "Direct shell execution detected without argument whitelisting."
                    .to_string(),
                remediation: "Use direct parameter vectors without invoking system shell."
                    .to_string(),
            });
        }

        // 2. Dynamic Eval check
        if self.dynamic_eval_re.is_match(source_code) {
            issues.push(McpSecurityIssue {
                tool_name: "eval_handler".to_string(),
                severity: "CRITICAL".to_string(),
                vulnerability_type: "Arbitrary Code Execution".to_string(),
                description: "Dynamic eval/exec detected in MCP tool body.".to_string(),
                remediation: "Replace dynamic evaluation with AST parser or strict serialization."
                    .to_string(),
            });
        }

        // 3. SSRF check
        if self.ssrf_re.is_match(source_code)
            && !source_code.contains("localhost")
            && !source_code.contains("allowed_domains")
        {
            issues.push(McpSecurityIssue {
                tool_name: "network_handler".to_string(),
                severity: "HIGH".to_string(),
                vulnerability_type: "Server-Side Request Forgery (SSRF)".to_string(),
                description: "Outbound HTTP request with unvalidated user URL.".to_string(),
                remediation: "Enforce domain whitelist and disallow private IP ranges (127.0.0.1, 169.254.169.254).".to_string(),
            });
        }

        // 4. Path Traversal check
        if self.path_traversal_re.is_match(source_code)
            && !source_code.contains("canonicalize")
            && !source_code.contains("resolve")
        {
            issues.push(McpSecurityIssue {
                tool_name: "filesystem_handler".to_string(),
                severity: "MEDIUM".to_string(),
                vulnerability_type: "Path Traversal".to_string(),
                description: "Unsanitized filesystem access without canonical path verification."
                    .to_string(),
                remediation:
                    "Verify that target paths strictly reside within the designated sandbox root."
                        .to_string(),
            });
        }

        let penalty: u32 = issues
            .iter()
            .map(|i| match i.severity.as_str() {
                "CRITICAL" => 40,
                "HIGH" => 20,
                _ => 10,
            })
            .sum();

        let security_score = 100u32.saturating_sub(penalty);
        let has_critical = issues.iter().any(|i| i.severity == "CRITICAL");
        let is_safe = security_score >= 70 && !has_critical;

        McpAuditReport {
            server_target: server_target.to_string(),
            total_tools_audited: source_code.matches("@mcp.tool").count().max(1),
            security_score,
            is_safe_for_deployment: is_safe,
            issues,
        }
    }
}
