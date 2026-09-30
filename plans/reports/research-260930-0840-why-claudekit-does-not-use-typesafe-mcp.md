# Why claudekit rarely calls `mcp__typesafe__typesafe_judge`

Date 2026-09-30. Source: `C:\Users\thant\.claude\` (legacy install). Paths below relative to it unless noted. Read-only research.

## Summary
- TypeSafe is wired as 100% optional, advisory, tighten-only, silent-on-failure everywhere. Every instruction is "(optional)" / "When available". No MUST anywhere.
- Enablement is fine for this project (allowlisted in global `.ck.json`, hint printed). Not an enablement problem.
- Cook: SKILL.md main body has one 3-line "(optional)" paragraph at the very end (`skills/cook/SKILL.md:130-134`); actual triggers live in reference files; 3 of 4 cook points are conditional on rare states (ambiguous intent / `--auto` review path / phase risk & quality gate only pointed at from steps, not enforced).
- Tool is deferred -> needs a ToolSearch step first; nothing tells the model that step is worth it, and nothing checks it happened.
- Most decision points execute inside subagents (`code-reviewer`, `fullstack-developer`, `tester`) whose `tools:` lists do NOT include the tool -> physically cannot call it. Only `planner` (+ main `orchestrator`, All tools) can.
- No enforcement hook, no call telemetry. Skipping has zero observable consequence, and by design "skip silently" is the correct behavior on any doubt.
- Kit's own eval says value is unproven (spec only, not run; expansion frozen; may be retired).

## Integration map

| Skill / agent | Decision point | file:line | Wording | Executor | Executor can call tool? |
|---|---|---|---|---|---|
| ck-plan | Mode auto-detect (`mode` choice) | `skills/ck-plan/references/workflow-modes.md:17-46` | "(Optional)", "When ... available" | main (ck-plan runs inline, orchestration-protocol) | Yes if main = orchestrator (All tools) + ToolSearch load |
| ck-plan | Scope challenge (`scope_recommendation`, `overengineering_risk`) | `references/scope-challenge.md:35-73` | "(Optional)" | main | Yes (same) |
| ck-plan | Red-team adjudication (`validity`, `actionability`) | `references/red-team-workflow.md:47-89` (Step 5.6) | "(Optional)"; only in red-team/hard modes | main | Yes |
| ck-plan | Validation question prioritization | `references/validate-workflow.md:59-88` (Step 3.5) | "(Optional)"; only in validate step | main | Yes |
| ck-plan | Plan quality self-assessment | `references/plan-organization.md:153-198` | "(Optional)"; "skip silently" | main / planner | planner: yes (listed) |
| ck-plan SKILL.md | Summary paragraph only | `skills/ck-plan/SKILL.md:284-286` (+ dotted "Optional" nodes 187-208) | "(optional)" | - | - |
| planner agent | generic "try it" | `agents/planner.md:7,30-32` | "Try ... if unavailable skip silently — your analysis is the primary source of truth" | planner | Yes (only agent with it in `tools:`, line 7) |
| cook | Intent disambiguation (`mode`) | `skills/cook/references/intent-detection.md:42-74` | "(Optional)"; "Only invoke when `heuristic_confidence == ambiguous`" (:70) | main | Yes, but rarely triggered |
| cook | Review auto-approval (`safe_to_auto_approve`) | `references/review-cycle.md:47-57, 71-99` | "IF TypeSafe available"; only in auto-handling cycle at score>=9.5 | main (after code-reviewer returns) | Yes, but only `--auto` path |
| cook | Phase risk pre-check (`implementation_risk`) | `references/workflow-steps.md:57` -> `typesafe-judgments.md:93-130` | "Optional ... see references" (one line pointer) | main or fullstack-developer (Step 3) | main yes; fullstack-developer NO |
| cook | Post-impl quality gate (`ready_to_ship`, `quality_tier`) | `references/workflow-steps.md:106` -> `typesafe-judgments.md:132-` | "Optional ... see references" (one line pointer) | main | Yes if main does it |
| cook SKILL.md | Summary paragraph | `skills/cook/SKILL.md:130-134`, `139` | "(optional)" | - | - |
| code-review | Finding triage (`finding`, `confidence`) | `skills/code-review/SKILL.md:65, 111`; `references/typesafe-triage.md` | "**Optional:** if ... available"; Stage 3 only | `code-reviewer` (forked: `context: fork`, `agent: code-reviewer`, SKILL.md:6-7) | NO (`agents/code-reviewer.md:7`) |
| test | none; explicitly "No TypeSafe ... used" | `skills/test/SKILL.md:77` | n/a (dropped 2026-09-27) | tester | n/a |
| shared spec | canonical rules, 8 decision points | `skills/shared/typesafe-integration.md:13,36-42,146-163` | "strictly optional and non-blocking ... MUST continue exactly as before" (:13) | - | - |

Agents lacking the tool (grep of `agents/*.md`): code-reviewer, tester, fullstack-developer, code-simplifier, debugger, advisor, researcher, docs-manager, git-manager, etc. Only `agents/planner.md:7` lists it (both `mcp__typesafe__typesafe_judge` and plugin form). `orchestrator` = "All tools except NotebookEdit" (agents/orchestrator.md:4-5), so main can call it.

## Enablement chain (this project = ENABLED)
1. `TYPESAFE_API_KEY` non-empty in process env, else off `no-key` (`hooks/lib/typesafe-enabled-resolver.cjs:6-7`; `.env.example:80-110`).
2. Global `~/.claude/.ck.json` readable; `typesafe: false` / non-object / `enabled` non-true kill switch (resolver :8-14).
3. Global `typesafe.projects[]` contains git root (realpath, case-insens win32). Project's own `.claude/.ck.json` is NEVER read (S4 #6, resolver :29-40). `schemas/ck-config.schema.json:465-482`.
4. This project: `.ck.json:2-9` lists `D:\100.Software\Github\eco_support_net_oracle` -> on ('global-allowlist'). Project `.claude/` has only `settings.local.json`, no typesafe key; no `.mcp.json`. Server registered globally (`~/.claude.json` mcpServers `typesafe` -> `mcp/typesafe/typesafe-mcp-server.cjs`).
5. session-init prints hint iff resolver enabled (`hooks/session-init.cjs:406-426`); wrapped in try/catch fail-open. `subagent-init.cjs:21` deliberately REMOVED the hint for subagents ("already covered by session-init for the orchestrating session").
6. MCP server re-runs resolver per call and returns `isError "TypeSafe disabled"` if off (`mcp/typesafe/typesafe-mcp-server.cjs:162-168`); 401/403 kills tool for process lifetime (:160-163). Client: 10 s timeout, no retry, 429/529/non-2xx = off (`shared/typesafe-integration.md:42`), 32 KB cap after redaction.
7. `runtime/bin/ck-launch.cjs:20,118` deletes `TYPESAFE_API_KEY` from env for the CLI process (RT#8) - only relevant if the launcher is used; server reads key from its own env. Worth confirming key reaches server (user says reachable, so OK).
8. Separate: `settings.json:265-267` enables official plugin `typesafe@typesafe-ai` (skill `typesafe:typesafe-ai`) - a different thing (docs/cookbook skill), not wired into ck workflows.

## Root causes, ranked
1. **Advisory by design, no MUST, no consequence.** Every step says Optional / "skip silently" / "proceed as if unconfigured" (`typesafe-integration.md:13`, `planner.md:32`). Tighten-only + "your analysis is the primary source of truth" makes skipping the rational, zero-cost choice. Highest impact.
2. **Wrong executor / no tool access.** Code-review triage runs in forked `code-reviewer` (no tool, `code-reviewer.md:7`); cook phase risk in `fullstack-developer` (no tool); tester n/a. `subagent-init` strips the hint. Instructions in those contexts are dead letters. Also fork skills have no ToolSearch guarantee.
3. **Instructions buried / conditional.** Cook SKILL.md body has only a trailing 3-line pointer (`:130-134`); real triggers in 4 different reference files loaded only when their step is reached; two points pointed to by a single line (`workflow-steps.md:57,106`). Intent point fires only when heuristic is "ambiguous" (`intent-detection.md:70`); auto-approval point only on `--auto` cycle at score>=9.5 (`review-cycle.md:47-57`). Typical `/cook` path (clear intent, interactive review) triggers NONE -> explains 0 calls in cook session.
4. **Deferred-tool friction.** Must ToolSearch first (`typesafe-integration.md:38`, session hint "load via ToolSearch if deferred"). Extra turn with no stated payoff; models skip.
5. **No enforcement, no telemetry.** Grep: `isTypesafeJudgeTool`/`TYPESAFE_JUDGE_TOOL_NAMES` referenced only in `hooks/lib/tool-names.cjs`, `session-init.cjs`, `scripts/validate-agents-routing.cjs` (static agent-list lint). No Stop/PostToolUse check, no call log (only opt-in `TYPESAFE_MCP_DEBUG=1` stderr projectDir line, server :154). Cannot measure usage except by transcript grep.
6. **One-line hint is weak.** "use at ck-plan/cook decision points" is generic; doesn't name which points or questions; printed once at SessionStart, decays in long sessions.
7. **Kit deliberately de-emphasized it.** Council D23/D28 froze TypeSafe expansion; test point removed; `evals/typesafe-triage-ab.md:3` says spec only, not run; retire rule if delta <=0 (:~ "Reopen / decision rule"). Owner intent is "keep optional".

## Evidence quotes
- "**Integration is strictly optional and non-blocking.**" - `skills/shared/typesafe-integration.md:13`
- "if unavailable, errored, or disabled, skip silently - your analysis is the primary source of truth" - `agents/planner.md:32`
- "Only invoke TypeSafe when `heuristic_confidence == "ambiguous"`" - `skills/cook/references/intent-detection.md:70`
- "Optional per-phase risk pre-check and TypeSafe judgments: see `references/typesafe-judgments.md`." - `skills/cook/references/workflow-steps.md:57`
- "No TypeSafe or other AI judgment is used for this decision" - `skills/test/SKILL.md:77`
- "Status: spec only, not yet run. ... froze all further TypeSafe expansion" - `evals/typesafe-triage-ab.md:3`
- Eval reopen rule: "Δ <= 0 on 4.1 ... retire 4.1 entirely"; Arm 3 native Haiku judge may replace TypeSafe dependency - same file. `evals/README.md:89-95` confirms manual, unrun.
- Hint text - `hooks/session-init.cjs:423`

## Recommendations (rank by impact/effort). Kit rule: do NOT edit `~/.claude`; make changes in the kit source repo (`D:\100.Software\Github\claudekit-custom`, which is in the allowlist) and reinstall.
1. (Low effort, high impact) Decide if you actually want it. Evals show no measured value; forcing it costs latency + external data egress. If "yes", do 2-5.
2. (Low) Add MUST step in `cook/SKILL.md` main body + `ck-plan/SKILL.md`: "At Step 0: ToolSearch select:mcp__typesafe__typesafe_judge; at named points X,Y,Z call it. Report `TypeSafe: called N / skipped (reason)` in the step output line." Making the outcome a required output line creates observable consequence. Drop "ambiguous only" gate for a cheap always-run `implementation_risk` + `ready_to_ship` pair.
3. (Low) Have session-init/subagent-init preload: instruct main to run ToolSearch select at session start (hint text: "First action: load via ToolSearch select:mcp__typesafe__typesafe_judge"). Removes deferral friction.
4. (Low) Add tool to `code-reviewer.md` tools (and `tester`/`fullstack-developer` only if you want their points). Update `TYPESAFE_JUDGE_TOOL_NAMES`-based lint (E13 is fine). Alternative: move triage into main after code-reviewer returns findings (main already has the tool) - no agent change, but keeps calls out of forks.
5. (Medium) Stop/SubagentStop or PostToolUse hook: scan transcript for `typesafe_judge` tool_use when cook/plan skill ran and project is enabled; emit warning (not block) if none. Also gives telemetry. Optionally log calls to a JSONL via PostToolUse matcher `mcp__typesafe__.*`.
6. (Medium) Restructure: cook step files inline the question set at the step (not "see references"), so it is in context when the decision arrives.
7. (Alt) If the real goal is cheap judgment, follow eval arm 3: native Haiku `type: prompt` hook/agent - no key, no egress, fewer moving parts.
Do NOT: make results gating-mandatory (violates tighten-only design, fail-closed on 429/529 would then block workflows).

## Unresolved questions
- Does a subagent (planner) actually see the deferred tool loaded, or must it ToolSearch itself? Not verified; `tools:` frontmatter only permits it.
- Is the session model Sonnet (main implements directly, cook may skip delegation)? Affects who runs steps 3/5 but not the conclusion.
- Does `TYPESAFE_API_KEY` reach the MCP server process given `ck-launch.cjs:118` deletes it from CLI env? User says reachable, so likely fine; confirm via `TYPESAFE_MCP_DEBUG=1`.
- Actual call counts: derived from user's account, not re-verified from transcripts.
- `plugin` form `mcp__plugin_ck-typesafe_typesafe__typesafe_judge` irrelevant on legacy install.

**Status:** DONE
**Summary:** TypeSafe is designed as optional/advisory with no enforcement, buried in reference files, deferred-tool friction, and most executing subagents lack the tool; project is properly enabled. Report written with ranked fixes routed via kit source repo.
**Concerns/Blockers:** None; subagent-level deferred-tool visibility and key propagation unverified.
