# DataGuard VS Code Extension: Enhanced Diagnostics, SP Scanning & DG010 Clarity

## Status: ✅ COMPLETED & VERIFIED (2026-09-28)

All phases and implementation steps outlined in this plan and the associated Red-Team Review report (`RED_TEAM_REVIEW_PREDICT_SCENARIO_PLAN.md`) have been fully executed, verified, and hardened.

### Summary of Completed Steps & Verification

| Step | Feature / Area | Status | Verification Summary |
|:----:|----------------|:------:|----------------------|
| **Step 1** | CodeLens Provider (`codelens-provider.ts`) | ✅ Completed | Implemented violation-focused CodeLens with per-document cap (max 50 lenses), method line grouping, and dashboard action binding. Registered in `extension.ts` and covered by TS tests. |
| **Step 2** | Editor Decorations (`decoration-manager.ts`) | ✅ Completed | Implemented theme-aware gutter icons (`editorError.foreground`, `editorWarning.foreground`) across visible text editors with clean teardown, clear hooks, and hover deduplication. |
| **Step 3** | Stored Procedure Scanning (`ProjectCSharpSqlSource.cs`) | ✅ Completed | Implemented SP scanning across ADO.NET `CommandType.StoredProcedure` (constructors, assignments, initializers) and Dapper `commandType:` parameters. Emits `RawSqlDescriptor` with `IsStoredProcedure = true` and `ProcedureName`. DG101 false positives suppressed. 16 tests in `RedTeamRegressionTests.cs`. |
| **Step 4** | Actionable DG010 Migration Guidance (`OracleDialectChecker.cs`) | ✅ Completed | Implemented `OracleKeywordMigrations` & `OracleOperatorMigrations` with SQL Server/ANSI alternatives, newline/whitespace tolerance (`\bCONNECT\s+BY\b`), and `properties.migration` metadata preservation in SARIF. |
| **Step 5** | Hover Provider (`hover-provider.ts`) | ✅ Completed | Rich Markdown hover display with safe formatting (`md.isTrusted = false`), dynamic codeblocks, migration hint surfacing, and overlap deduplication. |
| **Step 6** | SARIF Properties Pipeline (`redaction.ts`) | ✅ Completed | Extended `SarifResult` and `FindingItem` interfaces to retain `properties?: Record<string, unknown>` through parsing into Tree and Dashboard views. |
| **Step 7** | Extension Lifecycle & Configuration | ✅ Completed | Added configuration listeners (`onDidChangeConfiguration`) for dynamic provider toggling, hooked clear actions, and proper resource disposal in `deactivate()`. |
| **Suite** | Full Verification & Review Hardening | ✅ Completed | **904 C# tests + 62 TypeScript tests = 966 total tests passing (0 failures).** Review fixes applied for bracketed SP names regex, receiver-less CommandText false-match, and endCol +1 inflation. |

---

## Context

The DataGuard VS Code extension currently shows diagnostics in the Problems panel and fires DG010 + DG017 rules. Three gaps identified:

1. **Click-to-navigate already works** — `toRange()` in `extension.ts:743-749` correctly converts SARIF 1-based regions to VS Code 0-based `Range`. Double-clicking a Problem row already jumps to the code location. The Findings Tree View also has `vscode.open` commands with selection ranges. **No work needed here.**

2. **Additional code display surfaces missing** — No CodeLens, no editor inline decorations, no hover provider. Adding these gives developers in-editor visibility without switching to Problems/Tree views.

3. **Stored procedure call scanning is incomplete** — `ProjectCSharpSqlSource` detects `EXEC`/`EXECUTE` in SQL text and `sp_`/`usp_` prefixes, but **does not detect `CommandType.StoredProcedure` assignments** on `SqlCommand`/`OracleCommand`/`NpgsqlCommand` objects. This is the most common ADO.NET pattern for calling SPs without embedding `EXEC` in the SQL string. The `CommandText` is captured but classified as raw SQL, missing the SP semantics.

4. **DG010 warning message is confusing** — The message `"Oracle-specific keyword 'LISTAGG' used in non-Oracle context"` is technically correct but doesn't tell the user *what to do*. The rule fires because provider is configured as non-Oracle (default `sqlserver`), and the SQL contains Oracle-only constructs. For a migration project, this IS the desired behavior — it flags Oracle syntax that needs rewriting. But the message needs an actionable suggestion (e.g., the SQL Server equivalent).

## Approach

### Step 1: Add CodeLens provider for SQL call sites

Show inline CodeLens annotations above methods/lines containing SQL calls, displaying rule violations count and enabling one-click navigation to the Dashboard.

- Create `src/DataGuard.VSCode/src/ui/codelens-provider.ts`
  - Class `DataGuardCodeLensProvider implements vscode.CodeLensProvider`
  - Consumes the same `DiagnosticCollection` diagnostics
  - For each file with DataGuard diagnostics, group by method/line and show CodeLens like `"⚠ DataGuard: 3 findings"` or `"✅ DataGuard: No issues"`
  - On click → run `dataguard.openDashboard` or focus the Findings Tree
- Register in `extension.ts:activate()`:
  ```typescript
  const codeLensProvider = new DataGuardCodeLensProvider(diagnostics);
  context.subscriptions.push(
      vscode.languages.registerCodeLensProvider(
          { scheme: "file", language: "csharp" },
          codeLensProvider
      )
  );
  ```
- Add configuration `dataguard.codeLens.enabled` (default `true`) in `package.json` contributes.configuration
- Refresh CodeLens after `loadDiagnostics()` completes by calling `codeLensProvider.refresh()`
- No existing CodeLens/decoration providers exist — this is new surface

### Step 2: Add editor gutter/inline decorations for findings

Show warning/error decorations inline in the editor for DataGuard findings, making them visible without scrolling to the Problems panel.

- Create `src/DataGuard.VSCode/src/ui/decoration-manager.ts`
  - Class `DataGuardDecorationManager`
  - Creates `TextEditorDecorationType` instances for error (red gutter dot + light background) and warning (yellow gutter dot)
  - Method `applyDecorations(editor: vscode.TextEditor, diagnostics: vscode.DiagnosticCollection)` — reads diagnostics for the active editor's document, maps ranges to `DecorationOptions` with hover messages showing rule ID + message
  - Method `clearDecorations(editor: vscode.TextEditor)`
- Hook into `vscode.window.onDidChangeActiveTextEditor` and after `loadDiagnostics()` to refresh
- Register in `extension.ts:activate()`
- Add configuration `dataguard.decorations.enabled` (default `true`)

### Step 3: Detect `CommandType.StoredProcedure` in C# source scanning

`ProjectCSharpSqlSource` currently scans `CommandText` assignments (line 231-262) but doesn't look for adjacent `CommandType = CommandType.StoredProcedure` assignments. When a developer writes:

```csharp
cmd.CommandText = "GET_CUSTOMER_DETAILS";
cmd.CommandType = CommandType.StoredProcedure;
```

The `CommandText` value `"GET_CUSTOMER_DETAILS"` fails `IsSqlString()` (no SQL keyword match, no `sp_`/`usp_` prefix, no dot), so it's silently skipped. This is the gap.

- In `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`, after scanning `CommandText` assignments (section 2, line 231-262), add a new section **2b** that detects `CommandType.StoredProcedure` / `CommandType = System.Data.CommandType.StoredProcedure` assignments:
  1. Scan all `AssignmentExpressionSyntax` where left side ends with `CommandType` and right side resolves to `CommandType.StoredProcedure` (check via semantic model `ConstantValue` or name match on `MemberAccessExpressionSyntax`)
  2. From the matched assignment, walk the enclosing block/method to find the sibling `CommandText = "..."` assignment on the same receiver variable
  3. Extract the `CommandText` value as the stored procedure name
  4. Create a `RawSqlDescriptor` with `SqlText = $"EXEC {procName}"` to route it through existing SP validation rules (DG101/DG002/DG003), OR better: create a `StoredProcedureDescriptor` directly with the procedure name
  5. Reuse existing `TryResolveString()` for resolving the CommandText value
  6. Also handle the common pattern where `CommandType` is set via constructor parameter or object initializer

- Additionally, detect Dapper's `commandType: CommandType.StoredProcedure` named argument:
  ```csharp
  connection.Execute("MY_PROC", new { Id = 1 }, commandType: CommandType.StoredProcedure);
  ```
  In the existing Dapper method scanning (section 1, around line 193-229), check if the invocation has a named argument `commandType` with value `CommandType.StoredProcedure`. If so, treat the first string argument as a procedure name rather than raw SQL.

- Exact methods:
  - Add `private static bool IsStoredProcedureCommandType(AssignmentExpressionSyntax assignment, SemanticModel model)` — checks if right-hand side evaluates to `System.Data.CommandType.StoredProcedure`
  - Add `private string? FindSiblingCommandText(SyntaxNode commandTypeAssignment, SemanticModel model, CancellationToken ct)` — walks enclosing block to find `CommandText` assignment on same receiver
  - Modify the existing Dapper invocation handler to check for `commandType:` named parameter

### Step 4: Improve DG010 warning messages with actionable migration guidance

Current message: `"Oracle-specific keyword 'LISTAGG' used in non-Oracle context"`
Problem: Doesn't tell the developer what to use instead.

- In `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs`, replace the flat `OracleKeywords` `HashSet<string>` with a `Dictionary<string, string>` mapping each Oracle keyword to its migration guidance:

  ```csharp
  private static readonly Dictionary<string, string> OracleKeywordMigrations = new(StringComparer.OrdinalIgnoreCase)
  {
      ["DECODE"] = "Use CASE WHEN ... THEN ... ELSE ... END",
      ["NVL"] = "Use ISNULL() (SQL Server) or COALESCE() (ANSI SQL)",
      ["NVL2"] = "Use CASE WHEN expr IS NOT NULL THEN val1 ELSE val2 END",
      ["DUAL"] = "Remove FROM DUAL (SQL Server allows SELECT without FROM)",
      ["ROWNUM"] = "Use TOP N (SQL Server) or ROW_NUMBER() OVER (...)",
      ["CONNECT BY"] = "Use recursive CTE (WITH RECURSIVE ... AS ...)",
      ["START WITH"] = "Use recursive CTE anchor member",
      ["SYSDATE"] = "Use GETDATE() (SQL Server) or CURRENT_TIMESTAMP (ANSI)",
      ["SYSTIMESTAMP"] = "Use SYSDATETIMEOFFSET() (SQL Server) or CURRENT_TIMESTAMP",
      ["NEXTVAL"] = "Use NEXT VALUE FOR sequence_name (SQL Server)",
      ["CURRVAL"] = "No direct equivalent; query sys.sequences or use OUTPUT clause",
      ["ROWID"] = "No direct equivalent; use primary key or ROW_NUMBER()",
      ["LISTAGG"] = "Use STRING_AGG() (SQL Server 2017+) or FOR XML PATH",
      ["WM_CONCAT"] = "Use STRING_AGG() (SQL Server 2017+) or FOR XML PATH",
      ["XMLAGG"] = "Use FOR XML PATH (SQL Server)",
      ["XMLFOREST"] = "Use FOR XML PATH with explicit element construction",
      ["XMLELEMENT"] = "Use FOR XML PATH with explicit element construction",
      ["REGEXP_LIKE"] = "Use LIKE with wildcards or CLR regex (SQL Server)",
      ["REGEXP_REPLACE"] = "Use REPLACE() or CLR regex (SQL Server)",
      ["REGEXP_SUBSTR"] = "Use SUBSTRING() with PATINDEX() (SQL Server)",
      ["REGEXP_INSTR"] = "Use PATINDEX() (SQL Server)",
  };
  ```

- Change the violation message format from:
  `$"Oracle-specific keyword '{keyword}' used in non-Oracle context"`
  to:
  `$"[Migration] Oracle keyword '{keyword}' needs replacement. {migrationHint}"`

  Example output:
  `[DG010] [Migration] Oracle keyword 'LISTAGG' needs replacement. Use STRING_AGG() (SQL Server 2017+) or FOR XML PATH`

- Same treatment for operators: `(+)` → `"Use LEFT/RIGHT JOIN (ANSI SQL)"`, `**` → `"Use POWER() function"`

- The `CheckOracleSyntaxInNonOracleContext` loop changes from `foreach keyword in OracleKeywords` to `foreach (keyword, hint) in OracleKeywordMigrations`, passing `hint` into the message

- Update the `Properties` dictionary to include the migration hint: `{ { "keyword", keyword }, { "migration", hint } }` — this enables the Findings Tree and Dashboard to show the hint too

### Step 5: Add Hover provider showing DG rule details and migration hints

When hovering over a DataGuard diagnostic squiggle, show rich hover content with rule description, severity, and (for DG010) the migration suggestion.

- Create `src/DataGuard.VSCode/src/ui/hover-provider.ts`
  - Class `DataGuardHoverProvider implements vscode.HoverProvider`
  - In `provideHover()`, check if the position overlaps any DataGuard diagnostic in the `DiagnosticCollection`
  - Build a `MarkdownString` with:
    - Rule ID + severity icon
    - Full message
    - For DG010: extract migration hint from the message and display prominently
    - Link to quick-fix if available
- Register in `extension.ts`:
  ```typescript
  context.subscriptions.push(
      vscode.languages.registerHoverProvider(
          { scheme: "file", language: "csharp" },
          new DataGuardHoverProvider(diagnostics)
      )
  );
  ```

## Critical files & anchors

| File | Symbol/Region | Reason |
|------|--------------|--------|
| `src/DataGuard.VSCode/src/extension.ts` | `activate()` L53-100, `loadDiagnostics()` L675-741 | Registration hub for all new providers; refresh hooks after validation |
| `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` | Section 2 L231-262, `IsSqlString()` L738-760 | SP detection gap: `CommandType.StoredProcedure` not checked |
| `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs` | `OracleKeywords` L25-31, `CheckOracleSyntaxInNonOracleContext()` L49-91 | DG010 message format change |
| `src/DataGuard.VSCode/package.json` | `contributes.configuration` | New settings for CodeLens and decorations |

## Verification

1. **CodeLens**: Open a `.cs` file containing `FromSqlRaw` or `Execute` calls → run `DataGuard: Run Validation` → verify CodeLens annotations appear above SQL call lines showing finding count. Click CodeLens → Dashboard opens.

2. **Decorations**: After validation, verify gutter icons (red dot for errors, yellow for warnings) appear on lines with DataGuard findings. Hover over decorated line → see rule message in hover tooltip.

3. **SP Detection**: Create test `.cs` file:
   ```csharp
   var cmd = new SqlCommand("GET_CUSTOMER_BY_ID", conn);
   cmd.CommandType = CommandType.StoredProcedure;
   cmd.Parameters.AddWithValue("@CustomerId", id);
   ```
   Run `dataguard validate` → verify this SP call appears in SARIF output and Findings Tree. Also test Dapper pattern: `conn.Execute("MY_PROC", new { Id = 1 }, commandType: CommandType.StoredProcedure)`.

4. **DG010 messages**: Run `dataguard validate` on code containing `SYSDATE`, `LISTAGG`, `DUAL` → verify messages now include migration hint, e.g. `"[Migration] Oracle keyword 'SYSDATE' needs replacement. Use GETDATE() (SQL Server) or CURRENT_TIMESTAMP (ANSI)"`.

5. **Hover**: Hover over a DG010 squiggle → verify rich hover popup shows rule ID, severity, message with migration hint.

6. **Existing tests**: Run `cd src/DataGuard.VSCode && npm test` and `dotnet test` from repo root to ensure no regressions.

## Assumptions & contingencies

- **Provider default**: Config defaults to `sqlserver` when no `.dataguard.yml` exists. DG010 will fire on Oracle syntax in this default mode. If user's actual target is Oracle, they should set `default_provider: oracle` — the migration hints assume SQL Server target. If target is PostgreSQL, some hints (like `ISNULL→COALESCE`) are still valid; COALESCE alternatives are already included. No per-target-provider hint variants planned — ANSI SQL alternatives are given where applicable.
- **CodeLens performance**: Diagnostic-based CodeLens is reactive (only after validation run), not continuous. If performance is a concern, the `codeLens.enabled` setting provides opt-out.
- **Roslyn semantic model for CommandType**: If `CommandType` enum cannot be resolved by the semantic model (missing `System.Data` reference in the scanned project), fall back to name-based matching: right-hand side is `MemberAccessExpressionSyntax` with name `StoredProcedure` and expression ending in `CommandType`.
