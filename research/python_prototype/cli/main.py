"""EcoSupport Command-Line Interface (CLI).

Rich, interactive terminal interface for scanning niche ecosystems, triaging bugs,
generating FastMCP bridges, and serving MCP tools.
"""

from __future__ import annotations

import asyncio

import typer
from rich.console import Console
from rich.panel import Panel
from rich.table import Table
from rich.text import Text

from eco_support.agents.doc_bridge_agent import DocBridgeAgent
from eco_support.agents.triage_agent import TriageAgent
from eco_support.mcp.server import run_mcp_server
from eco_support.mcp.tools.security_auditor import MCPSecurityAuditor
from eco_support.radar.models import EcosystemCategory, RiskTier
from eco_support.radar.niche_scanner import NicheScanner

app = typer.Typer(
    name="eco-support",
    help="Autonomous Niche Ecosystem Radar & Support Engine for Open Source Foundations",
    add_completion=False,
)
console = Console()


@app.command()
def scan(
    category: str = typer.Option(
        "c-ffi",
        "--category",
        "-c",
        help="Niche category (c-ffi, geospatial, bio-ml, mcp-connectors, typing-infrastructure, general-niche)",
    ),
    limit: int = typer.Option(5, "--limit", "-l", help="Number of repositories to scan"),
) -> None:
    """Scans and prioritizes fragile, high-impact niche open-source repositories."""
    console.print(
        Panel.fit(
            "[bold green]📡 EcoSupport Niche Ecosystem Radar[/bold green]\nScanning open-source dependency graphs..."
        )
    )

    async def _run() -> None:
        scanner = NicheScanner()
        try:
            cat_enum = EcosystemCategory(category)
        except ValueError:
            cat_enum = EcosystemCategory.GENERAL_NICHE

        with console.status(
            "[bold cyan]Querying registries and calculating ECI scores...[/bold cyan]"
        ):
            report = await scanner.scan_category(cat_enum, limit=limit)

        table = Table(
            title=f"Ecosystem Radar: {category.upper()} (Top {len(report.top_candidates)})",
            show_lines=True,
        )
        table.add_column("Repository", style="bold cyan")
        table.add_column("ECI Score", justify="center", style="bold yellow")
        table.add_column("Risk Tier", justify="center")
        table.add_column("Dependents", justify="right", style="green")
        table.add_column("Stale Issues", justify="right", style="red")
        table.add_column("Recommended Action", style="white")

        for c in report.top_candidates:
            tier_color = (
                "red"
                if c.risk_tier == RiskTier.CRITICAL_EMERGENCY
                else "yellow"
                if c.risk_tier == RiskTier.HIGH_URGENCY
                else "blue"
            )
            table.add_row(
                c.repo,
                f"{c.eci_score:.1f}",
                f"[{tier_color}]{c.risk_tier.value.split('_')[-1]}[/{tier_color}]",
                str(c.health_metrics.downstream_dependents),
                str(c.health_metrics.stale_issues_count),
                c.recommended_action,
            )

        console.print(table)
        console.print(
            f"[dim]Scan complete at {report.scan_timestamp}. Total scanned: {report.scanned_count}[/dim]\n"
        )

    asyncio.run(_run())


@app.command()
def triage(
    repo: str = typer.Option(..., "--repo", "-r", help="Repository slug in format 'owner/repo'"),
    issue: str = typer.Option("1", "--issue", "-i", help="Issue number or identifier"),
    title: str = typer.Option("Bug Report", "--title", "-t", help="Issue title"),
    body: str = typer.Option(
        "Segmentation fault during async invocation", "--body", "-b", help="Issue body or trace"
    ),
    thinking_budget: int = typer.Option(
        4096, "--thinking-budget", help="Thinking tokens for Claude 3.7"
    ),
) -> None:
    """Performs deep automated triage using Claude 3.7 Sonnet Extended Thinking."""
    console.print(
        Panel.fit(
            f"[bold magenta]🧠 Claude 3.7 Extended Thinking Triage[/bold magenta]\nDiagnosing [bold]{repo}#{issue}[/bold]"
        )
    )

    async def _run() -> None:
        agent = TriageAgent()
        with console.status(
            f"[bold cyan]Running reasoning loop with {thinking_budget} thinking budget tokens...[/bold cyan]"
        ):
            result = await agent.triage_issue(
                repo, issue, title, body, thinking_budget=thinking_budget
            )

        if result.thinking_trace:
            console.print(
                Panel(
                    Text(result.thinking_trace, style="dim italic"),
                    title="[cyan]Extended Thinking Trace[/cyan]",
                )
            )

        console.print(
            Panel(
                result.formatted_maintainer_reply,
                title=f"[green]Draft Maintainer Response: {repo}#{issue}[/green]",
            )
        )

    asyncio.run(_run())


@app.command()
def synthesize_mcp(
    package: str = typer.Option(..., "--package", "-p", help="Package name"),
    api_summary: str = typer.Option(
        "read_array(path), write_array(path, data)", "--api", "-a", help="API signature summary"
    ),
) -> None:
    """Synthesizes a production FastMCP 2.0 server for a niche Python package."""
    console.print(
        Panel.fit(
            f"[bold purple]🔌 FastMCP 2.0 Bridge Synthesizer[/bold purple]\nTarget Package: [bold]{package}[/bold]"
        )
    )

    async def _run() -> None:
        agent = DocBridgeAgent()
        with console.status(
            "[bold cyan]Generating typed FastMCP 2.0 tool definitions...[/bold cyan]"
        ):
            result = await agent.generate_mcp_bridge(package, api_summary)

        console.print(
            Panel(
                result.server_source_code,
                title=f"[green]Generated {result.mcp_server_filename}[/green]",
            )
        )

    asyncio.run(_run())


@app.command()
def audit_mcp(
    file_path: str = typer.Argument(..., help="Path to FastMCP server Python file"),
) -> None:
    """Audits an MCP server source file for security risks (SSRF, Injection, Path Traversal)."""
    console.print(
        Panel.fit(f"[bold yellow]🛡️ MCP Security Auditor[/bold yellow]\nAuditing: {file_path}")
    )
    try:
        with open(file_path, encoding="utf-8") as f:
            code = f.read()
    except Exception as e:
        console.print(f"[red]Error opening file:[/red] {e}")
        raise typer.Exit(1) from e

    auditor = MCPSecurityAuditor()
    report = auditor.audit_tool_source(file_path, code)

    status_color = "green" if report.is_safe_for_deployment else "red"
    console.print(
        f"Safety Score: [{status_color}]{report.security_score}/100[/{status_color}] | Safe: {report.is_safe_for_deployment}"
    )

    if report.issues:
        table = Table(title="Detected Security Vulnerabilities", show_lines=True)
        table.add_column("Tool / Context", style="bold")
        table.add_column("Severity", style="red")
        table.add_column("Type", style="yellow")
        table.add_column("Description")
        table.add_column("Remediation", style="green")

        for issue in report.issues:
            table.add_row(
                issue.tool_name,
                issue.severity,
                issue.vulnerability_type,
                issue.description,
                issue.remediation,
            )
        console.print(table)
    else:
        console.print(
            "[bold green]✅ No high-severity vulnerabilities found in MCP tool source.[/bold green]"
        )


@app.command()
def mcp_serve(
    transport: str = typer.Option("stdio", "--transport", help="stdio or sse"),
    host: str = typer.Option("127.0.0.1", "--host", help="Host address for SSE"),
    port: int = typer.Option(8000, "--port", help="Port for SSE"),
) -> None:
    """Runs the EcoSupport FastMCP 2.0 Server."""
    console.print(f"[bold green]Starting EcoSupport FastMCP Server on {transport}...[/bold green]")
    run_mcp_server(transport=transport, host=host, port=port)


if __name__ == "__main__":
    app()
