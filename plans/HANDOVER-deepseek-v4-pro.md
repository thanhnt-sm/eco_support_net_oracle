# BÀN GIAO TRIỂN KHAI — DataGuard (cho DeepSeek V4 Pro)

**Ngày bàn giao**: 2026-08-21
**Người bàn giao**: Hội đồng redteam 6 chuyên gia (xem `plans/2026-08-21-redteam-review.md`)
**Repo**: `/Volumes/Data/101.AI/GitHub/eco_support_net_oracle` — remote `github.com/thanhnt-sm/eco_support_net_oracle` (branch `main`)
**Trạng thái baseline khi bàn giao**: main = `3f543f5`; `dotnet build` 0 errors; `dotnet test` 38/38 PASS (30 Core + 8 GoldenCorpus)

---

## 0. Luật vận hành (bắt buộc đọc trước)

- `plans/ACTIVE_SESSION_REGISTER.md` = Single Source of Truth (tiếng Việt). Đọc TRƯỚC, cập nhật SAU mỗi phiên (LUẬT 5).
- LUẬT 2: sửa code → cập nhật `docs/` + `docs/sitemap_and_component_registry.md` (nếu có).
- Không tạo file ngoài cấu trúc đã quy hoạch; file tạm để ngoài repo (như `/tmp`).
- Build/test trước khi commit; không force-push; push thẳng `main` (convention hiện tại, không có PR flow — gh CLI chưa login).
- Ngôn ngữ tài liệu: tiếng Việt; code/technical terms giữ tiếng Anh.

## 1. Kiến trúc hiện tại

```
DataGuard.Core          ← abstractions (ContractDescriptor...), rules (DG001-006, DG015/016), security, telemetry, baseline, snapshot, auto-detection
DataGuard.SqlServer.Adapter   ← sys.parameters + sp_describe_first_result_set parser (ScriptDOM cho raw SQL)
DataGuard.Oracle.Adapter      ← ALL_ARGUMENTS/ALL_TAB_COLUMNS/NLS readers, RefCursorDescriber (chưa wire), length rules DG007-009, dialect rules DG010-014
DataGuard.MySql.Adapter       ← SP parser + dialect MY001-002 + length MY003
DataGuard.PostgreSql.Adapter  ← SP parser + dialect PG001-002 + length PG003
DataGuard.Analyzers     ← IDE light (UnvalidatedSqlCallGenerator, IIncrementalGenerator) + CI heavy (ContractValidationAnalyzer) + code fixes + attributes
DataGuard.Cli           ← dotnet tool "dataguard": validate, baseline, snapshot (refresh/show/diff), init, config, oracle-check, migrate, version
DataGuard.VSCode        ← status-bar wrapper (spawn CLI)
tests/DataGuard.Core.Tests, tests/DataGuard.GoldenCorpus.Tests
```

- **RuleId**: DG001-016 (Core + Oracle), MY001-003, PG001-003. CLI `GetRulesForProvider` (Program.cs) đã wire đủ theo provider.
- **3 ground-truth modes**: Full (DB live), Snapshot (JSON, **default**), Manual (attributes `ExpectedColumnAttribute` v.v.). ⚠️ Chưa chạy end-to-end — xem T2.4.
- **Build**: `Directory.Build.props` net9.0, TreatWarningsAsErrors=false (~3200 warnings StyleCop/CS pre-existing — không phải lỗi mới của bạn).
- **CI/CD**: `.github/workflows/ci.yml` (branch/PR) + `release.yml` (tag v*). Trusted Publishing NuGet + cosign + SBOM + Docker multi-arch GHCR.

## 2. Công việc cần thực thi (theo ưu tiên — từ redteam review)

> Quy ước: Mỗi task = Target / Change / Acceptance / Verify. Hoàn thành Phase 0 trước, mỗi phase xong phải `dotnet build` + `dotnet test` xanh rồi mới commit.

### PHASE 0 — Pre-release hardening (BLOCKER, làm trước tiên)

**T0.1 — Fix packaging Analyzer (BLOCKER)**
- Target: `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj`.
- Change: Analyzer pack hiện chỉ chứa `$(AssemblyName).dll` (dòng 26-27) nhưng `Analyzers.cs` using `DataGuard.Core.*` → khi nạp qua `analyzers/dotnet/cs` compiler không resolve Core.dll → FileNotFoundException. Giải pháp ưu tiên: bundle `DataGuard.Core.dll` + mọi non-framework dependency vào `analyzers/dotnet/cs` (vd `dotnet build` với `CopyLocalLockFileAssemblies` + pack theo list, hoặc `_GetPackageContents`); phương án B: tách các record/contracts dùng chung sang assembly riêng không vendor deps.
- Acceptance: unpack `.nupkg` thấy Core.dll bên trong `analyzers/dotnet/cs`; một project test tham chiếu analyzer package chạy được diagnostic.
- Verify: tạo project mẫu trong `/tmp` cài package cục bộ, chạy `dotnet build` thấy DG diagnostics.

**T0.2 — Fix sp_describe_first_result_set (BLOCKER)**
- Target: `src/DataGuard.Core/Sources/SqlServerParsers.cs` (~dòng 149-161).
- Change: ordinal đúng của result set: `is_hidden(0), column_ordinal(1), name(2), is_nullable(3), system_type_id(4), system_type_name(5), max_length(6), precision(7), scale(8)` → name=2, is_nullable=3, system_type_name=5, max_length=6, precision=7, scale=8. `@tsql = N'EXEC [schema].[proc]'`. Bọc try/catch per-procedure (error 11512/11513 = no result set → skip, không abort loop).
- Acceptance: chạy trên SQL Server thật (Testcontainers nếu có Docker) trả đúng name/type/length.
- Verify: unit test parser với fake reader có đúng schema ordinal + test proc không result-set.

**T0.3 — Metadata package + SourceLink**
- Target: 7 csproj (`src/**/*.csproj`).
- Change: RepositoryUrl/PackageProjectUrl = `https://github.com/thanhnt-sm/eco_support_net_oracle` (đang sai `github.com/DataGuard/DataGuard` ở 4 adapter; Cli/Analyzers thiếu); thêm PackageTags, PackageReadmeFile, Company, RepositoryType=git cho Cli/Analyzers; thêm `Microsoft.SourceLink.GitHub` + `PublishRepositoryUrl=true` (Core đã có) + `ContinuousIntegrationBuild` khi CI (`-p:ContinuousIntegrationBuild=true` trong workflows); PackageOutputPath đồng nhất `./nupkg`.
- Acceptance: `dotnet pack` 7 package, nuspec đúng URL; SourceLink hoạt động (debugger vào được source).
- Verify: `dotnet pack -c Release` + kiểm tra nupkg (unzip nuspec).

**T0.4 — License thống nhất (cần chủ repo chốt; mặc định MIT OSI)**
- Target: LICENSE/LICENSED.md/README/csproj.
- Change: Chọn 1 license (khuyến nghị MIT): root LICENSE = MIT; bỏ/xóa `LICENSE.md` PolyForm Noncommercial khỏi package surface (3 adapter đang set cả `PackageLicenseExpression=MIT` LẪN `PackageLicenseFile=LICENSE.md` → NU5034 — chỉ giữ expression); sửa README badge license; đồng bộ `.github/copilot-instructions.md` (bỏ/no-AI-training nếu publish OSS).
- Acceptance: `dotnet pack` không NU5034/NU5110; nuspec chỉ 1 license.
- ⚠️ Đây là quyết định pháp lý — nếu chủ repo chưa chốt, dừng task này và ghi blocker vào register.

**T0.5 — Rewrite README (DataGuard landing)**
- Target: `README.md`, `README.vi.md`.
- Change: Mô tả DataGuard (contract validation Entity ↔ SP/Raw SQL, .NET 9, NuGet + dotnet tool); narrative: "port dbt model contracts (Core v1.5, 2023) vào khoảng trống .NET stored-procedure mà Microsoft đã từ chối (EF issue #245, 2014)"; quickstart `dotnet tool install -g DataGuard.Cli` + `dataguard init` + `validate`; bảng rules; badge build/test; xóa toàn bộ nội dung EcoSupport npm/Rust/MCP. Đây cũng là package readme của Core (Core.csproj:14,44) — sau khi rewrite là đúng.
- Acceptance: README không còn từ "EcoSupport"/"cargo"/"npm"/"MCP" (trừ phần history); NuGet.org render đúng nội dung DataGuard.
- Verify: grep không ra legacy keywords; đọc lại flow quickstart chạy thử.

**T0.6 — Gate security cho tag release**
- Target: `.github/workflows/release.yml` + `ci.yml`.
- Change: Thêm vào `build-and-test` job của release: vuln-scan JSON gate (copy từ ci.yml), TruffleHog (pin SHA, `fail: true`), CodeQL init/analyze. Hoặc đổi `ci.yml` trigger thêm `tags: ['v*']` và release job `needs` CI. Chặn `workflow_dispatch` với tags input arbitrary (chỉ cho phép tag đã qua CI, hoặc bỏ input tags).
- Acceptance: tag push chạy đủ 3 gate; dispatch không bypass.
- Verify: đọc lại workflow; actionlint nếu có.

**T0.7 — Secrets hygiene**
- Target: `src/DataGuard.Cli/Program.cs` (config show handler ~343-348), `.gitignore`, `.dockerignore`.
- Change: `config show` redact `ConnectionString` (in `***` hoặc bỏ field); thêm `.dataguard*` vào .gitignore + .dockerignore; docs: khuyến nghị env `DATAGUARD_CONNECTION_STRING` thay `--connection` (tránh ps/history).
- Acceptance: `dataguard config show` không in connection string; `git status` không thấy .dataguard* khi tạo.
- Verify: chạy `config show` với config có ConnectionString.

**T0.8 — messageFormat/args alignment**
- Target: `src/DataGuard.Analyzers/Analyzers.cs` (DiagnosticDescriptors + 3 điểm Diagnostic.Create ~554-604).
- Change: Tất cả descriptor `messageFormat: "{0}"` (violation.Message đã là message hoàn chỉnh, truyền đúng 1 arg) — đặc biệt DG003 (3 placeholder), DG004 (2), DG005 (2), DG006 (2) hiện đang lệch arity → FormatException/message rỗng.
- Acceptance: mọi rule emit qua analyzer không ném; message hiển thị đầy đủ.
- Verify: thêm test arity (mỗi descriptor có số placeholder == số arg truyền) — xem T3.1.

### PHASE 1 — v0.2 core correctness

**T1.1 — Đồng bộ RuleId ↔ DiagnosticId**
- Target: `src/DataGuard.Core/Rules/ContractRules.cs` (RuleId), `src/DataGuard.Analyzers/Analyzers.cs` (DiagnosticIds, AllDescriptors, GetValidationRules, GetDiagnosticDescriptor).
- Change: Tách DG001: engine ParameterCountRule đổi RuleId (vd "DG101") hoặc đổi IDE UnvalidatedSqlCall; bỏ violation nội bộ dùng "DG001" cho "Empty SQL text"/"Empty stored procedure call" (chuyển id khác); thêm DG015/DG016 descriptors (đã có DiagnosticDescriptors.PhantomTable/PhantomColumn — chỉ thiếu trong AllDescriptors); thêm PhantomIdentifierRule vào GetValidationRules.
- Acceptance: không còn 2 thành phần khác nhau dùng chung 1 RuleId; phantom detection chạy trong analyzer; không còn fallback DG002 cho DG015/016.
- Verify: `dotnet test` + scratch verify phantom qua analyzer.

**T1.2 — Fix RuleDependencyGraph**
- Target: `src/DataGuard.Core/Rules/RuleDependencyGraph.cs` (RegisterRule key, GetParallelGroups ~122-140), `BuiltInRuleDependencies.CreateDefault` (dependency ID "ParameterCountRule" → RuleId "DG001"...).
- Change: Dependency ID = RuleId duy nhất; GetParallelGroups remove placeholder khỏi remaining khi complete; thêm unit test: graph không infinite loop, thứ tự topological ổn định (chạy 2 lần so sánh).
- Acceptance: `CreateDefault().GetExecutionOrder()` trả thứ tự hợp lệ, deterministic, không throw.
- Verify: unit test mới trong `tests/DataGuard.Core.Tests`.

**T1.3 — Sửa code fix còn sinh code lỗi**
- Target: `src/DataGuard.Analyzers/CodeFixes/CodeFixProviders.cs` (kiểm tra toàn file, đặc biệt class `AddMaxLengthAttributeFixProvider` ~432 dùng string literal; `CreateExpectedSpParameterAttribute` ~358 truyền "" cho dbType/direction → `Enum.Parse<ParameterDirection>("")` ném khi người dùng build).
- Change: MaxLength dùng NumericLiteralExpression + lấy length từ diagnostic properties (không hardcode 2000); ExpectedSpParameter truyền giá trị hợp lệ ("IN") hoặc sửa ctor `Enum.TryParse` lenient; hợp nhất 2 provider UseOracle (giữ bản rename UseSqlServer→UseOracle giữ connection string, xóa bản chain `.UseOracle()` thứ 2); (T1.3b) chuyển SkipContractCheck/ExpectedSpParameter/ExpectedColumn attributes sang assembly người dùng reference được (Core) HOẶC fix chèn kèm using; đọc class-level [SkipContractCheck].
- Acceptance: mọi quick-fix sinh code biên dịch được; test phủ (xem T3.1).
- Verify: CSharpCodeFixTest cho từng fix.

**T1.4 — Wire ground-truth modes end-to-end**
- Target: `src/DataGuard.Cli/Program.cs` (RunValidationAsync, validate --offline, snapshot diff), `src/DataGuard.Core/Sources/EfModelSource.cs` (hoặc mới ManualSource đọc ExpectedColumnAttribute), `src/DataGuard.Core/Baseline/BaselineManager.cs` (snapshot lưu schema descriptor).
- Change: (a) Manual: đọc attributes từ assembly/reflection → ContractDescriptor; (b) Snapshot: lưu schema (tables/columns) — không chỉ Violations — và `validate --offline`/Snapshot mode nạp snapshot thay vì DB; (c) `snapshot diff` exit 1 khi drift (thêm option `--fail-on-drift` để không phá hành vi warn hiện tại); (d) `RunValidationAsync` wire `AllArgumentsReader` (Oracle) + `MySqlStoredProcedureParser` + `PostgreSqlStoredProcedureParser`; providerOption mô tả đủ 4 provider.
- Acceptance: `dataguard validate --offline` với snapshot tồn tại → chạy rules trên schema thật; drift diff trả exit 1 khi có flag; mysql/postgresql validate không còn no-op.
- Verify: fixture + chạy CLI với snapshot giả.

**T1.5 — Oracle overload + catalog case**
- Target: `src/DataGuard.Oracle.Adapter/OracleReaders.cs` (GetOverloadsAsync ~173-186, GetParametersAsync ~60-65, GetAllColumnsAsync ~294-295,351-352).
- Change: Group/filter theo OVERLOAD (+ subprogram_id trên 12c) — SEQUENCE chỉ để sắp thứ tự; bỏ row function return value (argument_name IS NULL hoặc position=0) khi đọc parameters; `owner = UPPER(:owner)`, `table_name = UPPER(:tableName)` + warn khi non-empty owner trả 0 rows.
- Acceptance: overload 3-arg không vỡ thành 3 fragment; không param rỗng tên; lowercase config vẫn đọc được.
- Verify: Testcontainers Oracle (xem T3.3) hoặc golden fixture giả lập rows.

**T1.6 — Rewrite RefCursorDescriber**
- Target: `src/DataGuard.Oracle.Adapter/OracleReaders.cs` (~550-592).
- Change: `col_char_used` → `col_charsetform` (+ suy CHAR/BYTE từ col_char_length); hỗ trợ PROCEDURE với OUT SYS_REFCURSOR (không chỉ FUNCTION return); sau đó wire vào đường Oracle validation (đọc result shape khi SP trả ref cursor) + đọc col_charsetform để map NCHAR/NVARCHAR2/NCLOB (hoàn tất item deferred trước).
- Acceptance: PL/SQL block chạy được trên Oracle thật (không PLS-00302); ref-cursor result columns xuất hiện trong contract.
- Verify: Testcontainers Oracle.

**T1.7 — ScriptDOM decimal/numeric**
- Target: `src/DataGuard.Core/Sources/SqlServerParsers.cs` (~255-272).
- Change: Dispatch theo `SqlDataTypeReference.SqlDataTypeOption`: char/binary → literals[0]=length; decimal/numeric → literals[0]=precision, literals[1]=scale; scale=0 là hợp lệ (không null).
- Acceptance: `decimal(10,2)` → Precision=10, Scale=2; DG002 không báo nhầm.
- Verify: unit test parser.

**T1.8 — PostgreSQL parser hardening**
- Target: `src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs` (~45-55).
- Change: Filler detection = `ordinal_position IS NULL` (KHÔNG dùng parameter_name IS NULL — unnamed positional param có parameter_name NULL nhưng ordinal_position NOT NULL); key overload theo specific_name/signature (không chỉ routine name).
- Acceptance: proc `foo(int, text)` giữ đủ 2 param; 2 overload cùng tên không gộp.
- Verify: unit test với fake reader rows.

**T1.9 — Dialect keyword lists**
- Target: `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs` (~13-37, 111, 121), `MySqlDialectChecker.cs`, `PostgreSqlDialectChecker.cs`.
- Change: Bỏ token SQL chuẩn khỏi danh sách "exclusive" (window functions ROW_NUMBER/RANK/LAG/LEAD/NTILE/FIRST_VALUE/..., PIVOT, PARTITION BY, KEEP, MODEL, MERGE, OFFSET, FETCH, IDENTITY, RETURNING, GENERATED ALWAYS AS IDENTITY, ::, `); TOP/LIMIT dùng token boundary (regex `\bTOP\b` có ngữ cảnh SELECT TOP n, `\bLIMIT\b` + số) thay raw Contains; thêm negative fixtures (window function SQL không bị DG010/011).
- Acceptance: SQL chuẩn hiện đại không false positive; MySQL/Pg không báo chéo.
- Verify: unit test per rule dương + âm.

**T1.10 — DG008 per-column char_used**
- Target: `src/DataGuard.Oracle.Adapter/LengthMismatch.cs` (Detect ~188-214, IsUnicodeType ~246-257), `LengthMismatchRuleHelper` (~351-355).
- Change: byte/char math dùng `column.CharUsed` ('B'/'C') làm ground truth (fallback session NLS); bytes-per-char từ NLS_CHARACTERSET (NlsSessionReader đã đọc — truyền qua); IsUnicode dựa annotation thay vì chỉ CLR type name.
- Acceptance: VARCHAR2(50 BYTE) AL32UTF8 với 14 ký tự tiếng Việt → đúng cảnh báo; cột CHAR-semantics không false positive.
- Verify: golden fixture byte-semantics sửa data_length/char_length thực tế + test.

**T1.11 — Credential fail-open → fail-closed**
- Target: `src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs` (ResolveCredentialAsync), `CredentialManager.cs` (~127-140 DPAPI).
- Change: Config knob `AllowConfigFileFallback` (mặc định false ở production, true Development); mọi downgrade source ghi audit warning; CredentialManager: cross-platform encryption (libsecret/keyring/DPAPI-NG) hoặc fail-fast rõ ràng + docs matrix (bỏ comment libsecret giả).
- Acceptance: KeyVault lỗi → không âm thầm dùng plaintext ở production; Linux không PlatformNotSupportedException khi bật EncryptConnectionStringAtRest (hoặc fail-fast có thông điệp rõ).
- Verify: unit test các nhánh lỗi source.

**T1.12 — SupplyChainVerifier thật**
- Target: `src/DataGuard.Core/Security/SupplyChainVerifier.cs`, `src/DataGuard.Core/Health/HealthChecks.cs` (~195).
- Change: Fail-closed khi expected hash file thiếu (nếu caller yêu cầu); allowlist exact `PackageID + version` (từ restore graph) thay prefix; anchor bằng GH attestation digest khi có; HealthChecks gọi với hash file thật hoặc bỏ check không gating.
- Acceptance: verifier có thể fail khi thực sự lệch; không còn "always green" giả.
- Verify: unit test các nhánh.

**T1.13 — Public API trung thực**
- Target: `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`.
- Change: Implement hoặc xóa 6 stub (WithPlugins/WithTelemetry/ForSqlServer/ForOracle/WithBaseline/WithSnapshot); hoặc `[Experimental]` + docs rõ. Quyết định: xóa nếu không có kế hoạch, giữ nếu wire thật (WithTelemetry có TelemetryCollector sẵn; WithBaseline/WithSnapshot có BaselineManager sẵn — implement được).
- Acceptance: không còn public method no-op im lặng.
- Verify: grep không stub comment "would be".

**T1.14 — ADR scope Core**
- Target: `plans/adr/002-core-dependency-scope.md` (mới) + sửa claim trong Core.csproj Description/ADR-001 nếu cần.
- Change: Quyết định + ghi ADR: (a) giữ vendor deps trong Core và sửa claim "zero vendor deps" thành danh sách rõ, hoặc (b) tách ScriptDom/SqlClient/EFCore/AWSSDK/Roslyn ra khỏi Core (công lớn). Khuyến nghị (a) cho v0.2, (b) backlog.
- Acceptance: tài liệu + code metadata khớp thực tế.
- Verify: review ADR.

**T1.15 — QA hardening (test)**
- Target: `tests/` + golden corpus.
- Change: (a) GoldenCorpusTests: assert `unexpectedErrors.Should().BeEmpty()` + align fixture H1_002 (sửa schema column PHONE→PHONE_NUMBER hoặc entity); (b) thêm fixtures: negative (không diagnostic), CTE, schema-qualified, biểu thức, NCHAR, provider SqlServer/MySql/PostgreSql; (c) thêm `DataGuard.Analyzers.Tests` dùng `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` + `CodeFix.Testing` (test DG001-006, DG015/016 descriptors + quick-fix MaxLength/UseOracle/SkipContractCheck/ExpectedSpParameter); (d) unit test 14 rule trống (DG003-005, DG010-014, MY001-003, PG001-003) — reference MySql/Pg adapters vào test project; (e) CLI smoke test qua Process (exit-code các lệnh chính, đặc biệt oracle-check không connection → 1).
- Acceptance: test suite tăng từ 38 lên ≥ 80, xanh; không rule nào zero test.
- Verify: `dotnet test` full.

### PHASE 2 — post-v0.1 (làm khi Phase 0-1 xanh)

- T2.1 packages.lock.json (`RestorePackagesWithLockFile`) + cache key branch-scoped.
- T2.2 TruffleHog: `--fail true`, scan history (`--since-commit`/trufflehog git), tier unverified (warn-only).
- T2.3 Vuln gate: `pkg.get('id')` (không 'name'), check `problems` array, thêm `--deprecated`, assert frameworks scanned.
- T2.4 Docker: pin digest base images, Trivy/Grype scan, buildx `provenance: true, sbom: true`, docker-smoke chạy cả PR.
- T2.5 Plugin: AssemblyLoadContext riêng + allowlist SHA + log hashes; `new Version(...)` try/catch.
- T2.6 Audit log: ReadLastHashAsync try/catch (JsonException → null).
- T2.7 SECURITY.md viết lại cho DataGuard; xóa docker-compose.yml legacy + HealthCheckServer (nếu không wire) + `.github/.DS_Store`; CI SBOM version từ artifact thật; CodeQL queries mở rộng (interpolation/StringBuilder).
- T2.8 Config: Serialize/Deserialize 30+ field (AutoDetect*, EnableSmartDefaults, EnableTelemetry, rotation, ValidationTimeoutSeconds, Excluded*, Oracle.*/SqlServer.*); thay parser tay bằng YamlDotNet (đã là dependency); init --provider persist.
- T2.9 Gộp MySQL/PostgreSQL dialect checker base class.
- T2.10 MinVer (`dotnet add package MinVer` 7 csproj, version từ git tag; bỏ hardcode 0.1.0-alpha.1; đồng bộ DataGuardApi.Version + GetSchemaVersion).
- T2.11 3201 warnings: baseline đếm hiện tại, `TreatWarningsAsErrors` cho code mới (hoặc `<WarningsNotAsErrors>`), ưu tiên CS1591 (XML doc) + CS860x.
- T2.12 Null-guard: `ParameterTypeMatchRule`/`ParameterDirectionRule` foreach Parameters null; PhantomIdentifierRule ToDictionary xử lý trùng key/null tên.
- T2.13 Grants: rewrite 4 artifact (SUBMISSION_CHECKLIST, grant_pitch, written_explanation, ecosystem_impact_matrix) quanh DataGuard + narrative dbt/#245; thay demo_scan.sh bằng demo .NET; thêm `samples/` project dùng được (`dotnet tool install` + validate SARIF); CHANGELOG.md + icon.
- T2.14 VSCode: publisher thật, repository, icon, license, package-lock.json + `npm ci`; chốt vai trò (wrapper CLI hay tích hợp Roslyn).
- T2.15 Generator: khớp signature Dapper thật (Query<T>/QueryAsync/Execute với SQL literal arg đầu); bỏ GetSymbolInfo/interp.ToString khỏi fast path khi có thể; 1 bản ToSnakeCase/ToPascalCase dùng chung (Core) cho cả analyzer/rule/codefix.
- T2.16 Fix placebo: analyzer đọc marker comment để suppress hoặc thay bằng fix thật.
- T2.17 Snapshot drift: so sánh major.minor (wire `BaselineManager.ExtractMajorMinor`) thay full banner.

---

## 3. Chuẩn verify chung (mỗi phase)

1. `dotnet build DataGuard.sln -c Debug` → 0 errors (warnings pre-existing không phải lỗi mới của bạn — nhưng **không được tăng số warnings** so với baseline 3216).
2. `dotnet test DataGuard.sln` → toàn bộ xanh (baseline 38, sau T1.15 ≥ 80).
3. Chạy `dotnet run --project src/DataGuard.Cli -- <lệnh liên quan>` smoke cho thay đổi CLI.
4. Commit theo conventional (feat/fix/chore/docs) + cập nhật register sau mỗi phiên.
5. Push `main` (không force). Không commit file nhạy cảm (`.dataguard*`, nupkg, bin/obj).

## 4. Blocker ngoài / cần chủ repo

| Blocker | Hành động cần |
|---------|---------------|
| License (T0.4) | Chủ repo chốt MIT OSI (khuyến nghị) |
| Secret `NUGET_USER` | Tạo trên nuget.org (Trusted Publishing) — hạn chót 01/11/2026 |
| Publish end-to-end | Tag `v0.1.0` + secrets → CI chạy release |
| Testcontainers/CI thật | GitHub runner có Docker (local không có daemon) |
| gh CLI | `gh auth login` nếu muốn issue/PR |

## 5. Checklist bàn giao

- [x] Redteam review 6 domain hoàn tất (plans/2026-08-21-redteam-review.md)
- [x] Baseline: build 0 error, tests 38/38, main 3f543f5
- [ ] Phase 0 (T0.1-T0.8) hoàn thành + test xanh
- [ ] Phase 1 (T1.1-T1.15) hoàn thành + test ≥ 80 xanh
- [ ] Phase 2 backlog theo ưu tiên
- [ ] Register cập nhật sau mỗi phiên
