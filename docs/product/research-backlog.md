# Research evidence and discovery backlog

Synthesis date: 2026-10-18. Primary pages below were retrieved during parent-coordinated research on that date. They support technical constraints and competitor capabilities, not demand, superiority, market size or willingness to pay. No customer research/outreach was performed. Owners below are roles awaiting assignment.

## Primary-source ledger

| ID / source | Observed capability or constraint | Product inference / next experiment |
|---|---|---|
| SA-SRC-001 [MSBuild security](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-security-best-practices?view=vs-2022) | Untrusted project/build tooling can execute code | Separate parse-only and opt-in execution boundaries; SA-EXP-003 |
| SA-SRC-002 [SQL Server metadata visibility](https://learn.microsoft.com/en-us/sql/relational-databases/security/metadata-visibility-configuration?view=sql-server-ver17) | Permissions restrict metadata visibility | Missing metadata may mean unknown, not absent; M0/M1 corpus |
| SA-SRC-003 [System.Text.Json nullable annotations](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations) | Nullability and required presence are separate concerns | Model input/output semantics explicitly; SA-EXP-001 |
| SA-SRC-004 [TypeScript assertions](https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#type-assertions) | Assertions are erased and do not validate runtime data | Separate generated types and runtime validation; M4 |
| SA-SRC-005 [Zod basics](https://zod.dev/basics) | Parsing validates input; transforms can change output types | Golden generator/runtime agreement; M4 |
| SA-SRC-006 [MCP security](https://modelcontextprotocol.io/docs/2026-07-28/tutorials/security/security_best_practices) | Protocol security requires explicit authorization boundaries | Pin adopted version, scope/audience checks, no token passthrough; SA-EXP-002 |
| SA-SRC-007 [Atlas analyzers](https://atlasgo.io/lint/analyzers) | Migration analysis includes destructive/backward-incompatible changes | Integrate where sufficient; test incremental source-linked value |
| SA-SRC-008 [Flyway drift](https://documentation.red-gate.com/fd/check-drift-277578903.html) | Drift checking is an existing capability | Do not claim schema drift as unique |
| SA-SRC-009 [NSwag](https://github.com/RicoSuter/NSwag) | OpenAPI tooling includes client generation | Generation alone is not differentiation; SA-EXP-009 |
| SA-SRC-010 [oasdiff](https://www.oasdiff.com/diff) | API change comparison is available | Integrate API diffs; evaluate cross-layer evidence value |
| SA-SRC-011 [Pact](https://docs.pact.io/) | Consumer/provider interaction contract testing exists | Complement rather than claim replacement |
| SA-SRC-012 [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) | SaveChanges has default transactional behavior where provider supports it | Avoid naive missing-transaction warnings; M5 corpus |

This is a bounded comparison of documented capabilities, not an exhaustive competitive audit. Proposed inference: source-linked DB→.NET→wire→consumer evidence may reduce diagnosis effort. Competing explanation: existing tools already solve enough of the problem; setup/noise may exceed the benefit. Test both.

## Discovery experiments — all planned

| ID / owner | Design and dependencies | Decision evidence / stop criterion |
|---|---|---|
| SA-EXP-007 / product researcher | 18–24 people across 10–12 teams: Oracle/SQL Server procedure-heavy, PostgreSQL/MySQL, .NET+React, enterprise/security buyers; separate user/champion/buyer/approver | Last-90-day incident, sanitized PR/fixture, frequency, time/root cause, current tools, false-positive cost, offline/DB access and budget decision; no pain ranking before evidence |
| SA-EXP-008 / researcher | After 007, survey 60–100 qualified convenience respondents; randomize pain order and allow none/unknown | Directional prioritization only, not representative TAM or population prevalence |
| SA-EXP-009 / QA + product | Replay 3–5 sanitized incidents from each of five teams, against current tools and StackAccord prototype | Incremental value for ≥3 teams; stop/pivot if duplicate tools or setup cost cancels benefit |
| SA-EXP-010 / developer experience lead | After safe M0/M1, 5–8-team concierge shadow CI pilot | Targets ≥4 installed, ≥3 active at week 4, ≥3 with evidenced value; record churn/noise and unsupported cases; no blocking before quality gate |
| SA-EXP-011 / product | Test positioning variants after 007; qualified pilot conversion with fixed eligibility and denominator | Do not optimize clicks as proof of usefulness; revise messaging if interest does not become successful use |
| SA-EXP-012 / product + commercial owner | Five qualified buyer discussions after incident evidence; no outreach authorized in this task | Two paid-pilot commitments are a signal, not revenue forecast; stop expansion without demand and maintenance capacity |

Before each experiment approve consent/data handling, sample and label protocol, baseline, primary metric and stopping rule. Preserve negative evidence. No customer production data or secrets belong in fixture repositories. Each module needs three design partners with actual relevant incidents; gates can stop or reorder the roadmap.

## Prioritized remaining backlog

| Priority / ID | Owner / dependency | Acceptance |
|---|---|---|
| P0 SA-DOC-011 release support matrix | Release + provider leads; actual artifacts/hosts | Operation/provider/mode/version matrix, same-ID upgrade evidence, claim review; publication remains paused |
| P0 SA-DOC-012 threat-model ADR | Security + core; M0 audit | Explicit parse/build/network/metadata trust boundaries and failing adversarial fixtures; no assumed sandbox |
| P1 SA-DOC-013 independent evaluation | QA; M0/M1 corpus | Held-out labels, uncertainty, unknown coverage, baseline and after results |
| P1 SA-DOC-014 discovery evidence | Product; SA-EXP-007 | Sanitized incident ledger and decision to continue/pivot |
| P1 SA-DOC-015 bilingual consumer reconciliation | Documentation; canonical claim ledger | Reconcile root/overview/feature/Marketplace consumer prose without overwriting historical records |
| P2 SA-DOC-016 API/agent ADRs | API/frontend/security; M1 and discovery | Serializer and versioned evidence/tool contract decisions before implementation |
| P2 SA-DOC-017 name and visual identity | Product/legal; adoption decision | Trademark/platform checks, logo accessibility and migration plan; no technical rename implied |

Existing SA-DOC-001–010 remain in [source map](source-map.md). No backlog ID indicates a confirmed code defect by itself.

## Source-level backlog from the bounded 20-document review

Baseline `ca68a3159425495f771da866188df75a8237ab4b`, 2026-10-18. These observations come from source, not live database/release execution. No production fix is included. Reproduction descriptions are synthetic regression specifications to run in a separately authorized implementation batch; never run a real release or query production to demonstrate them.

| ID / priority / owner | Source-observed discrepancy | Reproducible rationale and acceptance needed |
|---|---|---|
| SA-SRCBUG-001 / P2 / SQL Server adapter | `src/DataGuard.SqlServer.Adapter/SqlServerLiveQuerySchemaProvider.cs:60–76` copies describe `max_length` into both MaxLength and CharLength; `SqlServerParsers.cs:323–335` normalizes Unicode catalog lengths by dividing bytes by two | Feed equivalent synthetic metadata for `nvarchar(50)` with max_length=100 to both mappings: live CharLength becomes 100, normalized catalog length 50. Include nchar, sysname, varchar and MAX/-1. Confirm intended descriptor units and downstream effects with tests before choosing a fix; no live false-negative result is asserted here. |
| SA-SRCBUG-002 / P2 / Core rules | `src/DataGuard.Core/Rules/NamingConventionRule.cs:12,24,37–50` stores a convention but comparison never reads it; CLI `ProviderRuleCatalog.cs:152` constructs the default rule | Same descriptor CustomerName→customer_name under ExactMatch and SnakeCaseToPascalCase reaches identical case-insensitive snake/Pascal comparison. Configuration semantics are not enforced by this rule. Decide intended enum behavior, then test distinct conventions and CLI propagation; this is not a claim that a fix is already selected. |
| SA-SRCBUG-003 / P1 / release tooling | Addressed in source: `tools/git-tools/dg-release` now binds monitoring to tag/SHA/run attempt and reports channel job/step evidence; NuGet may still legitimately skip when unconfigured | Hermetic jq/Python regressions in `scripts/tests/test_dg_release_status.py`; see [implementation plan](../../plans/2026-10-19-release-channel-evidence.md). No live publication/registry validation; publication pause preserved. |
| SA-SRCBUG-004 / P2 / CLI | `src/DataGuard.Cli/Commands/OracleCheckCommand.cs:36,61–67` reads --format but always adds ConsoleDiagnosticSink and optional FileSarifSink regardless of its value | Inject synthetic violations/metadata in a test seam and compare --format text/json/sarif with/without output; source currently chooses the same sinks. Decide whether to honor formats or reject unsupported values. No Oracle connection or command execution was used for this observation. |

The DG004 extra-column threshold and static SELECT-star skip, DG009 CLOB/NCLOB fallback predicate, and Scorecard conservative readiness/mismatched-SHA upload behavior are documented **scope or policy**, not automatically classified as defects. Separate symptom, observed branch and inferred impact before prioritizing implementation.

### Editor boundaries requiring adversarial runtime acceptance

These are observed implementation boundaries with **unverified impact**, not confirmed exploits or selected fixes:

- **SA-HOST-001 — diagnostic path identity:** `src/DataGuard.VisualStudio/SarifErrorListPublisher.cs:34–68` and `src/DataGuard.VSCode/src/security.ts` perform lexical containment, not link-target resolution. Use a workspace link to an external disposable fixture and observe both path admission and actual editor navigation. Do not infer arbitrary file access from lexical acceptance alone.
- **SA-HOST-002 — report read bounds:** `src/DataGuard.VSCode/src/extension.ts:651–654,890–896` checks size before whole-file reads. In a synthetic test, grow/replace the report between stat and read; measure whether the intended 20/50 MiB ceiling bounds actual bytes/memory. Excess memory impact is not measured here.
- **SA-HOST-003 — POSIX escalation:** `src/DataGuard.VSCode/src/extension.ts:970,1009` uses `child.killed` as a guard. Model failed group SIGTERM, successful direct SIGTERM and a still-running child; verify whether the delayed SIGKILL path executes. No live child-process leak is claimed from source alone.
- **SA-HOST-004 — Windows termination classification:** `src/DataGuard.VisualStudio/ProcessTerminator.cs:68–96` classifies a successful kill request as Terminated even when exit is not observed; direct fallback does not prove descendants stopped. Test delayed exit, wait failure and descendants on Windows before strengthening shutdown guarantees.

**Rejected finding:** a missing `.dataguard.yml` is not itself a current IDE-safe validation failure. `ValidateCommand.cs:120` passes `allowMissingConfig: ideSafe`; `Services/ConfigLoader.cs:23–27` warns and uses defaults. The extension's explicit path alone does not prove rejection. Test this full caller chain, not the loader's default optional argument in isolation.
