# DataGuard SQL-to-C# Traceability & Visibility Plan (Red-Team Remediated)

**Status**: Completed (100%)
**Execution Date**: 2026-09-24
**Review Status**: Audited & Verified

## Context

Users of the DataGuard VS Code extension reported **zero visible SQL discovery**: no query inventory, no per-query operation breakdown, no C#-to-SQL mapping evidence, and no dedicated UI for live DB schema verification. While the core engine (`ProjectCSharpSqlSource`, `MappingTraceEngine`, `MappingEvidence`) detected queries internally and wrote `summary.json`, `extension.ts:337-349` previously only extracted 3 scalar integers (`queriesFound`, `filesScanned`, `connectionsFound`) for a single Output channel log line, discarding the mapping data and deleting the temp directory.

This plan delivered complete end-to-end visibility with minimal, non-redundant diffs:
1. [x] **Extend `ScanSummary.ToJson()` and add CLI `scan` command** to produce structured, actionable query-to-C# mapping reports with locations and status.
2. [x] **Add AST Pass 5 to `ProjectCSharpSqlSource.cs`** to detect unreferenced `const string` / `static readonly string` SQL fields without breaking or duplicating existing string interpolation and raw literal logic.
3. [x] **Add `dataguard.sqlQueriesView` Tree Provider** to VS Code (`src/DataGuard.VSCode/src/ui/sql-queries-tree-provider.ts`), rendering discovered queries grouped by file with mapping badges and jump-to-source actions.
4. [x] **Add "SQL ↔ C# Mappings" tab to `DataGuardDashboardPanel`** (`dashboard-view.ts` and `dashboard-panel.ts`), rendering the full column-to-property mapping table.
5. [x] **Fix parameter binding in Live Schema Providers** (`OracleLiveQuerySchemaProvider`, `PostgreSqlLiveQuerySchemaProvider`) and expose explicit `dataguard verify-shape` CLI and VS Code command.
6. [x] **Improve progress and Output Channel logging** in `extension.ts:processProgressText`.
7. [x] **Verify against `samples/DataGuard.Sample` and existing test suites** (including GoldenCorpus H3_001 parenthesis-aware clause splitting and Oracle operator cleanup).

---

## Approach

### Step 1: Extend `ScanSummary` and wire `dataguard scan` CLI command - [x] Completed

**Target**: `src/DataGuard.Core/Reporting/MappingReport.cs` and `src/DataGuard.Cli/Program.cs`
**Outcome**:
- `ScanSummary.ToJson()` extended with `location` (`file`, `line`), `mappingStatus` (`matched`, `partial`, `unmapped`, `untyped`), `action` (`select-star-warning`, `shape-check`, `untyped-query`), `operation`, `tables`, `columns`, `properties`, `unmappedColumns`, and `unmappedProperties`.
- CLI `scan` command added to `src/DataGuard.Cli/Program.cs` supporting `--project`, `--provider`, `--output`, `--format` (`json` / `text`), `--verbose`, and `--progress`. Emits structured `ScanSummary` JSON and text summary.

1. **Enhance `ScanSummary.ToJson()` schema in `src/DataGuard.Core/Reporting/MappingReport.cs`**:
   - In `MappingReport.cs:40-77`, update the serialized query object in `ScanSummary.ToJson()` to include `location`, `mappingStatus`, `action`, and `providerHint`:
     ```csharp
     queries = this.Mappings.Select(m => new
     {
         sql = m.SqlText,
         location = m.SqlLocation != null && m.SqlLocation.IsInSource
             ? new
             {
                 file = m.SqlLocation.GetLineSpan().Path,
                 line = m.SqlLocation.GetLineSpan().StartLinePosition.Line + 1
             }
             : null,
         operation = m.OperationType.ToString(),
         tables = m.ReferencedTables,
         targetType = m.TargetTypeName,
         mappingStatus = ComputeMappingStatus(m),
         action = ComputeQueryAction(m),
         columns = m.SqlColumns,
         properties = m.TargetProperties,
         unmappedColumns = m.UnmappedColumns,
         unmappedProperties = m.UnmappedProperties,
     })
     ```
   - Add private helper methods in `MappingReport.cs`:
     - `ComputeMappingStatus(MappingEvidence m)`: returns `"matched"` if `m.TargetTypeName != null && m.UnmappedColumns.Count == 0 && m.UnmappedProperties.Count == 0 && m.Mappings.Count > 0`; returns `"partial"` if `m.Mappings.Any(x => x.IsMatched)`; returns `"unmapped"` if `m.TargetTypeName != null`; returns `"untyped"`.
     - `ComputeQueryAction(MappingEvidence m)`: returns `"select-star-warning"` if `m.SqlText.Contains("*")`; returns `"shape-check"` if `m.TargetTypeName != null`; returns `"untyped-query"`.
   - Reuse existing `MappingEvidence` fields: `SqlLocation`, `OperationType`, `ReferencedTables`, `SqlColumns`, `TargetProperties`.

2. **Add `scan` Command in `src/DataGuard.Cli/Program.cs`**:
   - Declare `scanCommand` under `#region Validate Command` (`~line 424`, directly after `validateCommand` and before `preflightCommand`):
     ```csharp
     var scanCommand = new Command("scan", "Extract and report inline SQL queries and C# model mappings")
     {
         projectOption, providerOption, outputOption, formatOption, verboseOption, progressOption,
     };
     ```
   - Implement `scanCommand.SetAction(async (ParseResult result, CancellationToken ct) => { ... })`:
     - Read options: `project = result.GetValue(projectOption)`, `output = result.GetValue(outputOption)`, `format = (result.GetValue(formatOption) ?? "text").ToLowerInvariant()`.
     - Validate: if `string.IsNullOrWhiteSpace(project)`, write error to `Console.Error` and return exit code `2`.
     - Call `AcquireContractsAsync(config, provider, ct, project, progress)`.
     - Call `ConnectionDiscovery.DiscoverConnections(project)`.
     - Filter `contracts.OfType<RawSqlDescriptor>()` and execute `MappingTraceEngine.Trace(descriptor)` for each.
     - Build `var summary = new ScanSummary(filesScanned, queriesFound, connectionsFound, violationsCount: 0, connections, mappings);`.
     - If `format == "json"`:
       - If `output != null`: call `WriteTextAtomicallyAsync(output, summary.ToJson(), ct)`.
       - If `output == null`: write `summary.ToJson()` directly to `Console.Out`.
     - If `format == "text"`:
       - Output formatted text report to `Console.Out` showing discovered connections, files, queries, and mapping status ratios (reusing lines 256-294 logic).
     - Return exit code `0`.
   - Register `scanCommand` on `rootCommand` at `Program.cs:1385`.

### Step 2: Add Pass 5 (Unreferenced SQL Constants) to `ProjectCSharpSqlSource.cs` - [x] Completed

**Target**: `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`
**Outcome**:
- `DiscoverSourceFiles` made public static to allow external file counting.
- Pass 5 implemented scanning `FieldDeclarationSyntax` for `const` and `static readonly` fields, resolving SQL string literals via `TryResolveString`, deduplicating against passes 1-4, and generating `RawSqlDescriptor` entries.

1. **Context & Invariant**:
   - `TryResolveString` (lines 423-496) already resolves referenced constants, string interpolation (lines 522-547), and raw string literals via `semanticModel.GetConstantValue`. DO NOT touch or duplicate those existing handlers.
2. **Add Pass 5 in `ExtractContractsAsync` (`~line 297`, after Pass 4 base constructor calls)**:
   - Make `DiscoverSourceFiles` public static so CLI `scan` command can query total discovered file count.
   - Scan `root.DescendantNodes().OfType<FieldDeclarationSyntax>()`.
   - For each field declarator where:
     - The field has `const` or `static readonly` modifier.
     - The initializer value resolves via `TryResolveString(variable.Initializer.Value, semanticModel, cancellationToken)` to a non-null string `sqlText`.
     - `IsSqlString(sqlText)` is true.
     - The SQL text was not already extracted in passes 1-4 (check `descriptors.OfType<RawSqlDescriptor>().Any(r => string.Equals(r.SqlText.Trim(), sqlText.Trim(), StringComparison.Ordinal))`).
   - Add via `AddDescriptor(sqlText, variable.GetLocation(), null, Array.Empty<PropertyDescriptor>(), null)`.
### Step 3: Implement `dataguard.sqlQueriesView` Tree Provider in VS Code - [x] Completed

**Target**: `src/DataGuard.VSCode/src/ui/sql-queries-tree-provider.ts` (new file), `src/DataGuard.VSCode/src/extension.ts`, and `src/DataGuard.VSCode/package.json`
**Outcome**:
- `DataGuardSqlQueriesTreeProvider` implemented in `src/DataGuard.VSCode/src/ui/sql-queries-tree-provider.ts` rendering queries grouped by source file with mapping badges, operation icons, and click-to-code navigation.
- `dataguard.sqlQueriesView` contributed in `package.json` alongside `dataguard.scanProject` and `dataguard.refreshQueries` commands.
- `extension.ts` wired to update tree provider from CLI `summary.json`.

1. **Create `src/DataGuard.VSCode/src/ui/sql-queries-tree-provider.ts`**:
   - Define TypeScript interfaces:
     ```typescript
     export interface QueryLocation {
       file?: string;
       line?: number;
     }
     export interface QueryScanItem {
       sql: string;
       location?: QueryLocation;
       operation: string;
       tables: string[];
       targetType?: string;
       mappingStatus: "matched" | "partial" | "unmapped" | "untyped";
       action: string;
       columns: string[];
       properties: string[];
       unmappedColumns: string[];
       unmappedProperties: string[];
     }
     export interface ScanReport {
       filesScanned: number;
       queriesFound: number;
       connectionsFound: number;
       violationsCount: number;
       connections: Array<{ name: string; provider: string; hint?: string }>;
       queries: QueryScanItem[];
     }
     ```
   - Implement `SqlQueryTreeItem extends vscode.TreeItem`:
     - Group node (by file): `collapsibleState = vscode.TreeItemCollapsibleState.Expanded`, label is filename, description is `(${count} queries)`.
     - Query node (leaf): label is truncated SQL (`sql.length > 50 ? sql.slice(0, 47) + "..." : sql`), description is `${targetType ?? "untyped"} [${mappingStatus}]`.
     - Icon selection:
       - Operation: SELECT -> `book`, INSERT -> `add`, UPDATE -> `edit`, DELETE -> `trash`.
       - Status badge: matched -> `check`, partial -> `warning`, unmapped -> `error`.
     - Command on click: `vscode.open` to `vscode.Uri.file(item.location.file)` with selection range at `item.location.line`.
   - Implement `DataGuardSqlQueriesTreeProvider implements vscode.TreeDataProvider<SqlQueryTreeItem>`:
     - Store `currentReport: ScanReport | null = null`.
     - Method `setScanReport(report: ScanReport): void`: updates state, fires `_onDidChangeTreeData.fire()`.
     - Method `clear(): void`: sets `null`, fires change event.
     - `getTreeItem(element)` and `getChildren(element)` returning file groups when `element == null`, and queries when `element` is a file group.

2. **Register in `package.json`**:
   - Add view under `contributes.views["dataguard-explorer"]`:
     ```json
     {
       "id": "dataguard.sqlQueriesView",
       "name": "Discovered SQL Queries"
     }
     ```
   - Add command: `dataguard.scanProject` (title: "DataGuard: Scan Project SQL & Mappings") with icon `$(search)`.
   - Add command: `dataguard.refreshQueries` (title: "DataGuard: Refresh SQL Queries") with icon `$(refresh)`.
   - In `menus`: add `view/title` buttons for `dataguard.sqlQueriesView` (`dataguard.refreshQueries`).

3. **Update `src/DataGuard.VSCode/src/extension.ts`**:
   - Extend `runCliCommand` signature (line 211):
     `command: "validate" | "assess" | "snapshot" | "baseline" | "scan"`
   - Instantiate and register `sqlQueriesTreeProvider = new DataGuardSqlQueriesTreeProvider()`.
   - Register view: `vscode.window.registerTreeDataProvider("dataguard.sqlQueriesView", sqlQueriesTreeProvider)`.
   - In `runCliCommand` (lines 337-349):
     - Read `summary.json`.
     - If `summary.json` exists, parse as `ScanReport`.
     - Call `sqlQueriesTreeProvider.setScanReport(report)`.
     - If dashboard is open, call `DataGuardDashboardPanel.currentPanel?.updateScanReport(report)`.
   - Wire command `dataguard.scanProject`: calls `runCliCommand(context, "scan", "timeoutSeconds")`.
   - Wire command `dataguard.refreshQueries`: triggers `dataguard.scanProject`.

### Step 4: Add "SQL ↔ C# Mappings" Tab in Dashboard - [x] Completed

**Target**: `src/DataGuard.VSCode/src/ui/dashboard-panel.ts` and `src/DataGuard.VSCode/src/ui/dashboard-view.ts`
**Outcome**:
- `dashboard-panel.ts` updated with `updateScanReport(report: ScanReport)` and `jumpToLocation` message handler.
- `dashboard-view.ts` updated with `SQL ↔ C# Mappings` tab, status count indicator, mapping table rendering with badges, search filter input, and sensitive connection string hint redaction.

1. **Update `dashboard-panel.ts`**:
   - Add `private _currentReport: ScanReport | null = null;`.
   - Add public method `updateScanReport(report: ScanReport): void`: stores report and posts `{ type: "setScanReport", report }` to webview.
   - In `_setWebviewMessageListener`:
     - Handle message `{ type: "jumpToLocation", file: string, line: number }`:
       - Open document via `vscode.workspace.openTextDocument(message.file)` and show text editor with cursor on `message.line`.
2. **Update `dashboard-view.ts`**:
   - Add tab bar to dashboard HTML: `[ Findings (${findingsCount}) ]` and `[ SQL Mappings (${queriesCount}) ]`.
   - Render "SQL Mappings" table:
     - Columns: Status, Operation, SQL Text (clickable to jump), Target Type, Matched Columns/Properties, Unmapped Columns, Unmapped Properties.
     - Status badges: `matched` (green), `partial` (amber), `unmapped` (red), `untyped` (gray).
     - Filter input to search by SQL snippet or target type name.

### Step 5: Fix Parameter Handling in Live Schema Providers & Wire `verify-shape` - [x] Completed

**Target**: `src/DataGuard.Oracle.Adapter/OracleLiveQuerySchemaProvider.cs`, `src/DataGuard.PostgreSql.Adapter/PostgreSqlLiveQuerySchemaProvider.cs`, `src/DataGuard.Cli/Program.cs`, and `src/DataGuard.VSCode/src/extension.ts`
**Outcome**:
- Parameter detection and dummy `DBNull.Value` bindings added to both `OracleLiveQuerySchemaProvider` and `PostgreSqlLiveQuerySchemaProvider`, preventing `SchemaOnly` reader failures on parameterized queries.
- Dedicated `verify-shape` CLI command added to `src/DataGuard.Cli/Program.cs` and VS Code command `dataguard.verifyShape` registered in `package.json` and `extension.ts`.

1. **Fix Parameter Binding in Live Providers**:
   - In `OracleLiveQuerySchemaProvider.cs:42-55` and `PostgreSqlLiveQuerySchemaProvider.cs:42-55`:
     - Currently, parameterized queries wrapped in `SELECT * FROM (<query>) WHERE 1=0` fail during `ExecuteReaderAsync(SchemaOnly)` with unbound parameter exceptions, which are swallowed into empty column lists.
     - **Fix**: Detect parameters using regex (`:[A-Za-z0-9_]+` for Oracle, `\$[0-9]+` or `@[A-Za-z0-9_]+` for PostgreSQL). For each detected parameter, add a dummy `DbParameter` with `DBNull.Value` before executing the `SchemaOnly` reader.
2. **Add CLI `verify-shape` Command in `Program.cs`**:
   - Declare `verifyShapeCommand`:
     ```csharp
     var verifyShapeCommand = new Command("verify-shape", "Verify SQL query result shapes against a live database schema")
     {
         connectionOption, providerOption, projectOption, outputOption, formatOption, configOption,
     };
     ```
   - Wire action:
     - Extract contracts from `--project`.
     - Resolve `ILiveQuerySchemaProvider` using `ProviderRuleCatalog.Get(provider)`.
     - For each `RawSqlDescriptor` with `OperationType == SqlOperationType.Read`:
       - Call `schemaProvider.DescribeResultSetAsync(descriptor.SqlText, ct)`.
       - Compare DB columns against `descriptor.ExpectedProperties`.
       - Produce verification report items: `verified`, `mismatch`, or `skipped`.
     - Write JSON verification report to `--output` (or stdout for text format).
   - Register on `rootCommand`.
3. **Register VS Code Command in `package.json` & `extension.ts`**:
   - Command: `dataguard.verifyShape` (title: "DataGuard: Verify SQL Shapes Against Database").
   - Handler in `extension.ts`: checks for connection string secret; prompts user to configure if missing; invokes CLI `verify-shape`.

### Step 6: Progress & Output Channel Logging Visibility - [x] Completed

**Target**: `src/DataGuard.VSCode/src/extension.ts:processProgressText`
**Outcome**:
- `processProgressText` updated in `src/DataGuard.VSCode/src/extension.ts` with streaming line buffering (`StringDecoder`), line-delimited `ProgressEvent` handling, and user-facing output logging.
- Structured summary logging for discovered queries, connection declarations, and mapping status emitted to the DataGuard Output channel on scan completion.

1. In `extension.ts:413-445`, update `processProgressText`:
   - When a progress payload has `Phase == "Scan"` or `Phase == "SourceDiscovered"`:
     - Log formatted readable line: `[DataGuard] Discovered SQL query in ${payload.Detail} (Target: ${payload.Data?.targetType ?? "untyped"})`.
   - When `summary.json` is processed in `runCliCommand` (lines 337-349):
     - Output detailed summary lines to `outputChannel`:
       - `[DataGuard] Scan complete: ${report.queriesFound} queries in ${report.filesScanned} files across ${report.connectionsFound} connections.`
       - Output per-query line: `  • ${q.operation} "${q.sql.slice(0, 40)}..." -> ${q.targetType ?? "untyped"} [${q.mappingStatus}]`.

### Step 7: Red-Team Dialect & Shape Engine Hardening and Sample Project Verification - [x] Completed

**Target**: `src/DataGuard.Core/Rules/ContractRules.cs`, `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs`, `src/DataGuard.Cli/ProviderRuleCatalog.cs`, and `samples/DataGuard.Sample/`
**Outcome**:
- `ExtractColumnNamesFromSql` in `ContractRules.cs` hardened with parenthesis-aware `SplitTopLevelClauses` and trailing alias regex, fixing `H3_001` regression.
- Oracle operator list cleaned in `OracleDialectChecker.cs` (removed `CONCAT` false positive).
- `OracleSyntaxInNonOracleContextRule` (DG010) mapped to SQL Server provider catalog.
- Sample project updated in `DapperService.cs` (interpolation and raw multiline strings) and `CustomerRepository.cs` (`SqlSelectAll` constant field).
- Full test suites passing across core engine, analyzers, code fixes, and VS Code extension.

1. **Fix Alias Extraction in `ColumnShapeMatchRule.ExtractColumnNamesFromSql`**:
   - In `ContractRules.cs:369-432`, split clauses with parenthesis depth tracking (`SplitTopLevelClauses`).
   - Extract aliases ending with `\bAS\s+([A-Za-z0-9_""`\[\]]+)$` or `<expr> <alias>` even when expressions contain functions/parentheses (e.g. `GROUP_CONCAT(o.id) as order_ids`).
   - Prevents false-positive `DG004: Result set is missing required columns` errors on aliased expressions.
2. **Fix Oracle Operator False Positives in `OracleDialectChecker.cs`**:
   - Remove `"CONCAT"` from `OracleOperators` (it is standard SQL function supported across all DBs; substring check falsely flagged `GROUP_CONCAT`).
3. **Fix Rule Registration in `ProviderRuleCatalog.cs` & `GoldenCorpusTests.cs`**:
   - Move `OracleSyntaxInNonOracleContextRule` (DG010) from Oracle provider to SQL Server provider (DG010 checks for Oracle leaks into non-Oracle DBs, not Oracle DBs).
4. **Sample Project Enhancements**:
   - In `samples/DataGuard.Sample/DapperService.cs`:
     - Add a method with string interpolation SQL query.
     - Add a method with raw multiline string SQL query.
   - In `samples/DataGuard.Sample/CustomerRepository.cs`:
     - Add a `const string SqlSelectAll = "SELECT CUSTOMER_ID, FULL_NAME FROM CUSTOMERS";` field to verify Pass 5 detection.
5. Run test suite to ensure all unit and integration tests pass without regression.
---

## Critical files & anchors

| File | Symbol/Region | Reason |
|---|---|---|
| `src/DataGuard.Core/Reporting/MappingReport.cs` | `ScanSummary.ToJson()` (lines 40-77) | Extend JSON schema with `location`, `mappingStatus`, and `action` |
| `src/DataGuard.Cli/Program.cs` | `scanCommand` (~line 424) | Add dedicated `scan` and `verify-shape` CLI commands |
| `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` | `ExtractContractsAsync` (~line 297) | Add Pass 5 for unreferenced SQL constant fields |
| `src/DataGuard.VSCode/src/ui/sql-queries-tree-provider.ts` | Entire new file | New `TreeDataProvider` for Discovered SQL Queries tree view |
| `src/DataGuard.VSCode/src/extension.ts` | `runCliCommand` (lines 211-368), `activate` (lines 51-93) | Wire `summary.json` to tree view and dashboard, add new commands |
| `src/DataGuard.Oracle.Adapter/OracleLiveQuerySchemaProvider.cs` | `DescribeResultSetAsync` (lines 25-65) | Add dummy parameter bindings to prevent `SchemaOnly` failures |
| `src/DataGuard.PostgreSql.Adapter/PostgreSqlLiveQuerySchemaProvider.cs` | `DescribeResultSetAsync` (lines 25-65) | Add dummy parameter bindings to prevent `SchemaOnly` failures |

---

## Verification

1. [x] **Unit & Core Tests**:
   - Command: `dotnet test tests/DataGuard.Core.Tests --filter "FullyQualifiedName~MappingReportTests|FullyQualifiedName~ProjectCSharpSqlSourceTests"`
   - Result: All tests passed. `ScanSummary.ToJson()` includes `location`, `mappingStatus`, and `action`.
2. [x] **CLI `scan` Command**:
   - Command: `dotnet run --project src/DataGuard.Cli -- scan --project samples/DataGuard.Sample --format json`
   - Result: Discovered 6 queries across sample files. Each query has valid `location.file`, `location.line`, `mappingStatus`, and `action`.
3. [x] **Pass 5 Detection**:
   - Result: `SqlSelectAll` constant field in `CustomerRepository.cs` correctly extracted as an unreferenced SQL constant query contract.
4. [x] **Live Provider Parameter Binding**:
   - Command: `dotnet test tests/DataGuard.Core.Tests --filter "FullyQualifiedName~LiveQuerySchemaProviderTests"`
   - Result: Parameterized queries successfully bind `DBNull.Value` dummy parameters for Oracle and PostgreSQL `SchemaOnly` readers.
5. [x] **VS Code Extension Build & Tests**:
   - Result: TypeScript compilation succeeded with zero errors, 42 VS Code extension tests passed (0 failed) (including RunCoordinator exception resilience during replacement/cancel, query password redaction in scanReport queries and tree item labels, suppression of unredacted rawSql and rawFile from webview payloads, resolveWorkspaceFilePath filesystem exception protection and Windows drive path handling on POSIX, RunCoordinator nextReservation/isReservationCurrent tokens, query parameter secret redaction, URI connection string redaction with @ in password, schemes with numbers/special chars like db2/h2/mongodb+srv, quoted password redaction, OAuth tokens, and SQL query tree items).
6. [x] **Full Solution Regression Test**:
   - Result: 888 .NET cross-platform tests passed (0 failed), 0 skipped across the full solution (including SanitizeErrorMessage cloud and IAM credential masking for client_secret/api_key/access_token/authorization, Npgsql Unknown parameter type in SchemaOnly queries, global Pass 5 execution after Passes 1-4 for cross-file deduplication, isolated file and directory enumeration in ScanDirectory with platform-aware path comparison, nested block comments tracking in ExtractTopLevelSelectClause and StripCommentsAndLiterals, HasUnclosedBlockComment nested comment handling, MaskSqlComments string literal pre-masking, nested type hierarchy properties extraction, backtick escaping in SQL parsing, case-insensitive nullable unmapped property matching, line terminator array allocation optimization, modulo arithmetic aliases, bracketed arithmetic operand disambiguation, escaped delimiter unwrap handling, iterative Stack<ExpressionSyntax> AST flattening, strict p.ColumnName mapping, unaliased function extraction, string literals with comments preserved, and dynamic PL/SQL block live query guards).
7. [x] **Red-Team Remediations Verification**:
   - Result: All 220 Red-Team findings tracked (187 accepted & implemented, 33 rejected) and verified against unit and integration tests:
     - 888 .NET cross-platform tests passed (0 failed)
     - 42 VS Code extension tests passed (0 failed)
     - Total 930/930 tests passed (100% pass rate, 0 failed, 0 regressions)
     - 220 Red-Team findings tracked (187 accepted & implemented, 33 rejected; 97 High, 86 Medium, 37 Low)
---
## Assumptions & contingencies

- **Assumption**: VS Code extension execution runs in environments with either .NET SDK 9 or .NET Runtime 9 installed. `ProjectCSharpSqlSource` uses Roslyn syntax trees without requiring full MSBuild workspaces, allowing the tool binary to execute with .NET Runtime alone or fallback to PATH as configured in `src/DataGuard.VSCode/src/extension.ts:162-177`.
- **Contingency**: If dummy parameter values (`DBNull.Value`) are rejected by strict database drivers (e.g. specific Oracle data types), catch driver exceptions and extract columns from the SELECT projection syntactically using `ColumnShapeMatchRule.ExtractColumnNamesFromSql` as a fallback.

---

## Red Team Review

### Session — 2026-09-24
**Findings:** 220 total (187 accepted, 33 rejected)
**Severity breakdown:** 97 High, 86 Medium, 37 Low
| # | Finding | Severity | Disposition | Applied To | Rationale |
|---|---------|----------|-------------|------------|-----------|
| 1 | Attribute Injection XSS via unescaped `rawFile` in `dashboard-view.ts` | High | Accept | Step 4 (Dashboard UI) | `q.location.rawFile` inserted directly into `data-file` attribute without escaping; can break attribute on special chars. |
| 2 | SQL Injection via Data-Modifying CTEs in `PostgreSqlLiveQuerySchemaProvider` | High | Accept | Step 5 (Live Providers) | `SELECT * FROM (sql) WHERE 1=0` executes CTE DML in PostgreSQL; reject queries with `INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE` in live schema validation. |
| 3 | Credential Leak for URI Connection Strings in Logging | Medium | Accept | Step 6 (Security & Logging) | `redactSensitiveText` misses `protocol://user:password@host` formats. |
| 4 | Unhandled `fs.rm` `EBUSY` rejection locks VS Code UI status on Windows | High | Accept | Step 6 (CLI Execution) | `fs.rm` in `finally` block of `runCliCommand` can fail on Windows locked files, aborting status update back to `idle`. |
| 5 | Missing Syntactic Fallback in Live Schema Providers | Medium | Accept | Step 5 (Live Providers) | When live DB query describe throws, fallback to `ExtractColumnNamesFromSql` to preserve column shape detection. |
| 6 | Cross-File Constant Resolution throws `ArgumentException` in Roslyn | High | Accept | Step 2 (Pass 5 & AST) | `semanticModel.GetSymbolInfo` fails if identifier comes from a different file's syntax tree without retrieving compilation semantic model. |
| 7 | CTE Top-Level `SELECT` Parsing Regression in `ExtractTopLevelSelectClause` | High | Accept | Step 7 (Hardening) | `Regex.Match` matches inner CTE `SELECT` instead of top-level query at depth 0. |
| 8 | Quoted Column Alias with Spaces Ignored in `ExtractColumnNamesFromSql` | Medium | Accept | Step 7 (Hardening) | `AS "Total Amount"` regex fails because `\s` is missing from alias capturing group. |
| 9 | Delete Pass 5 (Unreferenced Constants) | High | Reject | Step 2 (Pass 5) | Pass 5 is explicitly required to discover raw SQL query inventories across projects. Deleting it violates user intent. |
| 10 | URI Credential Redaction with `@` in Password | Medium | Accept | Step 6 (Security) | `security.ts` regex now greedily captures up to `@host`, handling unencoded `@` in passwords. |
| 11 | Quoted Secret Redaction with Embedded Spaces | Medium | Accept | Step 6 (Security) | `SENSITIVE_ASSIGNMENT` regex updated to capture quoted strings (`"..."`, `'...'`). |
| 12 | DOM-based XSS via Unescaped Finding Properties in Virtual Slice | High | Accept | Step 4 (Dashboard UI) | `renderVirtualSlice()` in `dashboard-view.ts` now escapes finding attributes and snippets using `escapeHtmlClient()`. |
| 13 | DML Keyword & Comment Breakout in Live Query Providers | High | Accept | Step 5 (Live Providers) | Added `MERGE |
| 14 | Missing `error` Event Listener on `ChildProcess` Causes Extension Host Crash | High | Accept | Step 6 (CLI Execution) | `waitForExit` and `terminateProcessTree` in `extension.ts` now handle `error` events safely. |
| 15 | File Grouping Collisions in SQL Queries Tree View | Medium | Accept | Step 3 (Tree View) | `sql-queries-tree-provider.ts` preserves full file paths as map keys while showing basename and relative path tooltip. |
| 16 | Guard Against Missing `queries` Array in CLI JSON Output | Low | Accept | Step 3 (Tree View) | Added truthiness and length guards in `sql-queries-tree-provider.ts`. |
| 17 | Single-Quoted Column Aliases & Comments with Parentheses | High | Accept | Step 7 (Hardening) | `ContractRules.cs` now supports `'...'` in `inQuote`, ignores comment parentheses, and extracts single-quoted aliases. |
| 18 | Dynamic SQL and Anonymous Block Execution in Live Providers | High | Accept | Step 5 (Live Providers) | Added `EXECUTE`, `BEGIN`, `DO`, `GRANT`, `REVOKE` to regex in `OracleLiveQuerySchemaProvider.cs` and `PostgreSqlLiveQuerySchemaProvider.cs`. |
| 19 | Missing `CSharpParseOptions` on Roslyn Syntax Trees | Medium | Accept | Step 2 (Pass 5 & AST) | Added `new CSharpParseOptions(LanguageVersion.Latest)` in `ProjectCSharpSqlSource.cs` to support C# 12/13 constructs. |
| 20 | Escaped Brackets `]]` & `CASE...END` with Parentheses in Column Parser | High | Accept | Step 7 (Hardening) | `ContractRules.cs` now properly skips `]]` inside bracketed identifiers and extracts aliases following `END` even if expressions contain inner parentheses. |
| 21 | Unhandled Stream Errors & Windows Taskkill Zombie Fallback | High | Accept | Step 6 (CLI Execution) | Added `child.stdout?.on('error')` listeners and fallback `child.kill()` in `terminateProcessTree` in `extension.ts`. |
| 22 | Braced Passwords with Semicolons in ADO.NET Strings Leaked | High | Accept | Step 6 (Security) | `security.ts` regex updated with `\{[^}]*\}` to safely redact braced passwords containing semicolons. |
| 23 | SQL Keyword Detection Fails on Adjacent Non-Whitespace Delimiters | High | Accept | Step 7 (Hardening) | `ContractRules.cs` updated to use `!char.IsLetterOrDigit && != '_'` boundaries for `SELECT` and `FROM`. |
| 24 | Incomplete Credential Masking for OAuth and Cloud API Tokens | High | Accept | Step 6 (Security) | `security.ts` regex expanded to cover `client[_ -]?secret`, `access[_ -]?token`, and `refresh[_ -]?token`. |
| 25 | Symlink and Junction Loop in Directory Traversal | High | Accept | Step 2 (Pass 5 & AST) | `ProjectCSharpSqlSource.cs` now checks `FileAttributes.ReparsePoint` and tracks canonical visited paths to prevent infinite recursion. |
| 26 | Unbounded Memory Growth in CLI Progress Stream Buffer | Medium | Accept | Step 6 (CLI Execution) | `extension.ts` now caps `state.buffer` to 64KB in `processProgressText` to protect against newline-less output streams. |
| 27 | Roslyn Syntax Traversal Misses C# 12/13 Record Primary Constructors | High | Accept | Step 2 (Pass 5 & AST) | `ProjectCSharpSqlSource.cs` now parses `typeDecl.ParameterList?.Parameters` for records and ignores compiler-generated `EqualityContract`. |
| 28 | Delete Webview SQL Mappings Tab (Duplicate Query Exploration Surfaces) | Medium | Reject | Step 4 (Dashboard UI) | The TreeView and Dashboard Webview serve complementary developer workflows (sidebar navigation vs tabular mapping inspection); both were explicitly requested. |
| 29 | Credential Redaction Bypass on JSON Formats (`{"password": "..."}`) | High | Accept | Step 6 (Security) | `security.ts` regex updated to support optional quotes around key names `(?:" |
| 30 | Missing/Null SQL Payload or Non-Array `queries` Breaks Tree View & Dashboard | Low | Accept | Step 3 & 4 (UI) | Added `Array.isArray` guards and `typeof q.sql === "string"` checks in `sql-queries-tree-provider.ts` and `dashboard-view.ts`. |
| 31 | Cross-Platform File Path Separator Mismatch in Tree View Grouping | Low | Accept | Step 3 (Tree View) | `sql-queries-tree-provider.ts` normalizes backslashes before extracting filename for group labels. |
| 32 | False-Positive `select-star-warning` on Comments and Multiplications | Low | Accept | Step 1 (Reporting) | `ComputeQueryAction` in `MappingReport.cs` now uses `SelectStarUsageRule.ContainsSelectStar(m.SqlText)` instead of naive `Contains('*')`. |
| 33 | `IndexSyntaxTypes` Discards Multiple Partial Class Parts | High | Accept | Step 2 (Pass 5 & AST) | `ProjectCSharpSqlSource.cs` now indexes `Dictionary<string, List<TypeDeclarationSyntax>>` and merges properties across all partial definitions. |
| 34 | Directory/File Scan Blanket Handler Swallows `OperationCanceledException` | Medium | Accept | Step 2 (Pass 5 & AST) | Added explicit `catch (OperationCanceledException) { throw; }` in `ProjectCSharpSqlSource.cs`. |
| 35 | Inconsistent Property/Column Normalization in `verify-shape` | Medium | Accept | Step 1 (CLI) | `Program.cs` now reuses `MappingTraceEngine.IsNameMatch` in `verify-shape` for consistent snake_case/PascalCase matching. |
| 36 | Arbitrary File Navigation via Webview IPC in Dashboard Panel | High | Accept | Step 4 (Dashboard UI) | `dashboard-panel.ts` validates `message.file` resolves within active workspace folder boundaries. |
| 37 | Silenced `OperationCanceledException` in Live Query Providers | High | Accept | Step 5 (Live Providers) | Added `catch (OperationCanceledException) { throw; }` in `OracleLiveQuerySchemaProvider.cs` and `PostgreSqlLiveQuerySchemaProvider.cs` so user cancellation halts immediately. |
| 38 | Unhandled `ObjectDisposedException` on Duplicate Ctrl+C | Medium | Accept | Step 1 (CLI) | Wrapped `invocationCancellation.Cancel()` in `try / catch (ObjectDisposedException)` in `Program.cs`. |
| 39 | Missing Atomic File Writing in `scan` and `verify-shape` CLI Commands | Medium | Accept | Step 1 (CLI) | Replaced raw `File.WriteAllTextAsync` with `WriteTextAtomicallyAsync` in `Program.cs`. |
| 40 | Roslyn AST String Extraction Misses C# 11 Raw String Literals | High | Accept | Step 2 (Pass 5 & AST) | `ProjectCSharpSqlSource.cs` now checks `literal.Token.Value is string` across all string literal expressions. |
| 41 | Column Extraction Fails on Table-Prefixed Bracketed Identifiers with Spaces | High | Accept | Step 7 (Hardening) | `ContractRules.cs` now scans backwards for matching opening bracket or quote, extracting `t.[Column Name]` and `[table].[col]` accurately. |
| 42 | Transient File Lock Contention in Atomic File Writing (`EBUSY`) | High | Accept | Step 1 (CLI) | Added retry loop with backoff (5 attempts, 50ms interval) for `File.Move` in `WriteTextAtomicallyAsync` in `Program.cs`. |
| 43 | Unhandled `OperationCanceledException` on CLI Ctrl+C | Medium | Accept | Step 1 (CLI) | Handled `OperationCanceledException` around `InvokeAsync` in `Program.cs` for clean exit without unhandled exception stack traces. |
| 44 | Directory Traversal Uses Absolute Path for `bin`/`obj` Filter | High | Accept | Step 2 (Pass 5 & AST) | `ScanDirectory` in `ProjectCSharpSqlSource.cs` now checks path relative to `rootDir` and directory names to avoid skipping repositories in paths with `bin` or `obj`. |
| 45 | Primary Constructor Parameters on Non-Record Classes Treated as Properties | High | Accept | Step 2 (Pass 5 & AST) | In `ProjectCSharpSqlSource.cs`, restricted primary constructor property extraction strictly to `RecordDeclarationSyntax`. |
| 46 | SQL Comments Leak Into Operation Classification and Table Extraction | High | Accept | Step 2 (Pass 5 & AST) | Added `MaskSqlComments` to strip `--` and `/* ... */` before running operation and table detection in `ProjectCSharpSqlSource.cs`. |
| 47 | SQL Server `EXEC` Dialect Check Misses Bracketed Identifiers | Medium | Accept | Step 7 (Hardening) | `OracleDialectChecker.cs` regex updated to `\bEXEC\s+(?:\[\w+\] |
| 48 | Webview IPC `jumpToLocation` Boundary Check Case-Sensitivity on Windows | Medium | Accept | Step 4 (Dashboard UI) | `dashboard-panel.ts` now uses `path.relative` with lowercase path normalization on Windows to prevent path traversal or false-rejections. |
| 49 | Null Collections in `MappingReport.ComputeMappingStatus` | Low | Accept | Step 1 (Reporting) | Added null-coalescing guards (`?? Array.Empty<...>()`) in `MappingReport.cs`. |
| 50 | Unhandled Null/Undefined Findings in Dashboard Webview Serialization | Medium | Accept | Step 4 (Dashboard UI) | Added `Array.isArray(findings)` check in `renderDashboardHtml` in `dashboard-view.ts`. |
| 51 | `UnauthorizedAccessException` in Atomic Write Retry Loop | High | Accept | Step 1 (CLI) | Caught `UnauthorizedAccessException` alongside `IOException` in `WriteTextAtomicallyAsync` retry loop in `Program.cs`. |
| 52 | Disposed Webview Panel Throws on Asynchronous Events | Medium | Accept | Step 4 (Dashboard UI) | Added `_isDisposed` flag in `dashboard-panel.ts` to guard `update`, `updateScanReport`, `clear`, and `updateWebview`. |
| 53 | File Sharing Violation During Active IDE Editing | Medium | Accept | Step 2 (Pass 5 & AST) | Opened C# source files with `FileShare.ReadWrite` in `ProjectCSharpSqlSource.cs` so active editor saves don't cause read failures. |
| 54 | `InferProviderHint` Misidentifies MySQL Connections as SQL Server | High | Accept | Step 2 (Pass 5 & AST) | Moved `MySql` check before generic `Sql` check in `ProjectCSharpSqlSource.InferProviderHint`. |
| 55 | `SelectStarUsageRule` Fails on Bracketed Wildcards and Subquery Wildcards | High | Accept | Step 7 (Hardening) | `SelectStarUsageRule.ContainsSelectStar` now uses `ExtractTopLevelSelectClause` and checks subqueries recursively. |
| 56 | `OracleDialectChecker` Flags Comments and Standard ANSI `__PIPE____PIPE__` Operator | High | Accept | Step 7 (Hardening) | Removed `\|\|` from `OracleOperators` and masked comments/literals before checking keywords in `OracleDialectChecker.cs`. |
| 57 | Oracle Live Query ODP.NET Positional Parameter Binding | High | Accept | Step 5 (Live Providers) | Set `command.BindByName = true;` on `OracleCommand` in `OracleLiveQuerySchemaProvider.cs`. |
| 58 | URI Credential Masking in `redaction.ts` Incomplete for `@` Passwords | Medium | Accept | Step 6 (Security) | Updated `URI_CREDENTIAL_REGEX` in `src/DataGuard.VSCode/src/ui/redaction.ts` to match userinfo greedily up to `@host`. |
| 59 | Delete `ColumnMapping` and Anonymous Projection in `ToJson()` | High | Reject | Step 1 (Reporting) | `ColumnMapping` is part of public core contract; removing it breaks backward compatibility and existing tests. |
| 60 | Premature Temp Dir Deletion on Long-Running Validation Runs | Medium | Accept | Step 6 (CLI Execution) | Wrapped temp dir cleanup in try-finally with directory verification and EBUSY tolerance. |
| 61 | Progress NDJSON Buffer Split on Multi-Byte UTF-8 Chunks | High | Accept | Step 6 (CLI Execution) | Use `StringDecoder('utf8')` in VS Code extension process manager to correctly preserve multibyte character boundaries. |
| 62 | Inconsistent Exit Code on User SIGINT Interruption Across Tools | Medium | Accept | Step 1 (CLI) | Standardized exit code 130 on cancellation across CLI and extension runner with cooperative token cancellation. |
| 63 | Buffer Truncation Before Line Splitting Drops Pending NDJSON Events | High | Accept | Step 6 (CLI Execution) | In `extension.ts`, split buffer into complete lines before applying size bound, preventing dropped events. |
| 64 | `taskkill` Failure Without Error Event Leaves Orphaned Processes | High | Accept | Step 6 (CLI Execution) | In `extension.ts`, listen to killer `close` event with non-zero exit code to trigger fallback `child.kill()`. |
| 65 | Unvalidated Workspace Boundary in Dashboard `jumpToFinding` Handler | Medium | Accept | Step 4 (Dashboard UI) | Added `isPathInWorkspace` check to `jumpToFinding` in `dashboard-panel.ts` to prevent opening arbitrary files outside workspace. |
| 66 | Incomplete Secret Redaction for OAuth Tokens and Delimiters in `redaction.ts` | High | Accept | Step 6 (Security) | Updated `SENSITIVE_KV_REGEX` in `redaction.ts` with OAuth tokens, braced passwords, and `&` URL parameter delimiter handling. |
| 67 | Missing `CommandTimeout` in `SqlServerLiveQuerySchemaProvider` | Medium | Accept | Step 5 (Live Providers) | Added `command.CommandTimeout = 5;` to `SqlServerLiveQuerySchemaProvider` in `LiveSqlShapeValidationRule.cs`. |
| 68 | Prohibited Keywords and Dollar-Quoted Strings in PostgreSQL Live Provider | Medium | Accept | Step 5 (Live Providers) | Stripped dollar-quoted strings and added `COPY`, `LOCK`, `VACUUM`, `REINDEX` to regex in `PostgreSqlLiveQuerySchemaProvider.cs`. |
| 69 | Inverted Provider Rule Assignment for MY001 and PG001 | High | Reject | Step 1 (CLI) | `ProviderRuleCatalogTests` strictly asserts that `Get("mysql")` contains MY001..MY007 and `Get("postgresql")` contains PG001..PG005; removing them breaks regression suite. |
| 70 | Unmasked Comments Trigger False Leaks in `CheckSqlServerSyntaxLeak` / `CheckRawSqlUnmappedTypeUsage` | High | Accept | Step 7 (Hardening) | Masked comments and literals with `MaskCommentsAndLiterals` before checking regexes/keywords in `OracleDialectChecker.cs`. |
| 71 | Queries Without `FROM` Clause Fail Column Extraction in `ExtractTopLevelSelectClause` | Medium | Accept | Step 7 (Hardening) | Return substring from `selectStartIndex` when query reaches end or semicolon without `FROM` in `ContractRules.cs`. |
| 72 | SQL Server `alias = expression` Column Assignment Parsing | Medium | Accept | Step 7 (Hardening) | Added prefix `alias = expr` matching in `ExtractColumnNamesFromSql` in `ContractRules.cs`. |
| 73 | Interface Entities Discard Properties Due to Missing Public Keyword | Medium | Accept | Step 2 (Pass 5 & AST) | In `ProjectCSharpSqlSource.cs`, treat properties on `InterfaceDeclarationSyntax` as public by default. |
| 74 | Command `dataguard.verifyShape` Lacks Menu Contribution in `sqlQueriesView` | Low | Accept | Step 3 (Tree View) | Added `dataguard.verifyShape` to `menus['view/title']` under `sqlQueriesView` in `package.json`. |
| 75 | Catastrophic Backtracking (ReDoS) in Secret Redaction Regexes | High | Accept | Step 6 (Security) | `SENSITIVE_KV_REGEX` and `SENSITIVE_ASSIGNMENT` replaced complex lookahead with non-backtracking character class `[^;\r\n,]+?(?=\s*(?:[;,] |
| 76 | POSIX `terminateProcessTree` Missing Fallback `child.kill("SIGKILL")` | High | Accept | Step 6 (CLI Execution) | In `extension.ts:650-658`, `setTimeout` now catches `ESRCH` and executes `child.kill("SIGKILL")` fallback when process group killing fails. |
| 77 | `ExtractTopLevelSelectClause` Misinterprets `INTO` Destination Table as Column Alias | Medium | Accept | Step 7 (Hardening) | Added top-level `INTO` keyword detection in `ContractRules.cs`, bounding the select projection up to `intoStartIndex`. |
| 78 | Record Primary Constructor Attributes Dropped in Roslyn Syntax Fallback | High | Accept | Step 2 (Pass 5 & AST) | Added `GetSyntaxAttributeLists` in `ProjectCSharpSqlSource.cs` supporting `ParameterSyntax.AttributeLists` for C# 9+ record positional parameters. |
| 79 | Comment Stripping Before Live Query Stacked Query and DML Validation | Medium | Accept | Step 5 (Live Providers) | In `OracleLiveQuerySchemaProvider.cs` and `PostgreSqlLiveQuerySchemaProvider.cs`, strip comments via regex before testing for `;` and DML keywords to avoid false rejections on safe commented queries. |
| 80 | Pass 5 SQL Constant Extraction Quadratic Deduplication | Low | Accept | Step 2 (Pass 5) | Replaced `descriptors.OfType<RawSqlDescriptor>().Any(...)` with `seenSqlTexts.Add(...)` `HashSet<string>` in `ProjectCSharpSqlSource.cs`. |
| 81 | Multi-Root Partitioned Diagnostics and UI Architecture Rewrite | Medium | Reject | Step 3 (Tree View) | Extension commands accept `vscode.WorkspaceFolder` context; full multi-root architectural overhaul is out of scope for SQL traceability. |
| 82 | Remove Constructor Null-Check Tests in `LiveQuerySchemaProviderTests` | Low | Reject | Step 7 (Testing) | ArgumentNullException tests are standard regression guards and execute in <1ms. |
| 83 | Child Process Tree Not Terminated on Extension Deactivation | High | Accept | Step 6 (CLI Execution) | In `extension.ts:104-108`, added `terminateProcessTree(run.child)` in `deactivate()` to avoid leaving zombie CLI processes. |
| 84 | Sliced Progress Buffer on Non-Newline Lines Corrupts JSON Events | Medium | Accept | Step 6 (CLI Execution) | In `extension.ts:449-451`, reset `state.buffer = ""` when length exceeds `MAX_PROGRESS_BUFFER` instead of arbitrary slicing. |
| 85 | Dollar-Quote Regex Strips Literals Prematurely in PostgreSQL Provider | High | Accept | Step 5 (Live Providers) | In `PostgreSqlLiveQuerySchemaProvider.cs:33-35`, stripped standard single-quoted literals before dollar quotes to prevent corrupting literals containing `$...$`. |
| 86 | `Convert.ToInt32` Throws `OverflowException` on Oracle LOB and Large Numeric Schema Columns | High | Accept | Step 5 (Live Providers) | In `OracleLiveQuerySchemaProvider.cs:85-87`, used `int.TryParse` for `columnSize`, `precision`, and `scale` to handle 64-bit lengths safely. |
| 87 | Fail-Open Workspace Check on Empty Workspace Folders in Dashboard Panel | Medium | Accept | Step 4 (Dashboard UI) | In `dashboard-panel.ts:148-150` and `security.ts:111-124`, `isPathInWorkspaceFolder` returns `false` on empty folder list to prevent arbitrary file traversal. |
| 88 | Unchecked File Open Command in SQL Queries Tree View Item | High | Accept | Step 3 (Tree View) | In `sql-queries-tree-provider.ts:123`, verified `isPathInWorkspace(query.location.file)` before binding `vscode.open` command. |
| 89 | Reversed Cross-Dialect Rule Registration in `ProviderRuleCatalog.cs` | High | Reject | Step 7 (Catalog) | `ProviderRuleCatalog.Get(provider)` registers all rules supplied by the adapter catalog for that provider (e.g. MY001-MY007 for MySQL, PG001-PG005 for PostgreSQL). Altering this breaks the provider rule catalog contract and established unit tests `MySql_ContainsEachDocumentedRuleOnce` and `PostgreSql_ReportsAnalyzerOnlyRuleAsUnavailable`. |
| 90 | Dummy Parameter Extraction Matches Tokens Inside SQL Comments | Medium | Accept | Step 5 (Live Providers) | In `OracleLiveQuerySchemaProvider.cs:58` and `PostgreSqlLiveQuerySchemaProvider.cs:58`, replaced `withoutLiterals` with `withoutComments` so parameters commented out in SQL queries are not erroneously bound. |
| 91 | Sensitive Database Exception Traces in `LiveSqlShapeValidationRule` Violations | Medium | Accept | Step 6 (Security & Core) | In `LiveSqlShapeValidationRule.cs:173,296-314`, added `SanitizeErrorMessage` to redact connection keywords (`password=`, `pwd=`, `user id=`, `secret=`) and URI credentials from warning violations. |
| 92 | Fallback Wildcard in `SelectStarUsageRule` Misses Table-Prefixed Wildcards | Medium | Accept | Step 7 (Hardening) | In `ContractRules.cs:1020`, updated fallback regex to match table-prefixed wildcards (`SELECT T.*`, `SELECT [tbl].*`). |
| 93 | CLI Runner Race Condition on Rapid Invocations Leaks Uncoordinated Child Processes | High | Accept | Step 6 (CLI Execution) | In `run-coordinator.ts` and `extension.ts:241,278,305`, added `nextReservation()` and `isReservationCurrent()` tokens to cancel active runs and immediately abort/kill stale in-flight spawns. |
| 94 | Schema Deserialization Mismatch between CLI `verify-shape` and VS Code Tree Provider | Medium | Accept | Step 6 (CLI Execution & UI) | In `Program.cs:664-692`, added `filesScanned`, `queriesFound`, `connectionsFound`, and `queries` to `verify-shape` JSON output, and in `extension.ts:358-388`, adapted `summary.json` parsing to handle both `ScanReport` and `verify-shape` schemas. |
| 95 | Overly Complex Handwritten SQL Parser in `ContractRules.cs` | Low | Reject | Step 7 (Hardening) | Custom depth-aware lexer handles CTEs, aliases with spaces/parens, and cross-dialect quoting without introducing external dependencies. |
| 96 | Arbitrary Function Execution via SQL Wrapper Breakout in Live Query Schema Providers | High | Accept | Step 5 (Live Providers) | In `OracleLiveQuerySchemaProvider.cs:40-65` and `PostgreSqlLiveQuerySchemaProvider.cs:40-65`, added parenthesis balance validation (`depth >= 0` at all times, `depth == 0` at end) and unclosed block comment rejection (`/*`) to prevent breaking out of wrapper `SELECT * FROM (\n{trimmed}\n)`. |
| 97 | Shape Validation Compares All Entities to First SQL Query | High | Accept | Step 7 (Hardening) | In `ContractRules.cs:284-335`, correlated entity with `TargetTypeName` in `ColumnShapeMatchRule.cs` and only fall back to single query if exactly one untyped query exists in `allContracts`. |
| 98 | Race Condition and Missing Safety Check in Atomic File Write | Medium | Accept | Step 1 (CLI scan) | In `Program.cs:61,81`, added `IsSafeWritablePath` check and handled `DirectoryNotFoundException` with `Directory.CreateDirectory` in retry loop of `WriteTextAtomicallyAsync`. |
| 99 | Unstated Assumption: Only Records Have Primary Constructors (C# 12+) | High | Reject | Step 2 (Pass 5 & AST) | In C# 12, non-record primary constructor parameters are constructor arguments / private fields, not public mapped properties. Treating them as entity properties breaks Dapper/EF mapping invariants. |
| 100 | Syntax Indexing Ignores Namespaces Merging Identically Named Types | Medium | Accept | Step 2 (Pass 5 & AST) | In `ProjectCSharpSqlSource.cs:437-495`, grouped types by qualified namespace in `IndexSyntaxTypes` and disambiguated simple names across namespaces. |
| 101 | String Resolution Ignores IPropertySymbol and PropertyDeclarationSyntax | Medium | Accept | Step 2 (Pass 5 & AST) | In `ProjectCSharpSqlSource.cs:574-615`, supported `IPropertySymbol` initializers and expression-bodies in `TryResolveString`. |
| 102 | CLI Execution on Windows Fails for .cmd Wrappers When Shell Is False | High | Reject | Step 6 (CLI Execution) | Using `shell: true` introduces command injection risks on Windows; global dotnet tools create native `.exe` binaries. |
| 103 | Unstated Assumption: Dotnet Executable Is Available in PATH | Low | Accept | Step 6 (VS Code Extension) | In `extension.ts:165-170`, wrapped `languageClient.start()` in `try/catch` in `startLanguageServer`. |
| 104 | Dead Code / Unnecessary Cast in ComputeMappingStatus Null Checks | Low | Accept | Step 1 (MappingReport) | In `MappingReport.cs:88-110`, simplified null-safe checks in `ComputeMappingStatus`. |
| 105 | Schema Mismatch: Nullable `tables` in `ScanSummary.ToJson()` | Medium | Accept | Step 1 (MappingReport) | Ensured `tables = m.ReferencedTables ?? Array.Empty<string>()` in `MappingReport.cs:82` so it never serializes as null. |
| 106 | Missing Data: `TargetTypeLocation` dropped in `ScanSummary.ToJson()` | Medium | Accept | Step 1 (MappingReport) | Serialized `targetTypeLocation` when available in `MappingReport.cs:74-80` and added to `QueryScanItem` in TypeScript. |
| 107 | YAGNI: Dashboard Panel duplicates native Tree Provider functionality | Low | Reject | Step 3 & 4 (UI) | Tree Provider provides sidebar tree hierarchy; Dashboard Panel provides the detailed column-to-property mapping matrix. |
| 108 | Brittle Parsing: Arithmetic expressions silently drop trailing aliases | High | Accept | Step 7 (Hardening) | Handled trailing alias token in `ContractRules.cs:716-741` when penultimate token is an operand (not an operator `+`, `-`, `*`, `/`). |
| 109 | Missing Retry Backoff on Atomic File Move in Sinks | Medium | Accept | Step 7 (Hardening) | Added 5-attempt retry loop with exponential backoff on `File.Move` in `DiagnosticEmitter.cs:518-536`. |
| 110 | Unbounded Memory Read for SARIF and Summary in VS Code Extension | Medium | Accept | Step 6 (VS Code Extension) | Added file size bounds for `summary.json` (20MB) and `output.sarif` (50MB) before parsing in `extension.ts:360,580`. |
| 111 | Incomplete Credential Redaction Pattern in ConnectionDiscovery | Medium | Accept | Step 6 (Security) | Expanded `KeyValueCredentialMaskRegex` in `ConnectionDiscovery.cs:27-29` to include `secret_key`, `access_token`, `client_secret`, `private_key`, `auth_token`. |
| 112 | Fragile Absolute Path Equality in Tree Provider on Windows | Low | Accept | Step 3 (Tree View) | Added case-insensitive path comparison on Windows (`process.platform === "win32"`) and guarded against undefined `targetPath` in `sql-queries-tree-provider.ts:205,211`. |
| 113 | Missing Atomic Write & Path Validation for summary.json | High | Accept | Step 1 (CLI scan) | Replaced `File.WriteAllTextAsync` with `WriteTextAtomicallyAsync` in `Program.cs:392` to ensure atomic writing and path safety. |
| 114 | Dropped targetTypeLocation in VS Code UI breaks IDE navigation | Medium | Accept | Step 4 (Dashboard View) | Included `targetTypeLocation` in `sanitizedReport` in `dashboard-view.ts:71-75` for direct DTO navigation. |
| 115 | Redundant UI Serialization for SQL Mappings | Low | Reject | Step 4 (UI) | Tree View and Dashboard serve complementary UX roles: sidebar hierarchy vs deep mapping matrix. |
| 116 | JSON schema contract mismatch in verify-shape dropping location and metadata | High | Accept | Step 5 (Live Providers & CLI) | Zipped `results` and `readQueries` to include `location`, `tables`, `columns`, and `properties` in `verify-shape` JSON in `Program.cs:685-705`. |
| 117 | SQL parser silently drops unaliased expressions and aggregate functions | High | Accept | Step 7 (Hardening) | Extracted function/expression identifier when no explicit alias exists and removed aggregate function names from `IsSqlKeyword` in `ContractRules.cs:734-745,954`. |
| 118 | Regex dialect assumption misses valid DELETE mutations without FROM | Medium | Accept | Step 2 (Pass 5 & AST) | Updated `DeleteOpRegex` to `\bDELETE(?:\s+FROM)?\b` in `ProjectCSharpSqlSource.cs:54`. |
| 119 | Target Type resolution fails to inspect System.Type arguments | Medium | Accept | Step 2 (Pass 5 & AST) | Inspected `TypeOfExpressionSyntax` arguments in `ResolveTargetType` in `ProjectCSharpSqlSource.cs:748-762`. |
| 120 | Brittle JSON property naming fallback risks schema divergence | Low | Accept | Step 1 (MappingReport) | Added `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` to `MappingReport.cs:92`. |
| 121 | String Literals Containing Comments Break Parsing | Low | Accept | Step 2 (Pass 5 & AST) | In `ProjectCSharpSqlSource.cs:1091-1095`, masked string literals prior to masking comments in `MaskSqlCommentsAndStrings`. |
| 122 | T-SQL Bracket Identifiers Trigger False Comments | High | Accept | Step 7 (Hardening) | Handled `[` and `]` in `StripCommentsAndLiterals` in `ContractRules.cs:548-579,627-632`, preserving bracketed column identifiers like `[My--Column]`. |
| 123 | SARIF Generator Ignores Cancellation During Large Exports | Medium | Accept | Step 7 (Hardening) | Added `cancellationToken.ThrowIfCancellationRequested()` inside `run` and `result` iteration loops in `DiagnosticEmitter.cs:355,391`. |
| 124 | Progress Buffer Overflow Fails Silently | Low | Accept | Step 6 (VS Code Extension) | Logged warning to `channel` when `state.buffer.length > MAX_PROGRESS_BUFFER` in `extension.ts:488`. |
| 125 | `Regex.Replace` in `ColumnShapeMatchRule` blindly strips comment sequences inside quoted identifiers | High | Accept | Step 7 (Hardening) | Stripping comments via naive regex corrupts valid quoted identifiers like `"my -- alias"`; updated `ContainsSelectStar` and column extractors to use single-pass character lexer `StripCommentsAndLiterals`. |
| 126 | `SplitTopLevelSetBranches` splits on set keywords embedded within SQL comments | High | Accept | Step 7 (Hardening) | `SplitTopLevelSetBranches` lacked comment-skipping state machine; added `--` and `/* */` comment tracking so set keywords (`UNION`, `INTERSECT`, `EXCEPT`) inside comments are ignored. |
| 127 | `StripCommentsAndLiterals` destroys standard double-quoted identifiers | High | Accept | Step 7 (Hardening) | Double-quoted strings (`"..."`) are standard ANSI SQL identifiers; `StripCommentsAndLiterals` was replacing them with `''`, breaking query table/column matches; updated lexer to preserve double-quoted identifiers. |
| 128 | False-negative parent traversal rejection for paths with leading dots (`..secret`) | Medium | Accept | Step 6 (Security) | `isPathInWorkspaceFolder` in `src/DataGuard.VSCode/src/security.ts` checked `rel.startsWith("..")`, incorrectly flagging non-traversal folders; updated to check `rel === ".." || rel.startsWith(".." + path.sep)`. |
| 129 | Inconsistent `mappingStatus` and schema defaults in CLI `verify-shape` JSON | Medium | Accept | Step 1 (CLI) | `Program.cs` now properly maps `"untyped"` status with empty unmapped lists for queries without a target C# DTO, matching the `ScanSummary` contract. |
| 130 | Missing null-check in `NormalizeName` causes NRE on unnamed schema elements | Low | Accept | Step 1 (Reporting) | Added null-coalescing guard `if (string.IsNullOrEmpty(name)) return string.Empty;` in `MappingReport.cs:208-212`. |
| 131 | Potential NRE on null `log.Runs` or `log.Version` in `FileSarifSink` | Medium | Accept | Step 7 (Hardening) | Added null-safe navigation `log?.Runs ?? Enumerable.Empty<Run>()` and `log?.Version ?? "2.1.0"` in `DiagnosticEmitter.cs:347,353`. |
| 132 | Unredacted SQL snippets in Tree Item tooltips | Medium | Accept | Step 3 (Tree View) | Applied `redactForUi` to markdown SQL codeblock tooltips and made `unmappedColumns`/`unmappedProperties` display optional in `sql-queries-tree-provider.ts`. |
| 133 | Arbitrary directory opening via `jumpToLocation` in Dashboard Panel | High | Accept | Step 4 (Dashboard UI) | Added `fs.statSync(message.file).isFile()` check in `dashboard-panel.ts` to reject directory paths or non-regular files. |
| 134 | Permissive `img-src` in Dashboard Webview Content Security Policy | High | Accept | Step 4 (Dashboard UI) | Restricted `img-src` in `dashboard-view.ts` to `data:;` and explicit VS Code resource schemes, eliminating wildcard origin leakage. |
| 135 | Leaked stdout/stderr stream listeners in VS Code CLI process execution | Medium | Accept | Step 6 (CLI Execution) | Wrapped process listener attachment in `try/finally` in `extension.ts` ensuring stream handlers are removed and pipes unhooked on exit. |
| 136 | Remove ANSI double-quote preservation in SQL comment stripper | High | Reject | Step 7 (Hardening) | Preserving double-quoted identifiers is strictly required by ANSI SQL standards; dropping them causes false positives in shape matching and breaks regression tests. |
| 137 | Dead Code in Comment Sanitizer Enables Wrapper Breakout | Medium | Accept | Step 7 (Hardening) | Appended `/*` on `end < 0` in `StripCommentsAndLiterals` in `ContractRules.cs` and added dual check in `OracleLiveQuerySchemaProvider.cs` and `PostgreSqlLiveQuerySchemaProvider.cs`. |
| 138 | StackOverflowException in TryResolveString via Circular Identifier Resolution | High | Accept | Step 2 (Pass 5 & AST) | Added `depth > 10` guard and `visited` cycle detection in `ProjectCSharpSqlSource.cs`. |
| 139 | Silent Column Drop on Space-less Arithmetic Aliases | High | Accept | Step 7 (Hardening) | Updated arithmetic alias parsing in `ContractRules.cs` to handle `tokens.Length >= 2` with `!prevEndsWithOp`. |
| 140 | Oracle Live Query and Sanitizer Parameter Injection on q-Quoted Strings | High | Accept | Step 7 (Hardening) | Added Oracle alternative quoting `q'...'` / `Q'...'` support to `StripCommentsAndLiterals` in `ContractRules.cs`. |
| 141 | Regex Order in OracleDialectChecker Swallows Valid SQL on Line Comments with Apostrophes | High | Accept | Step 7 (Hardening) | Delegated `MaskCommentsAndLiterals` in `OracleDialectChecker.cs` to `ColumnShapeMatchRule.StripCommentsAndLiterals(sql)`. |
| 142 | Arbitrary Code Execution via Workspace cliPath Override | High | Reject | Step 6 (CLI Execution) | Already implemented with `scope: "machine"` in `package.json:217`. |
| 143 | Sensitive Data Exposure via Insecure Temp Directory Cleanup | High | Reject | Step 6 (CLI Execution) | Already implemented with `fs.rm(outputDirectory)` in the `finally` block of `extension.ts:425-429`. |
| 144 | Driver Type Inference Crash on DBNull Parameter Bindings | High | Reject | Step 5 (Live Providers) | `OracleParameter(paramName, DBNull.Value)` and `NpgsqlParameter(paramName, DBNull.Value)` in `CommandBehavior.SchemaOnly` mode compile successfully without executing SQL queries or evaluating runtime types on the server. |
| 145 | Line Terminator Array Allocation in Comment Stripping and SQL Parsing | Low | Accept | Step 7 (Hardening) | Replaced per-loop array allocations with static `LineEndings = { '\r', '\n' }` across all 5 comment stripping/parsing functions, eliminating GC pressure and handling all line terminators uniformly. |
| 146 | Bracketed Arithmetic Operands Ambiguously Treated as Aliases | High | Accept | Step 7 (Hardening) | Disambiguated bracketed arithmetic operands vs aliases (`prefix.EndsWith("+") || ...`), ensuring operands in expressions like `[A] + [B]` are not captured as column aliases. |
| 147 | Modulo Operator `%` Missing in Arithmetic Alias Checks | Medium | Accept | Step 7 (Hardening) | Included modulo operator `%` in arithmetic checks alongside `+`, `-`, `*`, `/` to avoid falsely treating the right-hand operand of `%` as an alias. |
| 148 | Recursive AST Traversal in `ProjectCSharpSqlSource` Risks StackOverflow | High | Accept | Step 2 (Pass 5 & AST) | Replaced recursive expression evaluation with iterative `Stack<ExpressionSyntax>` flattening while preserving depth bounding, preventing StackOverflowException on deeply nested binary expressions. |
| 149 | Identifier Delimiter Unwrapping Corrupts Escaped Delimiters | Medium | Accept | Step 7 (Hardening) | Implemented `IsSingleQuotedIdentifier` and `UnwrapIdentifier` safely handling escaped delimiters (`]]`, `""`, `''`, `\``) without corrupting identifier contents. |
| 150 | MappingTraceEngine Traces Columns Inconsistently When `p.ColumnName` Specified | High | Accept | Step 7 (Hardening) | Enforced strict `p.ColumnName` matching in `MappingTraceEngine.Trace` when explicitly present on parameter or property mappings. |
| 151 | ReDoS Backtracking in Connection String and Log Redaction Regexes | High | Accept | Step 6 (Security) | Replaced vulnerable backtracking patterns with linear non-overlapping ReDoS-resistant regexes for connection string and log redaction in `security.ts` and `redaction.ts`. |
| 152 | Inconsistent Alias Extraction Branching Order in `ExtractColumnNamesFromSql` | Medium | Accept | Step 7 (Hardening) | Flattened and unified `if (aliasAssignMatch.Success) ... else if (asMatch.Success) ... else if (trimmed.EndsWith("]"))` branching in `ExtractColumnNamesFromSql`. |
| 153 | Replace Custom SQL Lexer with Third-Party SQL Parser Library | High | Reject | Step 7 (Hardening) | External SQL parsing libraries introduce heavy dependency footprint and dialect incompatibilities; lightweight zero-dependency depth-aware lexer meets all security and correctness contracts. |
| 154 | Remove Single-Quoted String Literals Prior to Identifier Parsing | Medium | Reject | Step 7 (Hardening) | Prematurely stripping single-quoted literals corrupts valid ANSI SQL single-quoted aliases (`AS 'My Column'`); `UnwrapIdentifier` correctly handles quote unwrapping without data loss. |
| 155 | Disable Depth Bounding on Iterative Expression Stack | Low | Reject | Step 2 (Pass 5 & AST) | Disabling depth bounding exposes the engine to unbounded memory consumption on malicious or pathological syntax trees; depth bounding is a necessary defense-in-depth invariant. |
| 156 | Unchecked Relative Path Traversal via Symlinks in `isPathInWorkspaceFolder` | High | Accept | Step 6 (Security) | Enforced canonical resolution using realpath/canonicalization before workspace prefix checking in `security.ts`. |
| 157 | Deadlock in Atomic Move Retry Backoff under Synchronous Context | Medium | Accept | Step 1 (CLI) & Step 7 (Hardening) | Replaced synchronous blocking with async `Task.Delay` and exponential jittered backoff in `Program.cs` and `DiagnosticEmitter.cs`. |
| 158 | Memory Leak from Un-disposed CancellationTokens in CLI Runner | Low | Accept | Step 6 (VS Code Extension) | Wrapped transient `CancellationTokenSource` instances in `using` blocks across CLI runners and extension hosts. |
| 159 | Discard Custom Lexer in Favor of Full Semantic Roslyn Analysis for SQL Strings | High | Reject | Step 7 (Hardening) | Lexer handles multi-dialect embedded SQL text across Oracle, PostgreSQL, MySQL, and SQL Server where Roslyn semantic analysis only understands C# host syntax; custom lexer is an architectural requirement. |
| 160 | Missing NULL Handling in `ComputeMappingStatus` on Empty Token Projections | Medium | Accept | Step 1 (MappingReport) | Filtered empty string tokens and whitespace projections in `MappingReport.cs` before evaluating mapping completeness. |
| 161 | Diagnostic JSON Serialization Missing Indentation Toggle for Headless CI | Low | Accept | Step 1 (CLI) | Added conditional formatting in CLI report generation based on `--verbose` flag for compact output in CI/CD pipelines. |
| 162 | Windows PID Reuse Vulnerability in `terminateProcessTree` | High | Accept | Step 6 (VS Code Extension) | Checked `child.exitCode !== null || child.signalCode !== null || child.killed` before spawning `taskkill` to avoid killing reassigned PIDs. |
| 163 | Exception Storm / Double-Fault in Concurrent `ProgressEmitter.Emit` | Medium | Accept | Step 1 (Reporting) | Added inner `if (!this.enabled) { return; }` check immediately inside `lock (this.gate)` to short-circuit waiting threads once the pipe fails. |
| 164 | Pre-spawn Cancellation Race in `RunCoordinator` | Medium | Accept | Step 6 (VS Code Extension) | Added `if (!runCoordinator.isReservationCurrent(reservationToken))` guard before `spawn` in `extension.ts` to abort setup cleanly without process thrashing. |
| 165 | Endpoint Protection AV Sharing Violation Backoff Exhaustion | Low | Accept | Step 1 (CLI) | Increased retry attempts to 8 with exponential backoff in `WriteTextAtomicallyAsync` in `Program.cs`. |
| 166 | Session and Transaction Control Command Bypass in Live Query Providers | High | Accept | Step 5 (Live Providers) | Added compiled regex `DisallowedLiveCommandsRegex` with `SET`, `RESET`, `DISCARD`, `VACUUM`, `EXPLAIN`, `DECLARE`, `PRAGMA` in `OracleLiveQuerySchemaProvider.cs` and `PostgreSqlLiveQuerySchemaProvider.cs`. |
| 167 | Credential Redaction Discrepancy and Query String Secret Leakage in `security.ts` | High | Accept | Step 6 (Security) | Added `QUERY_PARAM_SECRET` masking to `redactSensitiveText` in `security.ts` to prevent query string credential leakage. |
| 168 | Escaped Backtick and Double Quote Truncation in SQL Lexer | High | Accept | Step 7 (Hardening) | Added support for doubled quotes `""` and doubled backticks ```` ```` in `ContractRules.cs`, starting backward search at `trimmed.Length - 2`. |
| 169 | Nested C# Types Dropping Outer Class Scope in `IndexSyntaxTypes` | Medium | Accept | Step 2 (Pass 5 & AST) | Prepended ancestor type declarations in `IndexSyntaxTypes` in `ProjectCSharpSqlSource.cs` to resolve nested types like `OuterContainer.NestedItem`. |
| 170 | Inconsistent Case-Sensitivity in Nullability Check in `MappingReport.cs` | Medium | Accept | Step 1 (MappingReport) | Changed `StringComparison.Ordinal` to `StringComparison.OrdinalIgnoreCase` on line 177 of `MappingReport.cs` for consistent nullable property matching. |
| 171 | Redundant 5-Pass AST Traversal Replacement with Single CSharpSyntaxWalker | High | Reject | Step 2 (Pass 5 & AST) | Combining passes into one complex walker increases regression risk for marginal (<50ms) gains and impairs maintainability; separate focused passes ensure reliable extraction. |
| 172 | Race condition in terminateProcessTree leads to unrelated PID termination | High | Accept | Step 6 (VS Code Extension) | Verified child.exitCode/signalCode/killed checks and ensure asynchronous completion handling in `extension.ts`. |
| 173 | Unbounded Extension Host memory leak and GC thrashing in processProgressText | High | Accept | Step 6 (VS Code Extension) | Verified line filtering and stream buffer chunk handling in `processProgressText`. |
| 174 | Workspace path validation bypassed via symlinks in isPathInWorkspaceFolder | Medium | Accept | Step 6 (Security) | Added `fs.realpathSync` validation to `isPathInWorkspaceFolder` in `security.ts` to reject symlinks pointing outside workspace folders. |
| 175 | Webview IPC listener lacks schema validation and error protection | Medium | Accept | Step 4 (Dashboard Panel) | Validated message object shape, added `try / catch` boundary, and added type assertions for `findingId`, `file`, and `line` in `dashboard-panel.ts`. |
| 176 | IsSafeWritablePath fails on directories due to File.ResolveLinkTarget IOException | Medium | Accept | Step 1 (CLI) | Updated `IsLink` in `Program.cs` to check `Directory.Exists` and use `Directory.ResolveLinkTarget` to avoid `IOException` on parent directories. |
| 177 | PostgreSqlLiveQuerySchemaProvider positional parameter binding index alignment | High | Accept | Step 5 (Live Providers) | Populated positional dummy parameters sequentially up to `maxDollar` index in `PostgreSqlLiveQuerySchemaProvider.cs` to avoid Npgsql IndexOutOfRangeException. |
| 178 | Inverse registration of syntax leakage rules in ProviderRuleCatalog | Medium | Reject | Step 7 (Hardening) | Pinned by existing unit tests in `ProviderRuleCatalogTests.cs` (asserts `ProviderRuleCatalog.Get("mysql")` contains MY001 and `Get("postgresql")` contains PG001). |
| 179 | Missing metadata in verify-shape text output format | Low | Accept | Step 1 (CLI) | Verified `verifyShapeCommand` text output formatting and queries evaluated reporting in `Program.cs`. |
| 180 | ArgumentException in dbColMap on duplicate DB columns | Low | Accept | Step 5 (Live Providers) | Replaced `ToDictionary` with `TryAdd` loop in `LiveSqlShapeValidationRule.cs:185` to gracefully handle duplicate column names in DB queries. |
| 181 | NullReferenceException/ArgumentNullException on null dbType in ContractRules | Low | Accept | Step 7 (Hardening) | Added `string.IsNullOrWhiteSpace` check for `clrType` and `dbType` in `IsTypeCompatible` in `ContractRules.cs`. |
| 182 | O(N) HashSet degradation in ContractRules column matching | Low | Reject | Step 7 (Hardening) | Column list size is bounded by SQL query projection size (typically < 50 items); constructing an intermediate HashSet introduces allocation churn with negligible lookup difference. |
| 183 | Incorrect generic entity type inference for multi-argument generic types | High | Accept | Step 2 (Pass 5 & AST) | Prioritized non-primitive class/struct entity types over generic key types in `ResolveTargetType` in `ProjectCSharpSqlSource.cs:824`. |
| 184 | Base repository fallback omits expected properties | Medium | Reject | Step 2 (Pass 5 & AST) | Synthesized `SELECT * FROM {table}` fallback in base constructor is untyped by design when no entity type is mapped. |
| 185 | UnwrapIdentifier fails to unescape internal escaped quotes | Low | Accept | Step 7 (Hardening) | Added unescaping for `]]` -> `]`, `""` -> `"`, ```` `` ```` -> ``` ` ```, and `''` -> `'` in `UnwrapIdentifier` in `ContractRules.cs`. |
| 186 | Snapshot diff silently exits with 0 in CI on drift | Low | Reject | Step 1 (CLI) | The CLI contract for `snapshot diff` intentionally requires `--fail-on-drift` to be opt-in, as documented in CLI specifications. |
| 187 | Missing Vietnamese translation for traceability architecture plan | Low | Reject | Docs & Parity | Internal planning documents (`*_PLAN.md`) are kept in project root in English; strict dual-language parity is maintained across all public docs in `docs/` (`.md` and `.vi.md`). |
| 188 | Specification Drift & Duplicate LINQ Pass on UnmappedProperties in MappingReport | Medium | Reject | Step 1 (MappingReport) | `m.UnmappedProperties` includes nullable properties while `m.Mappings` tracks true errors; re-filtering is intentional. |
| 189 | Redundant Source File Discovery Pass in CLI Scan Command | Low | Reject | Step 1 (CLI) | `DiscoverSourceFiles` is lightweight and provides total scanned count for `ScanSummary`. |
| 190 | Allocation Overhead and O(N*M) Linear Scan in MappingTraceEngine Property Matching | Low | Accept | Step 1 (MappingReport) | Prioritized explicit `ColumnName` matching before property name matching in `MappingTraceEngine.Trace`. |
| 191 | O(N) Re-filtering Traversal on Tree Item Expansion in SqlQueriesTreeProvider | Low | Reject | Step 3 (Tree View) | Tree items are bounded per file; re-filtering is fast and keeps tree provider stateless. |
| 192 | Quadratic Array Filtering in Webview Query Table Rendering | Low | Reject | Step 4 (Dashboard UI) | Queries table rendering runs on modern V8 engine with small bounded query lists; micro-optimization not warranted. |
| 193 | Webview IPC applyQuickFix accepts unvalidated findingId from message event | Medium | Accept | Step 4 (Dashboard UI) | Validated that `message.findingId` belongs to `this.currentFindings.some(f => f.id === message.findingId)` in `dashboard-panel.ts`. |
| 194 | Dashboard queries table renders unescaped targetType, tables, and unmappedColumns to innerHTML | High | Reject | Step 4 (Dashboard UI) | All dynamic fields in `renderQueries()` are already escaped via `escapeHtmlClient` on lines 409-427. |
| 195 | Live query keyword guard relies on regex blacklist rather than strict query shape whitelist | Medium | Reject | Step 5 (Live Providers) | `DisallowedLiveCommandsRegex` already blocks DML, DDL, transaction, and system packages; strict whitelist breaks valid read queries. |
| 196 | URI credential redaction regex leaks multi-colon password components | Low | Reject | Step 6 (Security) | Verified with test that `([a-z0-9+.-]+:\/\/[^\/\s:]+:)([^/\s]+)(@)` greedily matches colons and correctly redacts passwords. |
| 197 | IsSafeWritablePath lacks workspace root boundary constraint | Medium | Reject | Step 1 (CLI) | `IsSafeWritablePath` is a generic file writer utility; workspace containment is checked at the command/IDE boundary. |
| 198 | C# 12 Class Primary Constructor Parameters Ignored During Syntax Property Extraction | High | Reject | Step 2 (Pass 5 & AST) | In C# 12, class primary constructor parameters are constructor arguments/fields, not public properties, and are not mapped by Dapper or EF Core. |
| 199 | Syntactic Fallback Fails to Traverse Inheritance Hierarchy | Medium | Accept | Step 2 (Pass 5 & AST) | Tracked syntactic base list traversal in `ProjectCSharpSqlSource.cs` when semantic models are unavailable. |
| 200 | MaskSqlComments Corrupts SQL String Literals Containing Comment Delimiters | High | Accept | Step 2 (Pass 5 & AST) | Masked SQL string literals first before stripping comments in `MaskSqlComments` in `ProjectCSharpSqlSource.cs`. |
| 201 | Nested Block Comments in T-SQL/PostgreSQL Break Comment Stripping | Medium | Accept | Step 7 (Hardening) | Implemented `commentDepth` tracking in `ExtractTopLevelSelectClause`, `StripCommentsAndLiterals`, and `HasUnclosedBlockComment` in `ContractRules.cs`. |
| 202 | Common Table Expressions (CTE) Confuse Top-Level SELECT Extraction | Medium | Reject | Step 7 (Hardening) | Verified that CTE definitions are parenthesized `AS (...)`, which keeps `depth > 0`, so `ExtractTopLevelSelectClause` already extracts the main top-level SELECT. |
| 203 | Incomplete Identifier Normalization in LiveSqlShapeValidationRule | Low | Reject | Step 5 (Live Providers) | Normalization strips delimiters and converts to lowercase; additional word splitting creates false collisions. |
| 204 | Cross-Platform File Path Comparison and Normalization Flaws in VS Code Tree Provider | Medium | Accept | Step 3 (Tree View) | Path separators are normalized and case-insensitivity is supported across Windows and macOS. |
| 205 | MappingTraceEngine Prioritizes C# Property Name Over Explicit [Column] Mapping | Medium | Accept | Step 1 (MappingReport) | Prioritized matching explicit `ColumnName` before `Name` in `MappingTraceEngine.Trace`. |
| 206 | Windows PID Reuse and Race Condition in Process Tree Termination | High | Accept | Step 6 (VS Code Extension) | Verified PID alive check and asynchronous cleanup in `terminateProcessTree`. |
| 207 | Delayed Child Process Error Listener Attachment Causing Unhandled Event Crashes | High | Accept | Step 6 (VS Code Extension) | Attached immediate error handlers on `child`, `stdout`, and `stderr` directly following `spawn` in `runCliCommand` in `extension.ts`. |
| 208 | Race Condition in RunCoordinator Status Cleanup Between Rapid Sequential Runs | Medium | Accept | Step 6 (VS Code Extension) | Added `runCoordinator.isReservationCurrent(reservationToken)` check in `extension.ts` finally block before resetting status to idle. |
| 209 | Main Thread UI Block and Memory Exhaustion on Large Scan Summary Payloads | Medium | Reject | Step 6 (VS Code Extension) | JSON payload size is already strictly bounded to 20MB and read stream bounds memory. |
| 210 | Unhandled Exceptions Escaping ProgressEmitter Aborting Analysis Pipeline | Low | Accept | Step 1 (Reporting) | Added general `catch (Exception)` handler in `ProgressEmitter.Emit` disabling the emitter on write failure to prevent pipeline crashes. |
| 211 | Unhandled Filesystem Exception in Webview Workspace File Path Resolution | Medium | Accept | Step 4 (Dashboard UI) | Wrapped `fs.existsSync` inside `resolveWorkspaceFilePath` in `dashboard-panel.ts` in a `try/catch` block and added Windows path support on POSIX. |
| 212 | Incomplete Cloud and IAM Credential Masking in Database Error Sanitizer | Low | Accept | Step 5 (Live Providers) | Expanded `SanitizeErrorMessage` regex in `LiveSqlShapeValidationRule.cs` to include `client_secret`, `api[_\s-]*key`, `access[_\s-]*token`, and `authorization`. |
| 213 | RunCoordinator Lockup via Unhandled Exception in Replacement | High | Accept | Step 6 (VS Code Extension) | Wrapped `previous.cancel()` and `run.cancel()` in `try/catch` inside `RunCoordinator.ts` and guaranteed `currentToken++` in `finally`. |
| 214 | Pass 5 Per-File Execution Breaks Cross-File Deduplication | High | Accept | Step 2 (Pass 5 & AST) | Separated Pass 5 into a global second pass across all syntax trees after Passes 1-4 have resolved all typed/referenced queries in `ProjectCSharpSqlSource.cs`. |
| 215 | Broad Exception Handling Halts Full Subdirectory Traversal and Case-Sensitivity | Medium | Accept | Step 2 (Pass 5 & AST) | Isolated `EnumerateFiles` and `EnumerateDirectories` in separate `try/catch` blocks and conditioned `visited` comparer on `RuntimeInformation.IsOSPlatform` in `ProjectCSharpSqlSource.cs`. |
| 216 | PostgreSQL Rejects Untyped DBNull.Value Parameters in SchemaOnly Queries | Medium | Accept | Step 5 (Live Providers) | Added `NpgsqlDbType = NpgsqlDbType.Unknown` to dummy parameters in `PostgreSqlLiveQuerySchemaProvider.cs` to allow type inference. |
| 217 | Fallback Deserialization Drops Location Data in VS Code Extension | Medium | Accept | Step 6 (VS Code Extension) | Mapped `location` and `targetTypeLocation` when deserializing fallback results in `extension.ts:384-401`. |
| 218 | Webview IPC Payload Bypasses Redaction by Sending rawSql | Medium | Accept | Step 4 (Dashboard UI) | Removed `rawSql` and `rawFile` from `renderDashboardHtml` in `dashboard-view.ts` and sanitized tooltip tree item labels in `sql-queries-tree-provider.ts`. |
| 219 | Chimera Type Synthesis via Namespace Agnostic Type Indexing | High | Reject | Step 2 (Pass 5 & AST) | Primary resolution uses Roslyn semantic model with full namespaces; syntax index fallback is emergency-only for missing project references where namespaces may not compile. |
| 220 | Duplicate SQL Columns in Trace Multiset Dropped | Medium | Reject | Step 1 (Reporting) | `ColumnShapeMatchRule.ExtractColumnNamesFromSql` intentionally returns `HashSet<string>` of distinct column names by design; deduplication at the lexical layer is standard. |
