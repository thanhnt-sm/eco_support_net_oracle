---
phase: 4
title: "EF ModelSnapshot ground truth under --ide-safe (database phase in the IDE)"
status: pending
priority: P1
effort: "3.5h"
dependencies: []
---

# Phase 4: EF ModelSnapshot ground truth under --ide-safe (database phase in the IDE)

## Overview
Let `validate --ide-safe` obtain **entity contracts** from the EF Core design-time `*ModelSnapshot.cs` source already in the workspace, so the IDE can run the entity-vs-SQL rules (DG004/DG005/DG006, length rules) without a committed snapshot or a credential. Parsing that file is a Roslyn source parse (`EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync`, `src/DataGuard.Core/Sources/EfModelSource.cs:241-253`) and never loads an assembly, which is exactly the trust level `--ide-safe` already grants to inline-SQL extraction from `--project`.

## Key Insights
- `IdeSafePolicy.FirstRejectedValidateOption` rejects `--ef-snapshot`, `--ef-project`, `--ef-context` (`src/DataGuard.Cli/IdeSafePolicy.cs:188-200`), so the host cannot pass them; `validate` under ide-safe forces `GroundTruthMode: Snapshot` (`:84-88`).
- `ResolveEfSnapshotSource` already finds one `ModelSnapshot.cs` under `--ef-project` (`ValidateCommand.cs:527-…`) and feeds it at `:230-232`; it is not consulted when neither EF option is given.
- `AutoDetectEFContext` is a config default `true` (`src/DataGuard.Core/Models/Configuration.cs:43`) whose only consumer is an empty block (`:206-209`): the option promises auto-detection and does nothing.
- The no-ground-truth gate (`ValidateCommand.cs:337-347`) counts `EntityDescriptor` as ground truth, so EF entities alone lift the "syntactic only" warning (Phase 2 surfaces it).
- Root `README.md` Quickstart says `dataguard init` "writes .dataguard.yml + .dataguard-snapshot.json"; `InitCommand` writes only the YAML (reproduced: exit 0, one file). `samples/DataGuard.Sample` has `[ExpectedColumn]` attributes (Manual mode) which are also unreachable under ide-safe; it has no `ModelSnapshot.cs`, so a sample fixture is needed.
- `tests/DataGuard.Core.Tests/ModelSnapshotCSharpParserTests.cs` and `IdeSafeHandshakeEndToEndTests.cs` are the existing seams; `CliProcessTestRunner.cs` runs the real CLI.

## Requirements
- **Functional:**
  1. Under `--ide-safe`, when no snapshot file and no kept connection exist and `AutoDetectEFContext` is true, `validate` searches `--project` (bounded: depth ≤ 6, ≤ 2000 files, skip `bin/obj/node_modules/.git`) for `*ModelSnapshot.cs`; exactly one match → parse it as EF ground truth and print `ide-safe: ef model <relative path> (source parse)` on stderr; several matches → `ACQUISITION: multiple ModelSnapshot.cs found; pass --ef-context` and continue without; zero → unchanged.
  2. `--ef-snapshot`/`--ef-project`/`--ef-context` are **accepted** under `--ide-safe` (explicit selection), still rejected: `--assembly`, `--allow-assembly-from-config`, `--offline`, `--connection`, `--plugins-dir` (unchanged).
  3. A snapshot file or a kept env connection takes precedence (today's order); EF entities are added as a second source only when those give no `EntityDescriptor` (same as `:230-236` today).
  4. Docs: README Quickstart states what `init` writes; USAGE §`--ide-safe` lists EF source parse as allowed; VS Code README "Requirements" explains the three IDE ground-truth sources.
- **Non-functional:** auto-detect adds < 200 ms on a 2000-file tree; no new NuGet dependency; policy change is covered by `IdeSafePolicyTests`.

## Architecture
```
validate --ide-safe --project WS
  ├─ IdeSafePolicy.Apply  (ef options no longer rejected; still no assemblies/connections/plugins)
  ├─ ResolveEfSnapshotSource(explicit ef options)           (unchanged)
  ├─ NEW EfModelSnapshotLocator.TryAutoDetect(WS, config)   (only when ideSafe && AutoDetectEFContext && no explicit ef option)
  ├─ acquisition: snapshot | kept connection | ef source | inline SQL   (order unchanged)
  └─ gate at :337: EntityDescriptor present → database rules run; else Warning (Phase 2 shows it)
```
`EfModelSnapshotLocator` lives in `src/DataGuard.Cli/Services/` (CLI concern: file-system discovery), returns `(path | null, diagnostics)`; it reuses the single-match logic of `ResolveEfSnapshotSource` (extract the directory-scan part into the locator to stay DRY).

## Related Code Files
- **Create:** `src/DataGuard.Cli/Services/EfModelSnapshotLocator.cs`
- **Create:** `tests/DataGuard.Core.Tests/EfModelSnapshotLocatorTests.cs`
- **Create:** `tests/DataGuard.Core.Tests/Fixtures/EfProject/` (minimal csproj + `Migrations/ShopContextModelSnapshot.cs` + one repository `.cs` with inline SQL targeting an entity; also referenced by the new end-to-end test)
- **Modify:** `src/DataGuard.Cli/IdeSafePolicy.cs` (`FirstRejectedValidateOption`: drop the three EF checks; keep order of the others), `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs`
- **Modify:** `src/DataGuard.Cli/Commands/ValidateCommand.cs` (`:105-113` call locator when applicable; `:230-236` unchanged), `tests/DataGuard.Core.Tests/IdeSafeHandshakeEndToEndTests.cs`
- **Modify:** `src/DataGuard.Core/Models/Configuration.cs:206-209` (remove the dead stub or make it document that detection happens in the CLI; no behaviour)
- **Modify:** `README.md`, `README.vi.md` (Quickstart `init` line), `docs/USAGE.md` (§`--ide-safe`, §`init`), `src/DataGuard.VSCode/README.md`, `CHANGELOG.md` (Unreleased)
- **Run:** `./scripts/verify_docs_sync.sh` (docs/rules change gate per `CLAUDE.md`)

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs`, `tests/DataGuard.Core.Tests/IdeSafeHandshakeEndToEndTests.cs`, `tests/DataGuard.Core.Tests/EfModelSnapshotLocatorTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] Policy still rejects `--assembly`, `--allow-assembly-from-config`, `--offline`, `--connection`, `--plugins-dir`, and `--connection-env` without `--allow-env-connection` under ide-safe (existing cases at `IdeSafePolicyTests.cs` lines 185–396 stay green).
  - [ ] End-to-end: `validate --ide-safe --project <sample>` without snapshot → first stderr line `ide-safe: active`, line `Warning: no ground truth ...`, exit follows violations (today: exit 1 on `samples/DataGuard.Sample`).
  - [ ] End-to-end: with a snapshot file present, EF auto-detect must **not** run (pin: no `ide-safe: ef model` line when `.dataguard-snapshot.json` exists) — written against current code it trivially passes; it protects precedence after 3.I.
  - [ ] `ResolveEfSnapshotSource` single-match / multi-match / none behaviour (pin before extracting it into the locator).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** add the pins; green.
2. **Step B (Add shared infrastructure or seams):** `EfModelSnapshotLocator` with the directory-scan logic extracted from `ResolveEfSnapshotSource` (behaviour identical, pinned by 3.T); fixture `Fixtures/EfProject`.
3. **Step C (Refactor / Implement):** policy change (accept EF options under ide-safe); auto-detect call in `ValidateCommand` with the stderr line and `ACQUISITION:` multi-match diagnostic; docs.
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release`; `dotnet test tests/DataGuard.Core.Tests --filter "FullyQualifiedName~IdeSafe|FullyQualifiedName~EfModelSnapshotLocator"`; `./scripts/verify_docs_sync.sh`; manual: `validate --ide-safe --project tests/DataGuard.Core.Tests/Fixtures/EfProject` → no `Warning: no ground truth`, DG004/DG006 findings for the fixture's deliberate drift.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] Policy: `--ef-project`/`--ef-snapshot`/`--ef-context` accepted under ide-safe; `IdeSafePolicy.WriteReport` still lists suppressed config keys.
- [ ] Locator: one match → path; two matches → null + diagnostic naming both; skips `bin/obj`; respects depth/file caps (fixture with a deep decoy).
- [ ] End-to-end on `Fixtures/EfProject`: stderr contains `ide-safe: ef model Migrations/ShopContextModelSnapshot.cs (source parse)`, no `Warning: no ground truth`, SARIF contains the seeded DG004 (missing column) and DG006 (naming) results, exit 1.
- [ ] End-to-end precedence: fixture + `.dataguard-snapshot.json` → snapshot used, no `ef model` line.
- [ ] End-to-end explicit: `--ide-safe --ef-project <fixture> --ef-context ShopContext` → exit follows violations (not 2).
- [ ] Docs gate: `./scripts/verify_docs_sync.sh` exit 0.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release`
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Core.Tests --configuration Release --no-build --filter "FullyQualifiedName~IdeSafe|FullyQualifiedName~EfModelSnapshotLocator|FullyQualifiedName~ModelSnapshotCSharpParser"` và `./scripts/verify_docs_sync.sh`

## Todo List
- [ ] [3.T] Pin policy rejections, end-to-end no-ground-truth, precedence snapshot > EF, `ResolveEfSnapshotSource` behaviour
- [ ] [3.I] `EfModelSnapshotLocator` + fixture `EfProject`; nới `IdeSafePolicy` cho 3 option EF; auto-detect trong `ValidateCommand`; docs README/USAGE/VS Code README/CHANGELOG
- [ ] [3.V] Test hành vi mới (policy, locator, e2e auto-detect/precedence/explicit), `verify_docs_sync.sh`; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Một project EF Core với `ModelSnapshot.cs` chạy **Run Validation** trong VS Code (không snapshot, không credential) nhận được DG004/DG005/DG006 thật trong Problems, không còn cảnh báo "syntactic rules only".
- [ ] `--ide-safe` vẫn không bao giờ nạp assembly, mở kết nối hay plugin (test policy giữ nguyên).

## Risk Assessment & Security Considerations
- **Rủi ro:** auto-detect đọc file do repo kiểm soát → **Giảm thiểu:** chỉ parse C# source bằng Roslyn (không compile, không load), giới hạn kích thước như `ModelSnapshotCSharpParser` hiện có; đường dẫn phải nằm trong `--project` (dùng `PathNormalization`/kiểm tra prefix, không theo symlink ra ngoài — tái dùng guard của `ProjectCSharpSqlSource` nếu có [UNVERIFIED]).
- **Rủi ro:** nhiều `ModelSnapshot.cs` (multi-context) làm kết quả phụ thuộc thứ tự → **Giảm thiểu:** nhiều hơn một → không chọn, in `ACQUISITION:` hướng dẫn `--ef-context`; không bao giờ chọn ngầm.
- **Rủi ro:** đổi chính sách ide-safe là thay đổi bảo mật có chủ đích → **Giảm thiểu:** ghi ADR ngắn `docs/adr/` + câu hỏi validate Q1; red-team Security lens xác nhận không có đường dẫn tới nạp code.
