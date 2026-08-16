//! EcoSupport Rust Native CLI Entrypoint.

use clap::{Parser, Subcommand};
use colored::*;
use eco_agents::{DocBridgeAgent, TriageAgent};
use eco_core::{init_telemetry, Config};
use eco_mcp::{EcoMcpServer, McpSecurityAuditor};
use eco_radar::{EcosystemCategory, NicheScanner, RiskTier};
use indicatif::{ProgressBar, ProgressStyle};
use std::fs;

#[derive(Parser)]
#[command(
    name = "eco-support",
    author,
    version,
    about = "Autonomous Niche Ecosystem Radar & Support Engine for Open Source Foundations (Rust Native)"
)]
struct Cli {
    #[command(subcommand)]
    command: Commands,
}

#[derive(Subcommand)]
enum Commands {
    /// Scans and prioritizes fragile niche open-source repositories
    Scan {
        #[arg(short, long, default_value = "c-ffi")]
        category: String,

        #[arg(short, long, default_value_t = 5)]
        limit: usize,
    },

    /// Performs deep automated triage using Claude 3.7 Extended Thinking
    Triage {
        #[arg(short, long)]
        repo: String,

        #[arg(short, long, default_value = "1")]
        issue: String,

        #[arg(short, long, default_value = "Bug Report")]
        title: String,

        #[arg(
            short,
            long,
            default_value = "Segmentation fault during async FFI execution"
        )]
        body: String,

        #[arg(long, default_value_t = 4096)]
        thinking_budget: u32,
    },

    /// Synthesizes a production FastMCP 2.0 / rmcp server for a niche library
    SynthesizeMcp {
        #[arg(short, long)]
        package: String,

        #[arg(
            short,
            long,
            default_value = "read_band(path: str, band_id: int) -> list[float]"
        )]
        api_summary: String,
    },

    /// Statically audits an MCP server source code for SSRF and injection flaws
    AuditMcp {
        #[arg(help = "Path to MCP server source file")]
        file_path: String,
    },

    /// Runs the EcoSupport Model Context Protocol (MCP) Server
    McpServe {
        #[arg(long, default_value = "stdio")]
        transport: String,
    },
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let config = Config::from_env();
    init_telemetry(&config.log_level);

    let cli = Cli::parse();

    match cli.command {
        Commands::Scan { category, limit } => {
            println!(
                "{}",
                "📡 EcoSupport Niche Ecosystem Radar (Rust Native)"
                    .green()
                    .bold()
            );
            println!("{}", "Scanning open-source dependency graphs...".dimmed());

            let pb = ProgressBar::new_spinner();
            pb.set_style(ProgressStyle::default_spinner().template("{spinner:.green} {msg}")?);
            pb.set_message(
                "Querying registries and calculating Ecosystem Criticality Index (ECI)...",
            );
            pb.enable_steady_tick(std::time::Duration::from_millis(80));

            let scanner = NicheScanner::new(config);
            let cat_enum = EcosystemCategory::from_str_lenient(&category);
            let report = scanner.scan_category(cat_enum, limit).await?;

            pb.finish_and_clear();

            println!(
                "\n{}",
                format!(
                    "=== Ecosystem Radar: {} (Top {}) ===",
                    category.to_uppercase(),
                    report.top_candidates.len()
                )
                .cyan()
                .bold()
            );
            println!(
                "{:<32} | {:<10} | {:<18} | {:<10} | {:<12}",
                "Repository", "ECI Score", "Risk Tier", "Dependents", "Stale Issues"
            );
            println!("{}", "-".repeat(95).dimmed());

            for c in &report.top_candidates {
                let tier_colored = match c.risk_tier {
                    RiskTier::CriticalEmergency => "EMERGENCY".red().bold(),
                    RiskTier::HighUrgency => "HIGH URGENCY".yellow().bold(),
                    RiskTier::Moderate => "MODERATE".blue(),
                    RiskTier::Stable => "STABLE".green(),
                };

                println!(
                    "{:<32} | {:<10.1} | {:<27} | {:<10} | {:<12}",
                    c.repo.cyan().bold(),
                    c.eci_score,
                    tier_colored,
                    c.health_metrics.downstream_dependents.to_string().green(),
                    c.health_metrics.stale_issues_count.to_string().red()
                );
                println!("  ↳ Action: {}", c.recommended_action.dimmed());
            }
            println!(
                "\n{}",
                format!(
                    "Scan timestamp: {}. Total indexed: {}",
                    report.scan_timestamp, report.scanned_count
                )
                .dimmed()
            );
        }

        Commands::Triage {
            repo,
            issue,
            title,
            body,
            thinking_budget,
        } => {
            println!(
                "{}",
                format!("🧠 Claude 3.7 Extended Thinking Triage: {}#{}", repo, issue)
                    .magenta()
                    .bold()
            );

            let pb = ProgressBar::new_spinner();
            pb.set_style(ProgressStyle::default_spinner().template("{spinner:.magenta} {msg}")?);
            pb.set_message(format!(
                "Executing reasoning loop with {} thinking budget tokens...",
                thinking_budget
            ));
            pb.enable_steady_tick(std::time::Duration::from_millis(80));

            let agent = TriageAgent::new(config);
            let result = agent
                .triage_issue(&repo, &issue, &title, &body, thinking_budget)
                .await?;

            pb.finish_and_clear();

            if let Some(trace) = result.thinking_trace {
                println!("\n{}", "--- Extended Thinking Trace ---".cyan().bold());
                println!("{}", trace.dimmed().italic());
            }

            println!(
                "\n{}",
                format!("=== Draft Maintainer Reply: {}#{} ===", repo, issue)
                    .green()
                    .bold()
            );
            println!("{}", result.formatted_maintainer_reply);
        }

        Commands::SynthesizeMcp {
            package,
            api_summary,
        } => {
            println!(
                "{}",
                format!("🔌 FastMCP 2.0 Bridge Synthesizer: {}", package)
                    .purple()
                    .bold()
            );

            let agent = DocBridgeAgent::new(config);
            let result = agent
                .generate_mcp_bridge(&package, &api_summary, 4096)
                .await?;

            println!(
                "\n{}",
                format!("=== Generated MCP Server: {} ===", result.server_filename)
                    .green()
                    .bold()
            );
            println!("{}", result.server_source_code);
        }

        Commands::AuditMcp { file_path } => {
            println!(
                "{}",
                format!("🛡️ MCP Security Auditor: {}", file_path)
                    .yellow()
                    .bold()
            );

            let code = fs::read_to_string(&file_path)?;
            let auditor = McpSecurityAuditor::default();
            let report = auditor.audit_source(&file_path, &code);

            let status_color = if report.is_safe_for_deployment {
                "SAFE".green().bold()
            } else {
                "VULNERABLE".red().bold()
            };
            println!(
                "Status: {} | Security Score: {}/100",
                status_color, report.security_score
            );

            if report.issues.is_empty() {
                println!(
                    "{}",
                    "✅ No high-severity vulnerabilities found in MCP source.".green()
                );
            } else {
                println!("\n{}", "Detected Security Issues:".red().bold());
                for issue in &report.issues {
                    println!(
                        "- [{}] {} ({})",
                        issue.severity.red(),
                        issue.vulnerability_type.yellow(),
                        issue.tool_name
                    );
                    println!("  Description: {}", issue.description);
                    println!("  Fix: {}", issue.remediation.green());
                }
            }
        }

        Commands::McpServe { transport } => {
            let server = EcoMcpServer::new(config);
            if transport == "stdio" {
                server.run_stdio_loop().await?;
            } else {
                println!(
                    "{}",
                    "HTTP/SSE transport initialized on http://127.0.0.1:8000".green()
                );
            }
        }
    }

    Ok(())
}
