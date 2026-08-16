use eco_core::Config;
use eco_mcp::{EcoMcpServer, McpSecurityAuditor};

#[test]
fn test_mcp_server_initialization_tools() {
    let server = EcoMcpServer::new(Config::default());
    let tools = server.list_tools();
    assert_eq!(tools.len(), 4);
    let tool_names: Vec<String> = tools.into_iter().map(|t| t.name).collect();
    assert!(tool_names.contains(&"scan_niche_ecosystem".to_string()));
    assert!(tool_names.contains(&"diagnose_repo_bottleneck".to_string()));
    assert!(tool_names.contains(&"synthesize_mcp_bridge".to_string()));
    assert!(tool_names.contains(&"audit_mcp_security".to_string()));
}

#[test]
fn test_mcp_security_auditor_detects_flaws() {
    let auditor = McpSecurityAuditor::default();
    let code = r#"
        subprocess.run(user_cmd, shell=True)
        eval(user_code)
    "#;
    let report = auditor.audit_source("vulnerable.py", code);
    assert!(!report.is_safe_for_deployment);
    assert!(report.security_score < 50);
    assert_eq!(report.issues.len(), 2);
}

#[test]
fn test_mcp_security_auditor_approves_safe() {
    let auditor = McpSecurityAuditor::default();
    let code = r#"
        #[mcp::tool]
        fn calculate_sum(a: i64, b: i64) -> i64 {
            a + b
        }
    "#;
    let report = auditor.audit_source("safe.rs", code);
    assert!(report.is_safe_for_deployment);
    assert_eq!(report.security_score, 100);
}
