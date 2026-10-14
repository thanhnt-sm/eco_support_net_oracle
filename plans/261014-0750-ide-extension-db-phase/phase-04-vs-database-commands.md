---
phase: 4
title: "VS database commands: Refresh Snapshot and Verify SQL Shapes; drop dead Init"
status: pending
priority: P2
effort: "3.5h (+ Windows CI run)"
dependencies: [2]
---

# Phase 4: VS database commands: Refresh Snapshot and Verify SQL Shapes; drop dead Init

## Overview
Give the VS developer the two database-phase actions the CLI already provides, without VS ever storing a secret: **Refresh Snapshot** (`snapshot refresh --connection-env NAME`) and **Verify SQL Shapes Against Database** (`verify-shape --connection-env NAME`), each behind a modal that names the masked target host, mirroring the VS Code gate. Replace the dead **Initialize DataGuard** menu item.

## Key Insights
- CLI surface (verified by `--help` and source): `snapshot refresh [--connection-env NAME] [--config P] [--provider X]` (`src/DataGuard.Cli/Commands/SnapshotCommands.cs:29-32`; no connection → exit 3 `:54-59`; failure → exit 1 `:107-115`; writes `config.SnapshotFilePath ?? .dataguard-snapshot.json` relative to cwd); `verify-shape --project P [--connection-env NAME] [--config P] [--provider X] --format json --output OUT` (`VerifyShapeCommand.cs:33-36`; no connection → exit 2 `:56-61`; acquisition failure → exit 3 `:95-100`; mismatches → exit 1; JSON `results[].status ∈ verified|mismatch|untyped|undetermined`, `queries[]`, `violationsCount` always 0 `:190-243`). `--connection-env` resolution: `CliConfigurationResolver.cs:113-124` (unset/empty variable → exit 2). Provider comes from `--provider` → config `DefaultProvider` → `sqlserver` (`:71-79`); VS passes none today and keeps it that way.
- Neither command prints `ide-safe: active` (they are not ide-safe commands), so `PublishGate.Decide` (`ProgressReadModels.cs:75-90`) would discard them: live commands need a separate verdict path, and must never load the Error List from a SARIF they do not produce.
- VS run plumbing to reuse unchanged: `CliProcessRegistry` single slot, `CliRunSession` (status strings at `CliRunSession.cs:67` are validate/assess-specific), `ProgressStreamReader`, timeout handler, temp dir (`DataGuardPackage.Commands.cs:95-104`), consent gate keyed on the yml hash (`DataGuardPackage.Consent.cs:45-85`), modal pattern `VsShellUtilities.ShowMessageBox` (`:63-69`). Run-on-build calls only `RunValidationAsync` (`DataGuardPackage.Commands.cs:28-36`) and must stay that way.
- Options page has no connection field (`DataGuardOptionsPage.cs`); only the **name** of an environment variable is needed (`^[A-Za-z_][A-Za-z0-9_]*$`), read by the CLI, never by VS except to mask the host for the modal (VS Code does the same with its SecretStorage value: `live-database-confirmation.ts:21-68` `maskConnectionHost`).
- Dead item: `InitCommand` 0x0106 declared (`Commands/Menus.vsct:66-72,87`) with no handler (`DataGuardPackage.cs:84-92`); `UI/InfoBarManager.cs` executes it (`:136`) but is instantiated only by `InfoBarManagerTests.cs:68,90`.
- Env name validation and masking are pure; the only VS-SDK-bound code is the two command handlers and the options property.

## Requirements
- **Functional:**
  1. Tools → Options → DataGuard → General: **Connection Environment Variable** (string, default empty, validated name). Empty → both commands write `[DataGuard] Set Tools > Options > DataGuard > General > Connection Environment Variable to the name of an environment variable that holds the connection string (the value is read by the CLI only).` and do nothing.
  2. Tools → DataGuard → **Refresh Snapshot** (0x0106, replaces Init) and **Verify SQL Shapes Against Database** (0x0107): solution open → consent gate (same key as validate) → modal `DataGuard will connect to host <masked> (environment variable NAME) to {refresh the schema snapshot | describe N read SQL queries}. Continue?` (Yes/No, default No; N from the last scan if any, else "every read SQL query") → CLI run with the validation timeout → outcome rendering.
  3. argv: `snapshot refresh --config "<sln>\.dataguard.yml" --connection-env NAME`; `verify-shape --project "<slnDir>" --config "<sln>\.dataguard.yml" --connection-env NAME --format json --output "<tmp>\summary.json"`. `--config` only when the file exists (verify-shape/snapshot are not ide-safe: a missing `--config` file is exit 2 there, `ConfigLoader.cs:29-31`).
  4. Outcome: no handshake requirement; Error List untouched. snapshot: 0 → `[OK] Snapshot refreshed: <path>` (path from stdout `Snapshot written …` line if present, else the default name), 1 → `[ERROR] Snapshot refresh failed (see [DataGuard CLI] lines)`, 2 → arguments/config/credential, 3 → `[ERROR] No database connection: variable NAME is unset or empty, or the server is unreachable`. verify-shape: parse `summary.json` → `[OK|WARN] Verify shapes: <verified> verified, <mismatch> mismatches, <undetermined> undetermined` + one `[DataGuard]   <status> <file>:<line> <sql snippet 40 chars> → <targetType>` per non-verified result; exit 2/3 as above.
  5. Remove `InitCommand` from `Menus.vsct` (button + symbol) and delete `UI/InfoBarManager.cs` + `InfoBarManagerTests.cs` (dead code tied to it; decision Q4).
- **Non-functional:** no secret stored or logged by VS (`DataGuardLogger.Redact` on every echoed line as today); masked host only in the modal and the Output pane; both commands disabled while another run is active (existing single slot).

## Architecture
```
Menu RefreshSnapshot / VerifyShape ──► RunLiveCommandAsync(kind)
   ├─ ReadSolutionInfo, reserve slot, CliLocator, consent gate      (reuse)
   ├─ options.ConnectionEnvironmentVariable → EnvVariableName.Validate (pure)
   ├─ host = ConnectionHostMasker.Mask(Environment.GetEnvironmentVariable(NAME)) (pure; value never kept)
   ├─ modal (ShowMessageBox, default No)
   ├─ CliArgumentBuilder.BuildSnapshotRefreshArguments / BuildVerifyShapeArguments (pure)
   ├─ CliRunSession.RunAsync (status "DataGuard: Refreshing snapshot…"/"Verifying shapes…")
   └─ LiveCommandOutcome.Describe(kind, exitCode, summaryJson?) (pure) → Output pane + status; never PublishSarif
```

## Related Code Files
- **Create:** `src/DataGuard.VisualStudio/LiveDatabaseCommands.cs` (`EnvVariableName`, `ConnectionHostMasker`, `LiveCommandOutcome`, `VerifyShapeSummary` — pure, no VS SDK), `src/DataGuard.VisualStudio/DataGuardPackage.LiveCommands.cs` (handlers)
- **Create:** `tests/DataGuard.VisualStudio.Tests/LiveDatabaseCommandsTests.cs`
- **Modify:** `src/DataGuard.VisualStudio/Commands/Menus.vsct` (two buttons; remove Init), `DataGuardPackage.cs` (ids + `AddCommand`), `DataGuardOptionsPage.cs` (new property), `CliArgumentBuilder.cs` (two builders), `CliRunSession.cs:67` (status per command), `DataGuardPackage.Publishing.cs` (route live commands to `LiveCommandOutcome` before `PublishGate`), `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs`
- **Delete:** `src/DataGuard.VisualStudio/UI/InfoBarManager.cs`, `tests/DataGuard.VisualStudio.Tests/InfoBarManagerTests.cs`
- **Modify:** `docs/USAGE.md` §Visual Studio (new commands, trust model line "extension không có lệnh kết nối database" → updated), `src/DataGuard.VisualStudio/overview.md`, `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs`, `tests/DataGuard.VisualStudio.Tests/LiveDatabaseCommandsTests.cs`, `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `BuildValidateArguments`/`BuildAssessArguments` output unchanged (existing tests).
  - [ ] `PublishGate.Decide` without `ide-safe: active` → `HandshakeMissing` (existing) — live commands must bypass it, validate/assess must not.
  - [ ] `Menus.vsct` string pin: contains `ValidateCommand … ForgetConsentCommand` ids 0x0100–0x0105 (so the edit cannot drop an existing button).
  - [ ] `DataGuardOptionsPage` defaults (`ValidationTimeoutSeconds` 300 etc.) unchanged.

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** pins above; green on Windows CI.
2. **Step B (Add shared infrastructure or seams):** `LiveDatabaseCommands.cs` pure types with tests (name validation, host masking ported from `live-database-confirmation.ts`, outcome texts, summary parsing with the `verify-shape` JSON shape).
3. **Step C (Refactor / Implement):** builders, options property, vsct, handlers, session status strings, publishing route; delete Init/InfoBar; docs.
4. **Step D (Verify compile + tests):** Linux compile parity; Windows CI tests + VSIX gate (vsct compiles into `Menus.ctmenu`). Manual (Windows): set the variable name, run Refresh Snapshot against a local SQL Server → `.dataguard-snapshot.json` appears in the solution dir; then Run Validation → no "syntactic only" (Phase 2) and DG015/DG016 schema rules run.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `EnvVariableName.Validate`: accepts `DATAGUARD_CONNECTION_STRING`, `MyDb`; rejects empty, `1abc`, `a b`, `a;b`.
- [ ] `ConnectionHostMasker.Mask`: `Server=db-prod.corp.example.com,1433;…` → `db-***.com`; `Host=10.20.30.40` → `10.***.40`; Oracle `(HOST=ora1)` → `ora***`; URI `postgresql://u:p@pg.local/db` → `pg***.local`; no host → `undefined/none`; never returns any other part.
- [ ] Builders: `snapshot refresh --config "…" --connection-env NAME`; `verify-shape --project "…" --config "…" --connection-env NAME --format json --output "…"`; config omitted when the file does not exist; never `--ide-safe`, never `--connection`.
- [ ] `LiveCommandOutcome.Describe`: snapshot 0/1/2/3 texts; verify-shape with a summary of 3 results (verified/mismatch/undetermined) → counts + two detail lines; verify-shape exit 1 without summary → error text.
- [ ] `Menus.vsct` string pin: no `InitCommand`; contains `RefreshSnapshotCommand` 0x0106 and `VerifyShapeCommand` 0x0107.
- [ ] Publishing route test (if `DataGuardPackageTests` covers `HandleRunOutcomeAsync` seams): a live command outcome never calls `PublishSarifAsync`.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false` (Linux) ; MSBuild `CreateVsixContainer=true` (Windows CI job `Visual Studio VSIX Packaging Gate`)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release` (Windows CI)

## Todo List
- [ ] [3.T] Pin builders/PublishGate/vsct/options hiện tại
- [ ] [3.I] `LiveDatabaseCommands.cs` (pure) + handlers + options + vsct + builders + publishing route; xoá Init/InfoBar; docs
- [ ] [3.V] Test tên biến, mask host, builders, outcome, vsct pin; Windows CI xanh; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS (Windows CI).
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Từ VS, với biến môi trường cấu hình: Refresh Snapshot tạo `.dataguard-snapshot.json`; Verify SQL Shapes liệt kê mismatch trong Output pane; cả hai hỏi modal trước khi kết nối.
- [ ] VS không lưu/ghi log giá trị connection string (grep log file sau khi chạy: không có `Password=`/host thật).
- [ ] Menu không còn "Initialize DataGuard".

## Risk Assessment & Security Considerations
- **Rủi ro:** lệnh live chạy từ "Run Validation on Build" → **Giảm thiểu:** build hook chỉ gọi `RunValidationAsync` (pin bằng test đọc `BuildEventsHandler`/`RunValidationOnBuildAsync` nếu có seam; tối thiểu: review).
- **Rủi ro:** `.dataguard.yml` do repo kiểm soát trỏ `VaultAddress`/`KeyVaultUri` và lệnh live (không ide-safe) tra secret store với `VAULT_TOKEN` của developer (`ZeroTrustCredentialProvider.cs:71`, `ISecretStore.cs:133-144`) → **Giảm thiểu:** `--connection-env` được ưu tiên trước secret store (`CliConfigurationResolver.cs:113-124`) nên chuỗi dừng ở biến đã đặt; nếu biến rỗng CLI exit 2 **trước** khi tra store (xác nhận trong 3.V bằng e2e CLI: biến unset → exit 2, không có HTTP). Ghi rõ trong modal rằng chỉ biến NAME được dùng. Câu hỏi validate Q5 cho việc chặn secret store từ config repo ở các lệnh live.
- **Rủi ro:** giá trị biến được VS đọc để mask host → **Giảm thiểu:** chỉ trong hàm thuần, không lưu, không log; test mask không trả về phần khác.
- **Bảo mật:** modal mặc định **No**; consent của solution vẫn bắt buộc; không `--ide-safe` (đúng với VS Code).
