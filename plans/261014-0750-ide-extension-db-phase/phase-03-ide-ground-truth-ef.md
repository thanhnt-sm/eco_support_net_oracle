---
phase: 3
title: "EF ModelSnapshot ground truth under --ide-safe (CLI, serves VS)"
status: pending
priority: P1
effort: "4h"
dependencies: []
---

# Phase 3: EF ModelSnapshot ground truth under --ide-safe (CLI, serves VS)

<!-- Updated: Red Team Session 2 - reuse DiscoverSourceFiles (no new walker/caps), locator never throws and runs inside the try, open-once bounded read + ParseModelSnapshotWithDiagnostics(source), entity-derived violations get a Location, DG004 entity-branch duplicate suppressed, Summary.GroundTruth kind, no csproj fixture edit, minimal control-char escaping -->

## Overview
Let `validate --ide-safe --project <solutionDir>` (the VS argv) obtain **entity contracts** from the EF Core design-time `*ModelSnapshot.cs` already in the solution, without any new option, and make the entity-derived findings **visible in the VS Error List** (they currently have no location, so VS drops them). Parsing is a Roslyn source parse (`EfModelSource.ParseModelSnapshotWithDiagnostics`, `src/DataGuard.Core/Sources/EfModelSource.cs:264`, non-script `ParseText`, `ModelSnapshotCSharpParser.cs:52`); no assembly is loaded, so `--ide-safe` is **not** relaxed.

## Key Insights
- Policy stays: `IdeSafePolicy.FirstRejectedValidateOption` rejects `--ef-snapshot/--ef-project/--ef-context` (`src/DataGuard.Cli/IdeSafePolicy.cs:188-200`; pinned `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs:88-90`); explicit paths are not confined (`ValidateCommand.cs:542-545`); no host sends them. Auto-detect rooted at `--project` is the only addition. `IdeSafePolicy.Apply` does not touch `AutoDetectEFContext` (`IdeSafePolicy.cs:73-131`); the yml opt-out binds (`ConfigLoader.cs:257`).
- Walker: `ProjectCSharpSqlSource.DiscoverSourceFiles(string path)` is **public**, already called by the CLI for the same root in the same run (`src/DataGuard.Core/Sources/CSharp/ProjectCSharpSqlSource.Discovery.cs:16`; reparse-point directories skipped `:283-287`; prunes `bin obj .git .vs`; directory mode scans `*.cs` `:41-44`). The locator is a filter over it (name ends with `ModelSnapshot.cs`, no `node_modules` segment) — no second walker, no caps (the same run already reads every `.cs` under the root).
- Reading: `ExtractFromModelSnapshotWithDiagnosticsAsync` reads the whole file first (`EfModelSource.cs:252`) and only then the parser checks `source.Length > MaximumSourceBytes` (1 MiB chars, `ModelSnapshotCSharpParser.cs:13,49`); zero entities → `EfModelExtractionException` (`:270-273`; DG1306 for valid C# without entities, DG1303 for syntax errors) → `Validation failed:` exit 1 without SARIF (`ValidateCommand.cs:507-515`). The auto path therefore opens the file **once** with a bounded read and calls the public `ParseModelSnapshotWithDiagnostics(source, config, path)` (`:264`), inside the existing `try` (starts `:222`); a TOCTOU swap or a FIFO cannot bypass the bound.
- Injection: after `:224` (inside the try, after `IdeSafePolicy.Apply` `:139` and `FindDefaultSnapshot` `:199-207`), before `:230`. EF entities merge **unconditionally** (`:230-235`); snapshots yield only `DatabaseSchemaDescriptor` + procedures (`ContractAcquisition.cs:93-101`), so schema + entities is the goal, not a conflict. `ContractDiscovered` events for non-RawSql contracts come from `ValidateCommand.cs:246-257` after the merge.
- **Visibility gap (Critical, round 2):** entity-derived violations are created without a location — DG006 (`src/DataGuard.Core/Rules/NamingConventionRule.cs:52-55`, severity Info `:18`) and the DG004 entity branch (`ColumnShapeMatchRule.cs:71-76`); `ModelSnapshotCSharpParser` builds `EntityDescriptor` without `Location` (`:129`) although `ContractDescriptor` has `Location? Location = null` (`src/DataGuard.Core/Abstractions/Contracts.cs:99`); SARIF then has `locations: []` (`DiagnosticEmitter.cs:101-123`) and VS drops the result as "malformed" (`SarifErrorListPublisher.cs:130-137`, `Publishing.cs:97-99`). The DG004 entity branch also re-reports the query the RawSql branch (`:93-115`, with location) already reported → double ErrorCount. DG005 needs a schema (`NullableMismatchRule.cs:34-37`) and does not fire from entities alone.
- Fixtures are `.cs.txt` found by walking up to the source tree (`ProjectCSharpSqlSourceExtractorTests.cs:366-373`); the test csproj has no fixture items and needs none. A temp workspace copy with no csproj is scanned in directory mode.
- Docs: `README.md:20` / `README.vi.md:24` claim `init` writes a snapshot (it writes only the yml); `docs/USAGE.md:208`, `docs/03-components/tooling/cli.md:62-68` + `cli.vi.md` list the EF options as rejected under ide-safe (stays true). `Configuration.cs:206-209` is an empty `AutoDetectEFContext` stub.
- Line protocol: `ACQUISITION: {diagnostic.Path}` prints the absolute path (`:469`, `EfModelSource.cs:277`); yml unknown keys are echoed raw (`ConfigLoader.cs:58`). A double-quoted YAML key or (non-Windows) file name with `\n` forges a line start for any host; one tiny helper fixes both.

## Requirements
<!-- Updated: Validation Session 1 - Q7a: DG006 location + DG004 duplicate removal ship as a CLI behaviour change (CHANGELOG "Changed") -->
- **Functional:**
  1. Auto-detect (only when `--ide-safe && config.AutoDetectEFContext && efSnapshotPath is null && projectPath is not null`): candidates = `DiscoverSourceFiles(projectPath)` filtered to `*ModelSnapshot.cs` without a `node_modules` segment. Exactly one candidate that (a) has no `ReparsePoint|Device` attribute and `File.ResolveLinkTarget(path, false) is null`, (b) whose `Path.GetFullPath` starts with `Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)) + separator` (ordinal-ignore-case on Windows, ordinal elsewhere), (c) opened once with `FileShare.Read` and read through a stream capped at `MaximumSourceBytes + 1` bytes (over the cap → skipped) → its text is parsed with `ParseModelSnapshotWithDiagnostics(source, config, relativePath)`; stderr `ide-safe: ef model <relative path> (source parse)`.
  2. Zero candidates → silent. Several → `ACQUISITION: <n> ModelSnapshot.cs files found under --project (<first two relative paths>); commit .dataguard-snapshot.json or keep one design-time snapshot per solution`, none used. Skipped candidate → `ACQUISITION: <relative path>: EF model not used: <reason>`.
  3. The locator never throws (`IOException`, `UnauthorizedAccessException`, `SecurityException`, `ArgumentException`, `NotSupportedException` → `ACQUISITION: ef model auto-detect skipped: <message>`); parse failures (`EfModelExtractionException`, i.e. zero entities) → `ACQUISITION: <relative path>: EF model not used: <message>`; the run continues exactly as today (syntactic-only warning, exit by violations). Explicit `--ef-*` paths keep today's behaviour.
  4. **Locations for entity-derived findings:** `ModelSnapshotCSharpParser` records the `Entity(...)` invocation span on each `EntityDescriptor.Location` (file = the snapshot path, line/column of the call); `NamingConventionRule` and the DG004 entity branch pass `entityDesc.Location`; the DG004 entity branch is skipped for a `RawSqlDescriptor` that carries `ExpectedProperties` (the RawSql branch already reports it with the call-site location). Every SARIF result produced from the EF fixture has a non-empty `locations` array.
  5. Validate `Summary.Data.GroundTruth` = `none | entities | schema | schema+entities` (from the contract set at `:337`), consumed by Phase 2.
  6. `OutputText.EscapeControlCharacters` (one helper next to `RelativizeToWorkspace` in `Services/OutputSinks.cs`) applied to `ACQUISITION:` paths (`:469` and the new lines, relativised to the workspace) and to the unknown-key echo (`ConfigLoader.cs:58`).
  7. Docs: README/README.vi Quickstart say what `init` writes; USAGE §`--ide-safe`/§"Ground truth" and `cli.md`/`cli.vi.md` describe auto-detect; `src/DataGuard.VSCode/README.md` "Requirements" and USAGE §Visual Studio mention it; `Configuration.cs:206-209` stub replaced by a comment.
- **Non-functional:** no new NuGet dependency; policy tests untouched; locator pure and unit-testable (`internal`, IVT `DataGuard.Cli.csproj:29`); `./scripts/verify_docs_sync.sh` green.

## Architecture
```
validate --ide-safe --project SLN  (VS argv, unchanged)
  :105 ResolveEfSnapshotSource(explicit)   unchanged     :139 IdeSafePolicy.Apply   unchanged     :199 FindDefaultSnapshot   unchanged
  :222 try {
  :225   AcquireContractsAsync                                       unchanged
  NEW    if (ideSafe && config.AutoDetectEFContext && efSnapshotPath is null && projectPath is not null)
             var located = EfModelSnapshotLocator.TryLocate(projectRoot, DiscoverSourceFiles(projectRoot), maxBytes)   // never throws → (SourceText?, RelativePath?, Diagnostics[])
             print "ide-safe: ef model …" | ACQUISITION lines (escaped, relative)
             if (located.Source is not null) try { ParseModelSnapshotWithDiagnostics(source, config, relativePath) → contracts.AddRange(entities) } catch (EfModelExtractionException) { ACQUISITION line }
  :230   explicit efSnapshotPath path                                 unchanged
  :337   gate: EntityDescriptor present → no "Warning: no ground truth"; Summary.Data.GroundTruth = kind
Core: ModelSnapshotCSharpParser → EntityDescriptor.Location; NamingConventionRule/ColumnShapeMatchRule pass it; DG004 entity branch skips RawSql-with-ExpectedProperties
```

## Related Code Files
- **Create:** `src/DataGuard.Cli/Services/EfModelSnapshotLocator.cs`
- **Create:** `tests/DataGuard.Core.Tests/EfModelSnapshotLocatorTests.cs`, `tests/DataGuard.Core.Tests/IdeSafeEfAutoDetectEndToEndTests.cs`, `tests/DataGuard.Core.Tests/Fixtures/EfProject/ShopContextModelSnapshot.cs.txt`, `tests/DataGuard.Core.Tests/Fixtures/EfProject/OrderRepository.cs.txt` (snapshot declares `Order{OrderId, CustomerId, TotalAmount}` with `HasColumnName` naming drift for DG006; the repository selects a column set that differs from the entity)
- **Modify:** `src/DataGuard.Cli/Commands/ValidateCommand.cs` (new block after `:224`; `:469` escaping/relativising; Summary `GroundTruth` at `:494-499`), `src/DataGuard.Cli/Services/OutputSinks.cs` (helper), `src/DataGuard.Cli/Services/ConfigLoader.cs:58`
- **Modify:** `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs:129` (+ `Location`), `src/DataGuard.Core/Rules/NamingConventionRule.cs:52-55`, `src/DataGuard.Core/Rules/ColumnShapeMatchRule.cs:35-91` (location + duplicate skip), `src/DataGuard.Core/Models/Configuration.cs:206-209`
- **Modify:** `tests/DataGuard.Core.Tests/ModelSnapshotCSharpParserTests.cs` (Location), rule tests for DG006/DG004 (find with `Grep "NamingConventionRule\|ColumnShapeMatchRule" tests/`), `README.md`, `README.vi.md`, `docs/USAGE.md`, `docs/03-components/tooling/cli.md`, `docs/03-components/tooling/cli.vi.md`, `src/DataGuard.VSCode/README.md`, `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.Core.Tests/IdeSafeEfAutoDetectEndToEndTests.cs`, `ModelSnapshotCSharpParserTests.cs`, DG004/DG006 rule tests, `IdeSafePolicyTests.cs` (run as pin)
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] Policy: `--ef-*`, `--assembly`, `--offline`, `--connection`, `--plugins-dir` still rejected under ide-safe (`IdeSafePolicyTests.cs:85-110`, unchanged, green).
  - [ ] e2e today on the temp EF workspace: `validate --ide-safe --project <ws> --format sarif --output …` → `ide-safe: active` first, `Warning: no ground truth` present, no `ide-safe: ef model`, exit by violations (documents RC3; flipped in 3.V).
  - [ ] e2e explicit `--ef-project <ws>` **without** ide-safe → entities parsed, no warning (proves the fixture; protects it).
  - [ ] Parser pin: `ModelSnapshotCSharpParser.Parse` entity names/properties for the existing cases unchanged (Location is additive, `null` for the string-only overload).
  - [ ] Rule pins: DG006 fires for a `ColumnName` drift (today: no location); DG004 RawSql branch result keeps its call-site location; DG004 entity branch today produces a second location-less result for the same SQL (documents the duplicate; flipped in 3.V).
  - [ ] e2e: workspace with `.dataguard-snapshot.json` → `Using snapshot …` on stdout, unchanged.

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** fixtures + pins; green.
2. **Step B (Add shared infrastructure or seams):** `EfModelSnapshotLocator.TryLocate(root, candidates, maxBytes)` (pure over an injected file list; file checks via `Func<string, FileInfo>` seam for the delete-between-check test); `OutputText.EscapeControlCharacters`; `EntityDescriptor.Location` from the parser.
3. **Step C (Refactor / Implement):** `ValidateCommand` block + Summary kind + escaping; rules pass locations; DG004 duplicate skip; `Configuration.cs` stub; docs.
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release` (0 warnings); `dotnet test tests/DataGuard.Core.Tests --filter "FullyQualifiedName~IdeSafe|FullyQualifiedName~EfModelSnapshotLocator|FullyQualifiedName~ModelSnapshotCSharpParser|FullyQualifiedName~NamingConvention|FullyQualifiedName~ColumnShape|FullyQualifiedName~CliExitCode"`; GoldenCorpus suite (SARIF shape); `./scripts/verify_docs_sync.sh`; manual: `validate --ide-safe --project <temp EF ws>` → `ide-safe: ef model ShopContextModelSnapshot.cs (source parse)`, DG006 with a location in SARIF, no ground-truth warning, Summary `GroundTruth: entities`.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] Locator: one match → source + relative path; two → none + diagnostic naming both; `node_modules` filtered; file symlink skipped; path outside root skipped (`C:\Sln-evil\…` vs `C:\Sln\`); > 1 MiB skipped with reason without reading past the cap; `FileInfo` seam throwing `FileNotFoundException` → `ACQUISITION … skipped`, no exception.
- [ ] e2e auto-detect: stderr `ide-safe: ef model …`, no `Warning: no ground truth`, SARIF has DG006 **with** `locations[0].physicalLocation.artifactLocation.uri` ending in `ShopContextModelSnapshot.cs`, exactly one DG004 for the drifting query (RawSql location), Summary `GroundTruth: "entities"`, exit 1; every result in the SARIF has a non-empty `locations` array.
- [ ] e2e merge: snapshot **and** EF → `Using snapshot` + `ide-safe: ef model`; Summary `GroundTruth: "schema+entities"`.
- [ ] e2e malformed `FooModelSnapshot.cs` (valid C#, no entities) → `ACQUISITION: FooModelSnapshot.cs: EF model not used: …`, `Warning: no ground truth` present, SARIF written, exit by violations (never 1-without-SARIF).
- [ ] e2e `AutoDetectEFContext: false` → no auto-detect; e2e without `--ide-safe` → no auto-detect (CI path unchanged).
- [ ] Escaping: a yml with an unknown key containing `\n` → the `Warning: unknown configuration keys:` echo is one line with `\n` escaped.
- [ ] Docs gate `./scripts/verify_docs_sync.sh` exit 0.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release`
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Core.Tests --configuration Release --no-build --filter "FullyQualifiedName~IdeSafe|FullyQualifiedName~EfModelSnapshotLocator|FullyQualifiedName~ModelSnapshotCSharpParser|FullyQualifiedName~NamingConvention|FullyQualifiedName~ColumnShape|FullyQualifiedName~CliExitCode"`, `dotnet test tests/DataGuard.GoldenCorpus.Tests --configuration Release --no-build`, và `./scripts/verify_docs_sync.sh`

## Todo List
- [ ] [3.T] Fixture `EfProject/*.cs.txt`, e2e pins (no auto-detect today; explicit `--ef-project` parses; snapshot unchanged), parser/rule pins (DG006 không location, DG004 trùng), policy tests xanh
- [ ] [3.I] `EfModelSnapshotLocator` (filter trên `DiscoverSourceFiles`, open-once bounded read, không throw) + block trong `ValidateCommand` + Summary `GroundTruth` + escaping; `EntityDescriptor.Location` từ parser; DG006/DG004 truyền location, bỏ DG004 trùng; stub `Configuration.cs`; docs
- [ ] [3.V] Test locator + e2e (auto-detect với location, merge, malformed, opt-out, non-ide-safe) + escaping, `verify_docs_sync.sh`; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Từ VS (argv không đổi), một solution EF Core có `ModelSnapshot.cs` chạy **Run Validation**: Output pane có `ide-safe: ef model …`, không còn `Warning: no ground truth`, Phase 2 hiện `ground truth: entities`, Error List có DG006 với vị trí trong `ModelSnapshot.cs`, không có kết quả "skipped (malformed)".
- [ ] `--ide-safe` vẫn không nạp assembly, không mở kết nối, không nhận `--ef-*` (policy tests không đổi).

## Risk Assessment & Security Considerations
- **Rủi ro:** file do repo kiểm soát (symlink, FIFO, file lớn, bị đổi giữa check và read) → **Giảm thiểu:** attribute check, containment, open-once bounded stream (không có bước read-all), locator không throw; test symlink/oversize/delete-race.
- **Rủi ro:** snapshot EF không parse được làm hỏng lần chạy → **Giảm thiểu:** catch chỉ cho đường auto → `ACQUISITION:`; test malformed.
- **Rủi ro:** nhiều DbContext → **Giảm thiểu:** không chọn, thông điệp khuyên commit snapshot.
- **Rủi ro:** thêm Location vào DG006/DG004 đổi fingerprint baseline? → **Giảm thiểu:** kiểm tra `BaselineFingerprintTests` (fingerprint dùng `Properties`, không dùng location theo ghi chú `ColumnShapeMatchRule.cs:68`); chạy suite Core đầy đủ ở Step D.
- **Bảo mật:** không đổi `IdeSafePolicy`; đường dẫn/khóa in ra stderr được escape để host không nhận dòng giả.
