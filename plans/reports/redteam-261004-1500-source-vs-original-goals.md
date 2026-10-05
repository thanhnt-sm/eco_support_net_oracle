# Red-team toàn diện: source DataGuard hiện tại so với mục tiêu gốc

- Ngày: 2026-10-04 · HEAD `046f91d` · Nhánh `main`
- Mục tiêu gốc: `research/muc_tieu/1.md` → `5.md`
- Phương pháp: 6 agent chuyên trách chạy song song (scout source, red-team kiến trúc, review giải thuật, operational surface, chất lượng test, traceability mục tiêu), lead kiểm chứng lại độc lập mọi phát hiện mức Critical/High trên code. Mục đánh dấu **[V]** là lead đã tự đọc code xác nhận.
- Giới hạn: sandbox không có .NET SDK, nên không build/test được. Mọi kết luận hành vi là suy luận từ code tĩnh, có file:line. Những gì chưa chạy thật được ghi rõ **(suy luận)**.

---

## 1. Kết luận điều hành

**Hạ tầng đã vượt mục tiêu gốc, nhưng giá trị cốt lõi của MVP chưa được nối dây.**

Mục tiêu gốc (1.md, câu đầu tiên) là: *"phát hiện khi mapping entity ↔ stored procedure bị lệch khỏi thực tế, ngay tại CI, trước khi merge"*: tham số bị thêm/bớt, cột đổi tên, kiểu lệch. Source hiện tại đọc ground truth stored procedure từ cả 4 vendor (ALL_ARGUMENTS, sys.parameters, pg_proc, information_schema.routines) nhưng **không có rule nào tiêu thụ `StoredProcedureDescriptor`**. Ba rule hứa làm việc này đều chết trên đường production:

| Rule | Lý do chết | Bằng chứng [V] |
|---|---|---|
| DG002 ParameterType | `continue` khi `ClrType` rỗng; chỉ `ManualContractSource` gán ClrType | `ContractRules.cs:150-160`, `ManualContractSource.cs:94,121` |
| DG003 ParameterDirection | `CallSiteDirection` không được gán ở bất kỳ đâu trong `src/` | `ContractRules.cs:240`, grep = 0 |
| DG101 ParameterCount | Chỉ báo khi SQL bắt đầu `EXEC` và không có token `@` | `ContractRules.cs:69-90` |

Extractor Roslyn luôn sinh tham số `DataType:"unknown"` không có ClrType (`ProjectCSharpSqlSource.cs:1706-1714`). `RefCursorDescriber` (DBMS_SQL.DESCRIBE_COLUMNS3) viết xong nhưng 0 caller. README dòng 11 vẫn quảng cáo "parameter checks".

Trong khi đó, ~50% LOC trong `src/` (khoảng 19.6k trên 39.4k) nằm ngoài mục tiêu gốc: hai IDE extension, LSP, Observability, Host, Assessment/OSV, Telemetry HTTP, Plugins, AutoDetection, Health, hai adapter MySQL/PG. Tài liệu 1.md phản đối rõ việc làm VS extension làm kênh chính.

Thêm hai vấn đề chặn người dùng thật ngay ngày đầu:

1. **[V] `dataguard validate` cho Oracle và PostgreSQL luôn exit 3.** `ProviderRuleCatalog` đăng ký `ProviderOptionMismatchRule` (Oracle, dòng 54) và `PostgreSqlProviderOptionMismatchRule` (PG, dòng 77) với `RuleAvailability.Unavailable`; `Program.cs:353-366` thấy bất kỳ rule Unavailable nào là `ExitCode = 3; return;` trước khi `--skip-rules` được áp. Hai provider mục tiêu chính không thể làm cổng CI.
2. **[V] DG015 báo mọi bảng SQL Server là phantom (Error).** SQL Server lưu key bảng là `"dbo.Orders"` (`SqlServerParsers.cs:109`), còn `PhantomIdentifierRule` strip schema khỏi tham chiếu rồi tra `"ORDERS"` (`PhantomIdentifierRule.cs:71-98`). Rule này đăng ký cho mọi provider (`ProviderRuleCatalog.cs:100`). CI đỏ trên dự án sạch.

Điểm mạnh thật sự: supply chain (cosign keyless, SLSA provenance, SBOM, Scorecard, 100% action pin SHA), IDE-safe mode, plugin admission, OSV client, Oracle `char_used` per-column, baseline atomic write. Đây là lớp vỏ tốt. Vấn đề là lõi.

---

## 2. Đối chiếu từng mục tiêu gốc

Ký hiệu: ✅ đạt · 🟡 một phần · ❌ thiếu · 🔀 lệch có chủ ý

### 2.1 Module chính MVP (1.md, 5.md)

| Mục tiêu | Trạng thái | Ghi chú |
|---|---|---|
| Trích xuất call-site Roslyn (Dapper, FromSqlRaw, CommandType.StoredProcedure) | 🟡 | Có, 1720 LOC, nhưng FN/FP lớn (xem §3.3) |
| EfModelSource từ IModel / ModelSnapshot.cs | ✅ | `EfModelSource.cs`, `ModelSnapshotCSharpParser.cs` |
| ALL_ARGUMENTS reader | 🟡 | Có, nhưng hỏng với package (§3.3) |
| sp_describe_first_result_set | 🟡 | Chỉ dùng cho raw SQL, không cho SP |
| DBMS_SQL.DESCRIBE_COLUMNS cho ref cursor | ❌ | Code có, 0 caller [V]. Oracle SP luôn `ResultColumns=[]`, `ReturnsRefCursor=false` (`Program.cs:2249-2250`) |
| 6 rule: ParamType / ParamCount / ParamDirection / ColumnShape / Nullable / Naming | ❌ 3 chết, 🟡 3 lỗi | ParamType/Direction/Count chết; ColumnShape chỉ raw SQL; Nullable đảo ngược (§3.2); Naming bỏ qua config `_convention` |
| Diff call-site ↔ SP ground truth | ❌ | Khoảng trống trung tâm |
| Output Roslyn / SARIF / Markdown-HTML | 🟡 | Có text/sarif/evidence/contracts/yaml/typescript; không Markdown/HTML; SARIF phần lớn thiếu location |
| SQL Server trước qua ScriptDOM tĩnh | 🟡 | `TSql160Parser` có nhưng chỉ test gọi; CLI SQL Server cần kết nối live |

### 2.2 Module mở rộng Oracle (5.md)

| Mục tiêu | Trạng thái | Ghi chú |
|---|---|---|
| 5 rule dialect Oracle | ✅ | `OracleDialectChecker.cs:296-410`, regex có sanitize. DG012 bị Unavailable |
| H3: GROUP_CONCAT, LIMIT, ngoặc vuông PIVOT | 🟡 | Thiếu rule `[x]` và case pivot |
| Parser PL/SQL | 🔀 | Dùng catalog thay parser, đúng như 1.md khuyến nghị |
| NLS_LENGTH_SEMANTICS | 🟡 | Có ở Full mode; Snapshot mode hard-code `"CHAR"` (`Program.cs:2211`) |
| `char_used` per-column | ✅ | Tốt hơn pseudo-code gốc |
| Bytes/char AL32UTF8 | 🔀 sai | Hard-code 4. C# MaxLength đếm UTF-16 unit, mỗi unit tối đa 3 byte trong AL32UTF8. NlsSessionReader đọc charset từ sai view và không ai gọi |
| EfCoreInferenceSimulator (#33218) | 🟡 | Dead code, chỉ test gọi. DG009 hard-code chuỗi và chỉ bắn khi cột CLOB |
| DG007 length > column | ✅ | Nhưng tra bảng hỏng khi EF có schema (§3.3) |
| Test riêng dữ liệu tiếng Việt | 🟡 | 1 case golden, không có chuỗi tiếng Việt thật trong unit test |

### 2.3 Quyết định kiến trúc sau 2 vòng red-team (2.md, 3.md)

| Mục tiêu | Trạng thái | Ghi chú |
|---|---|---|
| IDE syntax-only, không semantic, không DB | 🔀 vi phạm | Generator gọi `SemanticModel.GetSymbolInfo` (`Analyzers.cs:413`); `ContractValidationAnalyzer : DiagnosticAnalyzer` dùng `RegisterOperationAction` (`:517,551`) cùng gói NuGet, bật mặc định [V]. Không có file IO/DB (tốt). Comment "with database connection" là sai |
| Diff nặng trong CLI riêng | ✅ | `DataGuard.Cli`, PackAsTool |
| 3 mode Full / Snapshot (mặc định) / Manual | 🟡 | Có cả 3. Nhưng `--offline` = Manual + bắt buộc `--assembly`, không phải "so với snapshot" như workflow 4.md. `validate` không tự đọc `.dataguard-snapshot.json` (SnapshotFilePath mặc định null) dù `snapshot refresh/show/diff` mặc định có [V] |
| Snapshot gắn version DB, cảnh báo lệch | ❌ | Snapshot có `DatabaseVersion`, nhưng cảnh báo là dead code: `Program.cs:1238-1239` tính `currentVersion`/`snapshotMajorMinor` rồi không dùng [V]. `validate` không kiểm version/provider/hash/tuổi khi nạp snapshot (`:2198-2213`) |
| Snapshot chứa shape SP | ❌ | Snapshot chỉ có bảng/cột (`SnapshotTable/SnapshotColumn`); không SP, tham số, ref cursor, NLS. Mode mặc định không thể kiểm SP offline |
| `dataguard baseline` + file baseline | ✅ | Có, atomic write, in số finding bị che |
| Fingerprint baseline | 🔀 yếu | `$"{RuleId}:{Message}"` (`BaselineManager.cs:361-366`) [V]. Message hằng (DG017, DG101) => baseline 1 lần che tất cả về sau; DG004 `Take(5)` => cột drift thứ 6 bị ẩn |
| SARIF 2.1 | 🟡 | Chỉ 4/13 `CreateViolation` trong ContractRules truyền location; LengthMismatch truyền null => không annotation PR; không `partialFingerprints`; version cứng `0.1.0-alpha.1`, InformationUri sai |
| Testcontainers.Oracle trong CI | ❌ thực tế | `gvenzl/oracle-free:23` có, nhưng gate `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1` không workflow nào đặt; 5 test integration `return;` thay vì skip => CI báo Passed cho test không chạy gì |
| Key overload theo package.procedure(signature) | 🔀 lỗi | Id = `oracle:{owner}.{procName}:{Sequence}:{Overload}`, **thiếu package trong Id** [V] => 2 package cùng tên proc va nhau. `GetProcedureNamesAsync` lọc `all_procedures.package_name` (`OracleReaders.cs:147`): cột này không có trong ALL_PROCEDURES (suy luận: ORA-00904). PG dùng `oid` (không ổn định giữa DB) |
| Normalize tên cấu hình được | 🟡 | So cả snake/Pascal không phân biệt hoa thường, nhưng `config.NamingConvention` bị bỏ qua |
| Core zero vendor deps, OSI license; Oracle driver chỉ trong adapter | 🔀 vi phạm một nửa | Core csproj có `Microsoft.Data.SqlClient`, `ScriptDom`, `AWSSDK.SecretsManager`, EF Core, Roslyn Workspaces [V]. `DataGuard.SqlServer.Adapter` là vỏ rỗng 0 file `.cs` nhưng vẫn publish [V]. Oracle driver đúng chỗ ✅. 4 adapter lặp danh sách package của Core kể cả SqlClient trong MySql/PG/Oracle |
| License MIT/Apache | 🔀 có lý do | GPL-3.0-only + Commercial từ v0.4.0 (plan `260930-1735`). GPL vẫn OSI nên tư cách Anthropic còn. Mất "Core MIT dễ được duyệt ở enterprise chặn GPL". Contracts giữ MIT ✅. Văn bản §7 chưa qua luật sư (plan tự ghi) |
| `[SkipContractCheck]` | 🟡 | Chỉ analyzer tôn trọng; Core và CLI không đọc (grep = 0) [V] => cổng CI bỏ qua escape hatch |
| Golden corpus H1/H2/H3/length, assert "không hơn không kém" | 🟡 | 8 case, 100% Oracle (H1/H2 không khai provider => default Oracle). 0 case negative. Chỉ Error bị kiểm "không dư", Warning dư không fail. JSON hỏng => `Console.WriteLine` rồi bỏ qua; thư mục thiếu => `yield break` => corpus teo mà CI xanh |
| Corpus từ hallucination LLM thật | ❌ | 0/8 case có provenance; không tool thu thập |
| Secret: env/vault, không log, không argv | 🟡 | Có `ZeroTrustCredentialProvider`/`CredentialManager`/secret stores, nhưng **CLI không dùng chúng**; credential qua `--connection` argv, env, YAML plaintext; `EncryptConnectionStringAtRest` mặc định false |
| Ký package + SBOM | ✅ khác hình thức | cosign keyless + SLSA + SBOM. Không `dotnet nuget sign` => client NuGet không tự verify |
| Phân phối NuGet analyzer, không VS extension làm kênh chính | 🔀 | Analyzer NuGet ✅. Nhưng VS ext 4846 LOC + VSCode 5478 LOC đã được xây, dù 1.md phản đối |
| Workflow init → baseline → IDE → `check --offline <1s` → CI SARIF → snapshot refresh | 🟡 | Lệnh là `validate`; hook chạy `validate` không `--offline`; <1s không đo; dựng `CSharpCompilation` toàn project không cache |

### 2.4 Mục tiêu gốc nào đã lỗi thời

- **`ForAttributeWithMetadataName`** không áp dụng được: call-site SQL không mang attribute. Roslyn khuyến nghị dùng `DiagnosticAnalyzer` với syntax-node action (rẻ) để báo diagnostic, không dùng generator. Mục tiêu nên viết lại thành *"chỉ đăng ký syntax action, model equatable, không IO, không semantic"*.
- **"MIT để đủ điều kiện Anthropic"** quá chặt. Điều kiện thật là OSI. Lý do còn đứng được cho MIT là độ chấp nhận ở enterprise.
- **"Sigstore/NuGet signing"** gộp hai cơ chế khác nhau. Cần chọn rõ author-signing hay chỉ cosign.
- **gvenzl/oracle-xe** đã được thay bằng oracle-free. Đúng hướng.
- **Ưu tiên SP contract đi ngược số liệu của chính 4.md** (H1+H2 = 96% là bảng/cột ma). Code dồn sang raw SQL và phantom detection vô tình khớp dữ liệu. Nhưng 2.md đòi Snapshot là mặc định trong khi shape ref cursor cần EXECUTE (DBA thường từ chối). Mục tiêu gốc chưa đặc tả snapshot phải chứa shape SP do DBA refresh. Đây là chỗ trống của mục tiêu, không phải lỗi thời.

---

## 3. Phát hiện theo mức độ

### 3.1 Critical: chặn giá trị sản phẩm

| # | Phát hiện | Bằng chứng | Tác động |
|---|---|---|---|
| C1 | **SP contract không được validate** (DG002/003/101 chết, SP descriptor write-only, RefCursorDescriber 0 caller) | §1 | Lệch tham số SP thật lọt CI dù test xanh. Golden test tạo descriptor bằng tay nên không bắt được |
| C2 | **[V] validate Oracle/PG luôn exit 3** | `ProviderRuleCatalog.cs:54,77`; `Program.cs:353-366` | Hai provider mục tiêu không dùng được làm cổng CI; team sẽ thêm `\|\| true` |
| C3 | **[V] DG015 báo mọi bảng SQL Server là phantom** | `SqlServerParsers.cs:109` vs `PhantomIdentifierRule.cs:71-98` | CI đỏ trên dự án sạch |
| C4 | **"PASS rỗng" không có cổng chặn** | `Program.cs:368` chỉ chặn khi `contracts.Count == 0`; `--provider` gõ sai => core rules only => exit 0; `--config` thiếu file => default im lặng (`:1914-1917`); snapshot không được đọc nếu không set `SnapshotFilePath` | Người dùng tưởng được bảo vệ |
| C5 | **[V] DG005 đảo ngược và gộp cột** | `ContractRules.cs:1536-1572`: dựa annotation `"Required"` không source nào sinh ra (grep = 0), bỏ qua `PropertyDescriptor.IsNullable`; dictionary nullability gộp mọi bảng theo tên cột | Mọi cột NOT NULL (kể cả PK `Id`) bị báo "property nullable"; chiều nguy hiểm (cột nullable → property non-nullable) không bao giờ báo |
| C6 | **[V] Oracle package catalog hỏng** | `OracleReaders.cs:147` (`all_procedures.package_name`), Id thiếu package (`Program.cs:2244`), filter package rỗng khớp mọi package, dùng nhầm `SEQUENCE` làm overload id (`:63`), mất overload 0 tham số (`:227,:274`) | Pattern Oracle chủ đạo (proc trong package) không được catalog, hoặc ORA-00904 (suy luận) |

### 3.2 High: sai kết quả hoặc che lỗi

| # | Phát hiện | Bằng chứng |
|---|---|---|
| H1 | **[V] Oracle/PG live schema bịa cột khi DB lỗi**: catch-all rồi `ExtractSyntacticColumns` sinh cột `VARCHAR2`/`text` giả | `OracleLiveQuerySchemaProvider.cs:136-170`; `PostgreSqlLiveQuerySchemaProvider.cs:147-175` |
| H2 | **Ngữ nghĩa lỗi DB không nhất quán**: SQL Server => DG020 Warning exit 0; Oracle/PG => cột bịa; MySQL => không có live provider, bỏ qua im lặng | `LiveSqlShapeValidationRule.cs:146-174`; `ProviderRuleCatalog.cs:106-112` |
| H3 | **[V] Baseline fingerprint `RuleId:Message`** che regression mới; DG004 `Take(5)`; message có số đếm không ổn định | `BaselineManager.cs:361-366`; `ContractRules.cs:328,365,1685,86` |
| H4 | **[V] Snapshot không integrity/version/staleness khi validate**; SHA-256 không khoá, sửa được trong cùng PR; `LengthSemantics` cứng `"CHAR"` | `Program.cs:2198-2213`; chỉ `snapshot diff` kiểm (`:1191-1225`) |
| H5 | **[V] FP tự gây trên Oracle SP**: extractor tổng hợp `"EXEC {proc}"` cho `CommandType.StoredProcedure`, rồi DG013 regex `\bEXEC\s+\w+\.` không xét `IsStoredProcedure` | `ProjectCSharpSqlSource.cs:279`; `OracleDialectChecker.cs:227,397-400`. Pattern chuẩn `CommandText="PKG.PROC"` luôn bị báo "SQL Server EXEC syntax" |
| H6 | **Type map chỉ nhị phân SQL Server/Oracle**, `isOracle` suy từ có chuỗi "NUMBER"; PG/MySQL mượn bảng SQL Server => `bool/boolean`, `Guid/uuid`, `int/integer` báo sai DG018; Oracle `bool` chỉ khớp `"NUMBER(1)"` nhưng ALL_ARGUMENTS trả `NUMBER`; thiếu nullable/enum/`System.Int32`/DateOnly | `ContractRules.cs:110-152,185-205`; `LiveSqlShapeValidationRule.cs:224-226` |
| H7 | **Mọi rule độ dài Oracle im lặng khi EF có schema**: tra bảng `t.Name == entity.TableName` nhưng EfModelSource tạo `"SCHEMA.TABLE"` | `LengthMismatch.cs:387-388`; `EfModelSource.cs:110,707` |
| H8 | **Length semantics sai ở 3 dialect**: Oracle hệ số 4 (đúng là 3 cho UTF-16 unit), bỏ charset/`IsUnicode(false)`, cột CHAR không kiểm trần 4000 byte, `ToOracleColumnName("CustomerID")` => `CUSTOMER_I_D`; MySQL TEXT limit coi là ký tự (thực byte), `utf8mb3 = 4`, MY007 tiền đề sai (Pomelo map `longtext`); PG003 báo đôi | `LengthMismatch.cs:220,267-272`; `MySqlDialectChecker.cs:412`; `MySqlLengthMismatchDetector.cs:96-102`; `PostgreSqlLengthMismatchDetector.cs:169-188,214-244` |
| H9 | **Extractor C# FN/FP lớn**: thiếu `QueryFirst*`/`SqlQueryRaw`/`ExecuteSql`; bỏ `?.`, target-typed `new`, wrapper, `+=`, StringBuilder; nối chuỗi có biến => null (chính chỗ injection bị bỏ sót); `IsSqlString` coi mọi chuỗi chứa `update/with/begin` là SQL (CQRS Command => bảng `your`); `base("DefaultConnection")` => `SELECT * FROM DefaultConnection` => DG015; `ExpectedProperties` gồm private/get-only/primitive (`Query<string>` => DG004 thiếu "Length"); references lấy từ AppDomain của DataGuard thay vì NuGet của project => semantic không tất định | `ProjectCSharpSqlSource.cs:23-38,122-132,279,571-597,1036-1061,1250-1340` |
| H10 | **Phantom rule FP hàng loạt ngoài C3**: `DUAL`, `EXTRACT(YEAR FROM x)`, `TRIM(... FROM)`, `IS DISTINCT FROM`, cross-db, multi-CTE/RECURSIVE, TVF/LATERAL/synonym/matview/temp, comment/chuỗi không mask, `COALESCE(a,b) AS x` => DG016 `X`, alias không `AS`, cột JOIN không qualifier, alias dùng lại trong subquery | `PhantomIdentifierRule.cs:26-28,69,88-202` |
| H11 | **DG016 trùng ID** giữa `PhantomIdentifierRule` (phantom column) và `RawSqlParseStatusRule` (parse error) => `--skip-rules DG016` sai. Rule ID rải 4 nơi | `PhantomIdentifierRule.cs:144,194`; `ContractRules.cs:1642` |
| H12 | **5 integration test false-green trong CI** (`return;` thay vì skip, env gate không ai đặt) | `OracleIntegrationTests.cs:49`, MySql 49, PG 50, SqlServer 56, SqlServerParser 59 |
| H13 | **Nightly và build_release phát hành artifact không qua test C#**; nightly còn cosign+attest dù CI có thể đỏ; `build_release.yml` không `assert-vsix`, không `prepare-lsp`, `TreatWarningsAsErrors=false` | `installers.yml:103-172,348-505`; `build_release.yml` |
| H14 | **`publish-vscode` chạy `npx @vscode/vsce publish` không pin trong job cầm PAT**, không verify checksum/attestation | `marketplace.yml:270-275` |
| H15 | **Release không gate**: tag `v*` không yêu cầu CI xanh/tag trên main; không `environment:`; release `--draft=false` trước NuGet/Docker/attest; NuGet `continue-on-error`; bug `github.ref_name` khi dispatch (`gh release upload main`) | `release.yml:3-7,568-666,673,770-773` |

### 3.3 Medium: chất lượng, bảo trì, drift

- **Hai pipeline validate song song**: CLI dùng `ProviderRuleCatalog` + `ConcurrentValidationEngine`; Public API `ValidationPipeline` dùng `RuleDependencyGraph` (9 rule core, không dialect) + `GraphValidationExecutor`. Plugin MEF chỉ gắn pipeline 2; **CLI không nạp plugin**. Cùng input, kết quả khác.
- **Plugin metadata lỗi**: `RulePluginManager.cs:141` đọc `ExportMetadataAttribute` thay vì `ExportRuleAttribute` => mọi plugin `RuleId=""`, `GroupBy` giữ 1 plugin. Không test nào nạp plugin thành công.
- **`RuleDependencyGraph`**: toposort/cycle đúng; nhưng `RegisterRule` bỏ ID trùng im lặng, `WithDependency` tạo `DummyRule` no-op có thể thay rule thật (`:28-38,317-322`).
- **Bảo mật "trang trí"**: CLI không dùng credential provider; audit chain SHA-256 không khoá, checkpoint cùng thư mục; `CredentialManager.LogAuditAsync` ghi dòng ngoài chuỗi vào cùng `audit.log` => `VerifyIntegrity` luôn false khi cả hai ghi (`IAuditLogger.cs:193-210`; `CredentialManager.cs:392-416`).
- **Manual mode `Assembly.LoadFrom` default ALC**, đường dẫn từ YAML trong repo; IDE-safe chặn, CI không (`ManualContractSource.cs:26`).
- **Nuốt lỗi acquisition**: file `.cs` không đọc được bỏ qua (`ProjectCSharpSqlSource.cs:116-119`); SQL >256KB bỏ qua (`:158`); ModelSnapshot parse một phần vứt diagnostics (`EfModelSource.cs:239-244`); `ParseStatus` mặc định `Parsed` và `RawSqlParser` không ai khởi tạo => DG016 parse chết; YAML key lạ bỏ qua (`Program.cs:2117`).
- **Catalog PG/MySQL/SQL Server**: PG `RETURNS TABLE` ('t') => Input, `pronargdefaults` SELECT nhưng không đọc, overload chỉ `oid`; MySQL bỏ FUNCTION, `schema=''` gộp mọi schema (kể cả `mysql.user`), `CharUsed` chứa charset; SQL Server `max_length` byte vs `CHARACTER_MAXIMUM_LENGTH` ký tự trộn, catch chỉ 11512/11513. Không có lớp Canon(dialect) cho identifier (PG fold lower, Oracle upper, MySQL theo `lower_case_table_names`).
- **`SqlClassifier`** là token scanner: `EXECUTE`/`CALL` bỏ sót, DDL vô hình, không biết comment/chuỗi (LINQ `.Select(` => DGSQL001), SQL nhiều dòng cắt tại `\n` => DG017 miss. LSP chạy nó trên toàn file C#; `Position` O(n·d).
- **DG001 (suy luận)**: nhiễu với mọi `Execute*`/`Query*`; trượt `FromSqlRaw` vì receiver là `IPropertySymbol` không phải `IMethodSymbol` (`Analyzers.cs:413`). Không test generator nào cho FromSqlRaw.
- **God files**: `Program.cs` 2508 dòng (21 command, logic nghiệp vụ `BuildContractsAsync`/`ValidateContractsAsync`, `ComputeSchemaHash` trùng BaselineManager); `ContractRules.cs` 1746 (`ColumnShapeMatchRule` ~1245 dòng kiêm SQL tokenizer, `Substring(i)` per `$` tại `:698,863`); `ProjectCSharpSqlSource.cs` 1720 (37 Regex, method 556 dòng).
- **Dead/orphan**: `SupplyChainVerifier`, `UpgradePlanner`, `AutoDetectionEngine` (651 LOC) không caller trong src; `DummyRule`/`CustomNamingConventionRule` trong production; `tests/DataGuard.BinaryCompatibilityFixture` không sln/CI; `benchmarks/DataGuard.Benchmarks` trùng `tools/benchmarks/`; `tests/git-tools` không CI; `Observability*` không src nào ref; `IDialectAnalyzer` 3 impl không ai gọi; `scripts/sign-vsix.ps1` không workflow nào gọi.
- **Test**: không dùng `Microsoft.CodeAnalysis.Testing`; DG012 Oracle và DG099 zero test; DG005/DG006 chỉ positive; `My003_LengthExceeds_Flags` assert MY004; `TimedOperation_Dispose_RecordsHistogram` không assert; mutation (đọc code): DG008 hệ số 4 và boundary `==` không bị bắt, DG101 thiếu `EXECUTE`/lowercase, phantom thiếu schema-qualified.
- **Governance/docs drift**: README rule table thiếu DG017/018/020, MY004-007, PG004-005, DG098/099, DG11xx-14xx và mô tả sai DG016; `.github/copilot-instructions.md` nói MIT; CLAUDE.md/governance nói chỉ 2 test project (thực 7); `.omo/run-continuation` tracked dù gitignore; `anti_garbage_guard` allowlist `claude`; `dg-git` mặc định `sync` trái `rules/git_workflow.md`; `commit-msg` regex lọt `chore(sync):`; `core.hooksPath` trống; `marketplace.yml` cấp `id-token`/`attestations: write` top-level cho mọi job; dependabot thiếu npm/docker; không `global.json`/`NuGet.config`.

---

## 4. Scope creep so với mục tiêu gốc

| Thành phần | LOC | Mục tiêu nói gì |
|---|---|---|
| DataGuard.VisualStudio (+ tests) | 4846 (+2305) | 1.md phản đối làm kênh chính; chỉ cho phép sau này như lớp squiggle tùy chọn |
| DataGuard.VSCode (TypeScript) | 5478 | Như trên; TS tests không chạy trong `ci.yml` |
| Core/Assessment (OSV qua mạng) | 2131 | Không có; mở rộng bề mặt tấn công mà Chuyên gia 3 cảnh báo |
| Core/Telemetry (HTTP export) | 1448 | Không có |
| PostgreSql.Adapter / MySql.Adapter | 1542 / 1196 | 3.md chỉ nói "sau này" |
| Core/Plugins | 845 | Không có; CLI không nạp |
| Core/AutoDetection | 651 | Không có; không caller trong src |
| Observability + AspNetCore + Messaging | 921 | Không có; không src nào ref |
| Host, Health, LanguageServer | 191 / 190 / 166 | Không có |

Hai extension gọi CLI qua process với `--ide-safe`, nên **không vi phạm** nguyên tắc "IDE nhẹ". Nhưng chúng ngốn công sức trong khi lõi MVP chưa nối. Không có ADR nào đối chiếu các phần này với mục tiêu gốc.

---

## 5. Mục tiêu gốc đã được vượt

- Schema hash canonical SHA-256 gồm provider/scope/canonicalization version; `snapshot diff` dừng exit 3 khi lệch.
- Oracle `char_used` per-column ưu tiên hơn NLS session; DG009 chỉ bắn cho CLOB/NCLOB, sát #33218 hơn pseudo-code.
- Supply chain: cosign keyless, SLSA provenance, SBOM, Scorecard, CodeQL, TruffleHog, 100% action pin SHA, lockfile, Dockerfile pin digest + non-root.
- IDE-safe mode tước connection/đường ghi khỏi môi trường IDE; handshake với extension.
- Plugin admission: hash-bound load, provenance verifier bắt buộc, chặn symlink/native (17 test).
- Baseline atomic write, giới hạn kích thước, in số finding bị che.
- Raw SQL shape không cần chạy thật: Oracle `WHERE 1=0` + `GetSchemaTable`, SQL Server `sp_describe_first_result_set`.
- Testcontainers cho 4 DB, image `oracle-free:23` (dù chưa chạy trong CI).

---

## 6. Khuyến nghị: giải pháp và giải thuật

Thứ tự theo giá trị trên chi phí. Mỗi mục là một PR độc lập, có test đi kèm.

### Đợt 1: Dừng chảy máu (1 đến 2 tuần)

1. **Bỏ chặn Oracle/PG**: rule `Unavailable` chỉ chặn khi được bật rõ trong config; áp `skipRuleIds` trước khi tính `unavailableOutcomes`. Test: `validate --provider oracle` trên fixture phải không exit 3.
2. **Sửa DG015 SQL Server**: chuẩn hoá key schema ở một chỗ. Đề xuất `DatabaseTableDescriptor` có `Schema` và `Name` tách riêng; `PhantomIdentifierRule` tra `(Canon(schema ?? default), Canon(name))`. Thêm golden case SQL Server với `dbo.Orders`.
3. **Sửa DG005**: dùng `PropertyDescriptor.IsNullable`, khớp theo `(TableName, ColumnName)`. Thêm test cả 2 chiều.
4. **Sửa DG013 tự gây**: không tổng hợp `"EXEC {proc}"` cho Oracle; hoặc DG013 bỏ qua khi `IsStoredProcedure`.
5. **Cổng "PASS rỗng"**: tính `evaluatedPairs = Σ(rule × contract có ground truth)`; bằng 0 hoặc ground truth thiếu => exit 3 kèm lý do. Whitelist `--provider`. `--config` thiếu file => exit 2. YAML strict (key lạ => lỗi).
6. **Chặn false-green test**: thay `return;` bằng `[SkippableFact]`/attribute tự định nghĩa; thêm job CI ubuntu đặt `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1` chạy 4 Testcontainers.
7. **Nightly/build_release phải `needs` test xanh**; mở rộng `check-workflow-policy.py` để bắt workflow có publish/sign mà thiếu `dotnet test`; pin `@vscode/vsce` trong job publish và verify attestation trước publish.

### Đợt 2: Nối lõi MVP (3 đến 5 tuần)

8. **Matching call-site ↔ catalog** (hiện không tồn tại):

```
call := (provider, schema?, package?, name, args[(name?, clrType: ITypeSymbol, refKind, isNamed)])
cands := catalog.Where(Canon(name)==Canon(sp.Name) && schema/package match với default schema/search_path)
for sig in cands:                       // overload resolution giống DB
  if any named arg ∉ sig.params → reject
  bind positional rồi named; missing := sig.params.Where(IsIn && !HasDefault && !bound)
  if missing.Any → reject
  score += Σ compat(Unwrap(arg.clrType), p)   // Unwrap: Nullable<T>, enum→underlying
pick best; none → DG101 với candidate gần nhất
```

   Yêu cầu: extractor gán `ClrType` (từ `ITypeSymbol.ToDisplayString()`) và `CallSiteDirection` (từ `RefKind` hoặc `DbParameter.Direction`); DG002/DG003 đọc `StoredProcedureDescriptor`; type map theo provider qua interface `ITypeCompatibility` keyed bằng `SpecialType`, có precision/scale/length.

9. **Sửa catalog Oracle**: một query duy nhất trên `ALL_ARGUMENTS` với `owner`, `package_name`, `object_name`, `subprogram_id`, `overload`, `position`, `in_out`, `data_type`, `defaulted`, lọc `data_level = 0`, `UPPER()` cả hai phía; group theo `(package_name, object_name, subprogram_id)` kể cả 0 tham số; Id = `oracle:{owner}.{pkg}.{proc}#{subprogram_id}`. Nối `RefCursorDescriber` vào Full mode có cờ opt-in rõ (vì thực thi proc). Đọc charset từ `nls_database_parameters`.
10. **Snapshot v2**: chứa SP (tham số, overload, ref cursor shape), NLS, `Provider`, `DatabaseVersion`; `validate` mặc định đọc `.dataguard-snapshot.json`, kiểm version/provider/hash và tuổi, cảnh báo lệch major.minor (nối lại dead code `Program.cs:1238`). `--offline` nghĩa là "snapshot", Manual là `--manual`. Ký snapshot bằng cosign hoặc ít nhất `CODEOWNERS` cho file này.
11. **Baseline fingerprint**:

```
fp := SHA256(ruleId | canonicalSubject | normalizedLocation)
  canonicalSubject: Properties có cấu trúc (table, column, procedure, entity, property), không phải free text
  normalizedLocation: repo-relative path + DocumentationCommentId của member bao ngoài + hash(normalized SQL)
baseline = multiset{fp → count}; new = fp có count(current) > count(baseline)
không Take(5) trong text được fingerprint; xuất partialFingerprints vào SARIF
```

12. **Phantom detection bằng AST**: ScriptDOM cho T-SQL; cho dialect khác dùng parser thật hoặc tắt DG015/016 khi parse fail. Xây scope per SELECT (base table, cte, derived, tvf, temp, table var); skip cross-db/synonym/temp; resolve cột theo scope chain; không coi output alias là column ref.
13. **Extractor C#**: chuyển sang `IOperation` (`IInvocationOperation.TargetMethod.ContainingType ∈ {Dapper.SqlMapper, RelationalQueryableExtensions, RelationalDatabaseFacadeExtensions, DbCommand}`), compile với references thật từ `project.assets.json`/MSBuildWorkspace; reaching-definitions trên CFG cho `+=`/StringBuilder; interpolation của `FromSqlRaw`/`$""` đánh dấu Dynamic (DG099) thay vì inline; `ExpectedProperties` chỉ public instance có setter, bỏ primitive/string; Id = hash(fullPath, span.Start, normalizedSql).
14. **Length semantics**: `bytesPerUtf16Unit(charset, isUnicode)`: AL32UTF8/utf8mb4/utf8mb3 = 3, AL16UTF16 = 2, single-byte = 1. Oracle: `limitBytes = char_used=='B' ? data_length : min(char_length × maxCharBytes, 4000|32767)`. MySQL TEXT theo byte, thêm row-size và index-prefix. PG: một PG003. Tra bảng bằng `(schema?, name)` canonical.
15. **Một khái niệm `Unevaluated` chung** cho mọi lỗi DB/parse/acquisition => exit 3; cấm fallback cú pháp khi đang ở live mode; `[SkipContractCheck]` được CLI tôn trọng.

### Đợt 3: Kiến trúc và vệ sinh (song song, ưu tiên thấp hơn)

16. Tách SQL Server ra `DataGuard.SqlServer.Adapter` thật; Core bỏ `SqlClient`/`ScriptDom`/`AWSSDK`/EF; adapter không lặp package của Core. Đưa `CharUsed`/`PackageName`/`ReturnsRefCursor` ra khỏi contract chung (dùng `Extensions` dictionary hoặc record riêng per dialect).
17. Một pipeline duy nhất: CLI dùng `ValidationPipeline`; `ProviderRuleCatalog` trở thành registry mà adapter tự đăng ký; rule ID tập trung một enum/source generator; gỡ DG016 trùng.
18. Analyzer: gỡ `ContractValidationAnalyzer` khỏi gói IDE hoặc chuyển thành syntax-node action thuần; generator không giữ `Location` trong model equatable; sửa `FromSqlRaw` receiver.
19. Golden corpus: thêm case negative (`expectedDiagnostics: []`), đa dialect, nối MY/PG rule, assert số case tối thiểu, JSON hỏng => fail, kiểm cả Warning. Dựng tool thu thập hallucination từ nhiều LLM như 4.md đề ra, gắn provenance.
20. Tách `Program.cs` theo command; `ColumnShapeMatchRule` tách SQL tokenizer; gỡ dead code (`AutoDetectionEngine`, `UpgradePlanner`, `SupplyChainVerifier`, `DummyRule`, `benchmarks/` root, `BinaryCompatibilityFixture` hoặc đưa vào CI).
21. CLI dùng `ZeroTrustCredentialProvider`; bỏ `--connection` argv (dùng `--connection-env`); audit log HMAC với khoá ngoài, một writer duy nhất.
22. Viết ADR đối chiếu mục tiêu gốc cho từng thành phần ngoài phạm vi (VS/VSCode/Observability/Assessment/Telemetry/Plugins), quyết định giữ, đóng băng hay tách repo theo manifest `from → keep | extract | rewrite | remove`.
23. Docs: sinh bảng rule README từ `ProviderRuleCatalog.RuleTitles` và test so khớp; sửa `copilot-instructions.md`; cập nhật CLAUDE.md/governance về 7 test project; nâng `verify_docs_sync.sh` thành validation nội dung như plan cleanup yêu cầu.

---

## 7. Chưa kiểm chứng, cần chạy thật

- ORA-00904 với `all_procedures.package_name` (cần Oracle thật hoặc Testcontainers).
- Hành vi DG001 với `FromSqlRaw` (receiver `IPropertySymbol`).
- `LoadWithMemoryMappedFileAsync` với baseline 1 000 001 byte (`accessor.Capacity` làm tròn page).
- Version thực tế trong Docker image (`-p:Version` vs MinVer).
- Hiệu năng analyzer và `validate --offline` so với mục tiêu <1s.
- Plugin bị lọc hết do AssemblyVersion `0.0.0.0` khi MinVer chưa có tag.

## 8. Nguồn chi tiết

Sáu báo cáo agent gốc (scout, kiến trúc, giải thuật, operational, test, traceability) được tóm tắt trong scratchpad phiên này. Mọi file:line trong tài liệu này trỏ vào HEAD `046f91d`.
