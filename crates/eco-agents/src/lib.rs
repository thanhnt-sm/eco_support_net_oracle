#![forbid(unsafe_code)]
//! # EcoSupport Agents (Rust Native)
//!
//! Autonomous multi-agent swarm for issue triage, patch synthesis, and FastMCP bridging.

pub mod bridge;
pub mod patch;
pub mod triage;

pub use bridge::{BridgeResult, DocBridgeAgent, OpencodeZenBridgeAgent, OpencodeZenConfig, OpencodeZenError};
pub use patch::{PatchResult, PatchSynthesizerAgent};
pub use triage::{TriageAgent, TriageResult};
