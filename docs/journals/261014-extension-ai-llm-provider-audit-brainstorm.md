---
title: "Extension AI/LLM & Provider Audit and DB Provider Brainstorm"
date: 2026-10-14
type: journal
tags: [brainstorm, scout, extension, vscode, visual-studio, provider, deterministic]
---

# Session Journal: Extension AI/LLM & Provider Audit (`2026-10-14`)

## Context
User requested a full workspace source audit using `ck:*` skills (`ck:brainstorm`, `ck:scout`, `ck:research`, `ck:ask`, `ck:council`, `ck:docs`, `ck:project-organization`) plus TypeSafe Judge and Bicameral Advisor to determine whether the extensions built in this repository (`DataGuard.VSCode` and `DataGuard.VisualStudio`) use any AI/LLM and whether they require any provider.

## What Happened
1. **Parallel Codebase Scouting (`ck:scout`)**:
   - Audited `src/DataGuard.VSCode/`, `src/DataGuard.LanguageServer/`, `src/DataGuard.VisualStudio/`, `src/DataGuard.Cli/`, `src/DataGuard.Core/`, `src/DataGuard.Analyzers/`, and `docs/`.
   - Confirmed **0% AI/LLM usage**: zero AI SDKs, zero LLM network calls, zero AI API keys.
   - Confirmed **`provider` = Relational Database Engine Dialect** (`sqlserver`, `postgresql`, `mysql`, `oracle`) + optional `ZeroTrustCredentialProvider` for DB connection strings.
   - Disambiguated 3 places where AI/API-key keywords appear:
     - `DG015` / `DG016` rules deterministically catch *AI-hallucinated SQL* (tables/columns invented by coding assistants).
     - Regex redaction in `security.ts`, `redaction.ts`, `SecretsPack.cs`, and `DiagnosticEmitter.cs` scrubs `api_key` / `password` in logs/UI.
     - Legacy `docs/mcp.md`, `docs/cli.md`, and `docs/overview/vibe_coder_guide.md` carry explicit `⚠️ ARCHIVED` banners from the pre-pivot Rust/Python project.
2. **TypeSafe System One & Bicameral Advisor Verification**:
   - `xd://typesafe_judge`: `uses_ai_or_llm = option_0` (`no_100_percent_deterministic`, confidence `1.0`), `provider_requirement = option_0` (`no_ai_provider_only_db_provider`, confidence `1.0`).
   - `xd://typesafe_advisor` & `ck:advisor`: `proceed` (`AUTO_APPROVE`, confidence `0.98` / `1.00`), recommending keeping the 100% deterministic architecture and completing the DB Provider workflow across IDE extensions.
3. **Report & User Alignment**:
   - Saved full audit and architectural brainstorm report to `plans/reports/brainstorm-261014-extension-ai-llm-provider-audit.md`.
   - User confirmed direction: continue perfecting the **Database Provider** capabilities for the IDE extensions.

## Decisions
- Maintain 100% deterministic, zero-AI runtime in both `DataGuard.VSCode` and `DataGuard.VisualStudio`.
- Focus extension work on the Database Provider (`sqlserver`, `postgresql`, `mysql`, `oracle`) lifecycle:
  - Visual Studio VSIX (`plans/261014-0750-ide-extension-db-phase` Phases 1–4 implemented in source: Roslyn `4.12.0` pin, EF Core `*ModelSnapshot.cs` ground truth under `--ide-safe`, `LiveDatabaseProvider`/`LiveConnectionEnvVar`/`LiveDatabaseSchema` options in `DataGuardOptionsPage.cs`, and `RefreshSnapshotCommand` / `VerifyShapeCommand` in `Menus.vsct`).
  - Remaining follow-ups: rebuild/stage `.vsix` artifacts (`artifacts/visualstudio/` & `artifacts/vscode/`) and address the 4 deferred VS Code / LanguageServer parity items documented in `plans/261014-0750-ide-extension-db-phase/plan.md:50-58`.
