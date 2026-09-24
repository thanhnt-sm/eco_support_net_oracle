# VS Code Extension

The DataGuard VS Code extension provides integrated contract validation directly in the VS Code editor, loading SARIF diagnostics into the Problems panel and offering real-time feedback during development.

## Architecture

```mermaid
graph TB
    subgraph "VS Code Extension (TypeScript)"
        EXT[extension.ts]
        CMD[Command Registration]
        SARIF[SARIF Diagnostic Loader]
        SB[Status Bar]
        OC[Output Channel]
        SEC[Security Module]
        PM[Process Manager]
    end

    subgraph "DataGuard CLI"
        DG[dataguard validate]
        DGA[dataguard assess]
    end

    subgraph "VS Code APIs"
        DIAG[DiagnosticCollection]
        WIN[Window Messages]
        TERM[Terminal Integration]
    end

    EXT --> CMD
    CMD --> PM
    PM -->|spawn| DG
    PM -->|spawn| DGA
    DG -->|SARIF output| SARIF
    SARIF --> DIAG
    PM --> SB
    PM --> OC
    SEC -->|redact| OC
```

## Commands

| Command | ID | Description |
|---------|----|-------------|
| Run Validation | `dataguard.runValidation` | Execute `dataguard validate` and load SARIF diagnostics |
| Cancel Validation | `dataguard.cancelValidation` | Terminate running validation process |
| Assess Workspace | `dataguard.assess` | Execute local-first `dataguard assess`; it never supplies remote-advisory consent |
| Refresh Snapshot | `dataguard.refreshSnapshot` | Ask for confirmation, then execute `dataguard snapshot refresh` with the trusted config |
| Create Baseline | `dataguard.createBaseline` | Ask for confirmation, then execute `dataguard baseline` with the trusted config |
| Scan Project | `dataguard.scanProject` | Run `dataguard scan` to discover inline SQL queries and C# model mappings |
| Refresh SQL Queries | `dataguard.refreshQueries` | Re-scan project SQL queries and refresh the Discovered SQL Queries tree view |
| Verify SQL Shapes | `dataguard.verifyShape` | Run `dataguard verify-shape` against a live DB schema with DML/CTE mutation protection |

Only one DataGuard command process runs globally at a time. Starting Validate or Assess cancels and replaces any prior Validate or Assess run, including one from another workspace.

`dataguard.cancelValidation` cancels the global DataGuard process independent of workspace. On POSIX it sends `SIGTERM` to the detached process group, verifying process exit before falling back to `SIGKILL` via an unreferenced timer if the process does not terminate within 3 seconds; on Windows it verifies child process liveness (`child.pid && child.exitCode === null && child.signalCode === null && !child.killed`) to guard against PID reuse before issuing `taskkill /pid <pid> /T /F` with immediate `child.kill()` fallback.

## Extension Activation & Deactivation

The extension activates on:
- Opening a `.cs` file in a workspace containing `.dataguard.yml`
- Running any `dataguard.*` command from the Command Palette
- Opening a workspace with a `DataGuard.Core` project reference

### Deactivation Cleanup

Upon extension deactivation (`deactivate()`):
- **Language Client**: Stops the Language Client (`languageClient.stop()`) and frees resources.
- **Asynchronous Process Tree Termination**: Cancels the active `RunCoordinator` execution (`runCoordinator.cancel()`), clears process timeouts, and ensures clean termination of running child processes. If the child process has not already exited (evaluating `exitCode !== null || signalCode !== null`), `deactivate()` waits asynchronously on `exit` raced against a 1-second timeout (`Promise.race([exitPromise, timeoutPromise])`) while dispatching `terminateProcessTree(run.child)`, preventing orphaned or zombie CLI processes without blocking IDE shutdown indefinitely.
- **Atomic Temporary Directory Purge**: Recursively deletes any active temporary directory (`run.outputDirectory`) created for SARIF and scan summaries, suppressing non-fatal filesystem exceptions.
- **UI & State Disposal**: Disposes the Status Bar item, Output Channel, Diagnostic Collection, and Dashboard Webview panel.
## SARIF Diagnostic Loading & Bounded Memory Reads

The extension runs `dataguard validate --format sarif --output <temp-file>` and parses the SARIF 2.1.0 output to populate VS Code's Problems panel. Artifact locations must be relative paths or `file:` URIs that resolve inside the selected workspace; malformed, remote, traversal, sibling-prefix, and external locations are logged and skipped.

### Bounded Memory File Reading
To protect the VS Code extension host against denial-of-service or out-of-memory (OOM) exhaustion from runaway or malicious file generation, file reads enforce strict upper-bound size limits before parsing:
- **SARIF Report (`output.sarif`)**: Capped at **50 MB** (`MAX_SARIF_BYTES = 50 * 1024 * 1024`). If the SARIF file exceeds 50 MB, reading is aborted, an alert is logged to the Output Channel, and diagnostic loading is safely skipped.
- **Scan Summary (`summary.json`)**: Capped at **20 MB** (`MAX_SUMMARY_BYTES = 20 * 1024 * 1024`). If the summary file exceeds 20 MB, reading is safely aborted without crashing the extension host.
### SARIF to VS Code Mapping

| SARIF Field | VS Code Diagnostic |
|-------------|-------------------|
| `result.ruleId` | `Diagnostic.code` |
| `result.level` | `DiagnosticSeverity` (error/warning/info) |
| `result.message.text` | `Diagnostic.message` |
| `result.locations[].physicalLocation` | `Diagnostic.range` + `Diagnostic.source` |

### Severity Mapping

| SARIF Level | VS Code Severity |
|-------------|-----------------|
| `error` | `DiagnosticSeverity.Error` |
| `warning` | `DiagnosticSeverity.Warning` |
| `note` | `DiagnosticSeverity.Information` |

## Discovered SQL Queries Tree View

The `dataguard.sqlQueriesView` view in the DataGuard Activity Bar displays an inventory of discovered inline SQL queries and C# model mappings across the workspace (supporting string literals, interpolations, constants, local variables, fields, and property SQL resolution including expression-bodied properties, with call-stack cycle detection preventing infinite recursion across cyclic dependencies):
- **File Grouping**: Queries are grouped by source file with query count indicators.
- **Operation Badges**: Visual icons identify query operations (Select, Insert, Update, Delete, Join).
- **Mapping Badges**: Shows status badges (`matched`, `partial`, `unmapped`, `untyped`).
- **Jump to Source & Model**: Clicking any query node immediately opens the file and places the cursor on the exact query line. The view resolves relative and absolute source file paths against workspace root folders (`resolveWorkspaceFilePath`), checking file existence and workspace containment (`isPathInWorkspaceFolder`) before navigation. The view also resolves `targetTypeLocation` to allow jumping directly to the target C# DTO declaration line (`{ file: string, line: number }`).
- **Mapping Badges & Operation Breakdown**: Each query displays its operation (`Select`, `Insert`, `Update`, `Delete`, `Join`) alongside status badges (`matched`, `partial`, `unmapped`, `untyped`). Nodes highlight matched column-to-property pairs, unmapped columns (in SQL query projections but missing from C# models), and unmapped properties (in C# models but missing from SQL queries).
- **Case-Insensitive Windows Path Comparison**: On Windows (`process.platform === "win32"`), source file matching and grouping in the tree provider performs case-insensitive path comparisons (`fullPath.toLowerCase() === targetPath.toLowerCase()`), ensuring reliable node grouping and navigation regardless of drive letter or file path casing differences.
- **Tooltip SQL Redaction**: Query tooltips render formatted Markdown codeblocks with sensitive credentials, passwords, tokens, and connection strings thoroughly redacted using `redactForUi`. Tooltips also display target DTO, mapping status, action, referenced tables, matched columns/properties, and optional unmapped columns/properties.
- **View Title Actions**: The tree view header title menu (`menus["view/title"]`) contributes three inline actions:
  1. `dataguard.refreshQueries` (`group: navigation@1`): Refresh discovered queries.
  2. `dataguard.scanProject` (`group: navigation@2`): Run full project SQL & mappings scan.
  3. `dataguard.verifyShape` (`group: navigation@3`): Verify discovered SQL query result shapes against the live database schema.

### TypeScript Interfaces (`ScanReport` & `QueryScanItem`)

```typescript
export interface QueryLocation {
    file?: string;
    line?: number;
}

export interface QueryScanItem {
    sql: string;
    location?: QueryLocation;
    targetTypeLocation?: QueryLocation;
    operation: string;
    tables: string[];
    targetType?: string | null;
    mappingStatus: "matched" | "partial" | "unmapped" | "untyped";
    action: string;
    columns: string[];
    properties: string[];
    unmappedColumns?: string[];
    unmappedProperties?: string[];
}

export interface ScanConnectionItem {
    name: string;
    provider: string;
    hint?: string;
}

export interface ScanReport {
    filesScanned: number;
    queriesFound: number;
    connectionsFound: number;
    violationsCount: number;
    connections: ScanConnectionItem[];
    queries: QueryScanItem[];
}
```
## Drift Dashboard: SQL ↔ C# Mappings Tab

The Contract Drift Dashboard (`dataguard.openDashboard`) provides interactive tabs:
1. **Contract Drift Findings**: Virtualized findings list with search and quick fixes.
2. **SQL ↔ C# Mappings**: Tabular view of all discovered SQL queries with:
   - Mapping status badges (`matched`, `partial`, `unmapped`, `untyped`)
   - Target C# DTO type names with direct navigation via `targetTypeLocation`
   - Matched columns and properties list
   - Highlighted unmapped columns and unmapped properties
   - Interactive jump-to-source navigation on query click (`jumpToLocation`) with relative and absolute path resolution across active workspace folders (`resolveWorkspaceFilePath`), strict workspace boundary containment (`isPathInWorkspace`), and regular file verification (`fs.existsSync(resolvedPath) && fs.statSync(resolvedPath).isFile()`) to prevent directory traversal, arbitrary directory opening, or invalid file navigation.
   - **Strict `rawSql` / `rawFile` Suppression & Credential Redaction**: When preparing the scan report for webview serialization, `dashboard-view.ts` strictly strips and never emits raw, unredacted SQL or unredacted file strings (`rawSql`, `rawFile`) into webview DOM elements, data attributes, or client scripts. All displayed SQL queries and connection hints are sanitized and credential-redacted via `redactForUi` before injection into the webview markup, preventing accidental leakage of database passwords, tokens, or raw queries.
3. **Content Security Policy (CSP) & Idempotent Entity-Aware HTML Escaping**:
   - **Strict Webview CSP**: The webview header enforces a strict Content Security Policy (`buildWebviewCsp`) configured as `default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: https://fonts.gstatic.com; img-src data:;`. Restricting `img-src` to `data:;` strictly prevents outbound image requests or external data exfiltration via image tags.
   - **Idempotent Entity-Aware HTML Escaping**: To prevent XSS vulnerabilities in webview rendering, all user-provided data, diagnostic messages, and SQL snippets are sanitized using `escapeHtml` (server-side) and `escapeHtmlClient` (client-side in webview script). The escaping logic is idempotent and entity-aware: it uses a negative lookahead regex `/&(?!([a-zA-Z0-9]+|#[0-9]{1,6}|#[xX][0-9a-fA-F]{1,6});)/g` to escape bare ampersands without double-escaping existing valid HTML entities or numeric character references.
   - **FindingId & IPC Message Validation**: To defend against webview-to-extension IPC message tampering, `applyQuickFix` and `jumpToFinding` validate that `message.findingId` is a non-empty string and strictly matches an existing finding currently tracked in the active session (`this.currentFindings.some(f => f.id === message.findingId)`). Untracked or malformed finding IDs are rejected before any automated quick-fix or document opening command is dispatched.

## Status Bar Integration

Shows validation status in the VS Code status bar:

| State | Text | Color |
|-------|------|-------|
| Idle | `$(check) DataGuard` | Default |
| Running | `$(sync~spin) DataGuard: Validating...` | Blue |
| Pass | `$(check) DataGuard: 0 issues` | Green |
| Fail | `$(warning) DataGuard: N issues` | Yellow |
| Error | `$(error) DataGuard: Failed` | Red |

Clicking the status bar item opens the Output Channel with detailed results.

## Output Channel

A dedicated `DataGuard` output channel displays:
- CLI command being executed
- Raw CLI stdout/stderr
- Parsed violation summary
- Error messages and stack traces (when verbose mode enabled)

All output passes through the security module before display.

## Process Management

### Spawn

Processes are spawned using Node.js `child_process.spawn()` with safe process execution settings:
- `shell: false` to eliminate shell injection vulnerabilities.
- Deterministic argument vectors with validated inputs.
- Passing connection strings strictly via the process environment (`DATAGUARD_CONNECTION_STRING`), never as command-line arguments.
- Workspace trust verification (`vscode.workspace.isTrusted`) before spawning any CLI command.
- `windowsHide: true` on Windows, and process group detachment on POSIX for reliable process tree termination.

```typescript
const args = buildCliArguments(command, workspaceFolder.uri.fsPath, provider, configPath, outputPath);
const child = spawn(cliPath, args, {
    cwd: workspaceFolder.uri.fsPath,
    env: connectionString === undefined
        ? undefined
        : { ...process.env, DATAGUARD_CONNECTION_STRING: connectionString },
    detached: process.platform !== "win32",
    shell: false,
    windowsHide: true,
});
```

The extension leverages `--project` to extract C# contracts and inline SQL directly without compiled assemblies, monitors real-time milestones via `--progress` NDJSON on `stderr`, and reads supplementary metrics from `summary.json` (supporting both `ScanReport` and `verify-shape` output schemas with `filesScanned`, `queriesFound`, and `connectionsFound`).

### Stream Error Handling & NDJSON Buffering

Standard I/O streams are handled with resilient lifecycle protections:
- **Stream Error Suppression**: Explicit error listeners (`child.stdout?.on("error")`, `child.stderr?.on("error")`, `child.on("error")`) prevent unhandled stream exceptions from crashing the extension host when the CLI exits abruptly or pipes disconnect.
- **Chunk Decoding & Line Buffering**: Streaming chunks from `stdout` and `stderr` are decoded via `StringDecoder("utf8")` and buffered across chunk boundaries to correctly parse multi-chunk JSON progress events.
- **Progress Buffer Overflow Protection**: In `processProgressText`, trailing incomplete line fragments in `state.buffer` are strictly monitored against a maximum size limit (`MAX_PROGRESS_BUFFER = 1 MiB`). If incoming text without newline delimiters exceeds this threshold, the buffer is safely cleared (`state.buffer = ""`) rather than sliced, preventing unbounded memory growth or corrupted partial JSON parsing.
- **Output Bound**: Output captured in memory is capped at a strict upper bound (`MAX_CLI_OUTPUT = 1 MiB`) to prevent memory exhaustion from verbose CLI runs.
- **NDJSON Resilience**: Malformed or non-JSON log lines on `stderr` are caught gracefully without interrupting progress monitoring or diagnostics loading.
### Termination & Zombie Process Fallback

The `dataguard.cancelValidation` command cancels the active DataGuard process tree with cross-platform termination protections:
- **Windows Process Liveness & PID Reuse Prevention**: Before dispatching termination commands, the coordinator verifies that the child process is still actively running (`child.pid && child.exitCode === null && child.signalCode === null && !child.killed`). This prevents inadvertent termination of unrelated system processes if the Windows OS recycles the PID after process exit. It then dispatches `taskkill /pid <pid> /T /F` without a shell. If `taskkill` fails or errors, it falls back immediately to `child.kill()`, preventing orphaned zombie CLI processes.
- **POSIX SIGKILL Timeout & Exit Verification**: Signals the detached process group via `process.kill(-child.pid, "SIGTERM")`, falling back to `child.kill("SIGTERM")`. If the process group does not terminate within 3 seconds, an unreferenced timer performs an exit check (`child.exitCode !== null || child.signalCode !== null || child.killed`) before issuing `process.kill(-child.pid, "SIGKILL")` (with `child.kill("SIGKILL")` fallback).

### Concurrency & Cancellation Semantics

Process concurrency and cancellation obey strict guarantees:
- **Global Singularity**: Only one DataGuard command process runs globally at a time. Starting Validate, Assess, Scan, or Verify Shape cancels and replaces any prior run, including one originating from another workspace.
- **Atomic Reservation Tokens (`nextReservation` / `isReservationCurrent`)**: To eliminate race conditions where rapid back-to-back command invocations could spawn uncoordinated, orphan child processes, the `RunCoordinator` manages atomic reservation tokens without duplicate increments via `nextReservation()`. Before and immediately after spawning child processes (as well as after temporary directory allocation), the extension checks `isReservationCurrent(reservationToken)`. If a subsequent command was requested in the interim, the stale spawn is immediately aborted, the child process tree killed, and the temporary output directory pruned before processing results.
- **Cancellation Propagation**: Invoking `dataguard.cancelValidation` or launching a superseding command terminates the active child process and its entire process tree. The run coordinator marks the execution state as cancelled (`run.cancelled = true`), resets the status bar indicator to `idle`, and suppresses diagnostic updates for aborted runs.
- **Exit Code Handling**: Clean cooperative cancellation in the CLI returns exit code `130`, which the extension recognizes as an intentional user/coordinator cancellation rather than a fatal process failure.
- **RunCoordinator Cancel Resilience & Race Condition Prevention**: The `RunCoordinator` encapsulates stateful command execution with atomic cancellation resilience. If a running command is cancelled or superseded while in-flight (during pre-spawn configuration, child process startup, stderr NDJSON streaming, or temporary output file parsing), the coordinator dispatches asynchronous process tree termination without blocking the extension event loop, sets `run.cancelled = true`, and safely skips diagnostic collection updates. Any lingering standard I/O events, delayed exit callbacks, or unhandled stream pipe errors from the terminated child process are discarded safely without triggering uncaught exceptions or inconsistent status bar states.

To prevent file locks, collision across workspaces, and partial file reads:
1. **Isolated Output Directory**: Each execution generates an isolated temporary directory via `fs.mkdtemp(path.join(os.tmpdir(), "dataguard-"))`.
2. **Protected Temp Files**: SARIF diagnostic reports and companion `summary.json` files are written into this dedicated temporary folder.
3. **Guaranteed Cleanup**: Upon command completion, error, or cancellation, a `finally` block purges the temporary directory via `fs.rm(outputDirectory, { recursive: true, force: true })`.
4. **Windows EBUSY Resilience**: On Windows systems where file indexing or virus scanners briefly lock released files, temporary cleanup suppresses `EBUSY` exceptions to ensure the extension state machine remains stable and operational.
## Security Module

### redactSensitiveText

Redacts connection strings and sensitive data from output before displaying in the Output Channel:

```typescript
const SENSITIVE_ASSIGNMENT = /(?:"|'|(?<![?&])\b)(password|pwd|secret|token|api[_ -]?key|client[_ -]?secret|access[_ -]?token|refresh[_ -]?token|connection\s*string)(?:"|'|\b)\s*[:=]\s*(?:bearer\s+)?(?:"[^"]*"|'[^']*'|\{[^}]*\}|[^;\r\n,\s]+(?:\s+[^;\r\n,\s]+)*(?=\s*(?:[;,]|\r?\n))|[^;\r\n,\s]+)/gi;
const AUTHORIZATION_BEARER = /\bauthorization\s*:\s*bearer\s+[^\s,;]+/gi;
const URI_CREDENTIALS = /([a-z0-9+.-]+:\/\/[^\/\s:]+:)([^/\s]+)(@)/gi;

export function redactSensitiveText(value: string): string {
    return value
        .replace(SENSITIVE_ASSIGNMENT, "$1=[REDACTED]")
        .replace(AUTHORIZATION_BEARER, "Authorization: Bearer [REDACTED]")
        .replace(URI_CREDENTIALS, "$1[REDACTED]$3");
}
```

Applied to every line written to the Output Channel, UI webviews, and loggers, ensuring credentials, authorization headers, and URI userinfo (`protocol://user:password@host`) never leak into editor panels, logs, or screenshots. The negative lookbehind `(?<![?&])\b` ensures URL query parameter credentials like `&token=xyz` or `?password=xyz` are matched accurately without corrupting adjacent query string keys.

**Linear Non-Backtracking Credential Redaction:**
To eliminate the risk of Regular Expression Denial of Service (ReDoS) from catastrophic backtracking, sensitive key-value masking (`SENSITIVE_ASSIGNMENT` and `SENSITIVE_KV_REGEX`) avoids nested unbounded quantifiers and complex multi-token lookaheads. The regex relies on deterministic character-class scanning (`[^;\r\n,\s]+`) with linear non-backtracking lookahead anchors (`(?=\s*(?:[;,]|\r?\n))`). Unquoted values containing spaces are resolved in linear time ($O(N)$ with respect to line length), preventing CPU lockup or extension host freezes when processing multi-megabyte log bursts or deeply structured connection strings.

### Live Shape Verification Protections

When running `dataguard.verifyShape`:
- **DML & Transaction CTE Guard**: Queries containing data-modifying statements (`INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `MERGE`), transaction control commands (`COMMIT`, `ROLLBACK`, `SAVEPOINT`), or administrative commands (`GRANT`, `REVOKE`), including those wrapped in Common Table Expressions (CTEs), are blocked from live execution to protect databases from unintentional writes and transaction state corruption.
- **Query Wrapper Breakout Protections & Character-Level Comment Checking**: Live query schema providers (Oracle and PostgreSQL) wrap arbitrary queries in execution subquery wrappers. To prevent breakout vulnerabilities (such as arbitrary function execution, stacked queries, or subquery escapes), providers strictly enforce parenthesis balance validation (`depth >= 0` at all times, `depth == 0` at termination), perform character-level `HasUnclosedBlockComment` protection against live query breakout (rejecting unclosed `/*` comment blocks while respecting delimiters in quoted strings and identifiers), and reject unquoted semicolons before wrapping. If any breakout attempt is detected, the provider falls back safely to syntactic column extraction without executing the query live.
- **Dynamic SQL & Anonymous PL/SQL Block Guards**: Statements calling dynamic execution (`EXEC`, `EXECUTE`, `EXECUTE IMMEDIATE`, `sp_executesql`), anonymous PL/SQL blocks (`BEGIN ... END;`, `DO $$ ... $$`), or database administrative commands (`CALL`, `DO`, `COPY`, `VACUUM`, `LOCK`, `REINDEX`) are intercepted and rejected from live database invocation.
- **Single-Pass Comment & String Literal Stripping (with Oracle Q-Quotes & Nested Comment Depth Tracking)**: SQL comments (`-- ...` and `/* ... */`) and string literals are stripped in a single lexical pass (`ColumnShapeMatchRule.StripCommentsAndLiterals`) before semicolon and statement keyword inspection. The parser preserves characters within bracket identifiers (`[My--Column]`, including escaped `]]`) and backtick identifiers (`` `user_orders` ``), preventing hyphens or slashes within delimited column and table identifiers from being mistakenly treated as comments. The comment stripper actively tracks nested block comment depth (`commentDepth`) to termination, preventing comment-hiding injection in dialects supporting nested block comments (such as T-SQL and PostgreSQL). In addition to standard single-quoted literals (`'(?:''|[^'])*'`) and PostgreSQL dollar-quoted strings (`$(?<tag>[A-Za-z0-9_]*)$.*?$\k<tag>$` with `Singleline`), the lexical engine provides comprehensive support for Oracle alternative quoting (Q-quotes: `q'...'` and `Q'...'`) across dialect checking and live query validation. It dynamically pairs delimiters (`q'[...]'`, `q'{...}'`, `q'(...)'`, `q'<...>'`, `q'!...!^'`), replacing them with safe empty literal tokens (`''`). This prevents unescaped apostrophes inside Q-quoted literals from corrupting tokenizer state, prevents live query breakout, and prevents parameter sniffers or dialect checkers from mistaking literal contents for query parameters or SQL keywords.
- **Sanitized Error Messages**: Diagnostic warnings generated when shape determination encounters database errors (`LiveSqlShapeValidationRule`) sanitize exception messages using `SanitizeErrorMessage`. Connection string parameters (`password=`, `pwd=`, `user id=`, `uid=`, `secret=`, `token=`) and URI credentials (`protocol://user:password@host`) are redacted to `[REDACTED]` to prevent credential leakage into SARIF findings, editor diagnostics, or logs.
- **Table-Prefixed Wildcard Support**: Fallback wildcard analysis (`SelectStarUsageRule.ContainsSelectStar`) accurately detects and resolves table-prefixed wildcards (`SELECT T.*`, `SELECT [tbl].*`, ``SELECT `db`.`tbl`.*``) across top-level and inner subqueries, preventing missing property false alarms when querying tables via wildcards.
- **Stacked Query Guard**: Queries with unquoted semicolons (stacked statements) are prevented from live execution.
- **Syntactic Fallback, Space-less Arithmetic Aliases & Keyword Handling**: If a live connection or schema inspection throws an exception, or if a query is blocked by the DML, dynamic SQL, query wrapper breakout guard, or procedural block guard, the provider falls back safely to syntactic AST/regex column extraction (`ColumnShapeMatchRule.ExtractColumnNamesFromSql`) to ensure queries are still inventoried without failing the command. Syntactic extraction accurately resolves trailing column aliases without the `AS` keyword—including space-less arithmetic aliases such as `Price*Quantity TotalCost` or `a+b c`—by verifying that the penultimate token is an operand rather than an arithmetic operator (`+`, `-`, `*`, `/`) before binding the trailing identifier as the column name. In addition, unaliased function calls (such as `COUNT(1)` or `MAX(Salary)`) are extracted by function identifier name, and non-structural SQL keywords are explicitly filtered out.
The extension enforces multi-layer workspace containment guarantees across configuration, diagnostic ingestion, and dashboard navigation:

#### resolveWorkspaceConfigPath

Resolves the `.dataguard.yml` config path relative to the trusted workspace folder and forbids directory traversal or external paths:

```typescript
export function resolveWorkspaceConfigPath(workspacePath: string, configuredPath: string): string {
    if (path.isAbsolute(configuredPath)) {
        throw new Error("dataguard.configPath must be relative to the trusted workspace folder.");
    }
    const resolved = path.resolve(workspacePath, configuredPath);
    const relative = path.relative(workspacePath, resolved);
    if (relative === "" || (!relative.startsWith(".." + path.sep) && relative !== ".." && !path.isAbsolute(relative))) {
        return resolved;
    }
    throw new Error("dataguard.configPath must remain inside the trusted workspace folder.");
}
```

#### resolveWorkspaceSarifPath & isPathInWorkspaceFolder

- **SARIF Location Containment**: `resolveWorkspaceSarifPath` ensures all artifact locations referenced in SARIF reports resolve strictly inside the trusted workspace root. Non-file URI schemes, sibling-prefix escapes (e.g. `/workspace/service-other`), and `..` directory traversal attempts are rejected.
- **Dashboard Navigation & Traversal Guard**: `isPathInWorkspaceFolder` validates that file jump targets from the SQL-to-C# Drift Dashboard or SQL Queries tree view reside inside active workspace folders. Rather than naive prefix checks (which could falsely flag folders starting with dots, such as `..secret`), it uses a refined parent directory traversal check (`rel === ".." || rel.startsWith(".." + path.sep) || rel.startsWith("../") || rel.startsWith("..\\")`). If no workspace folders are open or if the relative path escapes the root, it fails closed (`return false`) to prevent arbitrary file opening or directory escapes.
- **Canonical Symlink & Realpath Boundary Protection**: To defend against symlink escape attacks (such as symlinks or junction points inside a workspace pointing to sensitive system directories or external source trees), `isPathInWorkspaceFolder` performs canonical filesystem resolution using `fs.realpathSync`. For existing target paths or non-existent target files with existing ancestor directories, the resolver walks up to the nearest existing directory, resolves the canonical real path via `fs.realpathSync`, reconstructs the target, and validates that the canonical path strictly resides within the real, resolved workspace directory (`realBase`), ensuring symlink escapes outside the workspace boundary fail closed (`return false`).
## Extension Manifest

```json
{
    "name": "dataguard",
    "displayName": "DataGuard - Contract Validator",
    "description": "Validate Entity ↔ SP/Raw SQL contracts in .NET projects",
    "version": "0.1.0",
    "engines": { "vscode": "^1.85.0" },
    "categories": ["Linters", "Programming Languages"],
    "activationEvents": [
        "onLanguage:csharp",
        "workspaceContains:**/.dataguard.yml"
    ],
    "main": "./out/extension.js",
    "contributes": {
        "commands": [
            {
                "command": "dataguard.runValidation",
                "title": "DataGuard: Run Validation"
            },
            {
                "command": "dataguard.cancelValidation",
                "title": "DataGuard: Cancel Validation"
            },
            {
                "command": "dataguard.assess",
                "title": "DataGuard: Assess Workspace"
            },
            {
                "command": "dataguard.refreshSnapshot",
                "title": "DataGuard: Refresh Snapshot"
            },
            {
                "command": "dataguard.createBaseline",
                "title": "DataGuard: Create Baseline"
            },
            {
                "command": "dataguard.scanProject",
                "title": "DataGuard: Scan Project SQL & Mappings",
                "icon": "$(search)"
            },
            {
                "command": "dataguard.refreshQueries",
                "title": "DataGuard: Refresh SQL Queries",
                "icon": "$(refresh)"
            },
            {
                "command": "dataguard.verifyShape",
                "title": "DataGuard: Verify SQL Shapes Against Database",
                "icon": "$(check)"
            }
        ],
        "menus": {
            "view/title": [
                {
                    "command": "dataguard.refreshQueries",
                    "when": "view == dataguard.sqlQueriesView",
                    "group": "navigation@1"
                },
                {
                    "command": "dataguard.scanProject",
                    "when": "view == dataguard.sqlQueriesView",
                    "group": "navigation@2"
                },
                {
                    "command": "dataguard.verifyShape",
                    "when": "view == dataguard.sqlQueriesView",
                    "group": "navigation@3"
                }
            ]
        },
        "configuration": {
            "title": "DataGuard",
            "properties": {
                "dataguard.configPath": {
                    "type": "string",
                    "default": ".dataguard.yml",
                    "description": "Path to the DataGuard configuration file"
                },
                "dataguard.provider": {
                    "type": "string",
                    "enum": ["sqlserver", "oracle", "mysql", "postgresql"],
                    "default": "sqlserver",
                    "description": "Database provider for validation"
                }
            }
        }
    }
}
```

## Typical Workflow

```mermaid
sequenceDiagram
    participant Dev as Developer
    participant VSC as VS Code Extension
    participant CLI as dataguard CLI
    participant DB as Database

    Dev->>VSC: Ctrl+Shift+P → Run Validation
    VSC->>VSC: Resolve config path + provider
    VSC->>CLI: spawn validate --format sarif --output tmp.sarif
    VSC->>Dev: Status bar: Validating...
    CLI->>DB: Read schema / contracts
    DB-->>CLI: Metadata
    CLI-->>VSC: Exit code 0/1 + SARIF file
    VSC->>VSC: Parse SARIF → Diagnostics
    VSC->>Dev: Problems panel populated
    VSC->>Dev: Status bar: N issues
```

## Development

### Building

```bash
cd extensions/vscode
npm install
npm run compile
```

### Testing

```bash
npm test
npm run test:extension-host
```

### Packaging

```bash
npx vsce package
```

This produces a `.vsix` file installable via `code --install-extension dataguard-0.1.0.vsix`.

## Limitations

- Requires the `dataguard` CLI on PATH (or configured via settings)
- One validation at a time; concurrent saves queue or cancel
- SARIF file paths are resolved relative to the workspace root; files outside the workspace are skipped in the Problems panel
