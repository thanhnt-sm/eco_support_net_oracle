# Red Team Review + Validation: Visual Studio Extension Upgrade Plan

## Context

The existing plan `VISUAL_STUDIO_EXTENSION_UPGRADE_PLAN.md` (5 phases: Navigation, SP Scanning, DG010 Clarity, VSIX Roslyn Packaging, Output Transparency) was subjected to adversarial red-team review by 3 hostile reviewers (Security Adversary, Failure Mode Analyst, Assumption Destroyer), followed by codebase verification and TDD-first validation interview. The plan contains **critical factual errors** — several phases describe implementing features that already exist, reference a non-existent VS SDK API, and propose architectural changes that violate the existing build topology.

This plan prescribes the exact corrections to apply to `VISUAL_STUDIO_EXTENSION_UPGRADE_PLAN.md` so the implementer operates on ground truth, plus TDD test-first structure for each phase.

### Implementation Status (Completed — 2026-09-28)

All 7 corrective and implementation steps have been fully executed and verified:
- [x] **Step 1 (Phase 1 Fix — Navigation)**: Replaced non-existent `VsShellUtilities.OpenDocumentAndNavigateToPosition` with verified `VsShellUtilities.OpenDocument` + `IVsTextView.SetCaretPos` & `CenterLines` in `DataGuardPackage.cs`. Verified with `tests/DataGuard.VisualStudio.Tests/NavigationTests.cs`.
- [x] **Step 2 (Phase 2 Rewrite — Stored Procedure Detection)**: Audited existing Dapper / ADO.NET SP extraction; added `PROC_`, `FNC_`, and `P_` prefixes to `IsSqlString` in `ProjectCSharpSqlSource.cs`. Verified with `tests/DataGuard.Core.Tests/StoredProcPrefixTests.cs`.
- [x] **Step 3 (Phase 3 Correction — DG010 Clarity)**: Enriched `DG010` diagnostic message and dictionary with target provider migration details in `OracleDialectChecker.cs`. Verified with new tests in `tests/DataGuard.Core.Tests/OracleAdapterTests.cs`.
- [x] **Step 4 (Phase 4 Rewrite — Roslyn VSIX Packaging)**: Packaged analyzer assemblies into VSIX via MSBuild target (`BuildAnalyzers` + `IncludeAnalyzersInVsix`) without `<ProjectReference>` in `DataGuard.VisualStudio.csproj` and updated `source.extension.vsixmanifest`. Verified with `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs`.
- [x] **Step 5 (Phase 5 Rewrite — Output Window Rule Transparency)**: Aggregated `RuleExecuted` progress events into thread-safe `_ruleInventory` lock-guarded collection; emitted full validation summary banner on `Summary` event in `DataGuardPackage.cs`. Verified with `tests/DataGuard.VisualStudio.Tests/RuleInventoryTests.cs`.
- [x] **Step 6 (Verification Path Corrections)**: Fixed test paths from `test/` to `tests/` across plans and project files.
- [x] **Step 7 (Plan Alignment & Ground Truth Corrections)**: Aligned `VISUAL_STUDIO_EXTENSION_UPGRADE_PLAN.md` with verified code anchors and added critical correction banner.

---

## Red Team Review

### Session — 2026-09-28
**Findings:** 12 (8 accepted, 4 rejected as duplicates)
**Severity breakdown:** 4 Critical, 3 High, 1 Medium

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| 1 | Fabricated API: `VsShellUtilities.OpenDocumentAndNavigateToPosition` does not exist in VS SDK | Critical | Accept | Phase 1 |
| 2 | Phase 2 SP scanning (ADO.NET + Dapper) already fully implemented at `ProjectCSharpSqlSource.cs:207-470` | Critical | Accept | Phase 2 |
| 3 | Phase 3 `OracleKeywords` is NOT a HashSet — `OracleKeywordMigrations` already a `Dictionary<string, string>` at `OracleDialectChecker.cs:24-47` | Critical | Accept | Phase 3 |
| 4 | Phase 4 `<ProjectReference>` violates zero-ProjectReference VSIX architecture; CLI bundled via MSBuild Exec + VSIXSourceItem | Critical | Accept | Phase 4 |
| 5 | Phase 5 `summary.json` not available in VS extension; `RuleExecuted` progress events already emitted per rule | High | Accept | Phase 5 |
| 6 | Wrong method name `ExtractContractsFromCompilationAsync` — actual: `ExtractContractsAsync` at `ProjectCSharpSqlSource.cs:82` | High | Accept | Phase 2 |
| 7 | Wrong test directory `test/` — actual: `tests/` | High | Accept | Verification |
| 8 | DG010 diagnostic message already contains `"migration"` property in dict; only message string needs enrichment | Medium | Accept | Phase 3 |
| 9-12 | Duplicates of #2, #3, #4, #5 from other reviewers | — | Reject (dup) | — |

---

## Approach

Steps are ordered for a building tree: 1→2→3 are independent (can parallelize), 4 depends on Analyzers/CodeFixes existing, 5 is independent, 6-7 are plan-level corrections.

### Step 1: Fix Phase 1 — Replace fabricated API with correct VS SDK pattern

**What's wrong:** `VsShellUtilities.OpenDocumentAndNavigateToPosition(this, task.Document, task.Line, task.Column)` does not exist in `Microsoft.VisualStudio.Shell`. Confirmed via VS SDK docs: only `VsShellUtilities.OpenDocument()` exists. The correct navigation pattern is `OpenDocument()` → `IVsWindowFrame.Show()` → `IVsTextView.SetCaretPos(line, col)` → `CenterLines(line, 1)`.

**Exact edit to plan Phase 1, step 1 code block** — replace the entire Navigate handler:
```csharp
task.Navigate += (sender, e) =>
{
    this.JoinableTaskFactory.RunAsync(async () =>
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (File.Exists(task.Document))
        {
            VsShellUtilities.OpenDocument(
                this,
                task.Document,
                Microsoft.VisualStudio.VSConstants.LOGVIEWID_Code,
                out _,
                out _,
                out IVsWindowFrame windowFrame,
                out IVsTextView textView);
            windowFrame?.Show();
            if (textView != null)
            {
                textView.SetCaretPos(task.Line, task.Column);
                textView.CenterLines(task.Line, 1);
            }
        }
        else
        {
            await this.WriteOutputAsync($"[DataGuard] Cannot navigate: file not found '{task.Document}'.\r\n");
        }
    }).FileAndForget("DataGuard/NavigateTask");
};
```

**Add import**: `using Microsoft.VisualStudio.TextManager.Interop;` for `IVsTextView`.

Reuse existing pattern at `src/DataGuard.VisualStudio/DataGuardLogger.cs:385` which already uses `VsShellUtilities.OpenDocument`.

**TDD — write tests first:**
- Test file: `tests/DataGuard.VisualStudio.Tests/NavigationTests.cs`
- Framework: xUnit + FluentAssertions (matches existing `tests/DataGuard.VisualStudio.Tests/DataGuardPackageTests.cs` pattern)
- Tests:
  1. `Navigate_WhenFileExists_OpensDocumentAndSetsCaret` — VS SDK integration test using `VsShellTestContext`: create a temp .cs file, invoke Navigate handler, verify `IVsTextView.SetCaretPos` receives SARIF→0-based converted line/col via `IVsTextViewMock` or `VsShellUtilities.GetTextView(frame)`.
  2. `Navigate_WhenFileMissing_WritesOutputMessage` — verify output stream contains `"Cannot navigate"`.
  3. `Navigate_SarifOneBased_ConvertsToZeroBased` — verify that SARIF `startLine=5, startColumn=3` (1-based) results in `SetCaretPos(4, 2)` (0-based). The existing subtraction logic at `DataGuardPackage.cs` already does `Math.Max(0, startLine - 1)` — verify this is preserved.

### Step 2: Rewrite Phase 2 — audit + harden existing SP detection

**What's wrong:** Phase 2 proposes implementing ADO.NET and Dapper SP handling from scratch. Both are **already fully implemented**:
- Dapper SP: `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs:207-258` — detects `commandType: CommandType.StoredProcedure` named argument, resolves proc name from first string arg, validates against identifier regex, synthesizes `EXEC {procName}`.
- ADO.NET SP: `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs:332-470` — scans `CommandType` assignments, correlates with `CommandText` in enclosing block, handles `new SqlCommand("PROC", conn)` ctor pattern.
- Oracle prefixes: `IsSqlString` (line 945-967) recognizes `sp_`, `usp_`, and `PKG.PROC` dotted syntax.

**Correct method name:** `ExtractContractsAsync` at line 82 (NOT `ExtractContractsFromCompilationAsync` — this method does not exist).

**Revised Phase 2 scope — audit + harden + extend:**

1. **Add missing Oracle prefix patterns** to `IsSqlString` (line ~954):
   - Add `PROC_`, `FNC_`, `P_` as `StartsWith` checks alongside existing `sp_`/`usp_`:
   ```csharp
   if (trimmed.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) ||
       trimmed.StartsWith("usp_", StringComparison.OrdinalIgnoreCase) ||
       trimmed.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase) ||
       trimmed.StartsWith("FNC_", StringComparison.OrdinalIgnoreCase) ||
       trimmed.StartsWith("P_", StringComparison.OrdinalIgnoreCase))
   ```

2. **Audit Dapper SP edge cases** (line 207-258):
   - **Positional `commandType`**: Current code only checks `NameColon?.Name == "commandType"`. Dapper's `Query<T>(sql, param, transaction, buffered, commandType)` passes commandType as 5th positional arg. Add positional detection: if arg at index 4 (0-based) expression ends with `"StoredProcedure"`, treat as SP.
   - **`QueryMultiple` method**: Verify the `isDapperSp` logic fires for `QueryMultiple` — it uses the Dapper method name prefix check `"Query"` which matches `QueryMultiple`. Confirm with grep in test.

3. **Audit ADO.NET SP edge cases** (line 332-470):
   - **Cross-method assignment**: Plan's prediction question asks about `CommandText` and `CommandType` in different methods. Current code restricts to `enclosingBlock` (line 366). This is correct and intentional — document this as a known limitation, not a bug.
   - **DbCommand factory pattern**: `DbProviderFactory.CreateCommand()` returns generic `DbCommand`. Verify the receiver correlation works when `cmd` is declared as `var cmd = factory.CreateCommand()` — the identifier matching should work since it matches variable name, not type.

4. **Update Roslyn Analyzer `IsPotentialSqlCall`** (line 334-361 in `src/DataGuard.Analyzers/Analyzers.cs`):
   - Verify it recognizes expanded SP prefix set from step 1 consistently. The analyzer uses `SqlClassifier.Classify(str, ...)` — trace this to verify it calls through to `IsSqlString` or equivalent logic.

**TDD — write tests first:**
- Test file: `tests/DataGuard.Core.Tests/StoredProcPrefixTests.cs`
- Tests:
  1. `IsSqlString_OraclePrefixes_ReturnTrue` — `[Theory]` with `[InlineData("PROC_UPDATE_CUSTOMER")]`, `[InlineData("FNC_GET_TOTAL")]`, `[InlineData("P_ARCHIVE")]`.
  2. `IsSqlString_ExistingPrefixes_StillReturnTrue` — regression: `[InlineData("sp_GetUser")]`, `[InlineData("usp_UpdateOrder")]`, `[InlineData("PKG_CUSTOMER.GET_DETAILS")]`.
  3. `ExtractContractsAsync_DapperPositionalCommandType_DetectsSP` — full Roslyn compilation test: parse C# code with `conn.Execute("MY_PROC", param, null, null, CommandType.StoredProcedure)`, verify `RawSqlDescriptor.IsStoredProcedure == true`.
  4. `ExtractContractsAsync_DbCommandFactory_DetectsSP` — parse C# code with `var cmd = factory.CreateCommand(); cmd.CommandText = "GET_DATA"; cmd.CommandType = CommandType.StoredProcedure;`, verify SP detected.
  5. `ExtractContractsAsync_CrossMethodCommandType_NotDetected` — intentional negative test: `CommandText` in method A, `CommandType` in method B, verify SP NOT detected (documents known limitation).

- Integration test in VS SDK test harness: Load a test solution with SP calls using all patterns, run DataGuard validation, verify diagnostics appear for each pattern.

### Step 3: Rewrite Phase 3 — diagnostic message format upgrade only

**What's wrong:** Phase 3 claims `OracleKeywords` is a `HashSet<string>` needing conversion. Ground truth:
- `OracleKeywordMigrations` at `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs:24-47` — **already a `Dictionary<string, string>`** with 16 keyword→hint mappings.
- `OracleOperatorMigrations` at lines 50-54 — also a `Dictionary<string, string>` with `(+)` and `**`.
- `CheckOracleSyntaxInNonOracleContext` (lines 67-117) already emits `"keyword"` and `"migration"` in the properties dictionary.

**What actually needs changing:** The user-facing diagnostic message string is `$"Oracle-specific keyword '{keyword}' used in non-Oracle context"` — it does NOT include the migration hint or target provider. The hint is in the properties dict but not in the visible message.

**Revised Phase 3 scope:**

1. **Add `targetProvider` parameter** to `CheckOracleSyntaxInNonOracleContext` — add `string targetProvider = "sqlserver"` parameter. Thread this from the calling context where `.dataguard.yml` config provides the target provider.

2. **Upgrade keyword diagnostic message** (line 88):
   ```csharp
   // Before:
   $"Oracle-specific keyword `{keyword}` used in non-Oracle context"
   // After:
   $"[Migration: Oracle -> {targetProvider}] Keyword '{keyword}' is unsupported. {hint}. (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)"
   ```

3. **Upgrade operator diagnostic message** (line 106):
   ```csharp
   // Before:
   $"Oracle-specific operator `{op}` used in non-Oracle context"
   // After:
   $"[Migration: Oracle -> {targetProvider}] Operator '{op}' is unsupported. {hint}. (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)"
   ```

4. **Add `targetProvider` to properties dict** — add `{ "targetProvider", targetProvider }` to both keyword and operator violation dicts (lines 91-95 and 109-113).

5. **Update all callers of `CheckOracleSyntaxInNonOracleContext`** — grep shows callers via the `Analyze` wrapper at line 17-20. Trace the `targetProvider` from config context through `Analyze` → `CheckOracleSyntaxInNonOracleContext`.

Remove all "convert HashSet to Dictionary" steps. Remove all "Keyword Migration Map" creation steps. The data structure and all 16 keyword mappings plus 2 operator mappings are correct and complete.

**TDD — write tests first:**
- Test file: extend `tests/DataGuard.Core.Tests/` Oracle dialect tests (find existing test file for `OracleDialectChecker`)
- Tests:
  1. `CheckOracleSyntax_Listagg_MessageContainsMigrationHint` — assert violation message contains `"STRING_AGG"`, `"Migration: Oracle"`, and `"sqlserver"`.
  2. `CheckOracleSyntax_Listagg_PropertiesContainTargetProvider` — assert properties dict has key `"targetProvider"` with value `"sqlserver"`.
  3. `CheckOracleSyntax_PlusOperator_MessageContainsJoinHint` — assert violation for `(+)` contains `"OUTER JOIN"`.
  4. `CheckOracleSyntax_AllKeywords_MessageFormatConsistent` — `[Theory]` over all 16 keywords: each message starts with `"[Migration: Oracle -> sqlserver]"`.

### Step 4: Fix Phase 4 — MSBuild Exec + VSIXSourceItem, not ProjectReference

**What's wrong:** Adding `<ProjectReference>` with `<IncludeInVSIX>true</IncludeInVSIX>` would:
- Break the zero-ProjectReference architecture of `DataGuard.VisualStudio.csproj`
- Load analyzer DLLs into VS main AppDomain instead of Roslyn sandbox
- Conflict with the MSBuild `<Exec>` build pattern used for CLI packaging

Existing pattern: `PublishDataGuardCli` target at `DataGuard.VisualStudio.csproj:61-66` runs `dotnet publish` via `<Exec>` and includes output as `<VSIXSourceItem>`.

**Revised Phase 4 approach:**

1. **Add MSBuild target `BuildAnalyzers`** in `DataGuard.VisualStudio.csproj`:
   ```xml
   <Target Name="BuildAnalyzers" BeforeTargets="GetVsixSourceItems" Condition="!Exists('obj\analyzers\DataGuard.Analyzers.dll')">
     <Exec Command="dotnet build &quot;$(MSBuildThisFileDirectory)..\DataGuard.Analyzers\DataGuard.Analyzers.csproj&quot; -c $(Configuration) -o &quot;$(MSBuildProjectDirectory)\obj\analyzers&quot; --no-restore" />
     <Exec Command="dotnet build &quot;$(MSBuildThisFileDirectory)..\DataGuard.CodeFixes\DataGuard.CodeFixes.csproj&quot; -c $(Configuration) -o &quot;$(MSBuildProjectDirectory)\obj\analyzers&quot; --no-restore" />
   </Target>
   ```

2. **Add VSIXSourceItem entries**:
   ```xml
   <ItemGroup>
     <VSIXSourceItem Include="obj\analyzers\DataGuard.Analyzers.dll" />
     <VSIXSourceItem Include="obj\analyzers\DataGuard.CodeFixes.dll" />
   </ItemGroup>
   ```

3. **Update `source.extension.vsixmanifest` Assets** — use `d:Source="File"` (not `d:Source="Project"`):
   ```xml
   <Asset Type="Microsoft.VisualStudio.Analyzer"
          d:Source="File"
          Path="DataGuard.Analyzers.dll" />
   <Asset Type="Microsoft.VisualStudio.Analyzer"
          d:Source="File"
          Path="DataGuard.CodeFixes.dll" />
   ```

4. **Verify Roslyn analyzer loads in sandbox** — after VSIX install, open a C# file with unvalidated SQL. Squiggles must appear from `UnvalidatedSqlCallGenerator` without blocking VS typing (IIncrementalGenerator runs in Roslyn analyzer host, not main AppDomain).

**TDD — write tests first:**
- VSIX integration test:
  1. Build VSIX via `msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /t:Build`.
  2. Extract `.vsix` (ZIP), verify `DataGuard.Analyzers.dll` and `DataGuard.CodeFixes.dll` exist at root.
  3. Verify `[Content_Types].xml` and `extension.vsixmanifest` reference the analyzer DLLs.
- VS SDK integration test (full harness): Install VSIX in VS experimental instance, open test .sln with unvalidated SQL call, verify squiggly underlines appear and lightbulb offers code fixes.
- Regression: `dotnet test tests/DataGuard.Analyzers.Tests/` and `dotnet test tests/DataGuard.CodeFixes.Tests/` continue passing.

### Step 5: Fix Phase 5 — use existing RuleExecuted progress events

**What's wrong:** Phase 5 references `summary.json` which does not exist in VS extension context. However, the CLI **already emits per-rule `RuleExecuted` progress events** on stderr (at `src/DataGuard.Cli/Program.cs:2255-2265` and `2279-2289`) with `RuleId`, `RuleTitle`, `ContractCount`, and `ViolationCount`. The VS extension at `DataGuardPackage.cs:509` already handles `RuleExecuted` events but **discards zero-violation rules** (line 510-512: `if (!violations.HasValue || violations.Value <= 0) { formatted = null; }`).

**No CLI protocol extension needed.** All data is already flowing.

**Revised Phase 5 approach:**

1. **Collect all RuleExecuted events** — in `ProcessProgressLineAsync` or a new accumulator field on `DataGuardPackage`, collect every `RuleExecuted` event into a list:
   ```csharp
   private readonly List<(string RuleId, string? RuleTitle, int ViolationCount)> _ruleInventory = new();
   ```
   In `TryFormatProgress` when `kind == "RuleExecuted"`: always add to `_ruleInventory` regardless of violation count. The existing `formatted = null` for zero-violation rules is fine (don't log them individually), but capture them in the inventory.

2. **Emit summary banner after validation completes** — when `kind == "Summary"` event arrives (the last event, at `Program.cs:446-456`), format and emit the inventory banner to Output Window:
   ```csharp
   var evaluated = _ruleInventory.Where(r => r.ViolationCount >= 0).ToList();
   var withViolations = _ruleInventory.Where(r => r.ViolationCount > 0).ToList();
   var banner = new StringBuilder();
   banner.AppendLine("[DataGuard] ==================== Validation Summary ====================");
   banner.AppendLine($"[DataGuard] Rules Evaluated: {evaluated.Count} ({string.Join(", ", evaluated.Select(r => r.RuleId))})");
   if (withViolations.Any())
       banner.AppendLine($"[DataGuard] Rules with Findings: {string.Join(", ", withViolations.Select(r => $"{r.RuleId} ({r.ViolationCount})"))}");
   banner.AppendLine("[DataGuard] Double-click any Error List item to jump directly to code.");
   banner.AppendLine("[DataGuard] ==========================================================");
   ```

3. **Clear inventory on each validation run** — reset `_ruleInventory.Clear()` at the start of `RunDataGuardValidationAsync` to prevent accumulation across runs.

4. **Skipped rules**: Rules that require DB connection (DG002, DG004 in offline mode) are not instantiated in the `rules` list when their prerequisites are missing, so they won't emit `RuleExecuted` events. To show skipped rules, add a static set of all possible rules and diff against evaluated. The `ProviderRuleCatalog.RuleTitles` dictionary (referenced at `Program.cs:2262`) contains all rule IDs — if this is accessible from the SARIF metadata or progress events, use it. Otherwise, hardcode the full rule ID set as a known catalog constant.

**TDD — write tests first:**
- Test file: `tests/DataGuard.VisualStudio.Tests/RuleInventoryTests.cs`
- Tests:
  1. `TryFormatProgress_RuleExecutedWithZeroViolations_CollectsInInventory` — input RuleExecuted JSON with ViolationCount=0, verify `_ruleInventory` has entry (even though formatted output is null).
  2. `TryFormatProgress_SummaryEvent_EmitsBanner` — feed 3 RuleExecuted events then 1 Summary event, verify banner string contains `"Rules Evaluated: 3"` and lists all rule IDs.
  3. `RuleInventory_ClearedOnNewValidation` — verify inventory resets between validation runs.
  4. `TryFormatProgress_RuleExecutedWithViolations_ShowsInBanner` — verify rules with ViolationCount > 0 appear in "Rules with Findings" line.
- VS SDK integration test: Run validation in experimental instance, check Output Window → "DataGuard" for summary banner after validation completes.

### Step 6: Fix Verification section — correct test directory

Replace `test/` → `tests/` everywhere:
- `test/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj` → `tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj`
- `test/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj` → `tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj`

### Step 7: Fix Critical Files & Anchors table

Replace entire table with verified symbols and line numbers (see Critical Files & Anchors section below).

---

## Critical Files & Anchors

| File | Symbol/Region | Reason |
|------|--------------|--------|
| `src/DataGuard.VisualStudio/DataGuardPackage.cs` | `PublishSarifAsync` (L1313-1426), `TryFormatProgress` (L463), `case "RuleExecuted"` (L509) | Phase 1: wire Navigate; Phase 5: collect rule inventory |
| `src/DataGuard.VisualStudio/DataGuardLogger.cs` | `VsShellUtilities.OpenDocument` (L385) | Reuse pattern for Phase 1 navigation |
| `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` | `PublishDataGuardCli` target (L61-66) | Phase 4: replicate MSBuild Exec pattern for analyzers |
| `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` | `IsSqlString` (L945-967), Dapper SP (L207-258), ADO.NET SP (L332-470) | Phase 2: add prefixes, audit edge cases |
| `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs` | `OracleKeywordMigrations` (L24-47), `CheckOracleSyntaxInNonOracleContext` (L67-117) | Phase 3: message format upgrade only |
| `src/DataGuard.Cli/Program.cs` | `RuleExecuted` emission (L2255-2289) | Phase 5: data source already exists |

---

## Verification

1. **Double-Click Jump Proof** (Phase 1):
   - `dotnet test tests/DataGuard.VisualStudio.Tests/` — new navigation tests pass.
   - VS SDK integration: Install VSIX in VS 2022 experimental instance, run `DataGuard → Run Validation`, double-click any Error List entry.
   - **Observable Result**: Cursor lands on exact line/column in source file. Output Window shows `"Cannot navigate"` for deleted files.

2. **SP Detection Audit Proof** (Phase 2):
   - `dotnet test tests/DataGuard.Core.Tests/` — new prefix + edge case tests pass.
   - Test C# with `string sql = "FNC_GET_TOTAL";` → detected via expanded `IsSqlString`.
   - Test C# with positional Dapper `CommandType.StoredProcedure` → detected as SP.
   - VS SDK integration: Load test solution with all SP patterns, verify diagnostics for each.

3. **DG010 Message Proof** (Phase 3):
   - `dotnet test` Oracle dialect tests — new message format assertions pass.
   - SQL with `LISTAGG` → diagnostic reads `[Migration: Oracle -> sqlserver] Keyword 'LISTAGG' is unsupported. Use STRING_AGG()... (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)`.
   - Properties dict contains `"targetProvider": "sqlserver"`.

4. **VSIX Analyzer Packaging Proof** (Phase 4):
   - `msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /t:Build` succeeds.
   - Extract `.vsix` ZIP → `DataGuard.Analyzers.dll` and `DataGuard.CodeFixes.dll` present.
   - VS SDK integration: Install in experimental instance, open C# with unvalidated SQL → squiggles + lightbulb.
   - `dotnet test tests/DataGuard.Analyzers.Tests/` and `dotnet test tests/DataGuard.CodeFixes.Tests/` pass.

5. **Output Transparency Proof** (Phase 5):
   - `dotnet test tests/DataGuard.VisualStudio.Tests/` — rule inventory tests pass.
   - VS SDK integration: Run validation, check Output Window → "DataGuard".
   - **Observable Result**: Shows `Rules Evaluated: N (DG001, DG010, ...)` and `Rules with Findings: DG010 (3), DG017 (1)`.

6. **Full Regression**:
   - `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj`
   - `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj`
   - `dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj`
   - `dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj`
   - All pass with zero regressions.

---

## Validation Log

### Session 1 — 2026-09-28
**Trigger:** Red-team review revealed critical plan inaccuracies; TDD validation requested.
**Questions asked:** 4

#### Questions & Answers

1. **[Scope]** Phase 2 SP scanning is already 95% implemented. Scope?
   - Options: Keep minimal (prefixes only) | Audit + harden existing | Remove entirely
   - **Answer:** Expand Phase 2 to audit + harden existing SP detection
   - **Rationale:** Ensures edge cases (positional Dapper args, DbCommand factory) are covered; prevents false negatives in production.

2. **[Architecture]** Phase 5 data source for rule inventory?
   - Options: Extend CLI NDJSON | Infer from SARIF + hardcode | Port summary.json
   - **Answer:** Extend CLI --progress NDJSON protocol
   - **Rationale:** Moot — further investigation revealed `RuleExecuted` events already contain all needed data. No CLI change required; just collect existing events in VS extension.

3. **[Architecture]** Phase 4 VSIX analyzer packaging approach?
   - Options: MSBuild Exec + VSIXSourceItem | NuGet package reference
   - **Answer:** MSBuild Exec + VSIXSourceItem (match existing CLI pattern)
   - **Rationale:** Consistent with zero-ProjectReference architecture; avoids NuGet packaging overhead.

4. **[TDD]** Test depth for each phase?
   - Options: Unit + build integration | Full VS SDK integration | Unit only
   - **Answer:** Full integration tests with VS SDK test harness
   - **Rationale:** Navigation, squiggles, and Output Window output are visual/IDE-coupled; unit tests alone cannot prove correct behavior.

#### Confirmed Decisions
- Phase 2: Audit all SP detection paths, add missing prefixes, test edge cases.
- Phase 3: Message format upgrade only — data structure is already correct.
- Phase 4: MSBuild Exec pattern — no ProjectReferences.
- Phase 5: Use existing `RuleExecuted` events — no CLI change needed.
- All tests: Full VS SDK integration harness where applicable.

---

## Assumptions & Contingencies

- **`VsShellUtilities.OpenDocument` 7-parameter overload**: Available in `Microsoft.VisualStudio.SDK` 17.x (project references version 17.14.40265). If the specific overload returning `IVsTextView` is missing, fallback: call 4-parameter `OpenDocument`, then `VsShellUtilities.GetTextView(windowFrame)`.
- **`ProviderRuleCatalog.RuleTitles` accessibility**: If this dictionary is not accessible from the VS extension process (it's in the CLI), hardcode a static `AllKnownRules` set in the VS extension for the skip-list diff. Keep it in a single constant for easy maintenance.
- **Dapper positional arg index**: Dapper method signatures vary across versions. If positional index detection causes false positives, restrict to named argument detection only (existing behavior) and document as known limitation.
- **Analyzer DLL target framework**: `DataGuard.Analyzers.csproj` targets `netstandard2.0` (verified). VS 2022 Roslyn host requires `netstandard2.0` — compatible. If CodeFixes targets a different framework, verify or retarget before packaging.
