---
title: "DataGuard Init Setup and Documentation"
description: ""
status: pending
priority: P2
effort: 
branch: main
tags: []
blockedBy: []
blocks: []
created: 2026-10-02
---

# DataGuard Init Setup and Documentation

## Overview

This plan implements the `dataguard init` setup command and user-facing documentation improvements within the Visual Studio Extension. Based on the `ck:predict` analysis, the architectural direction explicitly avoids custom WPF UIs to strictly follow YAGNI and KISS. Instead, it relies on automation via the CLI, an InfoBar for discoverability, and contextual hyperlinks in the output window.

**Core Deliverables:**
1. **InfoBar Discovery:** Detect missing `.dataguard.yml` on solution load and prompt the user.
2. **Init Command & Minimal UX:** A VS command (`Tools > DataGuard > Initialize DataGuard`) that runs the CLI `dataguard init`. The CLI is upgraded to generate a **Minimal YAML with Schema IntelliSense** (prioritizing the Connection String at the top). The VS Extension automatically opens this file upon creation.
3. **Hyperlinked Docs & Diagrams:** Convert log error codes (e.g., `[DG010]`) into clickable links in the VS Output Window, and provide comprehensive Mermaid diagrams in the repository's `docs/` folder for developer transparency.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [InfoBar Discovery](./phase-01-infobar-discovery.md) | Completed |
| 2 | [Init Command and Auto-Open](./phase-02-init-command-and-auto-open.md) | Pending |
| 3 | [Documentation Hyperlinks](./phase-03-documentation-hyperlinks.md) | Pending |
