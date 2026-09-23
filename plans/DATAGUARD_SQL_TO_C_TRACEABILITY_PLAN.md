# DataGuard SQL-to-C# Traceability & Visibility Overhaul

## Context

The user reports that the DataGuard extension (VSCode + CLI) does not deliver actionable results:
1. **No visibility** into which SQL queries the extension found, what it checked, or what it will do with each query.
2. **No cross-layer tracing** — SQL queries are not linked back to the C# business code that consumes their results; no verification that C# properties match query columns.
3. **No real database validation** — no option to test queries against a live database (Oracle/PostgreSQL support absent in live validation).
4. **Narrow detection** — misses many connection declaration patterns, Oracle-specific SQL, PostgreSQL-specific SQL, and DAO/repository patterns beyond Dapper/EF Core.
5. **Language Server is a toy** — `SqlClassifier` only marks SQL keyword tokens (SELECT, INSERT...) with a generic info diagnostic `DGSQL001 "SQL {kind} statement requires offline contract validation."`. No contract extraction, no column matching, no output about what was found.

**ROOT CAUSE** (discovered via scout analysis): In `src/DataGuard.VSCode/src/command-args.ts:18`, `buildCliArguments("validate", ...)` generates CLI args that **NEVER include `--project`**. In the CLI (`Program.cs:1613-1617`), `ProjectCSharpSqlSource` (the Roslyn AST SQL extractor) is only instantiated when `projectPath` is non-empty. **Therefore, when a user clicks "Run Validation" in VSCode, zero inline C# SQL queries are ever analyzed.** The CLI only inspects database schema/snapshot stored procedures. If no SP errors exist, the user gets "0 findings" — believing all C# queries are verified when none were checked.

Additional root causes:
- VSCode extension redacts ALL CLI output (`extension.ts:275`): `"Detailed CLI output is not displayed to prevent credential disclosure."` — user sees nothing about what was found.
- Extension never passes `--progress` flag, so the CLI's `ProgressEmitter` events are never surfaced.
- Language Server never calls `DataGuard.Core` — only runs `SqlClassifier.Classify()` (keyword scan).
- `ParameterRegex` is hardcoded to `@(\w+)` — misses Oracle colon params (`:id`) and PostgreSQL positional params (`$1`).
- `ColumnShapeMatchRule.ExtractColumnNamesFromSql` discards any column containing `.` — table-qualified columns in JOINs (`u.Id`) are silently dropped, causing false-positive DG004 violations.
- Oracle adapter already has `RefCursorDescriber` (uses `DBMS_SQL.DESCRIBE_COLUMNS3`) and `AllTabColumnsReader` — but neither is wired as `ILiveQuerySchemaProvider`.
- PostgreSQL adapter already has `GetTableColumnsAsync` via `information_schema.columns` — also not wired as `ILiveQuerySchemaProvider`.

The reference skills from `10_DesignDatabase_Code_To_Relation_Key_Mermaid` demonstrate a proven multi-layer traceability pipeline: lexical masking → method call graph → SQL operation categorization (read/write/join/reference) → property assignment flow → tri-state relational authority (confirmed/proposed/unresolved) → evidence-backed reporting. DataGuard must adopt these approaches.

## Approach

### Step 0: Fix the root cause — VSCode extension must pass `--project` to CLI

**What**: The single most impactful fix. Without this, all other improvements are invisible to VSCode users.

**Changes** (file: `src/DataGuard.VSCode/src/command-args.ts`):

The function `buildCliArguments(command, workspacePath, provider, configPath, outputPath)` at line 18 builds the CLI argument array. Currently it produces:
```
["validate", "--config", configPath, "--provider", provider, "--format", "sarif", "--output", outputPath]
```

Add `--project` with the workspace folder path so `ProjectCSharpSqlSource` is instantiated:
```typescript
if (command === "validate") {
    args.push("--project", workspacePath);
}
```

Also add `--progress` so the CLI emits structured progress events:
```typescript
args.push("--progress");
```

**Changes** (file: `src/DataGuard.VSCode/src/extension.ts`):

1. In `runCliCommand()` (line 275), replace the blanket redaction message. Instead, parse `--progress` JSON events from CLI stdout and display them in the output channel:
   - `ContractDiscovered` → `[DataGuard] Found SQL in {file}:{line} targeting {type}`
   - `RuleExecuted` → `[DataGuard] Running rule {ruleId} on {contractId}`
   - Filter out any lines containing connection strings or passwords using the existing `redactSensitiveText()`.

2. After scan completion, emit a scan summary to the output channel:
   ```
   [DataGuard] Scan complete: {N} C# files scanned, {M} SQL queries found, {K} findings.
   ```
   This data comes from the SARIF `runs[0].invocations[0].toolExecutionNotifications` or from a new `summary` property in the SARIF output.

**Verification**: After this change alone, running "DataGuard: Run Validation" with `samples/DataGuard.Sample` open should produce findings in the Problems panel (DG006 naming mismatch on `PhoneNo` vs `PHONE`). Currently it produces 0 findings.


### Step 1: Enrich `ProjectCSharpSqlSource` — SQL detection breadth and connection awareness

**What**: Expand the SQL extraction engine to detect ALL patterns an enterprise C# codebase uses to interact with databases.

**Current state** (`ProjectCSharpSqlSource.cs`):
- Only detects Dapper (`Query`, `Execute`, etc.) and EF Core (`FromSqlRaw`, `ExecuteSqlRaw`) invocations.
- `TryResolveString` handles: string literals, interpolated strings, const evaluation, identifier lookup, binary concatenation.
- `IsCandidateInvocation` checks only `TargetMethodNames` — no `SqlCommand`, `OracleCommand`, `NpgsqlCommand`, `DbCommand`, ADO.NET patterns.

**Changes** (file: `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`):

1. Expand `TargetMethodNames` to include ADO.NET patterns:
   ```
   "ExecuteNonQuery", "ExecuteNonQueryAsync",
   "ExecuteReader", "ExecuteReaderAsync",  // already present partially
   "ExecuteScalar", "ExecuteScalarAsync",  // already present
   ```

2. Add a new detection path for `CommandText` property assignment (`cmd.CommandText = "SELECT ..."`) — walk `AssignmentExpressionSyntax` nodes where the left side is `*.CommandText` and the right side resolves to a SQL string. This covers `SqlCommand`, `OracleCommand`, `NpgsqlCommand`, and any `DbCommand` subclass.

3. Add detection for `new SqlCommand("SELECT ...", connection)` / `new OracleCommand(...)` / `new NpgsqlCommand(...)` — walk `ObjectCreationExpressionSyntax` where the type name ends in `Command` and the first constructor argument resolves to a SQL string.

4. Add detection for repository base-class patterns: `base("TABLE_NAME", "KEY_COLUMN")` constructor calls (used in the reference skills' Entity pattern).

5. Add SQL operation classification to the emitted `RawSqlDescriptor`. New property `SqlOperationType` (enum: `Read`, `Write`, `Join`, `Reference`, `Mixed`, `Unknown`). Parse from the SQL text using a regex similar to the reference skill's `_extract_table_links`:
   - `INSERT INTO|UPDATE|DELETE FROM|MERGE INTO` → `Write`
   - `SELECT ... FROM` → `Read`
   - contains `JOIN` → add `Join` flag
   - multiple operations → `Mixed`

6. Add table name extraction to `RawSqlDescriptor`. New property `ReferencedTables: IReadOnlyList<string>`. Parse `FROM <table>`, `JOIN <table>`, `INSERT INTO <table>`, `UPDATE <table>`, `DELETE FROM <table>` with alias stripping.

**Contract changes** (`src/DataGuard.Core/Abstractions/Contracts.cs`):
- Add `SqlOperationType` enum: `Unknown, Read, Write, Join, Reference, Mixed`
- Add to `RawSqlDescriptor`: `SqlOperationType OperationType` and `IReadOnlyList<string> ReferencedTables` (both with defaults for backward compat)
- Add `string? ConnectionProviderHint` to `RawSqlDescriptor` — `"oracle"`, `"sqlserver"`, `"postgresql"`, or `null` — inferred from the receiver type (e.g., `OracleCommand` → `"oracle"`)

**Existing code to reuse**: `ExtractColumnNamesFromSql` in `ColumnShapeMatchRule` (line 364-418) already parses SELECT columns. The new table extraction uses a similar regex approach.

7. Fix `ColumnShapeMatchRule.ExtractColumnNamesFromSql` (in `ContractRules.cs:391`): currently discards any column containing `.` (`trimmed.Contains('.')`), which causes table-qualified columns like `u.Id` in `SELECT u.Id, u.Name FROM Users u JOIN ...` to be silently dropped. Fix: strip the table qualifier prefix (everything before the last `.`) instead of discarding.

8. Expand `IsSqlString` (`ProjectCSharpSqlSource.cs:360-371`): add `BEGIN` to `SqlKeywordRegex` (needed for Oracle PL/SQL blocks like `BEGIN pkg.proc(:p1); END;`). Add Oracle stored procedure patterns: recognize `pkg.proc` format in addition to `sp_`/`usp_` prefixes.


### Step 2: Implement connection declaration discovery

**What**: Scan C# files for connection string declarations, `DbConnection` instantiations, and configuration patterns so the extension can report "found N connections to Oracle/SQL Server/PostgreSQL".

**Changes** (new file: `src/DataGuard.Core/Sources/ConnectionDiscovery.cs`):

Create a `ConnectionDiscovery` class that walks syntax trees looking for:
1. `new SqlConnection(...)` / `new OracleConnection(...)` / `new NpgsqlConnection(...)` — extract connection string argument if literal.
2. `IConfiguration.GetConnectionString("name")` — record the name.
3. `services.AddDbContext<T>(options => options.UseOracle/UseSqlServer/UseNpgsql(...))` — detect provider from the method name.
4. `appsettings.json` / `appsettings.*.json` parsing for `ConnectionStrings` section — read JSON files in the project directory.
5. `.dataguard.yml` already has provider config — reuse that.

Output: `IReadOnlyList<ConnectionInfo>` where `ConnectionInfo` is `record ConnectionInfo(string Name, string Provider, string? ConnectionStringHint, Location? Location)`.

This class is invoked during contract acquisition in the CLI and feeds into the diagnostic reporting.

### Step 3: Build the SQL-to-C# mapping trace engine

**What**: After SQL extraction, trace how each query's result columns are consumed by C# code. This is the core feature the user is missing.

**Current state**: `ColumnShapeMatchRule` (DG004) extracts column names from `SELECT` and compares to entity properties, but only for `EntityDescriptor` vs `RawSqlDescriptor` pairs. `LiveSqlShapeValidationRule` (DG018) does the same against live DB. Neither produces a visible mapping report.

**Changes** (file: `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`):

1. After finding a SQL invocation, trace the **usage site**: look at what variable receives the result and how its properties are accessed. For `var customers = conn.Query<Customer>(sql)`:
   - Identify the return variable via the parent `VariableDeclaratorSyntax` or `AssignmentExpressionSyntax`.
   - Walk downstream usages of that variable to find property accesses (`customer.Name`, `customer.Email`).
   - Record these as `UsedProperties: IReadOnlyList<string>` on the descriptor.

2. For `DataReader` patterns (`while (reader.Read()) { ... reader.GetString(0) ... reader["ColumnName"] }`):
   - Detect `reader["colName"]` and `reader.GetXxx(N)` patterns.
   - Map ordinal access to column names if the SQL SELECT list is known.

3. Emit a `MappingEvidence` record for each SQL-to-C# link:
   ```csharp
   public record MappingEvidence(
       string SqlText,
       IReadOnlyList<string> SqlColumns,
       string? TargetTypeName,
       IReadOnlyList<string> TargetProperties,
       IReadOnlyList<ColumnMapping> Mappings,  // matched pairs
       IReadOnlyList<string> UnmappedColumns,  // SQL cols with no C# property
       IReadOnlyList<string> UnmappedProperties, // C# props with no SQL col
       Location SqlLocation,
       Location? TargetTypeLocation);
   ```

**New file**: `src/DataGuard.Core/Reporting/ContractEvidence.cs` already exists (3.3KB). Extend it or create `src/DataGuard.Core/Reporting/MappingReport.cs` for the mapping evidence model.

### Step 4: Enrich diagnostic output — make the extension SHOW its work

**What**: The user's primary complaint is that the extension doesn't show what it found. Three output channels must be enriched.

#### 4a. CLI output (`src/DataGuard.Cli/Program.cs`)

Add a `--verbose` or `--report` mode to the `validate` command that emits a structured summary:
```
=== DataGuard Scan Report ===
Scanned 42 C# files in D:\MyProject

--- Connections Found ---
  [1] SqlConnection "DefaultConnection" at appsettings.json:3 (provider: sqlserver)
  [2] OracleConnection at Repository/OracleRepo.cs:15 (provider: oracle)

--- SQL Queries Found ---
  [Q1] SELECT CUSTOMER_ID, FULL_NAME, EMAIL FROM CUSTOMERS
       Location: CustomerRepository.cs:14
       Operation: Read | Tables: CUSTOMERS
       Target Type: Customer (4 properties)
       Mapping: 3/4 columns matched, 1 unmapped property (PhoneNo)

  [Q2] INSERT INTO ORDERS (ORDER_ID, CUSTOMER_ID, AMOUNT) VALUES (@id, @custId, @amount)
       Location: OrderService.cs:42
       Operation: Write | Tables: ORDERS
       Parameters: @id, @custId, @amount

--- Validation Results ---
  DG004 Warning: CustomerRepository.cs:14 — Property 'PhoneNo' has no matching column
  DG017 Warning: OrderService.cs:88 — SELECT * used; specify explicit columns
  DG018 Error: (live) CustomerRepository.cs:14 — Column 'EMAIL' is VARCHAR2(100) but C# property is int
```

**Implementation**: Extend `ConsoleDiagnosticSink` or add a new `ReportSink` in `src/DataGuard.Core/Reporting/`. The CLI `validate` command already has `--verbose` option (search confirmed); pipe the enriched data through it.

#### 4b. VSCode extension output panel (`src/DataGuard.VSCode/src/extension.ts`)

**Current state**: Line 275 says `"Detailed CLI output is not displayed to prevent credential disclosure."` — the CLI stdout is redacted, so the user sees NOTHING about what was found.

**Changes**:
1. CLI emits a structured JSON summary (separate from SARIF) to a `summary.json` file alongside the SARIF output.
2. VSCode extension reads `summary.json` and populates the output channel with the human-readable report above.
3. The `DataGuardFindingsTreeProvider` tree view is enhanced to show discovered queries as tree nodes (grouped by file), each expandable to show columns, target type, and mapping status.

#### 4c. Language Server diagnostics (`src/DataGuard.LanguageServer/Program.cs`)

**Current state**: Only publishes `DGSQL001` info diagnostics via `SqlClassifier.Classify()` — just marks SQL keyword positions. No column extraction, no mapping, no contract validation.

**Changes**: The Language Server should perform lightweight inline analysis per-file:
1. Parse the open C# file with Roslyn (single-file compilation, same approach as `ProjectCSharpSqlSource`).
2. For each detected SQL invocation, emit a diagnostic that includes:
   - The SQL text found
   - The target type (if resolved)
   - Column count and mapping status
3. Use diagnostic severity `Information` for found queries, `Warning` for mapping mismatches.

This requires adding a reference to `DataGuard.Core` from `DataGuard.LanguageServer` (currently only references `DataGuard.SqlClassification`).

### Step 5: Add Oracle and PostgreSQL live query validation

**Current state**: `LiveSqlShapeValidationRule` only supports SQL Server via `SqlServerLiveQuerySchemaProvider` (uses `sys.sp_describe_first_result_set`). Oracle and PostgreSQL return early at line 152 with `return; // Other database providers can be hooked in`.

**Existing infrastructure** (discovered via scout):
- Oracle adapter already has `RefCursorDescriber` (`OracleReaders.cs:650-825`) using `DBMS_SQL.TO_CURSOR_NUMBER` + `DBMS_SQL.DESCRIBE_COLUMNS3` — fully extracts column names, types, lengths, precisions, scales, nullabilities.
- Oracle adapter has `AllTabColumnsReader` (`OracleReaders.cs:345-479`) reading `ALL_TAB_COLUMNS` with BYTE/CHAR length semantics.
- PostgreSQL adapter has `PostgreSqlStoredProcedureParser.GetTableColumnsAsync` (`PostgreSqlStoredProcedureParser.cs:240-345`) querying `information_schema.columns`.
- Both adapters already instantiate `OracleConnection`/`NpgsqlConnection` via ADO.NET with configured connection strings.

**Changes**:

1. **Oracle** (file: `src/DataGuard.Oracle.Adapter/OracleLiveQuerySchemaProvider.cs` — new file):
   - Create `OracleLiveQuerySchemaProvider : ILiveQuerySchemaProvider`.
   - Implementation: Execute `SELECT * FROM ({sqlText}) WHERE 1=0` wrapped in a read-only transaction, then read `OracleDataReader.GetSchemaTable()` to extract column metadata. This avoids executing the query's data payload.
   - Alternative for complex queries (CTEs, PL/SQL): reuse `RefCursorDescriber`'s `DBMS_SQL.DESCRIBE_COLUMNS3` approach — open a cursor for the SQL, describe it, close it.
   - Map Oracle data types to `ColumnDescriptor` using existing `EfCoreInferenceSimulator` type mappings from `LengthMismatch.cs`.

2. **PostgreSQL** (file: `src/DataGuard.PostgreSql.Adapter/PostgreSqlLiveQuerySchemaProvider.cs` — new file):
   - Create `PostgreSqlLiveQuerySchemaProvider : ILiveQuerySchemaProvider`.
   - Implementation: Use `NpgsqlCommand.Prepare()` then read `NpgsqlDataReader.GetColumnSchema()` from the prepared statement without executing it. Npgsql's `Prepare()` sends a Parse message to PostgreSQL which returns column metadata.
   - Map PostgreSQL types to `ColumnDescriptor` using existing `PostgreSqlColumnTypeFactory` from `PostgreSqlLengthMismatchDetector.cs`.

3. Wire into `LiveSqlShapeValidationRule.ValidateCoreAsync` (line 146-153): add `else if (_provider.Equals("oracle", ...))` and `else if (_provider.Equals("postgresql", ...))` branches that instantiate the respective providers.

4. Fix `ParameterRegex` in `ProjectCSharpSqlSource.cs:37-39` to also match Oracle colon params and PostgreSQL positional params:
   ```csharp
   // Current: @([A-Za-z_][\w]*)
   // New: matches @param, :param, $1
   private static readonly Regex ParameterRegex = new(
       @"(?:@([A-Za-z_][\w]*)|:([A-Za-z_][\w]*)|\$(\d+))",
       RegexOptions.Compiled);
   ```

### Step 6: Enhance the sample project as a demo showcase

**Current state**: `samples/DataGuard.Sample` has one file with one SQL query (`SELECT CUSTOMER_ID, FULL_NAME, EMAIL, PHONE FROM CUSTOMERS`). This is insufficient to demonstrate the extension's capabilities.

**Changes** (directory: `samples/DataGuard.Sample/`):

Add files that exercise all detection paths:
1. `OracleRepository.cs` — uses `OracleCommand.CommandText = "SELECT ..."` and `OracleDataReader`.
2. `PostgreSqlService.cs` — uses `NpgsqlCommand` with parameterized queries.
3. `EfCoreContext.cs` — uses `DbSet<T>.FromSqlRaw(...)`.
4. `DapperService.cs` — uses `conn.Query<T>(sql)` with multi-table joins, `SELECT *`, and deliberate mismatches.
5. `Order.cs` + `Product.cs` — additional entity types with `[Column]`, `[ExpectedColumn]`, `[Key]`, `[NotMapped]` attributes.

Each file includes at least one intentional mismatch to trigger a diagnostic, demonstrating that the extension catches real issues.

### Step 7: Adopt reference skill techniques into DataGuard core

**What**: Port the three highest-value techniques from the reference skills.

1. **Lexical code masking** (from `source_code_analysis.py:_mask_csharp_code`):
   - Port as `internal static string MaskCodeForRegex(string source)` in `ProjectCSharpSqlSource.cs`.
   - Replaces comments (`//`, `/* */`) and string literals with spaces, preserving offsets.
   - Used before regex-based SQL extraction to avoid false positives (e.g., SQL keywords in comments or log messages).
   - DataGuard already uses Roslyn parsing, so this is a supplementary layer for the regex fallback paths.

2. **SQL operation categorization** (from `source_code_analysis.py:_extract_table_links`):
   - Already covered in Step 1. The `read`/`write`/`join`/`reference` classification maps directly to the new `SqlOperationType` enum.

3. **Ambiguity reification** (from `source_code_analysis.py:503-517`):
   - When DataGuard cannot resolve a target type or SQL text, instead of silently skipping, emit a `DG021` info diagnostic: `"Cannot resolve SQL target type for {invocation}. Reason: {reason}"`.
   - Reasons: `dynamic-sql`, `reflection`, `dependency-injection`, `external-method`, `unresolved-variable`.
   - Add to `ProjectCSharpSqlSource.cs`: when `ResolveTargetType` returns null, classify WHY and emit through `ProgressEmitter`.

## Critical Files & Anchors

| File | Symbol/Region | Reason |
|------|--------------|--------|
| `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` | `TargetMethodNames`, `IsCandidateInvocation`, `ExtractSqlText`, `ResolveTargetType` | Primary detection engine; all detection expansion happens here |
| `src/DataGuard.Core/Abstractions/Contracts.cs` | `RawSqlDescriptor`, `ContractDescriptor` | Schema changes for operation type, tables, connection provider |
| `src/DataGuard.Core/Rules/LiveSqlShapeValidationRule.cs` | `ValidateCoreAsync` line 146-153 | Oracle/PostgreSQL live validation hookpoint |
| `src/DataGuard.VSCode/src/extension.ts` | `runCliCommand` line 275, `loadDiagnostics` | User visibility — currently redacts all CLI output |
| `src/DataGuard.LanguageServer/Program.cs` | `PublishDebouncedAsync` line 56-78 | Inline analysis upgrade from keyword scan to contract extraction |

## Verification

### V1: Detection breadth (after Steps 1-2)
```powershell
cd D:\100.Software\Github\eco_support_net_oracle
dotnet build src/DataGuard.Core/DataGuard.Core.csproj
dotnet run --project src/DataGuard.Cli -- validate --project samples/DataGuard.Sample --provider sqlserver --verbose --offline
```
Expected: CLI output lists every SQL query in the sample project with operation type, referenced tables, target type, and column mapping status. At minimum:
- `SELECT CUSTOMER_ID, FULL_NAME, EMAIL, PHONE FROM CUSTOMERS` detected as `Read` operation on table `CUSTOMERS`.
- New sample files (Step 6) each produce at least one detected query.

### V2: Mapping validation (after Step 3)
Run the same command. Expected: output shows per-query mapping report:
- `Customer.PhoneNo` flagged as naming mismatch with column `PHONE` (DG006 already exists).
- Any `SELECT *` flagged with DG017.
- Unmapped properties listed explicitly.

### V3: VSCode visibility (after Step 4)
1. Open `samples/DataGuard.Sample` in VSCode with the DataGuard extension installed.
2. Run `DataGuard: Run Validation` command.
3. Expected: Output panel shows structured report (connections found, queries found, mapping results).
4. Expected: Findings tree view shows queries grouped by file, expandable to column mappings.
5. Expected: Problems panel shows diagnostics at correct source locations.

### V4: Live database validation (after Step 5)
```powershell
# SQL Server (existing)
dotnet run --project src/DataGuard.Cli -- validate --project samples/DataGuard.Sample --provider sqlserver --connection "Server=localhost;Database=test;..."

# Oracle (new)
dotnet run --project src/DataGuard.Cli -- validate --project samples/DataGuard.Sample --provider oracle --connection "Data Source=...;User Id=...;Password=..."

# PostgreSQL (new)
dotnet run --project src/DataGuard.Cli -- validate --project samples/DataGuard.Sample --provider postgresql --connection "Host=localhost;Database=test;..."
```
Expected: Each provider returns column-level shape validation results. If no live DB available, the `--offline` flag should produce a clear message explaining that live validation was skipped and showing only static analysis results.

### V5: Existing tests still pass
```powershell
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj
dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj
dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj
```
Expected: All existing tests pass. New contract properties have defaults, so existing code paths are unaffected.

## Assumptions & Contingencies

- **Roslyn single-file compilation limitations**: `ProjectCSharpSqlSource` creates a compilation from source files with runtime assembly references. Some types (`DbConnection`, `OracleCommand`) may not resolve if assemblies are not loaded. Contingency: the existing syntactic fallback (`ExtractPropertiesFromSyntax`, `IndexSyntaxTypes`) already handles this; extend to also syntactically detect command types by class name suffix `*Command`.
- **Language Server weight**: Adding `DataGuard.Core` reference to LanguageServer increases startup time and binary size. If this is unacceptable, implement a lighter inline analyzer that only does regex-based SQL detection + column extraction without full Roslyn compilation. Pre-decision: start with the full `DataGuard.Core` reference; if perf is >2s per file edit, fall back to regex-only.
- **Oracle DESCRIBE without connection**: Oracle live validation requires a live connection. If the user hasn't configured one, skip live validation and emit a clear info diagnostic instead of silent skip. Same for PostgreSQL.
