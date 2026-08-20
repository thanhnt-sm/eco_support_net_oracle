# Redteam Review — Hội đồng chuyên gia rà soát giải pháp DataGuard

**Ngày**: 2026-08-21
**Trạng thái**: Hoàn tất review — roadmap nâng cấp sẵn sàng bàn giao
**Cơ sở**: Redteam Council Protocol (plans/master-plan.md) — 6 chuyên gia độc lập, bằng chứng file:line, READ-ONLY

---

## 1. Thành viên hội đồng

| # | Chuyên gia | Domain | Output |
|---|-----------|--------|--------|
| 1 | Compiler/Roslyn Engineer | IDE perf, analyzer correctness, code-fix | `agent://CouncilCompiler` |
| 2 | DBA/Oracle Expert | Ground truth, catalog readers, parsers | `agent://CouncilDba` |
| 3 | DevSecOps/Supply-chain | CI/CD, packaging, secrets, supply chain | `agent://CouncilDevSecOps` |
| 4 | Enterprise Architect | Modularity, versioning, API surface | `agent://CouncilArchitect` |
| 5 | OSS Growth/Grant Reviewer | Positioning, docs, packaging, adoption | `agent://CouncilOssGrowth` |
| 6 | QA/Test Engineer | Coverage, golden corpus, integration | `agent://CouncilQa` |

**Hiện trạng được audit**: main = 3f543f5; build 0 errors; tests 38/38; 16 criticals + 24/27 informational đã fix ở 2 phiên trước.

---

## 2. Điểm mạnh xác nhận (strengths)

- Kiến trúc tách IDE-light (IIncrementalGenerator) / CI-heavy (DiagnosticAnalyzer) đúng hướng; descriptor + DiagnosticId tập trung 1 nơi.
- Hướng phụ thuộc solution đúng: Core ← Adapters ← CLI/Analyzers, không reference ngược.
- Oracle length semantics tách 3 rule (DG007/008/009) với CharLength/MaxLength riêng; NLS_LENGTH_SEMANTICS first-class.
- CI/CD: actions pin SHA, secret qua env var (CWE-094 đã fix), Trusted Publishing + cosign keyless bundle + attest v4, SBOM, gh CLI draft→publish, Docker multi-arch non-root, fail-loud duplicate version.
- Supply chain: audit log hash-chain + tail-truncation checkpoint (có test); vuln gate chạy được trên SDK 9.0.310 (top-level + transitive); repo sạch secret (scan 250 files).
- Rule set đã wire đầy đủ vào CLI `GetRulesForProvider` (DG001-016 + MY001-003 + PG001-003).
- Golden corpus data-driven (taxonomy H1/H2/H3/Length/Vietnamese); test adversarial (tail-truncation, deterministic concurrent).
- Docs/ phần lớn đã migrate sang DataGuard; narrative "dbt contracts + Microsoft refused (#245)" đã có trong research/muc_tieu/2.md.

---

## 3. Tổng hợp findings theo mức độ

### 3.1 BLOCKER — phải sửa TRƯỚC publish (P0)

| # | Finding | Bằng chứng | Nguồn |
|---|---------|-----------|-------|
| B1 | **Analyzer package sẽ vỡ khi nạp**: chỉ pack `$(AssemblyName).dll`, không có DataGuard.Core.dll → FileNotFoundException/MissingMethodException ở IDE/CI | Analyzers.csproj:26-27; Analyzers.cs:15-16 using Core | Architect, Compiler |
| B2 | **sp_describe_first_result_set đọc sai ordinal**: GetString(0) chạm bit is_hidden, GetBoolean(1) chạm int → InvalidCastException trên SQL Server thật; thiếu `EXEC` prefix trong @tsql; không try/catch proc không result-set (error 11512) | SqlServerParsers.cs:149,156-161 | DBA |
| B3 | **RepositoryUrl/ProjectUrl fake** `github.com/DataGuard/DataGuard` (repo không tồn tại) ở 4 adapter; Cli/Analyzers thiếu URL → SourceLink chết, typosquat risk | 4 adapter csproj:9-10; Cli/Analyzers csproj | DevSecOps, OSS, Architect |
| B4 | **License mâu thuẫn 3 chiều**: README Apache-2.0/PolyForm, csproj MIT, LICENSE.md PolyForm Noncommercial + AI-training ban; Oracle/MySql/Pg set đồng thời PackageLicenseExpression=MIT + PackageLicenseFile=LICENSE.md → NU5034 | README.md:7,145; LICENSE.md:1-20; LICENSE:1; Oracle.csproj:8+12 | OSS, Architect |
| B5 | **README.md 100% sản phẩm cũ EcoSupport** (npm/Rust/MCP) — vừa là landing page vừa là NuGet package readme của Core | README.md:1-147; Core.csproj:14,44 | OSS |
| B6 | **Tag release không có security gates**: ci.yml chỉ chạy branch/PR; release.yml (tag v* + dispatch) không có vuln scan/TruffleHog/CodeQL → commit lỗi ship thẳng NuGet/GHCR | ci.yml:6-11; release.yml:5-14,41-98 | DevSecOps |
| B7 | **`config show` in ConnectionString ra stdout** + .gitignore không có `.dataguard*` → rò rỉ credential qua log/commit | Program.cs:343-348,545; .gitignore | DevSecOps |
| B8 | **messageFormat/args mismatch**: Diagnostic.Create truyền 1 arg (message đã format) nhưng DG003/004/005/006 khai 2-3 placeholder → message rỗng chỗ {1}/{2} hoặc FormatException | Analyzers.cs:554-558,577-581,600-604 vs :73,82,91,100 | Compiler |

### 3.2 P1 — v0.2 core correctness (sau P0)

| # | Finding | Nguồn |
|---|---------|-------|
| P1.1 | RuleId collision: DG001 dùng cho cả ParameterCountRule (engine) và UnvalidatedSqlCall (IDE); AllDescriptors thiếu DG001/DG015/DG016 → fallback DG002 sai | Compiler |
| P1.2 | RuleDependencyGraph dependency ID = tên class nhưng node key = RuleId → placeholder null phá topological sort, GetParallelGroups loop vô hạn | Compiler |
| P1.3 | Code fix còn sinh code lỗi: bản AddMaxLengthAttributeFixProvider thứ 2 vẫn dùng string literal; ExpectedSpParameter(name,"","") → `Enum.Parse<ParameterDirection>("")` ném; attribute nằm trong assembly analyzer nên project người dùng không reference → không biên dịch; 2 provider UseOracle mâu thuẫn cho DG012 | Compiler |
| P1.4 | Ground-truth modes chưa end-to-end: Manual mode không code đọc ExpectedColumnAttribute; snapshot chỉ lưu Violations không lưu schema → `validate --offline` contract rỗng exit 0; `snapshot diff` exit 0 khi drift → CI không gate | Architect |
| P1.5 | MySQL/PostgreSQL `validate` là no-op: RunValidationAsync chỉ extract sqlserver (oracle là comment rỗng) → rules chạy trên contract rỗng | Architect, DBA |
| P1.6 | Oracle overload keying sai (group theo SEQUENCE thay vì OVERLOAD) → 1 overload vỡ thành N fragment, args trộn lẫn; function return value thành param OUT rỗng | DBA |
| P1.7 | RefCursorDescriber PL/SQL tham chiếu field không tồn tại `col_char_used` → PLS-00302 trên Oracle thật (nặng hơn "chưa wire" như register ghi); chỉ hỗ trợ FUNCTION trả SYS_REFCURSOR, không hỗ trợ OUT param | DBA |
| P1.8 | Oracle catalog query không UPPER() owner/table_name → cấu hình lowercase im lặng trả 0 rows | DBA |
| P1.9 | ScriptDOM decimal/numeric: literals[0]=maxLength nhưng thực tế là precision → DG002 sai mọi param decimal | DBA |
| P1.10 | PostgreSQL unnamed positional param bị nhầm filler (parameter_name NULL) → mất param thật; overload gộp theo tên | DBA |
| P1.11 | Dialect keyword lists chứa SQL chuẩn (window functions, MERGE, IDENTITY, RETURNING, GENERATED AS IDENTITY, ::, `) → false positive 2 chiều; TOP/LIMIT dùng raw Contains (khớp "TOPIC"/"LIMITED") | DBA |
| P1.12 | DG008 dùng session semantics + maxBytesPerChar=1 cho non-Unicode, không dùng per-column char_used + NLS_CHARACTERSET → false neg/pos ORA-12899 | DBA |
| P1.13 | ZeroTrustCredentialProvider fail-OPEN: mọi nguồn (KeyVault/AWS/Vault) lỗi bị nuốt → fallback plaintext config-file credential, chỉ stderr warning | DevSecOps |
| P1.14 | CredentialManager encryption Windows-only (DPAPI) → EncryptConnectionStringAtRest trên Linux/macOS/Docker ném PlatformNotSupportedException; comment libsecret không có thật | DevSecOps |
| P1.15 | SupplyChainVerifier fail-open/decorative: AssemblyIntegrity luôn true (không anchor), ExpectedHashMatch skip khi thiếu file, prefix-only trust ("Dapper.Extensions" pass), HealthChecks gọi VerifyAsync(null) không bao giờ fail | DevSecOps |
| P1.16 | snupkg không bao giờ publish (chỉ match *.nupkg) + thiếu SourceLink/DeterministicBuilds → mất provenance | DevSecOps |
| P1.17 | PublicApiSurface toàn dead code + 6 method stub no-op (WithPlugins/WithTelemetry/ForSqlServer/ForOracle/WithBaseline/WithSnapshot) | Architect |
| P1.18 | Core tuyên bố "zero vendor deps" nhưng tham chiếu 15+ package (AWSSDK, SqlClient, ScriptDom, EF Core, Roslyn) trái ADR-001 → cần ADR mới | Architect |
| P1.19 | Golden corpus chỉ assert "tồn tại ≥1 diagnostic", không assert exact-match; unexpectedErrors tính nhưng không assert; 14/22 rules zero test; không analyzer/codefix/CLI test; Testcontainers 4.14.0 reference nhưng zero usage; 8 fixture đều Oracle-only | QA |

### 3.3 P2 — post-v0.1 / hardening

| # | Finding | Nguồn |
|---|---------|-------|
| P2.1 | packages.lock.json absent + NuGet cache restore-keys cross-branch → poisoning exposure | DevSecOps |
| P2.2 | TruffleHog only_verified:true + chỉ scan working tree (không history) + không fail input | DevSecOps |
| P2.3 | Vuln gate: lỗi `pkg.get('name')` nhưng JSON key là 'id' → báo "Vulnerable package: ?"; không check 'problems' array; --deprecated không check | DevSecOps |
| P2.4 | Docker base tag float (sdk:9.0/runtime:9.0), không image scan (Trivy/Grype), build-push không sbom/provenance inputs; docker-smoke chỉ chạy main | DevSecOps |
| P2.5 | RulePluginManager load mọi *.dll từ AppData user-writable, không ALC isolate, không verify | DevSecOps |
| P2.6 | Audit log: ReadLastHashAsync không try/catch → 1 dòng hỏng DoS toàn logger | DevSecOps |
| P2.7 | SECURITY.md sai product (EcoSupport); docker-compose.yml trỏ Dockerfile đã xóa; HealthCheckServer dead code; CI SBOM hardcode 0.1.0-ci; CodeQL query chỉ bắt AddExpr | DevSecOps |
| P2.8 | Config round-trip 16/30+ field (mất AutoDetect*, EnableSmartDefaults, EnableTelemetry, rotation, Excluded*, Oracle.*/SqlServer.*); parser YAML tay không xử lý comment/nested/quoted, Enum.Parse không bắt lỗi; init --provider không persist | Architect |
| P2.9 | Trùng lặp copy-paste MySQL↔PostgreSQL dialect checker | Architect |
| P2.10 | Versioning: 0.1.0-alpha.1 hardcode 7 csproj + DataGuardApi.Version='1.0.0' + GetSchemaVersion='1.0' mâu thuẫn; MinVer chưa áp dụng | Architect |
| P2.11 | 3201 warnings (CS1591/CS1998/CS860x) không kế hoạch; TreatWarningsAsErrors=false → CI "Run analyzers" no-op gate | Architect, DevSecOps |
| P2.12 | PhantomIdentifierRule ToDictionary trùng key case-insensitive hoặc t.Name null → crash; ParameterTypeMatchRule/DirectionRule foreach không null-guard Parameters | QA |
| P2.13 | Grants artifacts (SUBMISSION_CHECKLIST, grant_pitch, written_explanation, ecosystem_impact_matrix) 100% mô tả EcoSupport Native Rust; narrative dbt không xuất hiện; demo_scan.sh chạy cargo → fail; không sample project; không CHANGELOG/icon | OSS |
| P2.14 | VSCode: publisher placeholder "dataguard", thiếu lockfile/icon/license/repository; trùng vai trò với Roslyn analyzers | OSS, Architect |
| P2.15 | Generator false-positive diện rộng Query*/Execute* (AG-003 High chưa fix); GetSymbolInfo trong transform "syntax-only" phá cam kết ~ms; 3 bản ToSnakeCase lệch nhau | Compiler |
| P2.16 | Fix placebo (comment "validate in CI only"/dialect note/CLOB note) không có logic suppress → diagnostic tái xuất hiện | Compiler |
| P2.17 | Snapshot drift so sánh raw banner (CU/patch khác → warning giả); ExtractMajorMinor dead code | DBA |
| P2.18 | Oracle catalog: UPPER() + cảnh báo zero rows (P1.8 bổ sung) | DBA |

---

## 4. Roadmap nâng cấp (ưu tiên theo phase)

### Phase 0 — Pre-release hardening (BLOCKER, làm trước khi publish bất kỳ package nào)
1. Fix packaging Analyzer: bundle Core.dll (và non-framework deps) vào `analyzers/dotnet/cs` hoặc tách types chung sang assembly không vendor deps. *(B1)*
2. Fix `sp_describe_first_result_set`: ordinal đúng (name=2, is_nullable=3, system_type_name=5, max_length=6, precision=7, scale=8), `@tsql = N'EXEC [schema].[proc]'`, try/catch per-proc (skip 11512/11513). *(B2)*
3. Sửa RepositoryUrl/PackageProjectUrl 7 csproj → `thanhnt-sm/eco_support_net_oracle`; thêm metadata đầy đủ Cli/Analyzers (tags, readme, company); thêm `Microsoft.SourceLink.GitHub` + `ContinuousIntegrationBuild` trong CI/release. *(B3)*
4. Chốt license 1 chiều (khuyến nghị: MIT OSI cho grant) — **cần quyết định của chủ repo**: bỏ/chuyển LICENSE.md PolyForm + clause AI-training ra khỏi package surface; sửa README badge; bỏ PackageLicenseFile ở 3 adapter. *(B4)*
5. Rewrite README.md (+ README.vi.md) thành landing DataGuard với narrative dbt contracts + Microsoft #245. *(B5)*
6. Gate tag release bằng đúng security scans của CI (vuln JSON gate + TruffleHog + CodeQL) — hoặc trigger ci.yml trên tags + release depends-on. *(B6)*
7. Redact ConnectionString trong `config show`; thêm `.dataguard*` vào .gitignore + .dockerignore. *(B7)*
8. Sửa messageFormat: chuyển toàn bộ descriptor về `{0}` (message đã format sẵn từ violation) + test arity bằng CSharpAnalyzerTest. *(B8)*

### Phase 1 — v0.2 core correctness
1. Đồng bộ RuleId ↔ DiagnosticId (tách DG001 engine vs IDE; loại DG001 khỏi violation nội bộ); thêm DG001/DG015/DG016 vào AllDescriptors; nối PhantomIdentifierRule vào GetValidationRules. *(P1.1)*
2. Sửa RuleDependencyGraph: dependency ID = RuleId; GetParallelGroups remove placeholder khỏi remaining; test unit graph. *(P1.2)*
3. Sửa code fix: MaxLength int literal (bản thứ 2); ExpectedSpParameter args hợp lệ (vd "IN") hoặc ctor lenient; chuyển attribute dùng chung sang assembly tham chiếu được (Core/Abstractions) + fix thêm using; hợp nhất 2 provider UseOracle. *(P1.3)*
4. Wire ground-truth end-to-end: đọc ExpectedColumnAttribute → ContractDescriptor (Manual); snapshot lưu schema descriptor (không phải violation); `validate --offline` đọc snapshot; `snapshot diff` exit ≠ 0 khi drift. *(P1.4)*
5. Wire AllArgumentsReader + MySql/Pg SP parsers vào RunValidationAsync; cập nhật providerOption ("sqlserver, oracle, mysql, postgresql"). *(P1.5)*
6. Oracle: keying theo OVERLOAD (+ subprogram_id 12c), bỏ return-value row (argument_name NULL / position 0); UPPER() owner/table_name + warn zero rows. *(P1.6, P1.8)*
7. Rewrite RefCursorDescriber: col_charsetform/col_char_length thay col_char_used; hỗ trợ PROCEDURE OUT SYS_REFCURSOR; test PL/SQL qua Testcontainers trước khi wire. *(P1.7)*
8. ScriptDOM: dispatch theo SqlDataTypeOption (char→length, numeric→precision/scale; scale=0 hợp lệ). *(P1.9)*
9. PostgreSQL: filler = `ordinal_position IS NULL`; key theo specific_name/signature. *(P1.10)*
10. Dialect lists: chỉ giữ token độc quyền thật sự; tokenize thay substring; thêm negative corpus (window functions, MERGE, IDENTITY). *(P1.11)*
11. DG008: per-column char_used làm ground truth, NLS_CHARACTERSET cho bytes/char, IsUnicode annotation thay CLR type name. *(P1.12)*
12. ZeroTrustCredentialProvider: fail-closed production (config knob cho phép config-file source chỉ ở Development); audit downgrade. *(P1.13)*
13. CredentialManager: implement cross-platform (libsecret/keyring/DPAPI-NG) hoặc fail-fast + docs matrix rõ. *(P1.14)*
14. SupplyChainVerifier: fail-closed khi thiếu hash file; allowlist exact PackageID+version từ restore graph; anchor bằng GH attestation digest. *(P1.15)*
15. Release: publish snupkg (match *.snupkg), re-verify cosign trước push. *(P1.16)*
16. PublicApiSurface: implement hoặc xóa stub; đánh dấu experimental. *(P1.17)*
17. ADR mới cho scope Core (zero-vendor vs thực tế): chuyển ScriptDom/SqlClient/EFCore/AWSSDK/Roslyn ra khỏi Core hoặc sửa claim. *(P1.18)*
18. QA: assert unexpectedErrors (strict) + align fixture H1_002; test project Analyzers (CSharpAnalyzerTest/CSharpCodeFixTest); unit test 14 rule trống + reference MySql/Pg vào test project; mở rộng corpus (negative, CTE, schema-qualified, NCHAR, SqlServer/MySql/Pg fixtures); Testcontainers integration tests (gate bằng trait, CI service containers). *(P1.19)*

### Phase 2 — post-v0.1 hardening & product
1. packages.lock.json + cache key branch-scoped, bỏ prefix restore-keys cho PR. *(P2.1)*
2. TruffleHog: scan history + fail:true + tier unverified. *(P2.2)*
3. Vuln gate: `pkg.get('id')`, check 'problems', --deprecated, assert framework scanned. *(P2.3)*
4. Docker: pin digest, Trivy/Grype scan, buildx sbom/provenance, docker-smoke trên PR. *(P2.4)*
5. Plugin ALC + allowlist + log hash. *(P2.5)*
6. Audit log ReadLastHashAsync try/catch. *(P2.6)*
7. SECURITY.md DataGuard; xóa docker-compose.yml/HealthCheckServer/.DS_Store; CI SBOM version từ build; CodeQL queries mở rộng. *(P2.7)*
8. Config: round-trip 30+ field, YamlDotNet (đã là dependency), init --provider persist. *(P2.8)*
9. Gộp dialect checker base chung. *(P2.9)*
10. MinVer + một nguồn version + PackageOutputPath đồng nhất. *(P2.10)*
11. Kế hoạch 3201 warnings: baseline count + TreatWarningsAsErrors cho code mới. *(P2.11)*
12. Null-guard + ToDictionary trùng key. *(P2.12)*
13. Rewrite grants/ 4 artifacts quanh DataGuard + narrative dbt; demo .NET thay demo_scan.sh; samples/ project; CHANGELOG + icon. *(P2.13)*
14. VSCode: publisher thật, lockfile, icon/license/repository; chốt vai trò. *(P2.14)*
15. Generator: khớp signature Dapper thật; bỏ GetSymbolInfo khỏi fast path; 1 bản ToSnakeCase chung. *(P2.15)*
16. Suppress marker comment hoặc bỏ fix placebo. *(P2.16)*
17. Drift so sánh major.minor (wire ExtractMajorMinor). *(P2.17)*

---

## 5. Quyết định cần chủ repo (blocker cho Phase 0)

1. **License**: chọn 1 license duy nhất — khuyến nghị **MIT (OSI)** cho track "Claude for Open Source"; PolyForm Noncommercial + clause AI-training phải rời khỏi package surface (có thể giữ riêng cho nội bộ).
2. **Scope Core**: chấp nhận Core có vendor deps (sửa claim + ADR) hay tách ScriptDom/SqlClient/EFCore/AWSSDK/Roslyn ra adapter/lớp riêng (công lớn hơn, giữ được claim grant).
3. **Định vị grant**: xác nhận narrative chính thức = "DataGuard — port dbt model contracts vào .NET stored-procedure gap, Microsoft refused (#245)" (thay EcoSupport Rust).
4. **Docker/CI scope**: có cần image scan + digest pin ngay hay để Phase 2.

## 6. Blockers ngoài (không làm được local)

- Secret `NUGET_USER` (Trusted Publishing, hạn chót 01/11/2026) + tag `v0.1.0` → publish NuGet end-to-end.
- CI run thật trên GitHub (docker smoke, CodeQL, Testcontainers cần runner có Docker).
- Testcontainers integration tests cần Docker daemon (local không có — kế hoạch: GitHub Actions service containers).
