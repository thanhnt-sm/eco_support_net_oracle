#![forbid(unsafe_code)]
//! # EcoSupport MCP (Rust Native)
//!
//! Model Context Protocol 2.0 server implementation, tool handlers, and static security auditors.

pub mod auditor;
pub mod protocol;
pub mod server;

pub use auditor::{McpAuditReport, McpSecurityAuditor, McpSecurityIssue};
pub use protocol::{JsonRpcRequest, JsonRpcResponse, McpTool};
pub use server::EcoMcpServer;
