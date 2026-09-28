# Technical Journal: Validation Logging Aggregation and Rule Identification

**Date:** 2026-09-28  
**Scope:** CLI (`DataGuard.Cli`), VS Extension (`DataGuard.VisualStudio`), Tests, and Documentation.  
**Commit:** `eab8b63b943d53de7a4d0d7d81e4a9ca60dfa6cc`  

## 1. Problem Statement
Previously, `ValidateContractsAsync` emitted a `RuleExecuted` progress event for every single contract evaluated against each rule. For solutions with thousands of contracts (e.g. 2886 contracts × N rules), this generated tens of thousands of redundant progress events, flooding the output stream. Furthermore:
- The Visual Studio Output Window displayed generic lines lacking human-readable rule titles.
- Visual Studio Error List entries did not indicate the originating Rule ID (e.g., `DG017`), making triage difficult.

## 2. Key Architectural Decisions & Implementation
1. **Per-Rule Progress Aggregation (`src/DataGuard.Cli/Program.cs`)**:
   - Replaced per-contract `RuleExecuted` events with aggregated per-rule summary events in both concurrent and sequential validation paths.
   - For concurrent execution, suppressed per-contract callbacks (`executionCompleted: null`), indexed violations by `RuleId` via `GroupBy`, and enumerated all active rules to guarantee zero-violation rules are preserved for complete tracking.
2. **Rule Titles Catalog (`src/DataGuard.Cli/ProviderRuleCatalog.cs`)**:
   - Introduced `ProviderRuleCatalog.RuleTitles` static mapping covering DG001–DG020, DG101.
   - Attached `"RuleTitle"` to each `RuleExecuted` event data dictionary.
3. **Structured Output Window Formatting (`src/DataGuard.VisualStudio/DataGuardPackage.cs`)**:
   - Added `GetProgressString(data, property)` helper for safe JSON string retrieval.
   - Formatted rule output as `[DataGuard]   {RuleId} ({RuleTitle}): {contracts} contracts checked → {violations} violations`.
4. **Error List Rule ID Prefix (`src/DataGuard.VisualStudio/DataGuardPackage.cs`)**:
   - In `PublishSarifAsync`, parsed the SARIF `ruleId` node and prefixed error/warning messages with `[{sarifRuleId}] {message}`.
5. **Documentation & Tests**:
   - Updated `docs/03-components/tooling/cli.md` and `cli.vi.md` with the new progress schema.
   - Added unit tests in `DataGuardPackageTests.cs` for format preservation and fallback behavior when `RuleTitle` is absent.

## 3. Verification & Metrics
- `DataGuard.VisualStudio.Tests`: 51/51 passed (0 failed, 0 skipped).
- `DataGuard.Core.Tests`: 785/785 passed (0 failed, 0 skipped).
- Code review: 2 cycles performed, critical concurrent zero-violation omission identified and resolved, final review approved with 0 critical issues.
