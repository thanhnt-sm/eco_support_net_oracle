# Red-Team Review, Predict & Scenario Report: DATAGUARD_VS_CODE_EXTENSION_PLAN.md

## Verdict: ✅ COMPLETED & VERIFIED (2026-09-28)

*(Historical review verdict: 🛑 STOP — Revise Before Implementation. All revisions were incorporated into the implementation steps and verified with 966 total passing tests.)*

### Implementation & Verification Status (Completed: 2026-09-28)

| Phase / Step | Description | Status | Evidence / Results |
|--------------|-------------|:------:|---------------------|
| **Step 3.T.1 - 3.T.2** | TDD Regression Tests (C# & TypeScript) | ✅ Complete | Added `tests/DataGuard.Core.Tests/RedTeamRegressionTests.cs` (16 tests) covering SP constructor & Dapper detection, DG101 suppression, whitespace keywords. TS test coverage for CodeLens, Hover, and Decorations. |
| **Step 3.I.1 - 3.I.10** | All Core & Extension Implementation Steps | ✅ Complete | `RawSqlDescriptor` augmented with `IsStoredProcedure` & `ProcedureName`. `ProjectCSharpSqlSource` detects SPs across constructor, assignment, and Dapper. `OracleDialectChecker` whitespace regex & migration hints. `redaction.ts` preserves SARIF `properties`. CodeLens, Decorations, and Hover providers registered and lifecycle-managed. |
| **Step 3.V.1 - 3.V.3** | Targeted Verification Runs | ✅ Complete | 801 C# Core tests + 62 TypeScript tests passing, 0 failures. |
| **Step 3.S** | Simplifier Pass | ✅ Complete | Code structure cleaned; zero regressions. |
| **Step 4** | Full Test Suite Execution | ✅ Complete | **904 C# tests + 62 TS tests = 966 total tests passing, 0 failures.** |
| **Step 5** | Code Review & Edge Case Hardening | ✅ Complete | Resolved 3 edge cases: (1) bracketed SP names regex handling, (2) receiver-less `CommandText` false-match avoidance, (3) `endCol + 1` range inflation fix. Re-verified all tests green. |

The plan's factual claims (file paths, line numbers, absence of existing providers) are **verified accurate**. However, 5 independent analysis perspectives (2 hostile red-team, 3 predict/scenario) converged on **4 Critical, 5 High, and 4 Medium** severity findings that would cause silent rule bypass, 100% false-positive storms, broken verification scenarios, and UI clutter if implemented as written. All mitigations detailed below were applied during implementation.
---

## Prediction Report (5-Persona Consensus)

### Agreements (all personas align)

- Steps 1/2/5 (CodeLens, Decorations, Hover) are valuable UX improvements and the extension currently has zero such providers — no conflicts with existing code.
- Step 4 (DG010 migration hints) is the right idea — actionable diagnostics improve developer experience significantly.
- `StoredProcedureDescriptor` MUST NOT be emitted from `ProjectCSharpSqlSource` — it would bypass 100% of validation rules.
- The SARIF `properties` bag is dropped by `parseSarifToFindings()` and `FindingItem` — the plan's claim about Tree/Dashboard showing hints is false without fixing the pipeline.
- Synthesizing `$"EXEC {procName}"` causes DG101 false positives (no inline `@params`) AND DG013 false positives in Oracle context.

### Conflicts & Resolutions

| Topic | Architect | Security | Performance | UX | Devil's Advocate | Resolution |
|-------|-----------|----------|-------------|-----|-----------------|------------|
| CodeLens data source | `DiagnosticCollection` cannot show "✅ No issues" — no knowledge of clean queries | N/A | O(D·F) per keystroke without URI index | "✅ No issues" on every non-SQL method = visual spam | Just show violations, skip clean-query marking | **Violation-only CodeLens** from `DiagnosticCollection`; remove "✅ No issues" claim. OR source from `latestScanReport.queries` for SQL-site awareness |
| SP descriptor type | MUST be `RawSqlDescriptor` per engine invariant | Synthetic `EXEC` concatenation risks SQL injection patterns | N/A | N/A | Add `IsStoredProcedure` flag to `RawSqlDescriptor` instead of string synthesis | **Add `IsStoredProcedure` bool + `ProcedureName` string** to `RawSqlDescriptor`. Emit provider-aware SQL (`EXEC` for SQL Server, `CALL` for Oracle). Rules check flag to suppress DG101 "no params" false positive |
| Hover deduplication | Single data source principle | `isTrusted: false` mandatory | Avoid triple-render | Triple identical popup is terrible UX | Decoration `hoverMessage` + native diagnostic hover + custom HoverProvider = 3 hovers | **Decorations: gutter icons + background only, NO `hoverMessage`**. HoverProvider: sole authority for rich Markdown with migration hints, rule links, quick-fix actions |
| Migration hint pipeline | Properties dropped at SARIF→Extension boundary | Static strings are safe, but operators need backtick-wrapping | N/A | Hints in hover/tree/dashboard require structured data | Just put hints in diagnostic message text | **Extend `SarifResult` and `FindingItem` in `redaction.ts` to preserve `properties`**. Pass structured `migrationHint` from SARIF to Hover/Tree/Dashboard |

### Risk Summary

| Risk | Severity | Mitigation |
|------|----------|------------|
| StoredProcedureDescriptor bypasses all rules | Critical | NEVER emit from ProjectCSharpSqlSource; always use RawSqlDescriptor |
| DG101 flags every SP call ("no parameters detected") | Critical | Add `IsStoredProcedure` flag; DG101 skips parameter count check when flag is true |
| Plan's own verification test fails (constructor SP name) | Critical | Section 2b must also scan constructor args in Section 3; not just sibling `CommandText` assignments |
| DG013 false positive on synthetic `EXEC` in Oracle | Critical | Use provider-aware SQL synthesis: `CALL` for Oracle, `EXEC` for SQL Server |
| Triple-stacked hover tooltips | High | Decorations: no hoverMessage; HoverProvider: sole rich hover authority |
| SARIF `properties` dropped by extension | High | Extend `SarifResult`/`FindingItem` to preserve properties bag |
| Dapper SP calls rejected by `ExtractSqlText` | High | Modify `ExtractSqlText` to bypass `IsSqlString` check when `commandType: StoredProcedure` argument detected |
| Stale decorations after validation failure/cancel | High | DecorationManager must listen for CLI start/cancel/error events and clear decorations |
| Multi-word Oracle keywords with whitespace variants escape detection | High | Use `\bCONNECT\s+BY\b` instead of `\bCONNECT BY\b` |

---

## Red-Team Findings (Consolidated, Deduplicated)

### CRITICAL Findings

#### F1. `StoredProcedureDescriptor` Bypasses Entire Rule Engine
- **Evidence**: `ContractRules.cs` — every rule starts with `if (contract is not RawSqlDescriptor) return;`. `StoredProcedureDescriptor` exists at `Contracts.cs:171-179` but is exclusively used for database catalog ground-truth by adapters (`SqlServerParsers`, `OracleAdapter`, etc.).
- **Impact**: Zero rules execute. SP calls silently vanish from analysis.
- **Required Fix**: Plan Step 3 must state: "ALWAYS emit `RawSqlDescriptor`. NEVER emit `StoredProcedureDescriptor` from source scanning."

#### F2. `$"EXEC {procName}"` Causes DG101 False-Positive Bomb
- **Evidence**: `ContractRules.cs:78-93` — when SQL starts with `EXEC` and `detectedCount == 0` (no inline `@param` regex matches), DG101 unconditionally flags: "Stored procedure call appears to have no parameters detected". ADO.NET/Dapper pass parameters via `Parameters.AddWithValue()` / anonymous objects, never inline in `CommandText`.
- **Impact**: Every SP detected by the new scanner produces a false DG101 violation.
- **Required Fix**: Add `IsStoredProcedure` property to `RawSqlDescriptor`. DG101 must skip the "no params" heuristic when `IsStoredProcedure == true`, because parameters are bound externally.

#### F3. Constructor-Based SP Names Missed (Plan's Own Verification Fails)
- **Evidence**: Plan Verification #3 uses `new SqlCommand("GET_CUSTOMER_BY_ID", conn)` — procedure name is the constructor argument, NOT a `CommandText` assignment. Section 3 (`ProjectCSharpSqlSource.cs:265-288`) already handles `new SqlCommand(...)` but rejects at line 281 because `IsSqlString("GET_CUSTOMER_BY_ID")` returns `false`. Section 2b only searches for sibling `cmd.CommandText = "..."` — there is none.
- **Impact**: The plan's primary verification test case silently passes through both Section 3 and proposed Section 2b without detection.
- **Required Fix**: Section 2b must ALSO check constructor arguments from Section 3's `ObjectCreationExpressionSyntax`. When `CommandType.StoredProcedure` is detected on the same variable, retroactively accept the constructor string that Section 3 rejected.

#### F4. Synthetic `EXEC` Triggers DG013 False Positive in Oracle Context
- **Evidence**: `OracleDialectChecker.CheckSqlServerSyntaxLeak()` (DG013) explicitly flags `EXEC` pattern as SQL Server syntax leak in Oracle context.
- **Impact**: Valid Oracle code using `OracleCommand` + `CommandType.StoredProcedure` produces false DG013 violation.
- **Required Fix**: Use provider-aware SQL synthesis: `EXEC {proc}` for SQL Server, `CALL {proc}()` for Oracle, `CALL {proc}()` for PostgreSQL. Or better: set `IsStoredProcedure = true` and let dialect checkers interpret accordingly.

### HIGH Findings

#### F5. CodeLens "✅ No Issues" Is Impossible from DiagnosticCollection
- **Evidence**: `DiagnosticCollection` only records files/ranges with violations. `diagnostics.get(uri)` returns `undefined` for clean files. It has zero knowledge of SQL call sites, method boundaries, or verified queries.
- **Impact**: "✅ DataGuard: No issues" cannot be rendered. If attempted on all methods, it spams every non-SQL method.
- **Required Fix**: Either (a) violation-only CodeLens showing `"⚠ DataGuard: N findings"` at diagnostic locations, OR (b) source CodeLens from `latestScanReport.queries` (which records verified call sites) for full SQL-site-aware coverage.

#### F6. Triple-Stacked Hover Tooltips
- **Evidence**: VS Code natively renders `diagnostic.message` on hover. Step 2 adds `DecorationOptions.hoverMessage`. Step 5 adds `HoverProvider`. Three near-identical popups stack.
- **Required Fix**: Decorations provide gutter icons + background highlights ONLY (no `hoverMessage`). HoverProvider is the sole authority for rich Markdown content.

#### F7. SARIF `properties` Dropped at Extension Boundary
- **Evidence**: `redaction.ts` `parseSarifToFindings()` does not deserialize `result.properties`. `FindingItem` has no `properties` field. `DiagnosticEmitter.cs:123` writes properties to SARIF, but they're lost.
- **Impact**: Plan claim "enables the Findings Tree and Dashboard to show the hint too" is false.
- **Required Fix**: Extend `SarifResult` interface and `FindingItem` to include `properties?: Record<string, unknown>`. Update `parseSarifToFindings()` to propagate.

#### F8. Dapper SP Calls Blocked by `ExtractSqlText`
- **Evidence**: `ExtractSqlText` (`ProjectCSharpSqlSource.cs:564-574`) applies `IsSqlString()` filter. `"MY_PROC"` fails `IsSqlString()`, returns `string.Empty`. Line 202: `if (string.IsNullOrWhiteSpace(sqlText)) continue;` aborts before any `commandType:` argument check can run.
- **Required Fix**: Check for `commandType: CommandType.StoredProcedure` named argument BEFORE calling `ExtractSqlText`. If detected, extract the first string argument directly, bypassing `IsSqlString()` gate.

#### F9. Stale Decorations After Validation Failure
- **Evidence**: `extension.ts:369` calls `diagnostics?.clear()` before CLI spawns. If CLI fails/times out, `loadDiagnostics()` never runs. Decorations remain permanently displayed on stale locations.
- **Required Fix**: DecorationManager must clear on CLI start (not just refresh on success). Hook into `clearFindingsAndDiagnostics()` at line 231.

### MEDIUM Findings

#### F10. DG010 Operator Property Key Mismatch
- **Evidence**: `OracleDialectChecker.cs:64-89` uses `"keyword"` property key for keywords and `"operator"` property key for operators. Tests assert on these keys. Plan applies keyword dictionary to operators without accounting for key difference.
- **Required Fix**: Maintain separate `OracleOperatorMigrations` dictionary with `"operator"` key. Or consolidate under a common key but update ALL test assertions.

#### F11. Multi-Word Keywords Miss Whitespace Variants
- **Evidence**: `ContainsKeyword()` uses `\b{keyword}\b` with exact literal match. `"CONNECT BY"` requires single space — `CONNECT\nBY` or `CONNECT\tBY` escapes.
- **Required Fix**: Use `\bCONNECT\s+BY\b` for multi-word entries.

#### F12. Lifecycle Gaps (Configuration, Clear, Dispose)
- **Evidence**: No `onDidChangeConfiguration` listener. `clearFindingsAndDiagnostics()` doesn't touch decorations/CodeLens. `deactivate()` doesn't dispose decoration types.
- **Required Fix**: Plan must include: (a) config change listener toggling providers, (b) clear hook for decorations/CodeLens, (c) proper disposal in deactivate.

#### F13. `package.json` Test Script Not Updated
- **Evidence**: Test script at `package.json:242` hardcodes test file list. New test files won't execute in CI.
- **Required Fix**: Add new test files to the script, or switch to glob-based test discovery.

---

## Scenario Report (27 Scenarios, 12-Dimension Decomposition)

Dimensions analyzed: Input Extremes, Timing, Scale, State Transitions, Environment, Error Cascades, Data Integrity, Integration
Dimensions skipped: User Types (single-user extension), Authorization (no auth), Compliance (no PII), Business Logic (no pricing)

### Critical Scenarios (7)

| # | Dimension | Scenario | Expected Behavior |
|---|-----------|----------|-------------------|
| 1 | Data Integrity | `StoredProcedureDescriptor` emitted → zero rules fire | MUST emit `RawSqlDescriptor` with `IsStoredProcedure = true` |
| 2 | Data Integrity | `$"EXEC {procName}"` with 0 inline params → DG101 false positive | DG101 must skip param-count heuristic when `IsStoredProcedure` |
| 3 | Integration | Constructor SP name `new SqlCommand("SP")` missed by Section 2b | Section 2b must scan constructor args retroactively |
| 4 | Integration | `EXEC` synthesis in Oracle context → DG013 false positive | Provider-aware synthesis or `IsStoredProcedure` flag |
| 5 | State Transitions | 0 diagnostics → "✅ No issues" CodeLens impossible | Violation-only CodeLens or `ScanReport`-backed |
| 6 | State Transitions | CLI failure → stale decorations remain on editor | DecorationManager clears on CLI start/error |
| 7 | Input Extremes | Object initializer `new SqlCommand { CommandText = "SP", CommandType = ... }` | Scan `InitializerExpressionSyntax` alongside block assignments |

### High Scenarios (11)

| # | Dimension | Scenario | Expected Behavior |
|---|-----------|----------|-------------------|
| 8 | Scale | 500+ diagnostics in one file → CodeLens spam | Per-document lens cap (max 50) with aggregate header |
| 9 | Environment | High Contrast / Dark theme → hardcoded decoration colors fail | Use semantic theme tokens (`editorError.foreground`) |
| 10 | Timing | Split editor panes → decorations only on `activeTextEditor` | Apply to all `visibleTextEditors` |
| 11 | Input Extremes | Dapper `CommandDefinition` struct with SP | Scan `ObjectCreationExpressionSyntax` for `CommandDefinition` |
| 12 | Input Extremes | Dapper positional `CommandType.StoredProcedure` (5th arg) | Handle positional overload, not just named argument |
| 13 | Data Integrity | Unknown Oracle keyword not in migration dictionary | `TryGetValue` + generic fallback hint |
| 14 | Data Integrity | Multi-word keyword `CONNECT\nBY` with newlines | `\bCONNECT\s+BY\b` regex |
| 15 | Integration | Target dialect is PostgreSQL → SQL Server-only hints mislead | Include ANSI SQL alternatives alongside SQL Server hints |
| 16 | Integration | Overlapping diagnostics on same line → multiple hovers | Combine into unified `MarkdownString` separated by `---` |
| 17 | Integration | Native diagnostic hover + decoration hover + HoverProvider = 3x | Decoration: no `hoverMessage`; HoverProvider sole authority |
| 18 | Input Extremes | Non-literal `CommandText` (ternary, method call) → `null` | Skip gracefully; log informational diagnostic |

### Medium Scenarios (9)

| # | Dimension | Scenario | Expected Behavior |
|---|-----------|----------|-------------------|
| 19 | Timing | Rapid file switching during CodeLens computation | Respect `CancellationToken` strictly |
| 20 | State Transitions | Document edited → line drift before re-validation | Clear CodeLens/decorations on dirty docs |
| 21 | Timing | Race condition: 2nd validation while 1st exits | Re-check `isReservationCurrent()` before `loadDiagnostics()` |
| 22 | Input Extremes | 1-char SARIF range → hover miss on cursor | Broaden hover hit-testing to token/line boundary |
| 23 | Input Extremes | `cmd` variable reused/shadowed in nested scope | Symbol identity via `SemanticModel.GetSymbolInfo()` |
| 24 | Input Extremes | Inter-procedural `CommandType` set in helper method | Abort sibling search cleanly at method boundary |
| 25 | Input Extremes | Inverted order: `CommandType` before `CommandText` | Bidirectional sibling search within block |
| 26 | Data Integrity | `SELECT "DUAL"` quoted identifier → false DG010 | Skip keywords inside double-quoted identifiers |
| 27 | Environment | `onDidChangeConfiguration` not wired → setting toggles ignored | Add config change listener |

---

## Security Assessment

| Finding | Severity | Mitigation |
|---------|----------|------------|
| Markdown/Command injection in HoverProvider if `isTrusted: true` | Medium | Set `isTrusted = false` or use `enabledCommands` allowlist. Use `appendText`/`appendCodeblock` over `appendMarkdown` for dynamic content |
| Synthetic SQL injection via `$"EXEC {procName}"` if procName contains `;` | Medium | Validate procName matches `^[A-Za-z0-9_#$]+(\.[A-Za-z0-9_#$]+)?$` before synthesis, or use `IsStoredProcedure` flag approach (no string synthesis needed) |
| Scope confusion in `FindSiblingCommandText` with shadowed variables | Low | Use `SemanticModel.GetSymbolInfo()` for receiver identity, not string name matching |
| Markdown formatting breakage with operators `(+)`, `**`, `<>` | Informational | Wrap SQL expressions in backticks in migration hint strings |

---

## Approach (Required Plan Revisions)

### Step 3 Rewrite: Stored Procedure Detection

1. Add `IsStoredProcedure` bool and `ProcedureName` string to `RawSqlDescriptor` in `Contracts.cs`
2. In `ProjectCSharpSqlSource.cs`:
   - After Section 3 (object creations), do NOT filter out strings that fail `IsSqlString()` if a sibling/co-located `CommandType.StoredProcedure` assignment exists on the same variable
   - Section 2b: scan `AssignmentExpressionSyntax` for `CommandType.StoredProcedure`. Use `SemanticModel` symbol identity to match the receiver. Search enclosing block bidirectionally for `CommandText` assignment OR constructor argument on same symbol
   - Handle object initializers (`InitializerExpressionSyntax`)
   - For Dapper: check `commandType:` named argument BEFORE `ExtractSqlText` call. If present and evaluates to `StoredProcedure`, extract first string arg directly
   - Emit `RawSqlDescriptor` with `IsStoredProcedure = true`, `ProcedureName = procName`, `SqlText` = provider-aware (`EXEC {proc}` for SQL Server, `CALL {proc}()` for Oracle/PostgreSQL)
3. In `ContractRules.cs`: DG101 skips "no parameters detected" heuristic when `IsStoredProcedure == true`
4. Validate procName against `^[A-Za-z0-9_#$]+(\.[A-Za-z0-9_#$]+)*$` before synthesis

### Step 1 Revision: CodeLens Scope

- Remove "✅ DataGuard: No issues" — impossible from `DiagnosticCollection`
- Violation-only: show `"⚠ DataGuard: N findings"` above lines with diagnostics, grouped by method via `vscode.executeDocumentSymbolProvider`
- Per-document cap: max 50 lenses; aggregate header if exceeded
- Respect `CancellationToken`; clear on dirty docs until re-validation

### Step 2 Revision: Decorations

- Gutter icons + background highlights ONLY — no `hoverMessage` (prevents triple hover)
- Use semantic theme tokens: `new vscode.ThemeColor('editorError.foreground')` / `editorWarning.foreground`
- Apply to all `vscode.window.visibleTextEditors`, not just `activeTextEditor`
- Clear decorations in `clearFindingsAndDiagnostics()` and on CLI start/cancel/error
- Dispose decoration types in `deactivate()`

### Step 4 Revision: DG010 Hints

- Maintain separate `OracleOperatorMigrations` with `"operator"` property key
- Multi-word keywords: use `\s+` instead of literal space in regex
- Include ANSI SQL alternatives alongside SQL Server-specific hints
- `TryGetValue()` with generic fallback for unknown keywords
- Wrap SQL expressions in backticks in hint strings for Markdown safety

### Step 5 Revision: Hover Provider

- Sole authority for rich Markdown hover content
- Set `md.isTrusted = false` and `md.supportHtml = false`
- Use `appendText`/`appendCodeblock` for dynamic content; `appendMarkdown` only for static structure
- Combine overlapping diagnostics into unified hover separated by `---`
- Read migration hints from `FindingItem.properties.migration` (requires Step 4 pipeline fix)
- Remove DG010 quick-fix link claim (no quick-fix exists for DG010)

### New Step 6: Pipeline Fix — Preserve SARIF Properties

- Extend `SarifResult` in `redaction.ts` to include `properties?: Record<string, unknown>`
- Extend `FindingItem` with `properties?: Record<string, unknown>`
- Update `parseSarifToFindings()` to propagate `result.properties` → `FindingItem.properties`

### New Step 7: Lifecycle Management

- Add `vscode.workspace.onDidChangeConfiguration` listener to toggle CodeLens/decorations on setting change
- Update `clearFindingsAndDiagnostics()` to call `decorationManager.clear()` and `codeLensProvider.refresh()`
- Add new test files to `package.json` test script

## Critical Files & Anchors

| File | Symbol/Region | Reason |
|------|--------------|--------|
| `src/DataGuard.Core/Abstractions/Contracts.cs` | `RawSqlDescriptor` L212 | Add `IsStoredProcedure` + `ProcedureName` properties |
| `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` | Section 2 L231-262, Section 3 L265-288, `ExtractSqlText` L564-574, `IsSqlString` L738-760 | SP detection rewrite across all scanning sections |
| `src/DataGuard.Core/Rules/ContractRules.cs` | `ParameterCountRule` L78-93 | Skip DG101 param heuristic when `IsStoredProcedure` |
| `src/DataGuard.VSCode/src/ui/redaction.ts` | `SarifResult`, `FindingItem`, `parseSarifToFindings` L146-204 | Preserve SARIF `properties` bag |
| `src/DataGuard.VSCode/src/extension.ts` | `activate()` L53-100, `loadDiagnostics()` L675-741, `clearFindingsAndDiagnostics()` L231 | Provider registration, lifecycle hooks, clear/dispose |

## Verification

1. **SP Detection — Constructor pattern**: Create test `.cs` with `new SqlCommand("GET_CUSTOMER_BY_ID", conn); cmd.CommandType = CommandType.StoredProcedure;` → Run `dataguard validate` → MUST appear in SARIF as `RawSqlDescriptor` with `IsStoredProcedure = true`. No DG101 "no params" false positive. No DG013 false positive if Oracle context.
2. **SP Detection — Dapper pattern**: `conn.Execute("MY_PROC", new { Id = 1 }, commandType: CommandType.StoredProcedure)` → MUST appear in SARIF.
3. **DG010 hints**: Run on code with `SYSDATE`, `LISTAGG`, `CONNECT\nBY` (with newline) → All three detected with migration hints. Verify `properties.migration` present in SARIF JSON.
4. **Hover deduplication**: Hover over a DG010 squiggle → exactly TWO hovers visible (VS Code native diagnostic + custom HoverProvider). NOT three.
5. **Decoration lifecycle**: Run validation → verify gutter icons. Click "Clear Findings" → gutter icons disappear. Run validation → cancel mid-run → gutter icons cleared.
6. **Existing tests**: `dotnet test` and `cd src/DataGuard.VSCode && npm test` — zero regressions.

## Assumptions & Contingencies

- **`IsStoredProcedure` on `RawSqlDescriptor`**: If adding a property to this hot-path model causes serialization issues with existing SARIF consumers, fallback: use a `Properties` dictionary entry `{ "isStoredProcedure", "true" }` and check it in DG101. Confirm first by checking `RawSqlDescriptor` serialization path in `DiagnosticEmitter`.
- **Provider-aware SQL synthesis**: Requires access to the configured provider context inside `ProjectCSharpSqlSource`. If unavailable at scan time, default to `EXEC` and suppress DG013 for descriptors with `IsStoredProcedure = true`.
- **`latestScanReport` for CodeLens**: If `ScanReport` is not retained after validation run (check `extension.ts` for lifecycle), fallback to violation-only CodeLens from `DiagnosticCollection` without clean-query marking.

---

## Pre-Execution & Compliance Verification (Gate Checks)

### 1. Consistency Verification
- **`RawSqlDescriptor` Call Sites**: `Contracts.cs:212`. Added properties `public bool IsStoredProcedure { get; init; } = false` and `public string? ProcedureName { get; init; } = null`. Default values guarantee all 6 existing instantiation sites in `ProjectCSharpSqlSource.cs` (lines 177, 219, 252, 281, 318, 355) remain 100% source- and binary-compatible without requiring signature churn.
- **Property Key Parity**: `OracleDialectChecker.cs` preserves distinct property keys: `"keyword"` for `OracleKeywordMigrations` and `"operator"` for `OracleOperatorMigrations`. Preserves test assertions in `OracleAdapterTests.cs:120-125`.
- **UI Model Invariants**: `FindingItem` in `redaction.ts` adds optional `properties?: Record<string, unknown>`. Consuming providers (`findings-tree-provider.ts`, `quick-fix-provider.ts`, `dashboard-view.ts`) maintain backward compatibility without null-reference risks.

### 2. Scope Verification (Minimal Issue-Resolving Diff)
- **No Engine Rewrite**: Avoids rewriting `ContractRules.cs` or adding new descriptor hierarchies. A single boolean flag `IsStoredProcedure` on `RawSqlDescriptor` resolves DG101, DG013, and dialect checker conflicts in under 15 lines of C# diff.
- **No Heavy AST Method Parser in VS Code**: CodeLens uses existing `DiagnosticCollection` line groupings rather than parsing full C# syntax in Node.js, avoiding Monaco thread stalls.
- **No Webview Creep**: Hover tooltips use native `vscode.Hover` with `vscode.MarkdownString` rather than webview popups, keeping extension memory footprint minimal.

### 3. Verification Test Plan (Full Modules)
- **C# Core & Dialect Tests**:
  - Command: `dotnet test tests/DataGuard.Core.Tests/ --filter "FullyQualifiedName~ProjectCSharpSqlSourceTests|FullyQualifiedName~OracleAdapterTests"`
  - Runs all 28 existing tests in `ProjectCSharpSqlSourceTests.cs` and all 35 tests in `OracleAdapterTests.cs`, plus new test methods for constructor SPs, Dapper SPs, and migration hints.
- **VS Code Extension Tests**:
  - Command: `cd src/DataGuard.VSCode && npm test`
  - Runs the complete test suite across all 8 existing test modules (`security.test.js`, `run-coordinator.test.js`, etc.) plus new `codelens-provider.test.js`, `decoration-manager.test.js`, and `hover-provider.test.js`.
