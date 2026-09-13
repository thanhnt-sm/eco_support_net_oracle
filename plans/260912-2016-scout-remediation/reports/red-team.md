---
type: red-team
date: 2026-09-12
status: incorporated-in-plan
---
# Red-team review — scout remediation plan

## Summary

Four independent GPT-5.6 Sol reviewers: Security Adversary/Fact Checker, Failure Mode Analyst/Flow Tracer, Assumption Destroyer/Scope Auditor, Scope & Complexity Critic/Contract Verifier. They read actual plan/source with rg/path/flow checks; no tests/build/production edits by reviewers.

20 raw findings consolidated into **15 findings: 4 Critical, 11 High; 15 Accept, 0 Reject**. Two raw Medium findings (legacy flush completion, TelemetryConfig ABI) merged into broader High/Critical issues. Every retained finding has actual codebase file:line evidence; no evidence-free suggestion accepted. User authorized plan/redteam work, so accepted corrections applied to plan autonomously without extra permission request. Accepted means **plan corrected**, not source issue fixed.

## Adjudication and coverage

| ID | Severity | Finding / failure scenario | Raw review mapping | Code evidence | Applied phases | Ledger AC parents | Decision |
|---|---|---|---|---|---|---|---|
| RT-01 | Critical | Public drift false-clean: Public CheckDrift chỉ so violation, missing/corrupt có thể thành HasDrift=false. | Failure1 | [src/DataGuard.Core/PublicApi/PublicApiSurface.cs:203](../../../src/DataGuard.Core/PublicApi/PublicApiSurface.cs:203) | 1,3 | CI-01, CS-02, F4 | Accept |
| RT-02 | Critical | Acquisition missing/empty và format consumers: Bare contracts list/early export bỏ trạng thái nguồn; format validate là evidence, không JSON. | Failure2 + Scope1 | [src/DataGuard.Cli/Program.cs:104](../../../src/DataGuard.Cli/Program.cs:104) | 3,5 | CI-01, V06 | Accept |
| RT-03 | Critical | Incomplete end-to-end và ABI: Legacy IsClean/return list hoặc đổi positional record phá an toàn/compiled consumers; TelemetryConfig cũng cần ABI gate. | Failure3 + Assumptions2 + Scope5 | [src/DataGuard.Core/PublicApi/PublicApiSurface.cs:247](../../../src/DataGuard.Core/PublicApi/PublicApiSurface.cs:247) | 1,2,3,4,5 | CE-02, CE-09, CI-09, CS-14, V06 | Accept |
| RT-04 | High | Hook transaction và destination trust: Write/chmod/cancel có thể mất hook; linked Husky/lefthook chuyển hướng outside; worktree root cần authority riêng. | Failure4 + Security5 | [src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs:186](../../../src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs:186) | 5 | CI-04, V06 | Accept |
| RT-05 | High | Telemetry shutdown và FlushEvents completion: Timer-only dispose mất queue; fire-and-forget adapter phá synchronous callers. | Failure5 + Scope4 | [src/DataGuard.Core/Telemetry/TelemetryCollector.cs:156](../../../src/DataGuard.Core/Telemetry/TelemetryCollector.cs:156) | 4 | CS-14 | Accept |
| RT-06 | High | Credential store file boundary: Plaintext opt-out vẫn cần permissions, atomicity, bounded read, link protection. | Security1 | [src/DataGuard.Core/Security/CredentialManager.cs:208](../../../src/DataGuard.Core/Security/CredentialManager.cs:208) | 4 | CS-07 | Accept |
| RT-07 | High | Assessment discovery/ancestor containment: Chỉ sửa ba readers bỏ BuildCiPack/DependencyHealthPack/recursive discover. | Security2 | [src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs:119](../../../src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs:119) | 4 | CS-04, CS-13 | Accept |
| RT-08 | High | All emission sinks và path privacy: DiagnosticEmitter vẫn chuyển raw violation cho sink, SARIF ghi absolute source path. | Security3 | [src/DataGuard.Core/Reporting/DiagnosticEmitter.cs:27](../../../src/DataGuard.Core/Reporting/DiagnosticEmitter.cs:27) | 4 | CS-08, CS-09 | Accept |
| RT-09 | High | EF explicit execution grant/Oracle default: Trust chưa có source; directory DLL fallback và default cursor describe=true nguy hiểm nếu wired. | Security4 | [src/DataGuard.Core/Sources/EfModelSource.cs:608](../../../src/DataGuard.Core/Sources/EfModelSource.cs:608) | 2 | CE-01, AD-04 | Accept |
| RT-10 | Critical | Lossless provider→schema-v3 map: PG không select default; MySQL DataDefault chứa COLUMN_TYPE, v3 có thể miss default/ordinal drift. | Assumptions1 | [src/DataGuard.MySql.Adapter/MySqlStoredProcedureParser.cs:117](../../../src/DataGuard.MySql.Adapter/MySqlStoredProcedureParser.cs:117) | 2,3 | CI-01, CS-02, AD-03, V03, V06 | Accept |
| RT-11 | High | Rule prerequisite result transport: PG004/Oracle DG012 no-op không đồng nghĩa Evaluated; violations-only interface mất coverage. | Assumptions3 | [src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs:532](../../../src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs:532) | 2,3,5 | AD-03, AD-04, CE-07, CE-09, V06 | Accept |
| RT-12 | High | EF/plugin lifetimes qua public caller: WithPlugins bỏ manager ownership; default-context assembly resolution không có unload contract. | Assumptions4 | [src/DataGuard.Core/PublicApi/PublicApiSurface.cs:87](../../../src/DataGuard.Core/PublicApi/PublicApiSurface.cs:87) | 1,2,4 | CE-01, CS-10 | Accept |
| RT-13 | High | Live DB/Windows harness không tồn tại sẵn: xUnit2 + MsSql fixture không tự tạo PG/MySQL markers; package Windows không chạy VS host. | Assumptions5 | [tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj:19](../../../tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj:19) | 1,7 | V02, V03, V04, F5 | Accept |
| RT-14 | High | Phase2/3 Program ownership contradiction: Phase2 catalog+Program wiring đụng Phase3 resolver và trái ledger AD02/03 owner. | Scope2 | [src/DataGuard.Cli/Program.cs:934](../../../src/DataGuard.Cli/Program.cs:934) | 2,3 | AD-02, AD-03, CE-02, V06 | Accept |
| RT-15 | High | Exit category chưa có số cụ thể: Exit1 hiện vừa findings vừa failures; yêu cầu distinct mà không matrix sẽ diverge. | Scope3 | [src/DataGuard.Cli/Program.cs:163](../../../src/DataGuard.Cli/Program.cs:163) | 1,3,5 | CI-01, CI-05, CE-09, V06 | Accept |

Each accepted finding is marked inline `RT-xx` in its phase(s). Supplemental acceptance is cumulative with the original 66-row ledger, not a replacement. Raw references map all 5 findings from each reviewer exactly once; overlaps are merged, not dropped. No arbitrary cap removed a unique finding (15 unique <=15 skill cap).

## Rationale and boundaries

- RT01–03/10–11 prevent false-clean at source, public API, engine, exports, CLI and IDE—not just self-hash CLI patch.
- RT04/06–09 close specific filesystem/emission/execution gaps; no promise of hostile-filesystem raceproofing, plugin sandboxing or forced in-process code termination.
- RT05 preserves legacy successful FlushEvents synchronous completion while making timer/shutdown behavior bounded and explicit.
- RT12 tests lifecycle through public caller, not direct manager fixture only.
- RT13 creates explicit test/profile/seam deliverables and separates manual host smoke from Windows packaging.
- RT14 keeps Phase2 pure primitives/public engine; Phase3 owns every Program wiring batch. CE02/09 do not close before CLI/IDE AC.
- RT15 freezes exact proposed exit numbers and compatibility release-note gate. No guess at implementation time.
- Scope1 JSON claim was already corrected during main review; its substance is retained under RT02 (the actual evidence format is covered).
- Primary-source research corroborated catalog field distinctions and metadata permission limitations after RT10; see [research](../research/solution-research.md).

## Contract verification census

Reviewer reported source baseline call/construction census (definitions separate, no claim generic rg hits are semantic callers):

| Surface | Definitions | Calls/constructions found |
|---|---|---|
| ValidationPipeline.ValidateAsync | 1 | 3 direct |
| CheckDriftAsync | 1 | 2 |
| DataGuardApi.CreatePipeline | 2 overloads | 10 |
| ConcurrentValidationEngine.ValidateAsync | 1 | 4 (CLI1 + tests3) |
| EfModelSource.ExtractFromDesignTimeAsync | 1 | 0 |
| DataGuardConfiguration | 1 positional record | 38 new expressions |
| BaselineFile | 1 | 2 explicit constructors |
| SnapshotColumn | 1 | 2 explicit named constructors |
| TelemetryConfig | 1 | 18 new expressions |
| FlushEvents | 1 | 11 calls |

CP1 must persist full file:line caller list, compiled-consumer fixture and new seams before edits; these baseline counts are not a license to ignore downstream consumers. New `DefaultProvider` and `--legacy-violation-diff` have no existing source implementation; they remain planned additions.

## Recommendations / unresolved

Plan readiness verdict **CAUTION**: feasible with incorporated gates, but execution remains Pending. DB/Windows availability, owner-approved cleanup and any rejected behavioral compatibility migration require explicit follow-up; not source-complete now. Final session must report blocked/open separately. Root performs artifact/coverage/link validation and Sol follow-up checks accepted amendments; no live DB or runtime vulnerability reproduction is claimed by red-team.
