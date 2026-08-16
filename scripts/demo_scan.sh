#!/usr/bin/env bash
# ==============================================================================
# EcoSupport Live CLI Demonstration Script
# Designed for Anthropic Grant Review & Terminal Recording
# ==============================================================================

set -e

GREEN="\033[0;32m"
CYAN="\033[0;36m"
YELLOW="\033[1;33m"
BOLD="\033[1m"
NC="\033[0m"

echo -e "${CYAN}${BOLD}"
echo "=============================================================================="
echo "  🚀 ECO-SUPPORT NATIVE — AUTONOMOUS NICHE ECOSYSTEM RADAR"
echo "  Target: Anthropic 'Claude for Open Source' — Ecosystem Impact Track"
echo "=============================================================================="
echo -e "${NC}"

export PATH="$HOME/.cargo/bin:$PATH"

echo -e "${YELLOW}Step 1: Running Pre-Flight Invariant & DocSync Check...${NC}"
./scripts/preflight_agent_check.sh

echo -e "\n${YELLOW}Step 2: Scanning C-FFI Critical Infrastructure Ecosystem...${NC}"
cargo run -p eco-cli --quiet -- scan --category c-ffi --limit 3

echo -e "\n${YELLOW}Step 3: Scanning Model Context Protocol (MCP) Ecosystem Gaps...${NC}"
cargo run -p eco-cli --quiet -- scan --category mcp-connectors --limit 2

echo -e "\n${GREEN}${BOLD}✨ Live Demo Completed Successfully!${NC}"
echo -e "${CYAN}EcoSupport is primed for high-speed autonomous telemetry and triage swarms.${NC}\n"
