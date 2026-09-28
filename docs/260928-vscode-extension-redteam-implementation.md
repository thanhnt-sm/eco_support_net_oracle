# 2026-09-28 — VS Code Extension Red-Team Plan: Full TDD Implementation

**Plan:** `RED_TEAM_REVIEW_PREDICT_SCENARIO_PLAN.md`  
**Mode:** `code --tdd`  
**Duration:** ~4h (multi-subagent)  
**Test gate:** 966/966 pass (904 C# + 62 TS, 0 failures)

---

## Context

Executed the red-team adversarial review plan for `DATAGUARD_VS_CODE_EXTENSION_PLAN.md`, which had flagged 4 Critical + 5 High + 4 Medium findings before any implementation was started. This session implemented all Critical and High fixes end-to-end using TDD: regression tests first (capturing current behavior), then implementation, then verify tests flip green.

---

## What Was Done

### TDD Regression Tests Written First (Step 3.T)

**C# (`tests/DataGuard.Core.Tests/RedTeamRegressionTests.cs`, 16 tests):**
- `RawSqlDescriptor` default values and new `IsStoredProcedure`/`ProcedureName` fields
- DG101 suppression when `IsStoredProcedure == true`
- DG013 detection via `IsSqlString` / `ExtractSqlText`
- `OracleDialectChecker` property key presence

**TypeScript (`redaction-redteam.test.ts`, 6 tests):**
- `SarifResult.properties` field propagation to `FindingItem`
- `parseSarifToFindings` contract (no DB connection logic)
- `clearFindingsAndDiagnostics` interface contract

### Implementation (Steps 3.I.1–3.I.10)

| Step | Change |
|---|---|
| 3.I.1 | Add `IsStoredProcedure` + `ProcedureName` to `RawSqlDescriptor` (init-only, binary-compatible at all 6 call sites) |
| 3.I.2 | Section 2b: scan `CommandType.StoredProcedure` assignments + Dapper `commandType:` named arg; Section 3: check constructor arg before SqlText extraction |
| 3.I.3 | `DG101 ParameterCountRule`: early-return when `IsStoredProcedure == true` |
| 3.I.4 | Oracle: `OracleKeywords HashSet` → `OracleKeywordMigrations` + `OracleOperatorMigrations` dicts; fix `ContainsKeyword` to use `Regex.Escape` + `\s+` for multi-word keywords |
| 3.I.5 | TS: `properties?: Record<string, unknown>` on `SarifResult` + `FindingItem`; propagated in `parseSarifToFindings` |
| 3.I.6 | `codelens-provider.ts`: violation-only CodeLens, 50-lens cap, `CancellationToken` |
| 3.I.7 | `decoration-manager.ts`: gutter icons + highlight decorations, theme tokens, no `hoverMessage` |
| 3.I.8 | `hover-provider.ts`: sole Markdown authority, deduplicated multi-finding, `isTrusted: false` |
| 3.I.9 | `extension.ts`: register all 3 providers, `onDidChangeConfiguration` reload, `clearFindingsAndDiagnostics` dispose |
| 3.I.10 | `package.json`: include new test files in `test` script |

---

## Code Review Bugs Caught and Fixed (Step 5)

Three real bugs found by the code-reviewer subagent after implementation:

1. **Bracketed SP name regex too strict** (`ProjectCSharpSqlSource.cs` lines 249, 441)  
   Original: `^[A-Za-z0-9_#$]+(\.[A-Za-z0-9_#$]+)*$`  
   Rejected `[dbo].[GetCustomer]` style SQL Server names.  
   Fixed: `^(\[[\w\s]+\]|[A-Za-z0-9_#$]+)(\.(\[[\w\s]+\]|[A-Za-z0-9_#$]+))*$`

2. **Receiver-less `CommandText` false association** (`ProjectCSharpSqlSource.cs` Section 2b)  
   When `CommandType = StoredProcedure` had no receiver (unqualified), the sibling `CommandText` search matched any `qualified.CommandText` in the same block.  
   Fixed: require `sibLeft.receiver == null` when `receiverName == null`.

3. **`endCol + 1` inflation in hover/decoration range** (`hover-provider.ts` line 40, `decoration-manager.ts` line 57)  
   `Math.max(startCol + 1, endCol)` forced minimum width of 1 even for zero-width SARIF regions, inflating hover zones.  
   Fixed: `Math.max(startCol, endCol)`.

---

## Verification

| Suite | Before | After |
|---|---|---|
| C# Core + Analyzers | 801 pass | 801 pass |
| C# full solution | 904 pass | 904 pass |
| TypeScript | 62 pass | 62 pass |
| **Total** | **966 pass** | **966 pass, 0 fail** |

---

## Files Changed

**New:**
- `tests/DataGuard.Core.Tests/RedTeamRegressionTests.cs` (16 regression tests)
- `src/DataGuard.VSCode/src/ui/codelens-provider.ts`
- `src/DataGuard.VSCode/src/ui/decoration-manager.ts`
- `src/DataGuard.VSCode/src/ui/hover-provider.ts`

**Modified:**
- `src/DataGuard.Core/Abstractions/Contracts.cs`
- `src/DataGuard.Core/Rules/ContractRules.cs`
- `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`
- `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs`
- `src/DataGuard.VSCode/src/ui/redaction.ts`
- `src/DataGuard.VSCode/src/ui/redaction-redteam.test.ts`
- `src/DataGuard.VSCode/src/extension.ts`
- `src/DataGuard.VSCode/package.json`
- `docs/03-components/**` (12 files EN + VI)
- Both plan `.md` files updated to COMPLETED

---

## Key Decisions

- `IsStoredProcedure`/`ProcedureName` as `init`-only with defaults → zero call-site churn at 6 existing construction points.
- No new `StoredProcedureDescriptor` type (red-team finding F1 fix) — single boolean flag on existing record is sufficient and avoids descriptor hierarchy.
- `hoverMessage` deliberately absent from `DecorationManager` — `HoverProvider` is sole Markdown authority (avoids double-display, red-team finding H3).
- `isTrusted: false` on all `MarkdownString` in hover — prevents arbitrary command link injection.
