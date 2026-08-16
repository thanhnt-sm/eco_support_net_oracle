#![forbid(unsafe_code)]
//! # EcoSupport Core (Rust Native)
//!
//! Core primitives, configuration, telemetry, and Claude 3.7 client wrappers.

pub mod client;
pub mod config;
pub mod error;
pub mod telemetry;

pub use client::{ClaudeClient, ThinkingResponse};
pub use config::Config;
pub use error::{EcoError, Result};
pub use telemetry::{init_telemetry, log_token_metrics, TokenUsage};
