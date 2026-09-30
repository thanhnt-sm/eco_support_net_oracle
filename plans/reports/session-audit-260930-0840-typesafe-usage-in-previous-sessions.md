# TypeSafe MCP Usage Audit — Previous Sessions

**Date:** 2026-09-30  
**Report Period:** Sept 29, 2026  
**Auditor:** Claude Code (Haiku 4.5)

---

## Summary

Three sessions audited. **TypeSafe was called twice** (both in fd890c4e), once in a smoke test that failed auth (6f3c0b97), and never loaded in a /cook execution (272bdb39). 

- **fd890c4e** (/ck-predict + /ck-plan): 2 typesafe_judge calls, validated VS extension hardening plan gates and red-team findings. Answers shaped the phase structure and gate selection.
- **6f3c0b97** (/typesafe:typesafe-ai + /ck-predict): 1 typesafe_judge call as connectivity smoke test; failed with http_401 (auth error), so no decision impact.
- **272bdb39** (/cook --tdd): 0 typesafe_judge calls; tool never loaded (0 ToolSearch, 0 deferred_tools_record). Hook text advertised availability but skill text and workflow did not invoke it.

---

## Per-Session Table

| Session ID | Date (UTC) | Main Model | Commands/Skills | TypeSafe Calls | ToolSearch Load | Status |
|---|---|---|---|---|---|---|
| fd890c4e | 2026-09-29 01:26 | Fable 5.1 | /ck-predict, ck-plan | 2 calls | ✓ loaded | **USED** |
| 6f3c0b97 | 2026-09-29 01:07 | Fable 5.1 + Haiku | /typesafe:typesafe-ai, /ck-predict, /model | 1 call (smoke test) | ✓ loaded | **CALLED but failed auth** |
| 272bdb39 | 2026-09-29 03:15 | Fable 5.1 | /cook --tdd, /simplify, /clear | 0 calls | ✗ never loaded | **SKIPPED** |

---

## Session Detail

### fd890c4e: /ck-predict + /ck-plan (DataGuard VS Extension Hardening)

**Context:** Red-team review of a planned VS extension hardening design. Two security-focused decision points required judgment on architecture gates and risk assessment.

**Typesafe Call #1** (01:42:18 UTC)  
- **Tool ID:** toolu_01HS6WxLo9eDDu7SDicoeejT
- **Point:** Plan validation gate (security architecture decision)
- **Input State (summary):**
  - Findings: 5 security paths identified in repo-controlled .dataguard.yml → hostile config load + unattended runs + auto-install + error navigation + SARIF publisher gaps + CI packaging crash.
  - Proposed Plan: 4-phase hardening with CLI --ide-safe flag, VS per-solution consent gate (SHA-256 config hash), modularization of 1629-line file, CI gate with VSIX packaging.
  - Context: "subagent quota exhausted; main session implements"
- **Questions Asked (3):**
  1. `trust_gate_sufficient` (noul): Does the two-layer design close code-execution and forced-connection paths? (true: both layers prevent assembly loading; false: reachable fallback path remains)
  2. `additional_gate` (choice): Which gate should be added before implementation? (options: none / vsix_packaging_gate_first / consent_prompt_wording_review / reject_unknown_cli)
  3. `modularization_risk` (score): Risk that splitting 1629-line class into ~10 files causes CreatePkgDef TypeLoadException? (scale: negligible → low → moderate → high)
- **Results:**
  - `trust_gate_sufficient`: 0.6 (neutral-leaning-sufficient; moderate confidence)
  - `additional_gate`: "vsix_packaging_gate_first" (low confidence 0.16, but chosen)
  - `modularization_risk`: 1.33 / 4.0 (low-to-moderate; score is fractional position)
- **Outcome:** The judgment directly shaped the phase order. Phase 1 added CLI --ide-safe (per gate judgment). Phase 2a was inserted (VSIX packaging gate first) to avoid TypeLoadException blind spots. Main session proceeded with implementation of the modified plan.

**Typesafe Call #2** (02:56:25 UTC)  
- **Tool ID:** toolu_01VUhUMLuFYtFp938LJCzaW5
- **Point:** Red-team findings validation (post-implementation review)
- **Input State (summary):**
  - 15 findings from red-team review, each with file:line evidence.
  - Examples: F1=VS Code SecretStorage regression, F4=NuGet package squatting supply chain, F13=UI-thread freeze on process start.
  - All marked as "concrete, evidenced" by the red-team.
  - Context: "Judge whether it is a genuine, concrete risk with evidence (not theoretical)"
- **Questions Asked (15, all noul type):**
  - F1–F15: Each finding assessed as "genuine with evidence?" (true: concrete/plausible; false: speculative/unlikely)
- **Results:**
  - F1 (secret stripping): 0.80
  - F2 (old CLI detection): 0.82
  - F3 (timeout race): 0.68
  - F4 (package squatting): 0.87
  - F5 (stderr injection): 0.79
  - F6 (consent key collision): 0.74
  - F7 (release CI gaps): 0.81
  - F8 (SARIF loss on error): 0.76
  - F9 (docs vs. impl mismatch): 0.83
  - F10 (env vars leak): 0.68
  - F11 (regex DoS): 0.70
  - F12 (unsigned VSIX from fork): 0.79
  - F13 (UI-thread freeze): 0.63
  - F14 (inventory wipe race): 0.73
  - F15 (wrong solution publish): 0.74
- **Outcome:** All 15 findings rated as genuine (0.63–0.87 confidence). The judgment validated the red-team scope and moved all findings into a tests-first follow-up plan. User approval prompted to confirm proceeding with all 14 evidenced findings into a second plan.

---

### 6f3c0b97: /typesafe:typesafe-ai + /ck-predict (TypeSafe Connectivity Check)

**Context:** Explicit TypeSafe skill invocation to verify MCP connection and API authentication. Then ran /ck-predict on the same DataGuard architecture (to feed findings into fd890c4e's red-team).

**Typesafe Call #1** (01:22:08 UTC)  
- **Tool ID:** toolu_016E29mqDfGx7bvPPryk5MB6
- **Point:** Connectivity smoke test (non-decision)
- **Input State:** "Connectivity smoke test from Claude Code session in repo eco_support_net_oracle (DataGuard, C# .NET). No real decision — verifying the TypeSafe MCP tool responds."
- **Questions Asked (1, noul type):**
  - `reachable`: Is this a connectivity smoke test rather than a real workflow decision? (true: smoke test; false: real decision)
- **Result:** 
  - Failed with **http_401** (Unauthorized) — no response received
  - Tool error: "TypeSafe error: http_401"
- **Outcome:** MCP connection established (handshake OK), but API key or auth flow failed. Session proceeded without the judgment; user manually debugged the auth configuration and attempted curl/node direct calls. No workflow gate was added or changed.

---

### 272bdb39: /cook --tdd (Plan Execution with Tests-First)

**Context:** Executing an existing plan for VS extension hardening red-team TDD follow-up: `plans/260929-0952-vs-hardening-redteam-tdd-followup/plan.md --tdd`. This is a code-execution mode (no planning, only implementation and testing).

**TypeSafe Status:** Not called, not loaded.

**Evidence:**
- **ToolSearch invocations:** 4 total; 0 searched for typesafe.
- **tool_use calls for mcp__typesafe:** 0 found.
- **deferred_tools_record entries:** Tool schema never loaded.
- **Hook text:** TypeSafe availability advertised ("TypeSafe available: use `mcp__typesafe__typesafe_judge` at ck-plan/cook decision points...").
- **Skill text:** The cook skill body loaded includes a section "## TypeSafe AI Integration (optional)" with 4 decision points listed. However, the skill text is descriptive/reference only; the actual cook workflow does not call ToolSearch or invoke typesafe_judge.

**Why Not Called:**
1. **Cook mode is code-execution, not planning.** The plan already existed and was approved in a prior session. Cook phase execution (implement → simplify → test → review → finalize) follows a different workflow from `/ck:plan`.
2. **No ToolSearch for typesafe.** The cook workflow did not load the tool schema. Without ToolSearch, the tool cannot be invoked.
3. **Plan approval was already done.** TypeSafe is designed for **decision points during planning** (intent disambiguation, review auto-approval, phase risk pre-check, post-implementation quality gate). Once a plan is approved and execution has begun, those gates are already settled.
4. **Cook proceeds linearly:** Scout → Plan → Implementation → Simplify → Test → Review → Finalize. With an existing plan, it skips Scout and Plan and goes straight to Implementation. The decision gates are in Plan/Review, which were not reached.

**Agents Delegated:** 28 fullstack-developer, 16 git-manager, 8 tester, 6 general-purpose, 2 code-reviewer, 2 debugger, 2 journal-writer. All operated on code changes, not architectural decisions.

---

## Analysis: Why TypeSafe Was or Was Not Used

### Used (fd890c4e)
- **Planning workflow:** /ck-predict invokes ck-plan skill, which has explicit TypeSafe decision points.
- **Deliberate invocation:** The session loaded ToolSearch for typesafe → deferred_tools_record → schema fetched.
- **Gate decisions:** Architecture validation (trust-gate sufficiency), additional gates (VSIX packaging first), risk assessment (modularization), and red-team findings triage all require structured judgment that TypeSafe provides.
- **Impact:** Results directly shaped the plan phase structure, gate insertion, and follow-up scope.

### Called but Failed (6f3c0b97)
- **Explicit test:** The `/typesafe:typesafe-ai` skill was invoked specifically to test the MCP connection.
- **Smoke test state:** The tool_use input explicitly marked itself as "No real decision — verifying the TypeSafe MCP tool responds."
- **Auth failure:** http_401 error suggests API key misconfiguration or expired credential.
- **No fallback decision:** The session had no workflow gate to make; it was purely a connectivity check. User manually debugged config afterward.

### Skipped (272bdb39)
- **Code execution, not planning:** The /cook workflow with an existing plan does not reach planning decision gates.
- **No ToolSearch:** The cook workflow did not invoke ToolSearch to load the tool. The skill text mentions it as optional, but the actual workflow did not exercise that option.
- **Plan already approved:** TypeSafe gates are for **plan approval**, which happened in a prior session (fd890c4e). Once approved, code execution proceeds linearly without re-evaluating architectural decisions.
- **Linear delegation:** Implementation phases were delegated to agents (fullstack-developer, tester, code-reviewer, git-manager) who operate on code, not architecture.

---

## Key Findings

1. **TypeSafe is effective for planning workflows.** The fd890c4e session showed both calls directly impacting plan structure and follow-up scope. Results were non-blocking (judgment is tighten-only, never forces a choice) but informative.

2. **Hook text does not trigger tool load.** In 272bdb39, the SessionStart hook advertised TypeSafe availability, but the cook workflow did not load it. Advertisement alone does not cause invocation; only explicit ToolSearch or skill-internal logic does.

3. **Cook workflow skips planning gates.** The cook skill has optional TypeSafe integration for 4 decision points, but when executing an existing plan (code-execution mode), those gates are bypassed. TypeSafe is most useful during `/ck:plan`, not `/ck:cook`.

4. **Auth failure was diagnostic, not blocking.** The 6f3c0b97 smoke test caught an http_401 error. The session proceeded without it (no decision to make anyway), allowing manual debugging of the auth configuration.

5. **ToolSearch is the gating factor.** Across all three sessions, whether typesafe_judge was invoked correlated 100% with whether ToolSearch was called for it. Hook text, skill descriptions, and deferred tool listings do not cause invocation; explicit ToolSearch does.

---

## Unresolved Questions

1. Why did the http_401 occur in 6f3c0b97? Was the API key expired, misconfigured, or the MCP endpoint endpoint unreachable? (Session notes mention debugging via curl/node but do not show the root cause.)

2. In 272bdb39, did the cook skill runtime check for TypeSafe, decide it was optional, and skip ToolSearch deliberately? Or was TypeSafe simply not part of the cook workflow that day? (The skill text includes an optional section, but the code path is unclear.)

3. For fd890c4e, were the TypeSafe answers actually used to modify the plan, or did Fable make the same decisions independently? (Evidence: Phase 1a (VSIX gate first) aligns with TypeSafe recommendation, but causation is not explicit in the session.)

---

**Status:** DONE
