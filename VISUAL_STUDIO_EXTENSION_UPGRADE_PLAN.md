# Visual Studio Extension Upgrade: Diagnostics Navigation, SP Scanning, Roslyn Integration & DG010 Clarity
> ⚠️ **Critical corrections applied** — see RED_TEAM_REVIEW_VALIDATION_PLAN.md. Fabricated API references corrected. Implementation complete as of 2026-09-28.


## Context
The DataGuard Visual Studio extension (`src/DataGuard.VisualStudio`) hosts contract validation for C# .NET solutions and stored procedures. Developers currently observe four key issues in Visual Studio:
1. Double-clicking Error List items does not consistently navigate to the exact code line/column.
2. No in-editor visual surfaces exist (no Roslyn squiggles, lightbulb quick-fixes, or custom Tool Windows).
3. Stored procedure calls using ADO.NET (`CommandType.StoredProcedure`), Dapper (`commandType: CommandType.StoredProcedure`), or Oracle prefixes (`PRC_`, `PKG_`) are completely missed by source scanning.
4. `DG010` warnings (`LISTAGG`, `DUAL`, `ROWNUM`, `SYSDATE`) are cryptic, lacking context on target provider or replacement syntax, while other rules (`DG002-DG009`, `DG101`) do not appear in logs due to offline prerequisites or missed SP extraction.

This plan consolidates, strengthens, and upgrades the C# code in the Visual Studio extension and its supporting C# engines (`DataGuard.VisualStudio`, `DataGuard.Analyzers`, `DataGuard.CodeFixes`, `DataGuard.Core`, and `DataGuard.Oracle.Adapter`).

---

## Prediction Report (ck:predict: 5 Personas)

### Verdict: GO (with targeted guardrails)

### Persona Consensus & Debate Matrix

| Topic | Architect | Security | Performance | UX | Devil's Advocate | Resolution |
|-------|-----------|----------|-------------|-----|------------------|------------|
| **Error List Double-Click Navigation** | Use `VsShellUtilities.OpenDocumentAndNavigateToPosition` on `task.Navigate` event. Decouple from project hierarchy. | Ensure path normalization prevents directory traversal out of solution. | O(1) operation on UI thread via `JoinableTaskFactory.SwitchToMainThreadAsync`. | Essential: double-click MUST center editor cursor on exact violation line & column. | Why not rely on standard VS Task.Navigate? (Answer: VS requires HierarchyItem or explicit event handler; without it, navigation fails silently). | **Implement explicit `task.Navigate` event using `VsShellUtilities` with path boundary validation.** |
| **Roslyn VSIX Integration** | Package `DataGuard.Analyzers.dll` and `DataGuard.CodeFixes.dll` as `Analyzer` asset in `source.extension.vsixmanifest`. | Analyzer runs in Roslyn sandbox; must remain zero-network, zero-credential, read-only syntax analysis. | Must strictly adhere to `IIncrementalGenerator` / `DiagnosticAnalyzer` zero-allocation guidelines to avoid typing lag. | Transformative: real-time in-editor squiggles + lightbulb (`Ctrl+.`) without running manual menu command. | Will bundling analyzers bloat the VSIX or conflict with existing CLI analyzer? (Answer: Analyzers are already netstandard2.0 with separate light layer). | **Package Roslyn Analyzer asset into VSIX; keep heavy database checks out-of-process in CLI.** |
| **Stored Procedure Scanning Expansion** | Correlate `cmd.CommandType = CommandType.StoredProcedure` with `cmd.CommandText` in `ProjectCSharpSqlSource` and detect Dapper `commandType` arguments. | No risk: purely AST syntax extraction. No query execution. | Roslyn AST traversal within existing single-pass syntax tree visitor. Negligible overhead. | Crucial: developers can finally see their stored procedure invocations validated in Visual Studio. | What if `CommandText` and `CommandType` are in different methods? (Answer: Restrict correlation to enclosing block/method scope to ensure deterministic resolution). | **Implement intra-method assignment correlation and Dapper named argument inspection.** |
| **DG010 Clarity & Migration Hints** | Enrich `DG010` diagnostic message with target database context and replacement SQL equivalent. | No security exposure in diagnostic text. Sanitize user identifiers. | Pre-computed static dictionary lookup: O(1), zero allocation. | Huge win: developer immediately understands what keyword is illegal on target DB and how to rewrite it. | What if developer actually targets Oracle? (Answer: Add actionable hint to configure `default_provider: oracle`). | **Upgrade `OracleDialectChecker` to output migration target, replacement syntax, and config guidance.** |
| **Rule Execution Transparency** | Output explicit summary banner in Output Window detailing which rules executed, which were skipped, and why (e.g. offline mode vs live DB). | Mask connection strings and sensitive hostnames in logs. | Minimal string concatenation on CLI completion. | Solves confusion: developer knows exactly why DG002/DG004 didn't fire (requires snapshot or live DB). | Don't spam the Error List with informational messages. (Answer: Route execution breakdown to Output Window only). | **Emit clear rule execution inventory to Output Window pane.** |

---

## Scenario Report (ck:scenario: 12 Dimensions)

### Dimensions Analyzed: 6 Relevant Dimensions (6 Skipped: User Types, Authorization, Compliance, Business Logic, Webhooks/Integration, Scale - irrelevant to local VS C# analyzer extension)

| # | Dimension | Scenario | Severity | Expected Behavior |
|---|-----------|----------|----------|-------------------|
| 1 | **Input Extremes** | `task.Document` in SARIF has relative path or mixed slashes (`dir/file.cs` vs `dir\file.cs`) | High | Resolve against solution root, normalize via `Path.GetFullPath()`, verify `File.Exists()` before navigating. |
| 2 | **Input Extremes** | `CommandText` is string concatenation or dynamic expression (`cmd.CommandText = "PRC_" + suffix`) | Medium | Gracefully skip or flag as `DG020` (Undetermined SQL/SP Name) without crashing analyzer. |
| 3 | **State Transitions** | Double-clicking error when document is already open vs closed vs in external preview tab | High | `VsShellUtilities.OpenDocumentAndNavigateToPosition` brings existing tab to focus, or opens new tab and places caret at (line, column). |
| 4 | **Timing / Concurrency** | Validation re-run triggered while previous CLI process is still running or being cancelled | High | `DataGuardPackage` process gate locks process handle, cancels previous child, clears old `ErrorListProvider.Tasks`, and reloads clean state. |
| 5 | **Environment** | Solution contains legacy `.csproj` (packages.config) alongside SDK-style projects | Medium | Roslyn analyzer loads in Visual Studio 2022 (v17.x amd64) independently of target project framework (.NET Framework 4.7.2 through .NET 9.0). |
| 6 | **Error Cascades** | CLI `dataguard.exe` missing or fails to execute during manual validation run | High | Detect missing binary, prompt to auto-install or bundle from extension directory (`cli\dataguard.exe`), output clear diagnostic in Output Pane. |

---

## Approach

### Phase 1: Robust Double-Click Navigation in Visual Studio Error List
Fix `DataGuardPackage.cs:PublishSarifAsync` so double-clicking any Error List diagnostic reliably navigates to the exact file, line, and column.

1. **Wire up `task.Navigate` event**:
   - In `src/DataGuard.VisualStudio/DataGuardPackage.cs`, inside `PublishSarifAsync()`, subscribe to `task.Navigate`:
     ```csharp
     task.Navigate += (sender, e) =>
     {
         this.JoinableTaskFactory.RunAsync(async () =>
         {
             await this.JoinableTaskFactory.SwitchToMainThreadAsync();
             if (File.Exists(task.Document))
             {
                 VsShellUtilities.OpenDocumentAndNavigateToPosition(
                     this,
                     task.Document,
                     task.Line,
                     task.Column);
             }
             else
             {
                 await this.WriteOutputAsync($"[DataGuard] Cannot navigate: file not found '{task.Document}'.\r\n");
             }
         }).FileAndForget("DataGuard/NavigateTask");
     };
     ```
2. **Normalize and validate file paths**:
   - In `ResolveSarifArtifactUri()`, enforce Windows path normalization (`Path.GetFullPath`) and case-insensitive file existence check against `solutionDirectory`.
3. **Verify 0-based coordinate contract**:
   - SARIF region is 1-based (`startLine`, `startColumn`). `DataGuardPackage.cs` correctly subtracts 1: `Math.Max(0, startLine - 1)`. `VsShellUtilities.OpenDocumentAndNavigateToPosition` accepts 0-based line and 0-based column. Retain exact 0-based index.

### Phase 2: Stored Procedure Scanning in C# Code & Roslyn Analyzers
Enable DataGuard to discover stored procedure calls in C# code, enabling SP contract validation rules (`DG101`, `DG002`, `DG003`).

1. **ADO.NET `CommandType.StoredProcedure` Detection**:
   - In `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`:
     - In `ExtractContractsFromCompilationAsync`, add pass **2b**: inspect `AssignmentExpressionSyntax` where right side is `CommandType.StoredProcedure` (or `System.Data.CommandType.StoredProcedure`).
     - Find sibling assignment `CommandText = "..."` on the same receiver identifier within the enclosing block or method body.
     - Extract procedure name via `TryResolveString`.
     - Register as `StoredProcedureDescriptor` (or `RawSqlDescriptor` with `IsStoredProcedure = true`).
2. **Dapper `commandType: CommandType.StoredProcedure` Detection**:
   - In `ProjectCSharpSqlSource.cs` (invocation pass 1), inspect invocations of Dapper methods (`Query`, `QueryAsync`, `Execute`, `ExecuteAsync`, `QueryMultiple`, etc.):
     - Check if argument list contains an argument named `commandType` whose expression is `CommandType.StoredProcedure`, OR if the 4th/5th positional argument is `CommandType.StoredProcedure`.
     - When detected, extract the first string argument as the stored procedure name and set `IsStoredProcedure = true`.
3. **Oracle Procedure Naming Conventions**:
   - In `IsSqlString()`, expand prefix matching beyond `sp_` / `usp_`:
     - Add `PRC_`, `PROC_`, `FNC_`, `P_`, and package call pattern `PACKAGE_NAME.PROCEDURE_NAME`.
4. **Roslyn Analyzer `UnvalidatedSqlCallGenerator` & `ContractValidationAnalyzer` Update**:
   - In `src/DataGuard.Analyzers/Analyzers.cs`:
     - Update `IsPotentialSqlCall` to recognize invocations passing `CommandType.StoredProcedure`.
     - In `AnalyzeDapperQuery` and `AnalyzeExecuteSql`, recognize `IsStoredProcedure` from the `commandType` argument.

### Phase 3: DG010 Warning Clarification & Actionable Migration Hints
Make `DG010` clear, actionable, and context-aware.

1. **Keyword Migration Map**:
   - In `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs`, replace `OracleKeywords` `HashSet<string>` with `Dictionary<string, string>` mapping each keyword to its SQL Server / ANSI SQL equivalent:
     - `LISTAGG` -> `Use STRING_AGG() (SQL Server 2017+) or FOR XML PATH`
     - `DUAL` -> `Remove 'FROM DUAL' (SQL Server supports SELECT without FROM)`
     - `ROWNUM` -> `Use TOP (n) or ROW_NUMBER() OVER (...)`
     - `SYSDATE` -> `Use GETDATE() (SQL Server) or CURRENT_TIMESTAMP`
     - `NVL` -> `Use ISNULL() (SQL Server) or COALESCE()`
     - `DECODE` -> `Use CASE WHEN ... THEN ... ELSE ... END`
2. **Enriched Diagnostic Message**:
   - Format: `$"[Migration: Oracle -> {targetProvider}] Keyword '{keyword}' is unsupported. {replacementHint}. (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)"`
3. **Operator Migration Map**:
   - `(+)` -> `Use ANSI LEFT/RIGHT OUTER JOIN`
   - `**` -> `Use POWER() function`
4. **Diagnostic Properties**:
   - Add `{ "keyword", keyword }`, `{ "hint", hint }`, and `{ "targetProvider", targetProvider }` to violation properties dictionary for consumption by Error List and IDE tooltip.

### Phase 4: Package Roslyn Analyzers into Visual Studio VSIX
Give Visual Studio developers in-editor squiggles and lightbulb fixes directly in the code editor without relying solely on manual menu command runs.

1. **Add Analyzer Asset to VSIX Manifest**:
   - In `src/DataGuard.VisualStudio/source.extension.vsixmanifest`, add Asset entry:
     ```xml
     <Asset Type="Microsoft.VisualStudio.Analyzer"
            d:Source="Project"
            d:ProjectName="DataGuard.Analyzers"
            Path="|DataGuard.Analyzers|" />
     ```
2. **Update VSIX Project References**:
   - In `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`, reference `DataGuard.Analyzers.csproj` and `DataGuard.CodeFixes.csproj` with `<IncludeInVSIX>true</IncludeInVSIX>`.
3. **Ensure Zero-Impact Typing Performance**:
   - Verify `UnvalidatedSqlCallGenerator` runs purely syntax-only on `IIncrementalGenerator` without thread blocking or allocation.

### Phase 5: Output Window Rule Execution Inventory & Transparency
Explain clearly in the Visual Studio Output Pane why certain rules ran and why others were skipped.

1. **Rule Summary Banner**:
   - In `DataGuardPackage.cs`, after CLI execution finishes, parse `summary.json` (or CLI output):
   - Print clear execution summary into Output Pane:
     ```text
     [DataGuard] ==================== Validation Summary ====================
     [DataGuard] Target Provider: SQL Server (Default)
     [DataGuard] Rules Evaluated: 14 (DG001, DG010, DG017, DG101, ...)
     [DataGuard] Rules Skipped: 4 (DG002, DG004 require snapshot.json or live DB connection)
     [DataGuard] Diagnostics: X errors, Y warnings loaded into Error List.
     [DataGuard] Double-click any Error List item to jump directly to code.
     [DataGuard] ==========================================================
     ```

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

1. **Double-Click Jump Proof**:
   - Open Visual Studio with `DataGuard.VisualStudio.vsix` installed.
   - Run `DataGuard -> Run Validation`.
   - Double-click any `[DG010]` or `[DG017]` entry in the Error List.
   - **Observable Result**: Visual Studio immediately opens the source file and places the cursor on the exact line and column where `LISTAGG` / `SELECT *` appears.
2. **Stored Procedure Detection Proof**:
   - Add test method in C# project:
     ```csharp
     using (var cmd = conn.CreateCommand())
     {
         cmd.CommandText = "PRC_UPDATE_CUSTOMER";
         cmd.CommandType = CommandType.StoredProcedure;
     }
     ```
   - Run `DataGuard -> Run Validation`.
   - **Observable Result**: The stored procedure `PRC_UPDATE_CUSTOMER` is listed in discovered SQL contracts, and rule `DG101` evaluates parameter bindings.
3. **DG010 Migration Guidance Proof**:
   - Include `SELECT LISTAGG(name, ',') WITHIN GROUP (ORDER BY id) FROM DUAL;` in code.
   - Run validation.
   - **Observable Result**: Error List displays:
     `[DG010] [Migration: Oracle -> sqlserver] Keyword 'LISTAGG' is unsupported. Use STRING_AGG() (SQL Server 2017+) or FOR XML PATH. (If targeting Oracle, set 'default_provider: oracle' in .dataguard.yml)`
4. **Output Window Transparency Proof**:
   - View Output Window -> Show output from: "DataGuard".
   - **Observable Result**: Shows clear summary of executed vs skipped rules (e.g. `DG002 skipped: offline mode without DB connection`).
5. **Regression Verification**:
   - Run `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj`
   - Run `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj`
   - Run `dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj`
   - Run `dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj`
   - All tests pass with zero regressions.

---

## Assumptions & Contingencies

- **Target Database Context**: When no `.dataguard.yml` is present, DataGuard assumes target provider is `sqlserver`. If the developer's application is pure Oracle (not migrating), the warning explicitly directs them to add `default_provider: oracle` to `.dataguard.yml`, which deactivates `DG010` and activates Oracle-native rules (`DG011`, `DG013`).
- **Visual Studio SDK Version**: Target framework is .NET Framework 4.7.2 with Visual Studio SDK 17.x (VS 2022 64-bit). Navigation pattern: `VsShellUtilities.OpenDocument()` → `IVsWindowFrame.Show()` → `IVsTextView.SetCaretPos(line, col)` → `CenterLines(line, 1)`. Existing usage at `DataGuardLogger.cs:385`.
- **Roslyn Memory Sandbox**: Analyzers run inside devenv.exe. Heavy semantic validations requiring database connections remain strictly outside devenv in `dataguard.exe` CLI process to protect Visual Studio stability.
