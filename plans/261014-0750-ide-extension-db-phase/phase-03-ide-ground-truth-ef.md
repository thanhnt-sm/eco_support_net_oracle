---
phase: 3
title: "EF ModelSnapshot ground truth under --ide-safe (CLI, serves VS)"
status: pending
priority: P1
effort: "3h"
dependencies: []
---

# Phase 3: EF ModelSnapshot ground truth under --ide-safe (CLI, serves VS)

## Overview
Let `validate --ide-safe --project <solutionDir>` (the VS argv) obtain **entity contracts** from the EF Core design-time `*ModelSnapshot.cs` already in the solution, without any new option, so the entity-vs-SQL rules (DG004 shape against real entities, DG006 naming, DG007/DG008/DG009 length rules once a schema exists) run from the IDE. Parsing that file is a Roslyn source parse (`EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync`, `src/DataGuard.Core/Sources/EfModelSource.cs:241-253`); no assembly is loaded, so `--ide-safe` is **not** relaxed.

## Key Insights
- `IdeSafePolicy.FirstRejectedValidateOption` rejects `--ef-snapshot/--ef-project/--ef-context` (`src/DataGuard.Cli/IdeSafePolicy.cs:188-200`; pinned by `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs:88-90`). This stays: no host sends them, and an explicit path is not confined to the workspace (`ValidateCommand.cs:542-545`). Auto-detect rooted at `--project` is the only path added.
- Existing scan `ResolveEfSnapshotSource` (`ValidateCommand.cs:527-593`, single caller `:105`) uses `Directory.EnumerateFiles(root, "*ModelSnapshot.cs", SearchOption.AllDirectories)` with a bin/obj/.git name filter only: no reparse-point skip, no depth/file caps, no `node_modules`. A bounded pattern exists in Core (`src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs:487-545`: `AttributesToSkip = FileAttributes.ReparsePoint`, pruned dirs, `FileInfo.Length > MaximumFileBytes`) but is `internal` to Core with no IVT to Cli → copy the pattern, do not call it.
- Extraction reads the whole file first (`EfModelSource.cs:252` `File.ReadAllTextAsync`) and only then the parser checks `source.Length > MaximumSourceBytes` (1 MiB, `ModelSnapshotCSharpParser.cs:13,49`); a parse with diagnostics and zero entities **throws** `EfModelExtractionException` (`EfModelSource.cs:270-273`), which `validate` turns into `Validation failed: …` exit 1 with no SARIF (`ValidateCommand.cs:507-515`). Auto-detect must check size before reading and must never let a bad file change the exit code.
- Injection point: after default-snapshot discovery and the ide-safe policy (`ValidateCommand.cs:139` `IdeSafePolicy.Apply`, `:199-207` `FindDefaultSnapshot`), before acquisition (`:225`); EF entities are merged **unconditionally** when `efSnapshotPath` is set (`:230-235`) — snapshots yield only `DatabaseSchemaDescriptor` + procedures (`ContractAcquisition.cs:93-101`), so merging EF entities on top of a snapshot is the desired combination (schema + entities), not a conflict.
- Oracles: the ground-truth gate counts `EntityDescriptor` (`:337`); DG004 already fires without ground truth through the `RawSqlDescriptor.ExpectedProperties` branch (`ColumnShapeMatchRule.cs:93`), and DG005 needs a `DatabaseSchemaDescriptor` (`NullableMismatchRule.cs:34-37`) — so the proof of EF use is DG006 (entity-only) and the absence of the `Warning: no ground truth` line, not DG004/DG005.
- Fixtures in `tests/DataGuard.Core.Tests/Fixtures/` are `.cs.txt` (the test project has no `Compile Remove`); e2e tests generate a temp workspace (`CliExitCodeTests.cs:316` pattern) and run the real CLI via `CliProcessTestRunner`.
- `AutoDetectEFContext` is a config default `true` (`Configuration.cs:43`) whose only consumer is an empty block (`:206-209`). Docs: `README.md:20` / `README.vi.md:24` claim `init` writes a snapshot (it writes only the yml); `docs/USAGE.md:208` and `docs/03-components/tooling/cli.md:62-68` list the EF options as rejected under ide-safe — these stay true.

## Requirements
- **Functional:**
  1. When `--ide-safe` and `config.AutoDetectEFContext` and no explicit EF option and `--project` is set: a bounded walker under the `--project` root (`EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 8, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }`, pruned dirs `bin obj .git .vs node_modules`, stop after 5 000 entries) looks for `*ModelSnapshot.cs`. Exactly one candidate that is a regular file (`File.ResolveLinkTarget(path, false) is null`), whose full path is under the root, with `FileInfo.Length <= 1 MiB` and no control character in its path → it becomes `efSnapshotPath`; stderr prints `ide-safe: ef model <relative path> (source parse)`.
  2. Zero candidates → silent. Several → `ACQUISITION: <n> ModelSnapshot.cs files found under --project (<first two relative paths>); commit .dataguard-snapshot.json or keep one design-time snapshot per solution` and no EF source. Oversized / symlink / control-char candidate → skipped with an `ACQUISITION:` line naming the reason (path escaped: control chars → `\uXXXX`).
  3. Extraction failure of the auto-detected file (`EfModelExtractionException`, `IOException`, `UnauthorizedAccessException`) → `ACQUISITION: <relative path>: EF model not used: <message>` and the run continues exactly as today (syntactic-only warning, exit by violations). Explicit `--ef-*` paths keep today's behaviour.
  4. `Configuration.cs:206-209` stub replaced by a comment pointing at the CLI auto-detect (no behaviour).
  5. Docs: README/README.vi Quickstart say what `init` writes; USAGE §`--ide-safe` and §"Ground truth" describe auto-detect; VS Code README "Requirements" + USAGE §Visual Studio mention it.
- **Non-functional:** walker < 200 ms on a 5 000-entry tree; no new NuGet dependency; policy tests untouched; `./scripts/verify_docs_sync.sh` green.

## Architecture
```
validate --ide-safe --project SLN  (VS argv, unchanged)
  :105 ResolveEfSnapshotSource(explicit)        unchanged
  :139 IdeSafePolicy.Apply                      unchanged (EF options still rejected)
  :199 FindDefaultSnapshot                      unchanged
  NEW  if (ideSafe && config.AutoDetectEFContext && efSnapshotPath is null && projectPath is not null)
           efSnapshotPath = EfModelSnapshotLocator.TryLocate(projectRoot, out diagnostics)   // bounded, reparse-safe, size-checked
           print "ide-safe: ef model …" | ACQUISITION lines
  :225 AcquireContractsAsync                    unchanged
  :230 if efSnapshotPath: Extract… (wrapped in try/catch ONLY for the auto-detected path) → contracts.AddRange(entities)
  :337 gate: EntityDescriptor present → no "Warning: no ground truth"
```
`EfModelSnapshotLocator` is a new `internal static` class in `src/DataGuard.Cli/Services/` (file-system discovery is a CLI concern); `ResolveEfSnapshotSource` is left as is (explicit-option semantics, exit 2 pins in `CliExitCodeTests.cs:355-369`).

## Related Code Files
- **Create:** `src/DataGuard.Cli/Services/EfModelSnapshotLocator.cs`
- **Create:** `tests/DataGuard.Core.Tests/EfModelSnapshotLocatorTests.cs`, `tests/DataGuard.Core.Tests/IdeSafeEfAutoDetectEndToEndTests.cs`
- **Create:** `tests/DataGuard.Core.Tests/Fixtures/EfProject/ShopContextModelSnapshot.cs.txt`, `Fixtures/EfProject/OrderRepository.cs.txt`, `Fixtures/EfProject/Shop.csproj.txt` (copied into a temp workspace by the e2e test; the snapshot declares `Order{OrderId, CustomerId, TotalAmount}` with a deliberate naming drift for DG006 and the repository selects a column set that differs from the entity)
- **Modify:** `src/DataGuard.Cli/Commands/ValidateCommand.cs` (after `:207`; `:230-235` try/catch for the auto path), `src/DataGuard.Core/Models/Configuration.cs:206-209`, `tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj` (fixture `None` items, same as existing `Fixtures/CSharpSql`)
- **Modify:** `README.md`, `README.vi.md`, `docs/USAGE.md`, `docs/03-components/tooling/cli.md`, `docs/03-components/tooling/cli.vi.md`, `src/DataGuard.VSCode/README.md`, `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.Core.Tests/IdeSafeEfAutoDetectEndToEndTests.cs`, `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs` (no change, run as pin)
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] Policy: `--ef-snapshot/--ef-project/--ef-context/--assembly/--offline/--connection/--plugins-dir` still rejected under ide-safe (`IdeSafePolicyTests.cs:85-110`, unchanged, run green).
  - [ ] e2e on the temp EF workspace **today**: `validate --ide-safe --project <ws> --format sarif --output …` → first stderr `ide-safe: active`, contains `Warning: no ground truth`, no `ide-safe: ef model` line, exit by violations (documents RC3; flipped in 3.V).
  - [ ] e2e with explicit `--ef-project` **without** ide-safe on the same workspace → entities parsed, no ground-truth warning (proves the fixture snapshot is parseable; protects the fixture).
  - [ ] e2e: a workspace with a `.dataguard-snapshot.json` → `Using snapshot …` on stdout (today), unchanged after 3.I.

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** fixtures + e2e pins; green.
2. **Step B (Add shared infrastructure or seams):** `EfModelSnapshotLocator` (pure function over a root path; unit-testable with temp dirs incl. symlink and oversized decoys) + `PathText.EscapeControlCharacters` helper used by every stderr path print.
3. **Step C (Refactor / Implement):** `ValidateCommand` branch + try/catch for the auto path; `Configuration.cs` stub; docs.
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release` (0 warnings); `dotnet test tests/DataGuard.Core.Tests --filter "FullyQualifiedName~IdeSafe|FullyQualifiedName~EfModelSnapshotLocator"`; `./scripts/verify_docs_sync.sh`; manual: `validate --ide-safe --project <temp EF ws>` → `ide-safe: ef model Migrations/ShopContextModelSnapshot.cs (source parse)`, DG006 in SARIF, no ground-truth warning.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] Locator: one match → path; two → null + diagnostic naming both; `bin/obj/node_modules/.vs` pruned; file symlink skipped; directory symlink not followed; > 1 MiB skipped with reason; > 5 000 entries stops without exception; path with `\n` skipped and escaped in the diagnostic.
- [ ] e2e auto-detect: stderr has `ide-safe: ef model …`, no `Warning: no ground truth`, SARIF contains DG006 for the seeded drift, progress has a `ContractDiscovered`/`PhaseCompleted` with `ContractCount` ≥ entities; exit 1.
- [ ] e2e precedence/merge: workspace with snapshot **and** EF → both `Using snapshot` and `ide-safe: ef model`; DG006 present.
- [ ] e2e malformed `FooModelSnapshot.cs` (not EF) → `ACQUISITION: … EF model not used: …`, `Warning: no ground truth` present, SARIF written, exit by violations (never 1-without-SARIF).
- [ ] e2e `AutoDetectEFContext: false` in the yml → no auto-detect line.
- [ ] e2e without ide-safe → no auto-detect (CI/CLI behaviour unchanged; explicit options remain the CI way).
- [ ] Docs gate `./scripts/verify_docs_sync.sh` exit 0.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release`
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Core.Tests --configuration Release --no-build --filter "FullyQualifiedName~IdeSafe|FullyQualifiedName~EfModelSnapshotLocator|FullyQualifiedName~ModelSnapshotCSharpParser|FullyQualifiedName~CliExitCode"` và `./scripts/verify_docs_sync.sh`

## Todo List
- [ ] [3.T] Fixture `EfProject/*.cs.txt`, e2e pins (no auto-detect today; explicit `--ef-project` parses; snapshot unchanged), policy tests chạy xanh
- [ ] [3.I] `EfModelSnapshotLocator` (bounded, reparse-safe, size-checked, escaped paths) + nhánh trong `ValidateCommand` + try/catch auto path; stub `Configuration.cs`; docs README/USAGE/cli.md/VS Code README/CHANGELOG
- [ ] [3.V] Test locator + e2e (auto-detect, merge với snapshot, malformed, opt-out, non-ide-safe), `verify_docs_sync.sh`; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Từ VS (argv không đổi), một solution EF Core có `ModelSnapshot.cs` chạy **Run Validation** không còn `Warning: no ground truth`; Output pane (Phase 2) không hiện "syntactic only"; Error List có DG006 thật.
- [ ] `--ide-safe` vẫn không nạp assembly, không mở kết nối, không nhận `--ef-*` (policy tests không đổi).

## Risk Assessment & Security Considerations
- **Rủi ro:** file do repo kiểm soát (symlink tới `/dev/zero`, file 4 GB, FIFO) → **Giảm thiểu:** regular-file check, containment dưới `--project`, `FileInfo.Length` trước khi đọc, reparse-point skip ở cả walker lẫn file; test symlink/oversize.
- **Rủi ro:** snapshot EF không parse được làm hỏng lần chạy → **Giảm thiểu:** try/catch chỉ cho đường auto, chuyển thành `ACQUISITION:` và tiếp tục; test malformed.
- **Rủi ro:** nhiều DbContext → chọn sai → **Giảm thiểu:** nhiều hơn một thì không chọn; thông điệp khuyên commit snapshot.
- **Bảo mật:** không đổi `IdeSafePolicy`; mọi đường dẫn in ra stderr được escape ký tự điều khiển để host (Phase 2) không nhận dòng giả.
