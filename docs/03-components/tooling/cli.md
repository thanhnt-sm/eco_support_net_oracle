# CLI Reference

The DataGuard CLI (`dataguard`) is the primary interface for contract validation, schema management, and environment assessment. Built with `System.CommandLine`, it provides 13 commands with consistent option patterns.

## Command Tree

```mermaid
graph TB
    ROOT[dataguard] --> V[validate]
    ROOT --> B[baseline]
    ROOT --> S[snapshot]
    ROOT --> I[init]
    ROOT --> H[hook]
    ROOT --> C[config]
    ROOT --> OC[oracle-check]
    ROOT --> M[migrate]
    ROOT --> A[assess]
    ROOT --> VER[version]
    ROOT --> SC[scan]
    ROOT --> VS[verify-shape]
    ROOT --> PF[preflight]

    S --> SR[refresh]
    S --> SS[show]
    S --> SD[diff]

    C --> CS[show]
    C --> CV[validate]

    H --> HI[install]
    H --> HS[status]
    H --> HU[uninstall]
```

## Commands

### `validate`

Validates entity contracts against database schema or snapshot.

```bash
dataguard validate [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | Database connection string |
| `--config` | — | Path to `.dataguard.yml` config file |
| `--output` | — | Output file path (required for sarif/evidence) |
| `--format` | `text` | Output format: `text`, `sarif`, `evidence`, `contracts`, `yaml`, `typescript` |
| `--offline` | `false` | Run in offline mode (no DB connection, requires `--assembly` or `--project`) |
| `--verbose` | `false` | Enable verbose output |
| `--provider` | Config `DefaultProvider`, then `sqlserver` | Database provider: `sqlserver`, `oracle`, `mysql`, `postgresql` |
| `--schema` | — | Database schema/owner name |
| `--assembly` | — | Path to compiled assembly for Manual ground-truth mode |
| `--ef-snapshot` | — | Explicit `ModelSnapshot.cs` source parsed with Roslyn; no assembly is loaded or executed |
| `--ef-project` | — | `.csproj` file or directory containing a source `*ModelSnapshot.cs`; never builds or loads an assembly |
| `--ef-context` | — | Context name used to select one snapshot under `--ef-project` |
| `--skip-rules` | — | Comma-separated rule IDs to skip (for example `DG002,DG017,MY001`) |
| `--project` | — | Path to C# project (`.csproj`), solution (`.sln`), or directory to extract inline SQL queries and C# models |
| `--progress` | `false` | Stream safe line-delimited JSON progress events to stderr |

**Behavior:**
- Without `--connection`: validates against committed snapshot (Snapshot mode)
- With `--offline`: runs validation without database access. Requires either `--assembly` (Manual ground-truth mode with attributes) or `--project` (Roslyn AST extraction mode for inline SQL and models). No compiled binary is required when using `--project`.
- `--project`: discovers C# source contracts and inline SQL queries (Dapper, ADO.NET) directly from source code (`.csproj`, `.sln`, or directory) via Roslyn AST without requiring a pre-compiled assembly
- `--progress`: outputs real-time step progress events as newline-delimited JSON (NDJSON) lines to `stderr` for tooling and IDE integration (e.g., VS Code extension)
- `--verbose`: prints detailed scan report including discovered connection strings/hints, detected SQL queries with line numbers, AST operation types, referenced tables, target DTO mappings, and unmapped column/property diagnostics
- `--format contracts`: exports extracted contracts as JSON
- `--format yaml`: exports the same contract schema as deterministic YAML
- `--format sarif`: emits SARIF 2.1.0 diagnostics to `--output`. When `--format sarif` is used, a supplementary `summary.json` file is automatically written to the same directory as `--output` containing aggregated scan metrics
- `--ef-snapshot`: adds bounded source-only EF descriptors; syntax/unsupported input fails visibly instead of producing empty contracts
- `--ef-project`: accepts only a directory or `.csproj`, finds source snapshots only, ignores `bin`, `obj`, and `.git`, and fails if selection is ambiguous; use `--ef-context` to select one context
- `--ef-snapshot` and `--ef-project` are mutually exclusive; `--ef-context` requires `--ef-project`
- `--skip-rules`: excludes the listed rule IDs before validation; matching is case-insensitive and surrounding whitespace is ignored
- `--format typescript`: exports TypeScript DTOs from entity descriptors

#### Progress Event Stream (`--progress`)

When `--progress` is enabled, the CLI streams machine-readable NDJSON events to `stderr`. Each line represents a discrete milestone or lifecycle phase:

```json
{"Kind":"PhaseStarted","Phase":"Acquiring contracts","Detail":"Scanning C# project for SQL queries and contracts.","Data":null}
{"Kind":"ContractDiscovered","Phase":"Acquiring contracts","Detail":"EntityDescriptor","Data":null}
{"Kind":"PhaseCompleted","Phase":"Acquiring contracts","Detail":"Contract acquisition completed.","Data":{"ContractCount":42,"Status":"Complete"}}
{"Kind":"PhaseStarted","Phase":"Validating rules","Detail":"Running enabled validation rules.","Data":{"ContractCount":42}}
{"Kind":"RuleExecuted","Phase":"Validating rules","Detail":"DG001","Data":null}
{"Kind":"Summary","Phase":"Validation complete","Detail":"Validation completed.","Data":{"ErrorCount":0,"WarningCount":2,"ViolationCount":2}}
```

Available `Kind` values: `PhaseStarted`, `PhaseCompleted`, `ContractDiscovered`, `RuleExecuted`, `Summary`. Event values strictly redact passwords, tokens, raw connection strings, and URI userinfo credentials (`protocol://user:password@host`).
- **Stream Error Handling & Buffer Overflow Protection**: The progress emitter isolates output stream operations within a synchronization gate and guards against broken pipe / unhandled stream errors. Downstream consumers (such as IDE extensions) enforce bounded line buffers (`MAX_PROGRESS_BUFFER = 1 MiB`) with fail-safe buffer resets on non-newline bursts. If downstream consumers close `stderr` prematurely or buffer drains fail (encountering `IOException` or `ObjectDisposedException`), the progress emitter employs a volatile thread-safe flag (`volatile bool enabled`) that safely disables subsequent event writes without throwing or interrupting the core validation pipeline.
#### Supplementary Scan Summary (`summary.json`)

When emitting SARIF output (`--format sarif --output <path>`), DataGuard automatically produces a companion `summary.json` in the same directory using atomic writes via `WriteTextAtomicallyAsync` and destination path validation via `IsSafeWritablePath`:

```json
{
  "filesScanned": 12,
  "queriesFound": 28,
  "connectionsFound": 2,
  "violationsCount": 1,
  "connections": [
    {
      "name": "DefaultConnection",
      "provider": "sqlserver",
      "hint": "Server=localhost;Database=Sales..."
    }
  ],
  "queries": [
    {
      "sql": "SELECT Id, Name, Email FROM Users WHERE TenantId = @TenantId",
      "location": {
        "file": "src/OrderService/Repositories/UserRepository.cs",
        "line": 42
      },
      "operation": "Read",
      "tables": ["Users"],
      "targetType": "UserDto",
      "targetTypeLocation": {
        "file": "src/OrderService/Models/UserDto.cs",
        "line": 15
      },
      "mappingStatus": "matched",
      "action": "shape-check",
      "columns": ["Id", "Name", "Email"],
      "properties": ["Id", "Name", "Email"],
      "unmappedColumns": [],
      "unmappedProperties": []
  ]
}
```

Key properties of `summary.json` items:
- `targetTypeLocation`: Contains the source file path and 1-based line number (`{ "file": string, "line": number }`) where the target C# DTO / model is declared, enabling IDE jump-to-model navigation. `null` if the model location is external or not resolved.
- `tables`: An array of referenced database tables. Always serialized as a non-null JSON array (`[]` when no tables are detected or referenced).

### `scan`

Discovers and reports inline SQL queries and C# model mappings directly from source code without requiring a database connection or pre-compiled binaries. Supports string literals, string interpolations, constants, local variables, fields, and property SQL resolution (including property initializers and expression-bodied properties).

**C# Extraction Resilience & AST Traversal:**
- **Call-Stack Cycle Detection in C# String Resolution**: When resolving SQL query strings across chained constants, local variables, fields, and properties (including expression-bodied properties), the Roslyn syntax analyzer tracks actively visited `SyntaxNode` expressions via an expression call-stack set (`visited.Add(expression)`) and caps recursion depth (`depth > 10`). If a cyclic or self-referential property/field dependency is encountered (e.g. `Query => Query` or mutual reference chains), the evaluator breaks the cycle immediately and returns `null` rather than triggering a stack overflow or infinite recursion.
- **Iterative AST Flattening for Deeply Concatenated SQL**: When resolving SQL statements constructed via binary addition expressions (`+`), the Roslyn analyzer flattens binary expression trees iteratively using an explicit `Stack<ExpressionSyntax>` rather than recursive descent. This eliminates recursion depth limits and prevents `StackOverflowException` when resolving long SQL queries built from hundreds of concatenated fragments.
- **Unicode & Full Line-Ending Support in SQL Parsing**: The SQL tokenizer, comment stripper, and clause extractors uniformly handle all standard and Unicode line-ending sequences across Windows, Linux, and macOS. Comment-stripping scanners seek line terminators using a shared character table (`LineEndings = { '\r', '\n', '\u0085', '\u2028', '\u2029' }`) that natively handles standard Carriage Return (`\r`) and Line Feed (`\n`) as well as Unicode Next Line (`\u0085`), Line Separator (`\u2028`), and Paragraph Separator (`\u2029`). This prevents multi-line comment bypasses, offset shifts, or dropped clauses when scanning SQL queries containing exotic or Unicode newline characters.
- **Strict `ColumnName` Property Mapping**: Model-to-SQL mapping and shape comparison rules strictly respect explicit `ColumnName` annotations (e.g., `[Column("...")]`, EF Core `HasColumnName`, or explicit column mappings). Resolution first attempts an exact match on `ColumnName`, then falls back to case-insensitive name matching, ensuring custom-mapped property names are verified accurately against database query result shapes without false-positive drift warnings.
- **SQL String Literal Masking & Comment Stripping (`MaskSqlCommentsAndStrings`)**: When classifying SQL operations (`SELECT`, `INSERT`, `UPDATE`, `DELETE`) and extracting referenced table names in C# source analysis, SQL string literals and comments are masked first before applying regular expression pattern matching. This prevents table regexes or clause identifiers from incorrectly matching keywords embedded inside string literals, constants, or SQL comments.
- **Global Pass 5 Execution & Cross-File Deduplication**: To discover untyped raw SQL queries without missing or duplicating contracts, `ProjectCSharpSqlSource` runs AST Pass 5 across all discovered C# source files after Passes 1–4 complete. Pass 5 inspects `FieldDeclarationSyntax` nodes for `const string` and `static readonly string` SQL fields. Extracted SQL strings are verified for valid query syntax and deduplicated against queries already captured in typed call sites (such as Dapper `QueryAsync<T>` or EF Core `FromSqlRaw<T>`), preventing duplicate or unmapped false positives for queries already bound to models elsewhere in the solution.
```bash
dataguard scan --project <path> [--format text|json] [--output <path>] [--verbose] [--progress]
```

| Option | Default | Description |
|---|---|---|
| `--project` | — | Path to C# project (`.csproj`), solution (`.sln`), or directory |
| `--format` | `text` | Output format: `text` or `json` |
| `--output` | stdout | Path to write the scan report (supports text summary or JSON report) |
| `--verbose` | `false` | Enable verbose scan details |
| `--progress` | `false` | Stream NDJSON progress events to stderr |

### `verify-shape`

Verifies extracted SQL query result shapes against a live database schema using non-executing query compilation (`CommandBehavior.SchemaOnly` with dummy parameter bindings or `sys.sp_describe_first_result_set`).

```bash
dataguard verify-shape --connection <conn-string> --provider <provider> --project <path> [--output <path>] [--format text|json] [--config <path>] [--verbose]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | (env/config) | Database connection string |
| `--provider` | (config) | Database provider (`sqlserver`, `postgresql`, `mysql`, `oracle`) |
| `--project` | Current directory | C# project directory or file to scan for SQL queries |
| `--output` | stdout | Output file path (supports formatted text summary or JSON report) |
| `--format` | `text` | Output format (`text`, `json`) |
| `--config` | `.dataguard.yml` | Custom path to configuration file |
| `--verbose` | `false` | Enable verbose error stack traces and details |

**Safety & Protections:**
- **Live DML & Transaction CTE Guard**: Queries containing data-modifying statements (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `MERGE`), transaction control commands (`COMMIT`, `ROLLBACK`, `SAVEPOINT`), or administrative commands (`GRANT`, `REVOKE`), even when wrapped inside Common Table Expressions (CTEs), are rejected from live execution to prevent unintended side effects, schema mutations, or transaction boundary interference.
- **Query Wrapper Breakout Protections & Character-Level Comment Checking**: Live query schema providers (Oracle and PostgreSQL) wrap arbitrary queries in `SELECT * FROM (\n{trimmed}\n)` or similar execution wrappers. To prevent breakout vulnerabilities (such as arbitrary function execution, stacked queries, or subquery escapes), providers strictly enforce parenthesis balance validation (`depth >= 0` at all times, `depth == 0` at termination), perform character-level `HasUnclosedBlockComment` protection against live query breakout (rejecting any unclosed `/*` comment blocks while respecting delimiters in quoted strings and identifiers), and reject unquoted semicolons before wrapper wrapping. If any breakout indicator is detected, the provider falls back safely to syntactic column extraction without executing the query live.
- **Dynamic SQL & Anonymous PL/SQL Block Guards**: Statements invoking dynamic execution (`EXEC`, `EXECUTE`, `EXECUTE IMMEDIATE`, `sp_executesql`), procedural anonymous blocks (`BEGIN ... END;`, `DO $$ ... $$`), or administrative control operations (`CALL`, `DO`, `COPY`, `VACUUM`, `LOCK`, `REINDEX`) are blocked from live execution to eliminate arbitrary code execution and unpredictable side effects.
- **Single-Pass Comment & String Literal Stripping (with Oracle Q-Quotes & Nested Comment Depth Tracking)**: SQL comments (`-- ...` and `/* ... */`) and string literals are stripped in a single lexical pass (`ColumnShapeMatchRule.StripCommentsAndLiterals`) before semicolon and statement keyword inspection. The parser preserves characters within bracket identifiers (`[My--Column]`, including escaped `]]`), backtick identifiers (`` `user_orders` ``), and standard ANSI double-quoted identifiers (`"column_name"`), preventing hyphens or slashes within delimited column and table identifiers from being mistakenly treated as comments. The comment stripper tracks nested block comment depth (`commentDepth`) up to balanced termination, preventing comment-hiding injection and ReDoS vulnerabilities in dialects supporting nested block comments (such as T-SQL and PostgreSQL). In addition to standard single-quoted literals (`'(?:''|[^'])*'`) and PostgreSQL dollar-quoted strings (`$(?<tag>[A-Za-z0-9_]*)$.*?$\k<tag>$`), the lexical engine provides comprehensive support for Oracle alternative quoting (Q-quotes: `q'...'` and `Q'...'`) across dialect checking and live query validation. It dynamically pairs brackets, braces, parentheses, angle brackets (`q'[...]'`, `q'{...}'`, `q'(...)'`, `q'<...>'`), and arbitrary single-character delimiters (`q'!...!^'`), replacing them with safe empty literal tokens (`''`). This prevents unescaped apostrophes inside Q-quoted literals from corrupting tokenizer state, prevents live query breakout, and prevents parameter sniffers or dialect checkers from mistaking literal contents for query parameters or SQL keywords.
- **Quote-Safe Set Branch Splitting**: Query set operations (`UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`) are split into discrete sub-branches (`ColumnShapeMatchRule.SplitTopLevelSetBranches`) using a quote-aware, comment-aware lexical scanner. The scanner tracks quote states (`'...'`, `"..."`, `[...]`, `` `...` ``), parenthetical nesting depths (`depth == 0`), and comment blocks (`--` and `/* ... */`), ensuring that set keywords appearing within string literals, quoted identifiers, or comments are never mistaken for branch boundaries.
- **Sanitized Error Messages**: Diagnostic warnings generated when shape determination encounters database errors (`LiveSqlShapeValidationRule`) sanitize exception messages using `SanitizeErrorMessage`. Connection string parameters (`password=`, `pwd=`, `user id=`, `uid=`, `secret=`, `token=`) and URI credentials (`protocol://user:password@host`) are redacted to `[REDACTED]` to prevent credential leakage into SARIF findings, editor diagnostics, or logs.
- **Table-Prefixed Wildcard Support**: Fallback wildcard analysis (`SelectStarUsageRule.ContainsSelectStar`) accurately detects and resolves table-prefixed wildcards (`SELECT T.*`, `SELECT [tbl].*`, ``SELECT `db`.`tbl`.*``) across top-level and inner subqueries, preventing missing property false alarms when querying tables via wildcards.
- **Oracle LOB & Large Numeric Parsing Safeguards**: When inspecting Oracle schema metadata (e.g. `CLOB`, `NCLOB`, `BLOB`, `LONG`, or high-precision `NUMBER`), numeric attributes such as column size, precision, and scale are converted using non-overflowing parsing (`int.TryParse` with bounds checking). This prevents runtime `OverflowException` errors when Oracle metadata exceeds standard 32-bit integer ranges or reports special unbounded sentinel values.
- **Stacked Query Guard**: Queries containing unquoted semicolons (stacked statements) are strictly rejected from live execution.
- **Syntactic Fallback, Space-less Arithmetic Aliases & Keyword Handling**: When live query description fails or when queries are rejected by live DML, dynamic SQL, query wrapper breakout guards, or block execution guards, the provider falls back gracefully to deterministic AST/regex syntactic column extraction (`ColumnShapeMatchRule.ExtractColumnNamesFromSql`). Syntactic column extraction accurately resolves trailing column aliases without the `AS` keyword—including space-less arithmetic aliases such as `Price*Quantity TotalCost` or `a+b c`—by inspecting preceding tokens, verifying that the penultimate token is an operand rather than an arithmetic operator (`+`, `-`, `*`, `/`), and ensuring the trailing token is a valid identifier before binding it as the column alias. In addition, unaliased function calls (such as `COUNT(1)`, `COUNT(*)`, or `MAX(Salary)`) are extracted by function identifier name, and non-structural SQL keywords (such as `SELECT`, `FROM`, `WHERE`, `JOIN`, `ON`, `INTO`, `SET`, `VALUES`, `AND`, `OR`, `NULL`) are explicitly filtered out from referenced table and column name bindings.
- **Expanded Credential Masking (`KeyValueCredentialMaskRegex`)**: All logged connection strings, URLs, exception messages, and progress events enforce strict credential redaction using `KeyValueCredentialMaskRegex` and negative lookbehinds. Redacted key-value credentials include `password`, `pwd`, `secret`, `secret_key`, `token`, `api_key`, `client_secret`, `access_token`, `private_key`, and `auth_token`, along with URI credentials (`protocol://user:password@host`), ensuring sensitive tokens are masked to `***` or `[REDACTED]` before output.
- **Error Handling & Non-Zero Exit Codes**: When shape verification encounters connection failures, unsupported dialect syntax, unmapped properties, or disallowed statements (such as live DML, transaction control, or unclosed comment blocks), the command reports clear actionable diagnostics with sanitized error messages. In strict validation modes, any shape mismatches or execution errors return non-zero exit codes (exit code `1` for validation/operational errors, exit code `2` for configuration/option errors), ensuring integration into automated CI/CD test gates.
- **Npgsql Parameter Binding with `NpgsqlDbType.Unknown`**: When compiling PostgreSQL queries under `CommandBehavior.SchemaOnly`, discovered query parameter placeholders (e.g. `@param` or `:param`) are bound as dummy parameters using `NpgsqlParameter` with `Value = DBNull.Value` and `NpgsqlDbType = NpgsqlDbType.Unknown`. This allows Npgsql and the PostgreSQL backend engine to infer parameter data types from context without triggering parameter type mismatches or unassigned type exceptions during schema-only compilation.

**Output Formats:**

**Text output (default):**
```text
=== DataGuard Verify-Shape Report (sqlserver) ===
Queries evaluated: 2

Query: SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status
  Status: verified (Target: OrderDto)
  DB Columns: 3

Query: SELECT Title FROM Books
  Status: verified (Target: BookDto)
  DB Columns: 1
```

**JSON output (`--format json`):**
```json
{
  "provider": "sqlserver",
  "project": "src/OrderService",
  "filesScanned": 2,
  "queriesFound": 2,
  "connectionsFound": 1,
  "violationsCount": 0,
  "connections": [
    {
      "name": "DefaultConnection",
      "provider": "sqlserver",
      "hint": "Server=localhost;Database=Sales..."
    }
  ],
  "queriesVerified": 2,
  "results": [
    {
      "sql": "SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status",
      "targetType": "OrderDto",
      "status": "verified",
      "dbColumnCount": 3,
      "matchedProperties": 3,
      "missingInDatabase": [],
      "extraInDatabase": []
    }
  ],
  "queries": [
    {
      "sql": "SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status",
      "location": {
        "file": "src/OrderService/Repositories/OrderRepository.cs",
        "line": 45
      },
      "targetType": "OrderDto",
      "operation": "Read",
      "targetTypeLocation": null,
      "mappingStatus": "matched",
      "action": "shape-check",
      "tables": [
        "Orders"
      ],
      "columns": [
        "Id",
        "Amount",
        "CustomerId"
      ],
      "properties": [
        "Id",
        "Amount",
        "CustomerId"
      ],
      "unmappedColumns": [],
      "unmappedProperties": []
    },
    {
      "sql": "SELECT COUNT(*) FROM AuditLogs",
      "location": {
        "file": "src/OrderService/Repositories/AuditRepository.cs",
        "line": 28
      },
      "targetType": null,
      "operation": "Read",
      "targetTypeLocation": null,
      "mappingStatus": "untyped",
      "action": "untyped-query",
      "tables": [
        "AuditLogs"
      ],
      "columns": [],
      "properties": [],
      "unmappedColumns": [],
      "unmappedProperties": []
    }
  ]
}

Running `dataguard scan --verbose` outputs a human-readable diagnostic report directly to `stdout`:

```text
=== DataGuard Scan Report ===
Scanned C# project/directory: src/OrderService
Files scanned: 5
Connections found: 1
SQL queries found: 2

--- Connections Found ---
  [1] SQLSERVER "DefaultConnection" (Server=localhost;Database=Sales...)

--- SQL Queries Found ---
  [Q1] SELECT Id, Amount, CustomerId FROM Orders WHERE Status = @Status
       Location: OrderRepository.cs:45
       Operation: Select | Tables: Orders | Target: OrderDto
       Mapping: 3/3 properties matched. Unmapped columns: 0, unmapped properties: 0

Validation complete: 0 issues (0 errors, 0 warnings)
```
## Managed pre-commit hooks

The hook installer writes POSIX `sh` scripts and invokes `dataguard validate --format text` so it uses the normal persisted Snapshot path. It never emits `--offline` without the required `--assembly`. On Unix it sets executable mode. Install and uninstall only replace or delete files marked as DataGuard-managed; an existing user hook or `lefthook.yml` is preserved, including when force is requested.

```bash
dataguard hook install [--type auto|native|husky|lefthook] [--force]
dataguard hook status
dataguard hook uninstall
```

`status` is read-only. `uninstall` only removes files carrying the DataGuard marker; it never deletes a user-owned hook. `--force` does not override that ownership rule.
Native Git hooks also resolve a linked-worktree `.git` file to its real `gitdir`, so status and removal operate on the same managed file as installation. The installer rejects a symbolic-link hook path and preserves its target outside the workspace.

### `baseline`

Creates a baseline from current violations for drift detection.

```bash
dataguard baseline [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | Database connection string |
| `--config` | — | Path to config file |
| `--output` | `.dataguard-baseline.json` | Baseline output path |
| `--verbose` | `false` | Verbose output |
| `--provider` | `sqlserver` | Database provider |
| `--schema` | — | Schema/owner name |
| `--package` | — | Oracle package name |

**Output includes:**
- Violation list with rule IDs and messages
- Database version (from `@@VERSION` or `V$VERSION`)
- Schema hash (SHA-256, first 16 hex chars)

### `preflight`

Performs an explicitly operator-authorized live acquisition and writes a bounded,
redacted manifest for offline MSBuild validation. Project properties and build
imports cannot invoke this command or grant it authority.

```bash
dataguard preflight --connection "..." --provider sqlserver \
  --target production-schema --output .dataguard/preflight.json
```

`--provider` is limited to `sqlserver`, `postgresql`, `mysql`, and `oracle`;
`--target` is bounded to 128 characters. The output contains a schema version,
target/provider binding, contract count, and SHA-256 digest without persisting
connection strings or contract payloads. The resulting absolute manifest can be
passed to `DataGuardOfflineManifest` during an offline build.

### `snapshot`

Manages schema snapshots for offline validation and drift detection.

#### `snapshot refresh`

Refreshes the snapshot from the live database.

```bash
dataguard snapshot refresh [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | Database connection string |
| `--config` | — | Path to config file |
| `--verbose` | `false` | Verbose output |
| `--provider` | `sqlserver` | Database provider |
| `--schema` | — | Schema/owner name |
| `--package` | — | Oracle package name |

**Oracle-specific:** When provider is Oracle, captures the full schema (all tables, all columns with `CHAR_USED`, `CHAR_LENGTH`) into the snapshot for offline length-mismatch detection.

This command requires a configured database connection. Without a fresh live
acquisition it returns `UNEVALUATED` (exit code 3) and does not create a
snapshot.

#### `snapshot show`

Displays current snapshot metadata.

```bash
dataguard snapshot show [--config <path>]
```

**Output:**
- Snapshot file path
- Version, schema version, ground truth mode
- Database version, schema hash, hash kind
- Provider, schema scope, canonicalization version
- Creation timestamp, violation count

#### `snapshot diff`

Compares current schema with the committed snapshot.

```bash
dataguard snapshot diff [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--fail-on-drift` | `false` | Exit non-zero when drift is detected |
| `--legacy-violation-diff` | `false` | Explicitly enable deprecated violation-only comparison for v1 snapshots |

**Drift detection:**
- Requires a fresh live acquisition before every comparison; persisted schema is
  never compared with itself
- Uses schema-based hashing when both persisted and freshly acquired schemas are available
- Returns `UNEVALUATED` (exit code 3) when no connection, provider result, or
  required fresh schema is available
- Legacy v1 comparison is disabled by default; the explicit
  `--legacy-violation-diff` opt-in is not structural drift evidence
- In CI environments (`CI` or `GITHUB_ACTIONS` set), warns about drift even without `--fail-on-drift`

Validation classifies contract acquisition as `Complete`, `Unavailable`,
`Incomplete`, or `Failed`. Non-complete acquisition returns `UNEVALUATED` (exit
code 3) and suppresses normal contract, YAML, TypeScript, SARIF, and evidence
exports; an explicit EF model-snapshot source may supply the contracts directly.

### `init`

Initializes a DataGuard configuration file.

```bash
dataguard init [--output <path>] [--provider <name>] [--wizard]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--output` | `.dataguard.yml` | Output config file path |
| `--provider` | `sqlserver` | Default provider |
| `--wizard` | `false` | Prompt for setup choices interactively and write to `--output` |

The wizard reads from the terminal and writes only to the explicit `--output` path (default `.dataguard.yml`). It does not put a connection string in generated configuration; use `DATAGUARD_CONNECTION_STRING` for credentials.

**Generated config:**
```yaml
GroundTruthMode: Snapshot
SnapshotFilePath: .dataguard-snapshot.json
BaselineFilePath: .dataguard-baseline.json
NamingConvention: SnakeCaseToPascalCase
EnableBaseline: true
```

### `config`

Manages DataGuard configuration.

#### `config show`

Displays current configuration with secrets redacted.

```bash
dataguard config show [--config <path>]
```

**Security:** Connection strings are always redacted to `***redacted***` in output.

#### `config validate`

Validates a configuration file.

```bash
dataguard config validate [--config <path>]
```

### `oracle-check`

Runs Oracle-specific dialect and length checks.

```bash
dataguard oracle-check [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | **Required.** Oracle connection string |
| `--config` | — | Path to config file |
| `--output` | — | Output file path |
| `--format` | `text` | Output format |
| `--verbose` | `false` | Verbose output |
| `--schema` | — | Oracle owner/schema |
| `--package` | — | Oracle package name |

**Pipeline:**
1. Resolves NLS length semantics (CHAR vs BYTE)
2. Reads full schema (all tables, all columns)
3. Runs dialect checks against column types
4. Reports unmapped type usage

### `migrate`

Migrates a legacy baseline file (v1) to v2 format.

```bash
dataguard migrate [--baseline <path>]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--baseline` | `.dataguard-baseline.json` | Path to baseline file to migrate |

### `assess`

Runs read-only environment/dependency/config assessment.

```bash
dataguard assess [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--workspace` | `.` | Workspace root to assess |
| `--project-filter` | — | Optional project path filters (substring, case-insensitive) |
| `--output` | — | Output file path |
| `--format` | `text` | Output format: `text`, `json`, `sarif` |
| `--verbose` | `false` | Verbose output |

**Assessment packs:**
- Inventory: project files, target frameworks
- Dependencies: NuGet packages, version analysis
- Build/CI: build scripts, CI configuration
- Secrets: hardcoded credentials detection
- Dependency health: outdated/vulnerable packages

### `version`

Displays DataGuard version information.

```bash
dataguard version
```

**Output:**
- CLI version (from `AssemblyInformationalVersion`)
- .NET runtime version
- OS version
- Component versions: Core, Oracle.Adapter, SqlServer.Adapter, Analyzers

## Common Options

| Option | Short | Description |
|--------|-------|-------------|
| `--connection` | — | Database connection string |
| `--config` | `-c` | Path to `.dataguard.yml` |
| `--output` | `-o` | Output file path |
| `--format` | `-f` | Output format |
| `--offline` | — | Offline mode (no DB) |
| `--verbose` | `-v` | Verbose output |
| `--provider` | `-p` | Database provider |
| `--schema` | `-s` | Schema/owner name |
| `--package` | — | Oracle package name |
| `--assembly` | — | Assembly path for Manual mode |
| `--fail-on-drift` | — | Exit non-zero on drift |

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Pass — no errors found |
| `1` | Fail — errors detected or operational failure |
| `2` | Config error — invalid options or unsupported format |
| `3` | Unevaluated — incomplete contract acquisition or missing prerequisite |
| `4` | Assess failure — operational error in assessment engine |
| `130` | Interrupted — operation cancelled via SIGINT / Ctrl+C or cancellation token |

## Atomic Writing & Cancellation Semantics

### Atomic File Writing
All machine-readable file artifacts (`sarif`, `summary.json`, `evidence`, `contracts`, `yaml`, `typescript`, `baseline`, snapshots, manifests, and hooks) use atomic file creation semantics via `WriteTextAtomicallyAsync` to prevent partially written or corrupt files:
1. **Safe Writable Path Validation**: The destination path is validated via `IsSafeWritablePath` to prevent writing to symlinks, directory traversal targets outside allowed boundaries, or unsafe device paths. The validator inspects the candidate file path, its immediate parent, and every ancestor directory in the hierarchy, checking `LinkTarget` and `FileAttributes.ReparsePoint` (covering Unix symbolic links, Windows directory junctions, and reparse points). Writing through any symbolic link or junction point is strictly refused (`Refusing to write through a symbolic link or invalid path`).
2. **Directory Creation & Temporary Staging**: The target directory is created if missing, and content is written to a private temporary file in the destination directory using the naming pattern `.{filename}.{guid}.tmp`.
3. **Pre-commit Cancellation Verification**: `CancellationToken.ThrowIfCancellationRequested()` is verified immediately prior to file replacement.
4. **Atomic File Move**: The temporary file is atomically moved to the final target path (`File.Move(tempPath, outputPath, overwrite: true)`).
5. **Retry Backoff with Directory Recovery**: File moves and `StreamingSarifSink` implement a 5-attempt retry loop with backoff (attempts 1 to 5 with delays of `50ms * attempt`: 50ms, 100ms, 150ms, 200ms, 250ms) and automatic `Directory.CreateDirectory` recovery to withstand transient file sharing violations, anti-virus locks, or temporary file contention.
6. **Guaranteed Cleanup**: A `finally` block guarantees deletion of the temporary file if writing or replacement fails or is aborted.
### Cancellation Semantics
The CLI wires `Console.CancelKeyPress` to an internal `CancellationTokenSource`:
- Interactive user cancellation (`Ctrl+C` / `SIGINT`) or host-initiated termination initiates cooperative cancellation across all async pipelines, readers, and validators.
- Active operations catch `OperationCanceledException` and emit cancellation notices (`Validation cancelled.`, `Scan cancelled.`, `verify-shape cancelled.`, `Assessment cancelled.`) to `stderr`.
- Incomplete machine-readable files are discarded before atomic commit, ensuring destination files remain uncorrupted.
- Cancelled executions terminate with exit code `130`.
## Output Formats

### `text` (default)

Human-readable console output with color-coded severity.

### `sarif`

SARIF 2.1.0 JSON format for IDE integration and CI pipelines. Requires `--output`.

### `evidence`

Contract evidence JSON for audit trails. Requires `--output`.

### `contracts`

Exported contract descriptors as JSON. Requires `--output`.

### `typescript`

TypeScript DTO definitions exported from entity descriptors. Requires `--output`.

## Configuration File

The `.dataguard.yml` file supports all configuration options:

```yaml
GroundTruthMode: Snapshot          # Snapshot | Manual | Full
ConnectionString: "Server=..."     # Prefer env DATAGUARD_CONNECTION_STRING
DefaultSchema: dbo
DefaultPackage: ""                 # Oracle package name
NamingConvention: SnakeCaseToPascalCase
EnableBaseline: true
BaselineFilePath: .dataguard-baseline.json
SnapshotFilePath: .dataguard-snapshot.json
EnableConcurrentValidation: true
MaxDegreeOfParallelism: 4
```

**Security note:** Never commit connection strings to source control. Use environment variable `DATAGUARD_CONNECTION_STRING` instead.

For every database-backed command, connection resolution is deterministic: `--connection` takes precedence, then `DATAGUARD_CONNECTION_STRING`, then `ConnectionString` from the selected config file. Provider resolution is `--provider`, then the config's persisted `DefaultProvider`, then `sqlserver`. `dataguard init --provider oracle` writes that fallback without storing a credential.

When a selected provider includes a rule whose required analyzer context is unavailable, `validate` reports the rule ID and prerequisite, exits with code `3`, and suppresses normal text/SARIF/evidence/contracts/TypeScript success output. This is an incomplete run, not a clean result.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `DATAGUARD_CONNECTION_STRING` | Database connection string (overrides config) |
| `CI` | Detected for CI-specific behavior |
| `GITHUB_ACTIONS` | Detected for GitHub Actions-specific behavior |
