---
status: completed
---

# DataGuard Visual Studio Extension — UX & Logging Overhaul

## Context

The DataGuard Visual Studio extension has two critical usability problems:

1. **UX black-box**: The menu items under Tools → DataGuard ("Run Validation", "Cancel Validation", "Assess Workspace", "View Diagnostic Logs", "Validation Rules & Descriptions") are opaque. Users click a command and get no meaningful feedback: no confirmation of what was selected, no progress indication in the Output Window during execution, no explanation of what the tool is doing, no summary of what it scanned, and no guidance on what the results mean. The Output Window shows only start/end timestamps and exit codes. The "Validation Rules & Descriptions" command dumps a wall of text with no interactivity.

2. **Logging black-box**: The log file (`%APPDATA%\DataGuard\logs\dataguard-vs.log`) records only initialization banners and one-line command start/finish entries. It contains zero information about: which C# source files were scanned, which SQL statements were discovered, which entities/DTOs were matched, which rules were applied against which contracts, why "no SARIF diagnostics" was produced (no contracts found? no connection? no violations?). A human reading the log cannot diagnose whether the extension even attempted useful work.

The root cause for both: the VS extension calls `DrainAsync` on CLI stdout/stderr (deliberately discarding output for security), and the CLI's internal pipeline — contract acquisition, source scanning, rule execution — emits no structured progress events that the VS extension could consume safely.

## Approach

### Step 1: Add structured progress output to CLI (`--progress` flag)

The CLI already supports `--format sarif` for results. Add a `--progress` flag (or `--format sarif` implicitly enables it) that writes **line-delimited JSON progress events to stderr** during execution. These events contain no credentials or connection strings — only structural metadata.

**Event schema** (new file `src/DataGuard.Core/Reporting/ProgressEvent.cs`):
```csharp
public enum ProgressEventKind
{
    PhaseStarted,      // "Acquiring contracts…", "Scanning source files…", "Validating rules…"
    PhaseCompleted,
    SourceFileScanned,  // path, sql-count, entity-count
    ContractDiscovered, // contract type, name, parameter count
    RuleExecuted,       // rule ID, contract ID, violation count
    Summary             // totals
}

public record ProgressEvent(
    ProgressEventKind Kind,
    string Phase,
    string? Detail,
    IReadOnlyDictionary<string, object?>? Data);
```

**Emitter** (new file `src/DataGuard.Core/Reporting/ProgressEmitter.cs`): A simple class that writes one JSON line per event to a `TextWriter` (stderr). It has a `bool Enabled` guard so zero overhead when not requested. The CLI's `validate` and `assess` commands inject it into the pipeline.

**Integration points in CLI `Program.cs`**:
- Add `--progress` `Option<bool>` to `validateCommand` and `assessCommand`.
- In `validateCommand.SetAction`: after `AcquireContractsAsync` returns, emit `PhaseCompleted` with contract counts. Before `ValidateContractsAsync`, emit `PhaseStarted("Validating rules")`. After, emit `Summary` with violation counts by severity.
- In `BuildContractsAsync`: emit `ContractDiscovered` for each `ContractDescriptor` added.
- In `ValidateContractsAsync`: emit `RuleExecuted` for each rule/contract pair result.

**No existing function serves this purpose.** `TelemetryCollector` is for metrics export, not user-facing progress. `DiagnosticEmitter` is for final results only.

### Step 2: VS extension reads progress events from CLI stderr

Currently `DataGuardPackage.RunCliAsync` calls `DrainAsync(process.StandardError)` which discards all stderr. Change this:

- Replace `DrainAsync(process.StandardError)` with a new method `ReadProgressAsync(StreamReader stderr)` that reads lines, attempts to parse each as a `ProgressEvent` JSON, and for recognized events calls `WriteOutputAsync` with a human-friendly formatted message. Lines that fail JSON parse are treated as plain diagnostic text and logged with `[DataGuard CLI]` prefix (still redacted).
- When `--progress` is passed to CLI, stderr will contain these JSON lines. Unrecognized lines are redacted and logged as before.
- `DrainAsync(process.StandardOutput)` remains unchanged — stdout is still fully drained without display.

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs`
- Modify `RunCliAsync`: add `--progress` to CLI arguments.
- Replace the `stderrDrainTask` with the new progress reader.
- Format each event type as a human-readable Output Window line.

**Formatted output examples**:
```
[DataGuard] ▶ Run Validation — scanning your solution for database contract issues…
[DataGuard] ✔ Configuration loaded from .dataguard.yml (provider: sqlserver)
[DataGuard] ▶ Phase: Acquiring contracts from database/snapshot…
[DataGuard] ✔ Contracts acquired: 12 stored procedures, 8 entities, 3 raw SQL
[DataGuard] ▶ Phase: Validating 17 rules against 23 contracts…
[DataGuard]   Rule DG002 (Parameter Count & Type Match): checked 12 contracts → 2 violations
[DataGuard]   Rule DG004 (Result Set Column Shape): checked 8 contracts → 0 violations
[DataGuard]   …
[DataGuard] ✔ Validation complete: 3 errors, 5 warnings (455 ms)
[DataGuard]   → 8 diagnostics loaded into Error List
```

### Step 3: Improve menu command UX with pre/post feedback

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs`

**3a. "Run Validation" command** — add pre-flight banner and post-run summary:

Before launching CLI process, write to Output Window:
```
═══════════════════════════════════════════════════════════
 DataGuard — Run Validation
═══════════════════════════════════════════════════════════
 What: Validates C# ↔ Database contracts (SP parameters,
       result set shapes, column types, naming, SQL dialect)
 Scope: Current solution ({solutionName})
 Config: {configPath}
 Rules: {enabledCount} enabled, {disabledCount} disabled
═══════════════════════════════════════════════════════════
```

After completion, write summary block:
```
═══════════════════════════════════════════════════════════
 Result: {errorCount} errors, {warningCount} warnings
 Action: Open Error List (Ctrl+\, E) to see details.
         Click any item to jump to the source location.
 Docs:   Tools → Options → DataGuard → Validation Rules
═══════════════════════════════════════════════════════════
```

The pre-flight banner requires reading `DataGuardRulesOptionsPage` to count enabled/disabled rules, and resolving the solution directory + config path — all data already available in `RunCliAsync`.

**3b. "Assess Workspace" command** — same pattern with assess-specific text:
```
 What: Environment/dependency/config assessment
       (NuGet advisories, .NET version, build config)
```

**3c. "Cancel Validation" command** — after cancellation, write confirmation:
```
[DataGuard] ✋ Validation cancelled by user. No diagnostics were produced.
```

**3d. "View Diagnostic Logs" command** — before opening, write log location and tip:
```
[DataGuard] 📄 Opening log file: {logPath}
[DataGuard]    Tip: Search for [ERROR] or [WARN] to find issues.
[DataGuard]    Tip: Each validation run is delimited by ═══ borders.
```

**3e. "Validation Rules & Descriptions" command** — improve the existing dump in `ExecuteViewRules`:

Currently it outputs a flat text block. Change to a formatted table with status indicators and actionable instruction:

```
═══════════════════════════════════════════════════════════
 DataGuard Validation Rules — Current Configuration
═══════════════════════════════════════════════════════════

 Category: Contract & Schema Alignment
 ───────────────────────────────────────────────────────
  ✅ DG002: Parameter Count & Type Match
     Validates C# call parameters against DB SP/queries.
  ✅ DG003: Parameter Direction (In/Out/Return)
     Validates parameter directions for SP outputs.
  ❌ DG006: Naming Convention Compliance  [DISABLED]
     Verifies snake_case ↔ PascalCase mapping.
  …

═══════════════════════════════════════════════════════════
 ✅ = enabled    ❌ = disabled
 To change: Tools → Options → DataGuard → Validation Rules
═══════════════════════════════════════════════════════════
```

### Step 4: Enrich log file with validation run details

**File**: `src/DataGuard.VisualStudio/DataGuardLogger.cs`

Add a new method `LogValidationRun(...)` that writes a structured run delimiter to the log file:

```csharp
public static void LogValidationRun(
    string command,
    string solutionDirectory,
    string configPath,
    IReadOnlyList<string> disabledRules,
    int exitCode,
    long elapsedMs,
    int diagnosticCount)
```

Format in log file:
```
════════════════════════════════════════════════════════════════
[2026-09-18 01:31:31 UTC] VALIDATION RUN
  Command       : validate
  Solution      : D:\Projects\MyApp
  Config        : D:\Projects\MyApp\.dataguard.yml
  Disabled Rules: DG006, DG017
  Exit Code     : 0
  Duration      : 455 ms
  Diagnostics   : 8 loaded into Error List
════════════════════════════════════════════════════════════════
```

**Call site**: `DataGuardPackage.RunCliAsync`, after `PublishSarifAsync` and before the final "completed" message.

### Step 5: Log progress events to file

When `ReadProgressAsync` (Step 2) receives parsed progress events, also log them via `DataGuardLogger.LogInfo`. This means the log file will contain the full scan trace:

```
[2026-09-18 01:31:31 UTC] [INFO] Phase: Acquiring contracts…
[2026-09-18 01:31:31 UTC] [INFO] Contracts acquired: 12 SPs, 8 entities, 3 raw SQL
[2026-09-18 01:31:31 UTC] [INFO] Phase: Validating rules…
[2026-09-18 01:31:31 UTC] [INFO] Rule DG002: checked 12 contracts, 2 violations
[2026-09-18 01:31:31 UTC] [INFO] Rule DG004: checked 8 contracts, 0 violations
```

### Step 6: Explain exit codes in Output Window

Currently the output says `completed with exit code 4` which is meaningless to users. Map exit codes to human text:

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs`, in `RunCliAsync` after getting `process.ExitCode`:

```csharp
private static string ExplainExitCode(string command, int exitCode) => (command, exitCode) switch
{
    (_, 0) => "✔ No issues found.",
    ("validate", 1) => "⚠ Validation found errors. See Error List.",
    ("validate", 2) => "❌ Invalid arguments or configuration. Check .dataguard.yml.",
    ("validate", 3) => "⚠ Validation incomplete — contract acquisition failed (no DB connection or snapshot).",
    ("assess", 1) => "⚠ Assessment found findings. See Error List.",
    ("assess", 4) => "⚠ Assessment completed with tool errors (check config/permissions).",
    (_, 130) => "✋ Cancelled by user.",
    _ => $"Unexpected exit code {exitCode}.",
};
```

Write this explanation immediately after the exit code line.

### Step 7: Status bar integration (VS extension)

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs`

Use the VS status bar (`IVsStatusbar`) to show running state:

- On command start: `SetText("DataGuard: Validating…")` with animation.
- On completion: `SetText("DataGuard: ✔ 3 errors, 5 warnings")` for 10 seconds, then clear.
- On cancel: `SetText("DataGuard: Cancelled")` for 5 seconds.

Obtain `IVsStatusbar` via `GetServiceAsync(typeof(SVsStatusbar))`. This is a well-known VS API pattern — no new dependencies.

## Critical files & anchors

| File | Region | Reason |
|------|--------|--------|
| `src/DataGuard.VisualStudio/DataGuardPackage.cs` | `RunCliAsync` (L275-393), `ExecuteViewRules` (L226-263), `CancelValidationAsync` (L540-558) | All command handlers that need UX improvement |
| `src/DataGuard.VisualStudio/DataGuardLogger.cs` | `WriteEntry` (L303-321) | Add structured run logging |
| `src/DataGuard.Cli/Program.cs` | `validateCommand.SetAction` (L133-298), `assessCommand.SetAction` (L1077-1179) | Inject progress event emission |
| `src/DataGuard.Core/Reporting/` | New files | `ProgressEvent.cs`, `ProgressEmitter.cs` |

## Verification

1. **Build**: `dotnet build src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -c Release` — must compile without errors.

2. **CLI progress output**: Run `dataguard validate --config .dataguard.yml --progress 2>progress.txt` and verify `progress.txt` contains JSON lines with `Kind`, `Phase`, `Detail` fields. Verify no connection strings or passwords appear in the progress output.

3. **Output Window UX**: Install VSIX in VS Experimental Instance. Click Tools → DataGuard → Run Validation. Verify:
   - Pre-flight banner appears immediately with solution name, config path, rule counts.
   - Progress lines appear during execution showing phases, contract counts, rule results.
   - Post-run summary appears with error/warning counts and actionable instructions.
   - Exit code is translated to human-readable explanation.

4. **Log file enrichment**: After running validation, open `%APPDATA%\DataGuard\logs\dataguard-vs.log`. Verify:
   - Structured run delimiter block with command, solution, config, exit code, duration.
   - Progress event lines showing contract acquisition and rule execution details.
   - Searchable `═══` delimiters between runs.

5. **"Validation Rules" command**: Click Tools → DataGuard → Validation Rules & Descriptions. Verify formatted table with ✅/❌ status, category grouping, and actionable footer about how to change settings.

6. **Edge case — no config file**: Run validation without `.dataguard.yml`. Verify exit code 3 is explained as "contract acquisition failed" rather than just "exit code 3".

## Assumptions & contingencies

- **CLI stderr is safe for progress events**: Progress events contain only structural metadata (file paths, rule IDs, counts). File paths are workspace-relative when possible. If a progress line somehow contains sensitive data, the existing `Redact()` method in both `DataGuardPackage` and `DataGuardLogger` will scrub it before display/logging. If this assumption is wrong during implementation (e.g., contract names contain connection info), add a `ProgressEmitter.Redact()` call in the emitter itself.
- **Status bar API availability**: `IVsStatusbar` is available in all supported VS versions (2019+). If Step 7 fails at runtime on older VS, catch the exception and skip status bar updates — the Output Window feedback from Steps 2-3 is sufficient standalone.
- **Unicode symbols (✅, ❌, ▶, ✔, ✋)**: VS Output Window supports Unicode. If a user reports garbled output, fall back to ASCII: `[OK]`, `[DISABLED]`, `[>]`, `[DONE]`, `[CANCELLED]`. Check by writing a test line during initialization; if readback fails, set a flag to use ASCII mode. Simpler fallback: just use ASCII from the start — VS Output Window Unicode support is reliable since VS 2017.
