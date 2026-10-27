---
phase: 4
title: "VS database commands: Refresh Snapshot and Verify SQL Shapes; drop dead Init"
status: completed
priority: P2
effort: "4h (+ Windows CI run)"
dependencies: [2]
---

# Phase 4: VS database commands: Refresh Snapshot and Verify SQL Shapes; drop dead Init

<!-- Updated: Red Team Session 2 - never pass --config to live commands (repo yml could redirect the snapshot write / run REF CURSOR procedures), Live Provider/Schema options, separate consent key + prompt + banner, CLI snapshot write containment (exit 4) + stderr "snapshot: wrote" line, unmasked host in the modal, outcome mapping corrected (exit 2/3/4, results zipped with queries[i].location, untyped), no "N from last scan" -->

## Overview
Give the VS developer the two database-phase actions the CLI already provides, without VS ever storing a secret: **Refresh Snapshot** (`snapshot refresh --connection-env NAME --provider P`) and **Verify SQL Shapes Against Database** (`verify-shape --connection-env NAME --provider P`), each behind its own consent and a modal that names the target host, and replace the dead **Initialize DataGuard** menu item.

## Key Insights
- CLI surface: `snapshot refresh [--connection-env NAME] [--config P] [--provider X] [--schema S]` (`src/DataGuard.Cli/Commands/SnapshotCommands.cs:29-32`); exit 3 only when the resolved connection is empty (`:54-59`, impossible with `--connection-env`: unset/empty variable is exit 2 at `ConfigLoader.cs:137-141` / `CliConfigurationResolver.cs:113-124`, **before** any secret-store lookup, `:138-145`); unreachable server → `InvalidOperationException` → exit 1 (`:67-70,107-115`); writes `config.SnapshotFilePath ?? .dataguard-snapshot.json` relative to cwd via `BaselineManager` (`:75`; `Directory.CreateDirectory` + `File.Move(…, overwrite: true)`, `src/DataGuard.Core/Baseline/BaselineManager.cs:238-255`) with **no containment** and prints only `Snapshot refreshed with N violations` on stdout (`:97`), which VS discards (`ProcessTerminator.cs:137-151`). `verify-shape --project P [--connection-env NAME] [--config P] [--provider X] --format json --output OUT` (`VerifyShapeCommand.cs:33-36`): exit 2 no connection/project/unsupported provider (`:56-68,87-91`), 4 unsafe output (`:71`), 3 acquisition not complete (`:95-100`), 1 mismatches (`:281`) **or** exception (`:288-295`), 0 clean; JSON `results[i] {sql,targetType,status∈verified|mismatch|untyped|undetermined,…}` without location (`:128-137,173-182`) zipped by index with `queries[i].location {file,line} | null` (`:207-219`); `violationsCount` always 0. Provider precedence `--provider` → config → `sqlserver` (`CliConfigurationResolver.cs:71-79`). Without `--config`, the CLI uses defaults (`ConfigLoader.cs:16-19`); a missing `--config` file is exit 2 (`:29-31`).
- **Why `--config` is never passed to live commands (Critical, round 2):** these commands run without `IdeSafePolicy.Apply`, so a repo-controlled `.dataguard.yml` would (a) redirect the snapshot write anywhere (`SnapshotFilePath: ..\..\..\Users\dev\.gitconfig`), (b) with `DefaultProvider: oracle` + `Oracle.DescribeRefCursors: true` execute every REF CURSOR procedure in the developer's database (`src/DataGuard.Core/Models/Configuration.cs:147-150`, `OracleCatalog.cs:352`), (c) for verify-shape read a repo-chosen file through `ManualAssemblyPath` (no `RefuseAssemblyFromConfig` in `VerifyShapeCommand`; callers only `BaselineCommand.cs:55`, `ValidateCommand.cs:172`), (d) run unclamped parallelism/timeouts. The existing consent text promises the opposite (`SolutionTrustGate.cs:85-86`: "IDE-safe mode … write paths from the config … are ignored"), and `WriteCommandBannerAsync` prints "Mode: ide-safe" for every command (`DataGuardPackage.Commands.cs:120`, `OutputPaneWriter.cs`). Hence: no `--config`, VS supplies `--provider`/`--schema` from its own options, a separate consent key and prompt, a per-command banner, and CLI-side containment as defence in depth.
- VS plumbing reused unchanged: single run slot (`CliProcessRegistry`), `CliRunSession.RunAsync` (status strings `CliRunSession.cs:65` are validate/assess-specific; `sarifPath` is used only by the timeout publish rule `:76` → for live commands `ProceedToPublish=false` after a timeout kill and `Publishing.cs:32-36` returns silently), `ProgressStreamReader`, temp dir `%TEMP%\DataGuard\<guid>\` (`TempDirectoryCleaner.cs:20`; deleted in `finally` `Commands.cs:138`, so `summary.json` must be parsed inside `HandleRunOutcomeAsync`), timeout choice `Commands.cs:66` (validate/assess only → add a branch), modal pattern `VsShellUtilities.ShowMessageBox` with `OLEMSGDEFBUTTON_SECOND` (`Consent.cs:63-69`). Run-on-build calls only `RunValidationAsync` (`Commands.cs:28-36`). `PublishGate.Decide` requires `ide-safe: active` (`ProgressReadModels.cs:80-90`), which live commands never print → branch at `Publishing.cs:32-38` before the gate; never load the Error List.
- Options page has no connection field (`DataGuardOptionsPage.cs`; string properties persist per user like `CustomCliPath` `:45`). VS needs only the **name** of an environment variable (`^[A-Za-z_][A-Za-z0-9_]*$`); its value is read once, in a pure helper, to show the target host in the modal — shown **unmasked** (the host belongs to the developer; masking `db-prod` and `db-dev` to `db-***` defeats the confirmation) and redacted only in logs/Output via `DataGuardLogger.Redact`. Host extraction ports `extractHost` from `src/DataGuard.VSCode/src/live-database-confirmation.ts:21-54` (ADO.NET keys, EZConnect, TNS `(HOST=…)`, URI); vectors in `live-database-confirmation.test.ts:31-47`.
- Dead item: `InitCommand` 0x0106 (`Commands/Menus.vsct:66-72,87`) has no handler (`DataGuardPackage.cs:84-92`); `UI/InfoBarManager.cs` executes it (`:136`) but is instantiated only by `InfoBarManagerTests.cs:68,90`; `docs/adr/ADR-0001-visual-studio-extension.md:37` mentions the InfoBar.

## Requirements
<!-- Updated: Validation Session 1 - Q4a delete InfoBarManager; Q5a Live Provider default sqlserver; Q6a unmasked host in the modal -->
- **Functional:**
  1. Tools → Options → DataGuard → General, category "Live Database": **Connection Environment Variable** (name, validated), **Live Provider** (`sqlserver|oracle|mysql|postgresql`, default `sqlserver`), **Live Schema** (optional identifier `^[A-Za-z_][A-Za-z0-9_$]*$`). Empty variable name → both commands write `[DataGuard] Set Tools > Options > DataGuard > General > Connection Environment Variable to the name of an environment variable holding the connection string (the value is read by the CLI only).` and do nothing.
  2. Tools → DataGuard → **Refresh Snapshot** (0x0106, replaces Init) and **Verify SQL Shapes Against Database** (0x0107): solution open → **live consent** (key `ComputeConsentKey(...) + ":live"`, prompt states: not IDE-safe, connects to the database named by variable NAME with the developer's credential, writes `<solutionDir>\.dataguard-snapshot.json`, never reads `.dataguard.yml`) → modal `DataGuard will connect to <host[:port]> (environment variable NAME, provider P) to {refresh the schema snapshot into <solutionDir>\.dataguard-snapshot.json | describe every read SQL query under <solutionDir>}. Continue?` (Yes/No, default No) → CLI run with the validation timeout → outcome rendering. The Output banner prints `Mode: live database (not ide-safe; reads environment variable NAME; .dataguard.yml is not read)` for these commands.
  3. argv (never `--config`, never `--ide-safe`, never `--connection`): `snapshot refresh --connection-env NAME --provider P [--schema S]`; `verify-shape --project "<slnDir>" --connection-env NAME --provider P --format json --output "<tmp>\summary.json"`. cwd = solution directory, so the snapshot lands at `<slnDir>\.dataguard-snapshot.json` where `FindDefaultSnapshot` (`CliConfigurationResolver.cs:296-313`) finds it for the next validate.
  4. **CLI (defence in depth):** `snapshot refresh` refuses a resolved `SnapshotFilePath` whose full path is outside the config directory (or cwd when no config) or fails the `OutputSinks` safe-path rules → `Refusing to write snapshot …` exit 4; on success prints `snapshot: wrote <path relative to cwd>` on **stderr**. VS takes the path only from that prefix and additionally checks `File.Exists` + `LastWriteTimeUtc > run start`.
  5. Outcome (no handshake; Error List untouched): snapshot 0 → `[OK] Snapshot refreshed: <path>` (or `[ERROR] CLI exited 0 but no snapshot was written in the solution directory`), 1 → `[ERROR] Snapshot refresh failed: unreachable server, permissions or acquisition error (see [WARN]/[DataGuard CLI] lines)`, 2 → `[ERROR] Arguments or credential problem: variable NAME is unset/empty or the provider is unsupported`, 3 → `[ERROR] No database connection resolved`, 4 → `[ERROR] The CLI refused the snapshot path`. verify-shape 0/1 with `summary.json` → `[OK|WARN] Verify shapes: <verified> verified, <mismatch> mismatches, <undetermined> undetermined, <untyped> untyped` + one `[DataGuard]   <status> <file relative to solution or ?>:<line or ?> "<sql 40 chars>" → <targetType>` per non-verified result (location from `queries[i].location`, nullable); 1 without summary → `[ERROR] verify-shape failed (see lines above)`; 2/3/4 as above (4 → refused output path). Status bar mirrors the first line.
  6. Remove `InitCommand` from `Menus.vsct` (button + symbol); delete `UI/InfoBarManager.cs` + `InfoBarManagerTests.cs`; update `ADR-0001:37`.
- **Non-functional:** no secret stored or logged by VS; host shown only in the modal; both commands blocked while another run is active (existing slot); run-on-build never triggers them; docs say to add `.dataguard-snapshot.json.*.tmp` to `.gitignore` (timeout kill during the atomic write can leave it).

## Architecture
```
Menu RefreshSnapshot / VerifyShape ──► RunLiveCommandAsync(kind)
   ├─ ReadSolutionInfo, reserve slot, CliLocator                                  (reuse)
   ├─ LiveDatabaseOptions.Validate(name, provider, schema) (pure)
   ├─ live consent (key + ":live", own prompt)  → modal (host via ConnectionHostExtractor.Extract(Environment.GetEnvironmentVariable(NAME)), default No)
   ├─ CliArgumentBuilder.BuildSnapshotRefreshArguments / BuildVerifyShapeArguments (pure; no --config)
   ├─ banner "Mode: live database …" → CliRunSession.RunAsync (status "DataGuard: Refreshing snapshot…" / "Verifying shapes…")
   └─ Publishing.HandleRunOutcomeAsync: live branch before PublishGate → LiveCommandOutcome.Describe(kind, exitCode, stderrSignals, summaryJson?) (pure) → Output pane + status
CLI: SnapshotCommands.cs:75 → containment check (exit 4) → BaselineManager save → stderr "snapshot: wrote <relative>"
```

## Related Code Files
- **Create:** `src/DataGuard.VisualStudio/LiveDatabaseCommands.cs` (`LiveDatabaseOptions`, `ConnectionHostExtractor`, `LiveCommandOutcome`, `VerifyShapeSummary` — pure, no VS SDK), `src/DataGuard.VisualStudio/DataGuardPackage.LiveCommands.cs` (handlers)
- **Create:** `tests/DataGuard.VisualStudio.Tests/LiveDatabaseCommandsTests.cs`
- **Modify:** `src/DataGuard.VisualStudio/Commands/Menus.vsct`, `DataGuardPackage.cs` (ids + `AddCommand`), `DataGuardOptionsPage.cs`, `CliArgumentBuilder.cs`, `CliRunSession.cs:65`, `OutputPaneWriter.cs` (`WriteCommandBannerAsync` mode line parameter), `DataGuardPackage.Commands.cs:66,120` (timeout + banner), `DataGuardPackage.Consent.cs` (live key/prompt), `SolutionTrustGate.cs` (`BuildLivePromptText`), `DataGuardPackage.Publishing.cs:32-38` (live branch), `ProgressStreamReader.cs` (capture `snapshot: wrote …` line)
- **Modify (CLI):** `src/DataGuard.Cli/Commands/SnapshotCommands.cs:75-97` (containment + stderr line), `tests/DataGuard.Core.Tests/CliExitCodeTests.cs` (snapshot path containment e2e)
- **Delete:** `src/DataGuard.VisualStudio/UI/InfoBarManager.cs`, `tests/DataGuard.VisualStudio.Tests/InfoBarManagerTests.cs`
- **Modify:** `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs`, `SolutionTrustGateTests.cs`, `docs/USAGE.md` §Visual Studio (new commands; the "no database command" trust-model line), `src/DataGuard.VisualStudio/overview.md`, `docs/adr/ADR-0001-visual-studio-extension.md:37`, `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs`, `LiveDatabaseCommandsTests.cs`, `SolutionTrustGateTests.cs`, `tests/DataGuard.Core.Tests/CliExitCodeTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `BuildValidateArguments`/`BuildAssessArguments` unchanged (existing).
  - [ ] `PublishGate.Decide` without `ide-safe: active` → `HandshakeMissing` (existing; validate/assess keep it).
  - [ ] `SolutionTrustGate.BuildPromptText` existing text + consent key behaviour (existing tests).
  - [ ] `Menus.vsct` string pin: ids 0x0100–0x0105 present.
  - [ ] CLI e2e today: `snapshot refresh` with a yml `SnapshotFilePath: ../outside.json` and a reachable fake? — not runnable without a DB; pin instead the unit seam: `BaselineManager` save path resolution for a relative traversal path **today** writes outside (documents the gap; flipped in 3.V via the new CLI check before acquisition).
  - [ ] CLI e2e: `snapshot refresh --connection-env NAME` with NAME unset → exit 2 and **no** network/secret-store access (assert no `warning: Secret store` line and exit before connecting).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** pins; green on Windows CI (Linux compile) + Core e2e on Linux.
2. **Step B (Add shared infrastructure or seams):** `LiveDatabaseCommands.cs` pure types with tests (name/provider/schema validation, host extraction vectors ported verbatim, outcome texts, summary parsing with the real `verify-shape` JSON shape incl. `location: null`); CLI containment helper in `SnapshotCommands` + stderr line.
3. **Step C (Refactor / Implement):** builders, options, vsct, handlers, live consent/prompt, banner, session status, publishing branch, timeout branch; delete Init/InfoBar; docs.
4. **Step D (Verify compile + tests):** Linux compile parity + Core e2e; Windows CI tests + VSIX gate (vsct compiles). Manual (Windows, local SQL Server): set the variable, Refresh Snapshot → modal shows the host, `.dataguard-snapshot.json` appears in the solution dir, Output `[OK] Snapshot refreshed: .dataguard-snapshot.json`; Run Validation → Phase 2 shows `ground truth: schema` (or `schema+entities`), DG015/DG016 run; Verify SQL Shapes lists mismatches.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `LiveDatabaseOptions.Validate`: accepts `DATAGUARD_CONNECTION_STRING`/`MyDb`, provider whitelist, schema identifier; rejects empty, `1abc`, `a b`, `a;b`, provider `oracle; drop`.
- [ ] `ConnectionHostExtractor.Extract`: the seven vectors from `live-database-confirmation.test.ts:31-47` (unmasked host, e.g. `db-prod.corp.example.com`, `10.20.30.40`, `ora1`, `pg.local`); no host → `(unknown host)`; never returns user/password.
- [ ] Builders: exact argv for both commands; never `--config`, `--ide-safe`, `--connection`; `--schema` only when set.
- [ ] Live consent: key differs from the validate key for the same solution; prompt names NAME, the snapshot path and "not IDE-safe"; validate consent does not grant live consent and vice versa.
- [ ] `LiveCommandOutcome.Describe`: snapshot 0 with `snapshot: wrote` line + existing file → `[OK] … <path>`; 0 without line/file → `[ERROR] … no snapshot was written`; 1/2/3/4 texts; verify-shape with summary (verified/mismatch/undetermined/untyped, one `location: null`) → counts + detail lines with `?`; 1 without summary → failed text; 2/3/4 texts.
- [ ] `Menus.vsct` pin: no `InitCommand`; `RefreshSnapshotCommand` 0x0106, `VerifyShapeCommand` 0x0107.
- [ ] CLI e2e (Linux): `snapshot refresh` with `SnapshotFilePath: ../outside.json` → exit 4 `Refusing to write snapshot`, no file (check happens before connecting); default path → stderr `snapshot: wrote .dataguard-snapshot.json` (needs the LiveDb category test with Testcontainers, or a unit test on the extracted path-check function + a non-DB e2e asserting the refusal happens before the connection attempt).
- [ ] Publishing route (if `DataGuardPackageTests` has the seam): a live outcome never calls `PublishSarifAsync`.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release` và `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false` (Linux); MSBuild `CreateVsixContainer=true` (Windows CI job `Visual Studio VSIX Packaging Gate`)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Core.Tests -c Release --no-build --filter "FullyQualifiedName~CliExitCode|FullyQualifiedName~Snapshot"` (Linux) và `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release` (Windows CI)

## Todo List
- [ ] [3.T] Pin builders/PublishGate/consent/vsct/options; pin CLI snapshot-path gap và exit 2 trước secret store
- [ ] [3.I] `LiveDatabaseCommands.cs` (pure) + handlers + options + vsct + builders + live consent/prompt + banner + publishing branch + timeout; CLI containment + `snapshot: wrote` stderr; xoá Init/InfoBar; docs/ADR
- [ ] [3.V] Test options, host extraction (7 vector), builders, consent, outcome (snapshot/verify-shape đủ exit code, `location: null`), vsct pin, CLI e2e containment; Windows CI xanh; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS (Windows CI + Core e2e Linux).
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Từ VS: Refresh Snapshot tạo `.dataguard-snapshot.json` trong thư mục solution sau modal nêu đúng host; Verify SQL Shapes liệt kê mismatch với file:line; một `.dataguard.yml` độc hại (`SnapshotFilePath` traversal, `DescribeRefCursors`) không ảnh hưởng vì không được đọc.
- [ ] VS không lưu/ghi log giá trị connection string (log file sau khi chạy: không có `Password=`; host chỉ trong modal).
- [ ] Menu không còn "Initialize DataGuard".

## Risk Assessment & Security Considerations
- **Rủi ro:** lệnh live chạy từ "Run Validation on Build" → **Giảm thiểu:** build hook chỉ gọi `RunValidationAsync` (`Commands.cs:28-36`); giữ nguyên, review ở 3.I.
- **Rủi ro:** `.dataguard.yml` do repo kiểm soát (`SnapshotFilePath`, `DescribeRefCursors`, `ManualAssemblyPath`, `VaultAddress`) → **Giảm thiểu:** không truyền `--config`; CLI containment exit 4; `--connection-env` được giải quyết trước secret store và exit 2 khi biến rỗng (`CliConfigurationResolver.cs:113-124`), pin bằng e2e; consent/banner nói rõ "not ide-safe".
- **Rủi ro:** giá trị biến được VS đọc để lấy host → **Giảm thiểu:** chỉ trong hàm thuần, không lưu, không log; modal hiện host đầy đủ để developer phân biệt prod/dev; Output pane qua `Redact`.
- **Rủi ro:** timeout kill giữa lúc ghi atomic để lại `.dataguard-snapshot.json.<guid>.tmp` → **Giảm thiểu:** docs khuyên `.gitignore` pattern; Output ghi rõ khi kill.
- **Bảo mật:** modal mặc định **No**; consent live tách khỏi consent validate; không `--ide-safe` (đúng với VS Code).
