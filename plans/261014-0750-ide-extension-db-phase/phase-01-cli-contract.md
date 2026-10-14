---
phase: 1
title: "Host–CLI argv contract pinned on both sides (fixes Assess)"
status: pending
priority: P1
effort: "3h"
dependencies: []
---

# Phase 1: Host–CLI argv contract pinned on both sides (fixes Assess)

## Overview
Stop the VS Code host from sending `--provider` to `assess` (RC1), and make the argv vectors the IDE hosts build a **shared fixture** that the C# suite parses through the real `System.CommandLine` root so that any future host/CLI drift fails a test on the side that has `dotnet` in CI.

## Key Insights
- `assess` only registers `--workspace --project-filter --remote-advisories --allow-network --remote-public-package --output --format --verbose --progress --ide-safe` (`src/DataGuard.Cli/Commands/AssessCommand.cs:45-70`). The VS Code host sends `--provider` (`src/DataGuard.VSCode/src/command-args.ts:84`); Visual Studio does not (`src/DataGuard.VisualStudio/CliArgumentBuilder.cs:98-102`) and therefore works.
- Unknown options: System.CommandLine 2.0.12 prints `Unrecognized command or argument '<tok>'.` per token on stderr, help on stdout, **exit 1** (reproduced). The action never runs, so no `ide-safe: active` (`AssessCommand.cs:85` is inside the action).
- The host's old-CLI detector (`ide-safe-contract.ts:20`) is anchored to `--ide-safe|--allow-env-connection` only; any other rejected token yields the generic "did not confirm IDE-safe mode" message with no hint.
- The CI job `vscode-extension` has Node only (`.github/workflows/ci.yml:200-225`); the C# suite `tests/DataGuard.Core.Tests` already references `DataGuard.Cli` (`DataGuard.Core.Tests.csproj:15`) and has `InternalsVisibleTo` (`DataGuard.Cli.csproj:29`), and every command exposes `public static Command Create(CommonOptions)` (`AssessCommand.cs:14`, `ValidateCommand.cs:20`). The root command is assembled inline in top-level statements (`src/DataGuard.Cli/Program.cs:15-29`), so there is no reusable factory yet.
- When no credential is stored the host spawns with `env: undefined` (`extension.ts:459-461`), so the child **inherits** a developer's shell `DATAGUARD_CONNECTION_STRING`; `snapshot refresh`/`baseline`/`verify-shape` read it (`CliConfigurationResolver.cs:126-130`) after a modal that says "no SecretStorage credential is stored" (`live-database-confirmation.ts:95-98`). The modal text is then wrong about the target.

## Requirements
- **Functional:** `assess` argv = `assess --workspace WS --format sarif --output OUT --progress --ide-safe` (identical to Visual Studio). Every host argv (6 commands × credential variants) is listed in one fixture and parses with zero errors on the real CLI root. A rejected-token line for any option the host sends produces a message naming the token and the update hint.
- **Non-functional:** no new runtime dependency; fixture is plain JSON readable by `node --test` and `System.Text.Json`; no process spawn in the C# contract test (parse only, < 50 ms).

## Architecture
```
src/DataGuard.VSCode/test/fixtures/cli-contract.json        ← single source of truth
  { "version": 1, "vectors": [ { "name": "validate-no-credential", "argv": [...] }, ... ] }
        │                                   │
        ▼                                   ▼
command-args.test.ts (node --test)   tests/DataGuard.Core.Tests/IdeCliContractTests.cs
asserts buildCliArguments(...)       CliRootCommandFactory.Create(version).Parse(argv).Errors == []
=== fixture vector                   and the bound command name == vector.command
```
`CliRootCommandFactory` (new, `src/DataGuard.Cli/CliRootCommandFactory.cs`) is the seam: `Program.cs` calls it; the test parses through it. Behaviour of `Program.cs` is unchanged (same commands, same order).

## Related Code Files
- **Create:** `src/DataGuard.VSCode/test/fixtures/cli-contract.json`
- **Create:** `src/DataGuard.Cli/CliRootCommandFactory.cs`
- **Create:** `tests/DataGuard.Core.Tests/IdeCliContractTests.cs`
- **Modify:** `src/DataGuard.VSCode/src/command-args.ts` (drop `--provider` from `assess`; `assess` gains `--progress` for parity with VS — the CLI accepts it, `AssessCommand.cs:65`)
- **Modify:** `src/DataGuard.VSCode/src/command-args.test.ts` (load fixture; assert equality per vector)
- **Modify:** `src/DataGuard.VSCode/src/ide-safe-contract.ts` (`OLD_CLI_REJECTION` → generic `^Unrecognized command or argument '(--[a-z-]+)'` capture; message names the token)
- **Modify:** `src/DataGuard.VSCode/src/ide-safe-contract.test.ts`
- **Modify:** `src/DataGuard.VSCode/src/extension.ts:457-465` (always pass an explicit env: copy of `process.env` with `DATAGUARD_CONNECTION_STRING` **removed** when no SecretStorage credential; set when present)
- **Modify:** `src/DataGuard.Cli/Program.cs` (use the factory)
- **Modify:** `src/DataGuard.VSCode/CHANGELOG.md`, `docs/USAGE.md` (§VS Code Extension: assess argv, env isolation)
- **Modify:** `tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj` (link the fixture as `<None Include=... CopyToOutputDirectory=PreserveNewest>`; path relative to the test project)

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.Core.Tests/IdeCliContractTests.cs`, `src/DataGuard.VSCode/src/command-args.test.ts`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] C#: `validate` / `scan` / `verify-shape` / `snapshot refresh` / `baseline` vectors from the fixture parse with `Errors.Count == 0` on the real root (these already work today → green before the change).
  - [ ] C#: the **current** `assess` vector with `--provider` parses with an `Unrecognized command or argument '--provider'` error (documents RC1; this test is flipped in 3.V).
  - [ ] C#: `Program.cs` still registers exactly the 13 commands in the same order after the factory extraction (assert names: validate, preflight, baseline, snapshot, init, hook, config, oracle-check, migrate, assess, version, scan, verify-shape).
  - [ ] TS: `buildCliArguments` for the 5 working commands equals the fixture vectors (replace the hand-written literals in `command-args.test.ts:28-55`).
  - [ ] TS: `isOldCliRejection` still true for `Unrecognized command or argument '--ide-safe'` and `'--allow-env-connection'`, false for a mid-line mention (`ide-safe-contract.test.ts` current cases).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** write the fixture with today's vectors (including the broken `assess`), the C# contract test and the TS fixture-driven test; run both suites; all green with the RC1 vector asserted as *rejected*.
2. **Step B (Add shared infrastructure or seams):** extract `CliRootCommandFactory.Create(string version)` from `Program.cs:13-29`; `Program.cs` becomes `var rootCommand = CliRootCommandFactory.Create(version);`. Link the fixture into `DataGuard.Core.Tests`.
3. **Step C (Refactor / Implement):** change the `assess` case in `command-args.ts:84` to `["assess","--workspace",ws,"--format","sarif","--output",out,"--progress",IDE_SAFE_FLAG]`; update the fixture vector; broaden `OLD_CLI_REJECTION` to capture any `--token` and include it in `buildIdeSafeFailureMessage`; in `extension.ts` build `env` explicitly (`const env = { ...process.env }; delete env.DATAGUARD_CONNECTION_STRING; if (connectionString) env.DATAGUARD_CONNECTION_STRING = connectionString;`).
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release` (0 warnings) and `npm test` in `src/DataGuard.VSCode`; then run the real CLI with the new `assess` vector against `samples/DataGuard.Sample`: exit 1 with first stderr line `ide-safe: active` and `assess.sarif` written (DG1301 expected on the sample).

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] C#: the `assess` fixture vector parses with zero errors and binds to the `assess` command (flip of the 3.T RC1 test).
- [ ] C#: every fixture vector whose name contains `ide-safe` includes `--ide-safe`; every live-DB vector (`snapshot`, `baseline`, `verify-shape`) does not (same invariant the TS suite holds; now enforced on both sides).
- [ ] TS: `buildCliArguments("assess", ...)` equals the fixture vector; `assess` never carries `--provider`.
- [ ] TS: `buildIdeSafeFailureMessage("Unrecognized command or argument '--provider'.")` names `--provider` and the update hint.
- [ ] TS: env builder helper (extract `buildChildEnvironment(base, connectionString)` into `command-args.ts` so it is testable): removes an inherited `DATAGUARD_CONNECTION_STRING` when the host has no credential; sets it when it has one; never touches other keys.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release` và `npm run compile` (trong `src/DataGuard.VSCode`)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Core.Tests --configuration Release --no-build --filter "FullyQualifiedName~IdeCliContractTests|FullyQualifiedName~IdeSafeHandshakeEndToEndTests"` và `npm test` (trong `src/DataGuard.VSCode`)

## Todo List
- [ ] [3.T] Viết fixture `cli-contract.json` với vector hiện tại + `IdeCliContractTests.cs` (vector assess hiện tại bị từ chối) + test TS đọc fixture
- [ ] [3.I] Tách `CliRootCommandFactory` khỏi `Program.cs`; sửa `assess` argv trong `command-args.ts`; mở rộng `OLD_CLI_REJECTION`; env tường minh trong `extension.ts`
- [ ] [3.V] Lật test RC1, thêm test invariant ide-safe/live-DB hai phía, test `buildChildEnvironment`, cập nhật CHANGELOG + USAGE; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] "DataGuard: Assess Workspace" trong VS Code nạp được SARIF (xác nhận bằng CLI thật: exit 0/1, dòng stderr đầu `ide-safe: active`).
- [ ] Một vector argv sai (ví dụ thêm lại `--provider` vào assess) làm **cả** `IdeCliContractTests` đỏ trước khi tới người dùng.

## Risk Assessment & Security Considerations
- **Rủi ro:** fixture và code TS lệch nhau khi ai đó sửa một bên → **Giảm thiểu:** test TS so sánh `deepEqual` với fixture; test C# parse fixture; cả hai chạy trong CI (`vscode-extension`, `build-and-test`).
- **Rủi ro:** tách factory làm đổi thứ tự/đăng ký command → **Giảm thiểu:** test 3.T khóa danh sách 13 command theo thứ tự.
- **Bảo mật:** env tường minh ngăn credential shell của developer rò vào tiến trình CLI mà modal nói "không có credential"; `--ide-safe` vẫn scrub env (`IdeSafeEnvironment.cs`) cho validate/assess, không đổi.
