# Hướng Dẫn Sử Dụng / Usage Guide / Hướng Dẫn Sử Dụng

## Cài Đặt Nhanh / Quick Start / Cài Đặt Nhanh

### 1. Source/artifact installation / Cài từ source hoặc artifact

Use the [installation guide](05-operations/installation-guide.md) ([VI](05-operations/installation-guide.vi.md)) for a source build or exact inspected package. This source review does not establish NuGet, Marketplace or GitHub Release asset availability. Record the actual SHA/version/hash; do not assume a released 0.3.0 or 1.0.0 package from examples.

Dùng bản source/artifact đã kiểm tra; không suy ra package công khai tồn tại từ ID trong source. RID archives from `release.yml`/`installers.yml` are framework-dependent (.NET 9 runtime); `build_release.yml` publishes self-contained CLI archives. The Visual Studio VSIX bundles a self-contained CLI.

### 2. Release channels / Kênh phân phối

`installers.yml` runs on qualifying main pushes, excluding changes limited to `**/*.md`, `docs/**`, `plans/**`. Manual dispatch defaults `publish_prerelease=true`; explicitly select false for artifact-only preparation. Publication replaces the rolling nightly release/tag and signs blobs with Sigstore bundles and provenance attestations. Those signatures are distinct from a Windows embedded VSIX certificate. Filename nightly versions do not establish the embedded VSIX version or editor acceptance.

Workflow nightly chỉ chạy với trigger/path phù hợp; không phải mọi push. Nightly là URL có thể đổi, nên lưu run/SHA/hash và bản gói được nghiệm thu. Không suy ra listing Marketplace đã được công bố.

A manual `release.yml` run defaults to `dry_run=true`: it still runs hosted build/test/signing and uploads artifacts, but its publication jobs are gated. Cosign keyless signing can contact public signing/transparency services; it is not a local/no-external-write check. Tag pushes and manual `dry_run=false` enter publication gates. See [release guide](operations/release_guide.md) for the distinction from the local helper's dry-run mode, which does not dispatch a workflow.

Release dry run trên GitHub vẫn build/ký/upload artifact; không đồng nghĩa dry run cục bộ của helper. Xem [hướng dẫn release](operations/release_guide.vi.md). Actual package downloads, hosted signatures, installed editors and registry publication must be verified for the selected run; none are established by this source-level documentation review.

### 3. Khởi Tạo Cấu Hình / Initialize Config

```bash
# Wizard tương tác (khuyên dùng / recommended)
dataguard init --wizard

# Hoặc tạo config mặc định
dataguard init
```

---

## Cấu Hình / Configuration / Cấu Hình

### File .dataguard.yml

Hướng dẫn đã kiểm thử: [EN](guides/configuration.md) · [VI](guides/configuration.vi.md), [Oracle tối thiểu](guides/oracle-snapshot.minimal.yml) và [đầy đủ có chú thích](guides/oracle-snapshot.full.yml). Oracle/SqlServer là mapping, không phải boolean; provider chọn bởi --provider → DefaultProvider → sqlserver. AutoDetectProvider không ghi đè provider CLI. config validate kiểm tra kiểu/provider, không xác thực nội dung database. init và wizard không ghi đè file đã có để giữ comment thủ công.


```yaml
# DataGuard Configuration
GroundTruthMode: Snapshot          # Snapshot | Full | Manual
EnableSmartDefaults: true          # Library defaults; không ghi đè DefaultProvider của CLI
EnableBaseline: true               # Bật baseline cho legacy
NamingConvention: SnakeCaseToPascalCase  # SnakeCaseToPascalCase | PascalCaseToSnakeCase | ExactMatch
DefaultProvider: sqlserver         # sqlserver | oracle | mysql | postgresql (postgres); giá trị khác => exit 2
SnapshotFilePath: .dataguard-snapshot.json
FailOnUnavailableRules: false      # true = exit 3 khi rule không khả dụng (như --fail-on-unavailable)
StrictConfig: false                # true = key top-level không biết => exit 2 (mặc định chỉ cảnh báo)
StrictProcedureContracts: false    # true = DG101/DG002/DG003 từ catalog stored procedure là Error (exit 1) thay vì Warning
DefaultSchema: dbo                 # schema cho lời gọi stored procedure không qualify (cũng là scope snapshot)
DefaultPackage: ""                 # Oracle: package cho lời gọi không qualify
BaselineFilePath: .dataguard-baseline.json

# Rule plugins (chỉ dùng với validate --plugins-dir)
Plugins:
  AllowUnsignedLocal: false        # true = nạp plugin không có signed provenance (vẫn kiểm manifest, SHA-256, dependency, rule ID)
EnableTelemetry: false             # Opt-in telemetry

# Oracle-specific (nếu dùng Oracle)
Oracle:
  Owner: MY_SCHEMA                 # Schema owner
  UseRefCursorDescribe: false      # Không còn dùng cho catalog; xem DescribeRefCursors
  DescribeRefCursors: false        # true = THỰC THI procedure trả REF CURSOR (IN = NULL) để đọc cột kết quả; procedure vẫn có thể có side effect; cần xác minh procedure và quyền trước khi bật
  UseAllArguments: true            # Dùng ALL_ARGUMENTS
  UseAllTabColumns: true           # Dùng ALL_TAB_COLUMNS

# SQL Server-specific
SqlServer:
  Schema: dbo                      # Schema mặc định
  UseFirstResultSet: true          # Dùng sp_describe_first_result_set

# Bảo mật / Security
EnableCredentialRotationDetection: true
CredentialRotationWarningDays: 30
EncryptConnectionStringAtRest: true   # mặc định: DPAPI / Keychain / Secret Service; không có backend => plaintext + cảnh báo
RequireEncryptedCredentialStore: false # true = từ chối lưu khi không có backend mã hoá
KeyVaultUri: ""                    # Azure Key Vault URI (nếu có)
AwsRegion: ""                      # AWS Secrets Manager (CLI đăng ký store)
VaultAddress: ""                   # HashiCorp Vault (https, VAULT_TOKEN)
AllowPlaintextConfigFallback: false # true = cho phép dùng ConnectionString plaintext trong file này (chỉ dev)
EnableAuditLogging: true
AuditLogPath: ""                   # Tùy chọn custom path
AuditKeyFile: ""                   # File chứa khoá HMAC cho audit chain (hoặc env DATAGUARD_AUDIT_KEY); đặt ngoài repo
```

---

## Các Lệnh CLI / CLI Commands / Các Lệnh CLI

### 1. `dataguard validate` - Xác Thực Hợp Đồng

```bash
# Cơ bản: connection lấy từ biến môi trường do bạn đặt tên (không lộ trên process listing)
export ORACLE_CI="User Id=...;Password=...;Data Source=..."
dataguard validate --connection-env ORACLE_CI

# --connection vẫn chạy nhưng in cảnh báo: chuỗi trên dòng lệnh hiện trong process listing
dataguard validate --connection "Server=...;Database=...;"

# Chế độ offline (không DB): dùng snapshot đã commit (.dataguard-snapshot.json)
dataguard validate --offline --format text

# Offline Manual mode: đọc attribute [ExpectedColumn]/[ExpectedSpParameter] từ assembly
dataguard validate --offline --assembly bin/Release/net9.0/MyApp.dll

# Chỉ lint cú pháp SQL inline, không có ground truth (nếu không có cờ này => exit 3)
dataguard validate --project src/MyApp --allow-syntactic-only

# Output SARIF cho GitHub Code Scanning
dataguard validate --connection "..." --format sarif --output results.sarif

# Chỉ định provider
dataguard validate --connection "..." --provider oracle

# Verbose logging
dataguard validate --connection "..." --verbose
```

**Options / Tùy Chọn**:
| Option | Mô Tả | Mặc Định |
|--------|-------|----------|
| `--connection` | Connection string (deprecated: in `warning: a connection string on the command line is visible to process listings; prefer --connection-env`) | - |
| `--connection-env` | **Tên** biến môi trường chứa connection string; biến chưa đặt/rỗng => exit `2`. Dưới `--ide-safe` chỉ được dùng cùng `--allow-env-connection` | - |
| `--allow-assembly-from-config` | Cho phép Manual mode đọc `ManualAssemblyPath` trong file config (thiếu cờ => exit `2`); `--offline --assembly` không cần cờ; luôn bị từ chối dưới `--ide-safe`. Có ở `validate` và `baseline` | `false` |
| `--config` | Đường dẫn .dataguard.yml; file không tồn tại => exit `2` (không tự dùng mặc định) | - (mặc định cấu hình built-in) |
| `--output` | File output SARIF/JSON | Stdout |
| `--format` | `text` \| `sarif` \| `evidence` \| `contracts` \| `yaml` \| `typescript` | `text` |
| `--offline` | Không DB. Không có `--assembly` => Snapshot mode (dùng snapshot đã commit); có `--assembly` => Manual mode | `false` |
| `--assembly` | Assembly cho Manual mode (cùng `--offline`) | - |
| `--verbose` | Log chi tiết | `false` |
| `--provider` | `sqlserver` \| `oracle` \| `mysql` \| `postgresql` (`postgres`), không phân biệt hoa thường; giá trị khác => exit `2` kèm danh sách hợp lệ | Từ config, rồi `sqlserver` |
| `--schema` | Schema/owner name | Từ config |
| `--project` | `.csproj` / `.sln` / thư mục để trích SQL inline và model C# | - |
| `--skip-rules` | Danh sách rule ID bỏ qua, phân cách dấu phẩy (`DG002,DG017`); áp dụng trước kiểm tra rule không khả dụng | - |
| `--fail-on-unavailable` | Exit `3` khi có rule không khả dụng cho provider (config `FailOnUnavailableRules`) | `false` |
| `--allow-syntactic-only` | Cho phép chạy khi không có ground truth: cảnh báo thay vì exit `3` | `false` |
| `--allow-unevaluated` | Vẫn in contract chưa đánh giá (`UNEVALUATED:`) và chẩn đoán acquisition (`ACQUISITION:`), nhưng exit theo violation thay vì `3`; `--ide-safe` ngầm bật | `false` |
| `--progress` | Ghi JSON progress event từng dòng ra stderr (IDE dùng) | `false` |
| `--ide-safe` | Chế độ IDE-safe cho repo chưa tin cậy (xem dưới) | `false` |
| `--allow-env-connection` | Chỉ với `--ide-safe`: giữ lại `DATAGUARD_CONNECTION_STRING` do host cung cấp (chỉ `validate`) | `false` |
| `--plugins-dir` | Thư mục rule plugin (`*.dll` + manifest `<dll>.dataguard-plugin.json`). Plugin qua `PluginAdmission` (manifest, SHA-256, dependency closure, rule ID manifest = `[ExportRule]` = runtime) rồi chạy cùng provider rules trong cùng pipeline. Mặc định bắt buộc signed provenance và CLI không có verifier, nên plugin bị từ chối (`Plugin X.dll not loaded: ...`, như rule không khả dụng) trừ khi config `Plugins.AllowUnsignedLocal: true`. Thư mục không tồn tại => exit `2`; bị từ chối với `--ide-safe` (exit `2`). Ví dụ: `samples/plugins/DataGuard.Samples.NamingPlugin` | - |

**Một pipeline / One pipeline**: `validate`, `baseline`, `snapshot refresh` và `snapshot diff --legacy-violation-diff` chạy cùng danh sách rule (`ProviderRuleCatalog`, cấu hình bởi `StrictProcedureContracts`/`DefaultSchema`/`DefaultPackage`) qua cùng executor với API `ValidationPipeline`. Contract chưa đánh giá luôn được liệt kê (`UNEVALUATED:`), không bao giờ ghi vào baseline/snapshot như finding; `snapshot diff --legacy-violation-diff` thoát `3` khi có contract chưa đánh giá.

**Thứ tự resolve connection** (mọi lệnh dùng database: `validate`, `baseline`, `snapshot refresh/diff`, `oracle-check`, `verify-shape`, `preflight`):
1. `--connection <value>` (kèm cảnh báo deprecated)
2. `--connection-env NAME`
3. `DATAGUARD_CONNECTION_STRING`
4. Credential provider: Azure Key Vault (`KeyVaultUri`) → AWS Secrets Manager (`AwsRegion`) → HashiCorp Vault (`VaultAddress` khớp origin HTTPS tin cậy trong `VAULT_ADDR` + `VAULT_TOKEN`) → biến `DATAGUARD_DATABASECONNECTION` → file credential đã mã hoá (`ApplicationData/DataGuard/credentials.json`). Store lỗi => một dòng `warning: Secret store <Name> lookup ... failed` (không bao giờ in secret) rồi thử nguồn kế tiếp. Bỏ qua hoàn toàn với `--offline`/`--ide-safe`.
5. `ConnectionString` trong `.dataguard.yml` **chỉ khi** `AllowPlaintextConfigFallback: true`; mặc định key này bị bỏ qua kèm `warning: ignoring the plaintext ConnectionString key in the configuration file ...`.

Khi credential provider được dùng, mọi truy cập được ghi vào audit log (`AuditLogPath`) qua một writer duy nhất; đặt `DATAGUARD_AUDIT_KEY` (hoặc `AuditKeyFile`, ≥ 16 byte) để chuỗi được khoá bằng HMAC-SHA256 — xem `SECURITY.md`.

**`--ide-safe` (IDE hosts / untrusted repositories — chỉ `validate` và `assess`)**:
`.dataguard.yml` nằm trong repo nên attacker kiểm soát được nó. Với `--ide-safe`, CLI **bỏ qua** mọi thứ trong config/env có thể nạp code hoặc mở kết nối: `GroundTruthMode` bị ép về `Snapshot`, `ManualAssemblyPath`, `ConnectionString` (kể cả `DATAGUARD_CONNECTION_STRING`, trừ khi có `--allow-env-connection`), `KeyVaultUri`/`AwsRegion`/`VaultAddress`, `AuditLogPath`, `EnableTelemetry`/`TelemetryFileDirectory` đều bị xoá và liệt kê trong một dòng stderr `ide-safe: suppressed ...`. Các option `--connection`, `--offline`, `--assembly`, `--allow-assembly-from-config`, `--ef-snapshot`, `--ef-project`, `--ef-context`, `--connection-env` khi thiếu `--allow-env-connection` (validate) và `--allow-network`, `--remote-advisories` (assess) bị từ chối với exit code `2`. Extension Visual Studio và VS Code luôn truyền cờ này cho `validate`/`assess`. Dưới `--ide-safe`, `--plugins-dir` cũng bị từ chối (exit 2).

- **Handshake**: dòng stderr **đầu tiên** luôn là `ide-safe: active` (in trước cả dòng từ chối option). Host chỉ chấp nhận kết quả khi thấy dòng này; CLI cũ (≤ 0.2.2) không biết cờ, in `Unrecognized command or argument '--ide-safe'` và **exit `1`** — host báo "CLI quá cũ" và không bao giờ chạy lại mà thiếu cờ.
- **`--allow-env-connection`** (chỉ `validate`, không có tác dụng nếu thiếu `--ide-safe`): giữ lại đúng một credential là `DATAGUARD_CONNECTION_STRING` do host đặt; `ConnectionString` trong file config **luôn** bị xoá. Khi giữ, stderr in `ide-safe: kept environment connection (--allow-env-connection)`; `GroundTruthMode: Manual` bị ép về `Snapshot`. Credential được giữ **chỉ dùng để đọc catalog ground-truth** (schema, định nghĩa stored procedure); `validate` không gửi hay describe SQL trích từ repo lên database — rule live SQL shape (`sp_describe_first_result_set` trên SQL của repo) vẫn tắt dưới `--ide-safe` (stderr liệt kê `live SQL shape rule disabled (use verify-shape)`), chỉ `verify-shape` (có xác nhận) mới thực hiện. VS Code chỉ truyền cờ này khi có credential trong SecretStorage.
- **Giới hạn tài nguyên**: `MaxDegreeOfParallelism` > số CPU, `MaxViolationQueueSize` > 100000, `ValidationTimeoutSeconds` > 900 bị kẹp về giới hạn và liệt kê dạng `Name=value (clamped to bound)` trong dòng `ide-safe: suppressed ...`.
- **EF ground truth tự phát hiện** (chỉ `validate --ide-safe --project <path>`): CLI tìm đúng **một** file `*ModelSnapshot.cs` (EF Core design-time) trong các file nguồn đã quét dưới `--project` (bỏ `bin`/`obj`/`.git`/`.vs`/`node_modules`, bỏ symlink/junction, file rỗng, file > 1 MiB, file ngoài thư mục `--project`), đọc nó **một lần** với giới hạn kích thước và parse bằng Roslyn như `--ef-snapshot` (không nạp assembly). Thành công: stderr `ide-safe: ef model <đường dẫn tương đối> (source parse)`, entity trở thành ground truth và không còn cảnh báo "no ground truth". Không có file: im lặng. Nhiều file: `ACQUISITION: <n> ModelSnapshot.cs files found under --project (...); commit .dataguard-snapshot.json or keep one design-time snapshot per solution` và không dùng file nào. File bị bỏ qua hoặc không parse được (không có `modelBuilder.Entity<T>`): `ACQUISITION: <path>: EF model not used: <lý do>`, run tiếp tục như cũ. Tắt bằng `AutoDetectEFContext: false` trong `.dataguard.yml`. `--ef-snapshot`/`--ef-project` vẫn bị từ chối dưới `--ide-safe`; không có `--ide-safe` thì không tự phát hiện (CI không đổi). Event `Summary` (`--progress`) có thêm `GroundTruth`: `none | entities | schema | schema+entities`. Đường dẫn và key YAML in ra stderr được escape ký tự điều khiển (`\n` → `\\n`) để không giả mạo dòng mới cho host.
- **Baseline**: vẫn được áp dụng, nhưng mọi suppression đều hiển thị: stderr `baseline: <n> violations suppressed by <path>` và progress event `BaselineApplied` (`--progress`); host echo thành `[WARN]`.
- **Gia cố regex**: mọi regex trong tiến trình CLI có match timeout 1 giây; input bệnh hoạn làm regex vượt timeout được **báo là rule failure** (rule đó không bị bỏ qua âm thầm) thay vì treo tiến trình; SQL literal dài hơn 256 KiB bị bỏ qua với `[WARN] DG1291 SQL literal in <file>:<line> is <n> chars (cap 262144); skipped`.

**Cổng chống "PASS rỗng" (empty-pass gate)**:
- **Snapshot mặc định**: khi không có connection và config không đặt `SnapshotFilePath`, `validate` tìm `.dataguard-snapshot.json` cạnh file `--config`, rồi trong thư mục hiện tại (nơi `snapshot refresh` ghi mặc định); nếu thấy, stdout in `Using snapshot <path>`.
- **Ground truth bắt buộc**: nếu sau khi thu thập không có schema (snapshot/connection), stored procedure, hay entity (assembly Manual / EF `--ef-snapshot`/`--ef-project`, hoặc `*ModelSnapshot.cs` tự phát hiện dưới `--ide-safe --project`) — tức chỉ có SQL inline từ `--project` — stderr in `UNEVALUATED: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` và exit `3`. `--allow-syntactic-only` hạ xuống cảnh báo. Không áp dụng cho `--format contracts|yaml|typescript`. Dưới `--ide-safe` cổng này luôn chỉ là cảnh báo (IDE-safe đã cố ý tắt mọi nguồn nạp code/kết nối), và `--config` thiếu file chỉ là cảnh báo vì host luôn truyền đường dẫn config chuẩn của workspace.
- **Contract chưa đánh giá** (red-team H1/H2): describe live lỗi hoặc bị từ chối (DB không kết nối được, thiếu quyền, temp table, câu lệnh không chỉ-đọc hoặc nhiều câu lệnh) không còn sinh cảnh báo `DG020` exit `0` và Oracle/PostgreSQL không còn bịa cột `VARCHAR2`/`text` từ text SQL. Sau validate, stderr in `UNEVALUATED: <n> contract(s) could not be evaluated:` rồi mỗi dòng `<ruleId> <contractId>: <reason>` (`DG020` = shape query không xác định). ModelSnapshot parse một phần (`--ef-snapshot`/`--ef-project`) giữ entity đã parse và in `ACQUISITION: <path>: <message>` cho từng cấu hình bị bỏ qua. Có một trong hai => exit `3`, trừ khi `--allow-unevaluated` (hoặc `--ide-safe`) => exit theo violation. MySQL giờ có describer live (`CommandBehavior.SchemaOnly` + `GetColumnSchema()`, wrapper `WHERE 1=0 LIMIT 0`, session read-only).
- **Rule không khả dụng** (ví dụ `DG012` Oracle, `PG004` PostgreSQL cần metadata DbContext từ analyzer): in một lần ra stderr `Rule <id> not evaluated: <reason>` và **không** chặn run. `--fail-on-unavailable` / `FailOnUnavailableRules: true` khôi phục exit `3`; rule nằm trong `--skip-rules` không được tính.
- **Config**: key top-level không biết => stderr `Warning: unknown configuration keys: a, b`; `StrictConfig: true` => exit `2`. Giá trị sai kiểu (ví dụ `EnableBaseline: maybe`) => exit `2`.

**Kiểm tra snapshot khi `validate` (red-team H4)**: khi `validate` đọc snapshot (không có connection):
- `Provider` trong snapshot khác `--provider`/`DefaultProvider` (`postgres` = `postgresql`) => stderr `UNEVALUATED: snapshot provider 'x' does not match 'y'`, exit `3`.
- `SchemaHash` được tính lại trên nội dung đã nạp; khác giá trị lưu (file bị sửa tay trong PR, hoặc hỏng) => `UNEVALUATED: snapshot integrity check failed: ...`, exit `3`. `snapshot diff` cũng kiểm tra trước khi kết nối.
- Snapshot cũ hơn `SnapshotMaxAgeDays` (config, mặc định `90`; `0` tắt) => `Warning: snapshot is N days old ...` (không chặn).
- Snapshot định dạng cũ (v2/v3, chỉ có bảng) vẫn đọc được, kèm `Warning: snapshot has no stored procedures; run 'dataguard snapshot refresh' to enable procedure checks`.
- `LengthSemantics` và charset của cột lấy từ file (không còn cứng `CHAR`; snapshot cũ mới mặc định `CHAR`); stored procedure trong snapshot v4 được nạp làm ground truth.
- Baseline còn entry cũ (không có fingerprint) => stderr `baseline contains N legacy entries; run 'dataguard baseline' to upgrade`; các entry này vẫn suppress theo `RuleId:Message`.

**Exit Codes**:
- `0` = Pass (không violation mới)
- `1` = Fail (có violation mới, hoặc CLI lỗi trước khi in summary)
- `2` = Sai tham số / config: `--provider` hoặc `DefaultProvider` ngoài danh sách, `--config` trỏ tới file không tồn tại, config sai kiểu hoặc có key lạ khi `StrictConfig: true` (kể cả option bị `--ide-safe` từ chối; lưu ý CLI ≤ 0.2.2 không biết `--ide-safe` thì exit `1`)
- `3` = Validation incomplete: không có nguồn contract, không có ground truth (trừ khi `--allow-syntactic-only`), rule không khả dụng khi bật `--fail-on-unavailable`, hoặc có contract chưa đánh giá / chẩn đoán acquisition (trừ khi `--allow-unevaluated`)
- `130` = Bị huỷ (Ctrl+C)

---

### 2. `dataguard baseline` - Tạo Baseline

```bash
# Tạo baseline từ validation hiện tại
dataguard baseline --connection-env ORACLE_CI --output .dataguard-baseline.json

# Không có connection: dùng cùng snapshot mà validate dùng (.dataguard-snapshot.json)
dataguard baseline

# Baseline sẽ chứa:
# - Version: 2
# - SchemaVersion: "1.0"
# - GroundTruthMode: "Snapshot"
# - DatabaseVersion: "Oracle Database 19c..."
# - SchemaHash: "a1b2c3d4e5f67890" (SHA256-64bit)
# - Violations: mỗi entry có Fingerprint (dataguard/v2), Count và Properties
```

**Fingerprint v2 (red-team H3)**: `Fingerprint` = SHA-256 của `ruleId | subject | location`. Subject là các `Properties` có cấu trúc (`table`, `column`, `columns`, `entity`, `property`, ...) sắp xếp theo key, bỏ key dễ đổi (số dòng, số đếm, message); không có property thì dùng message. Location là đường dẫn tương đối với thư mục chạy lệnh (dấu `/`) cộng `sqlHash` khi rule có. Hệ quả:
- Dời finding sang dòng khác => vẫn khớp; **đổi tên/di chuyển file => finding mới** (chạy lại `dataguard baseline`).
- Chạy `baseline` và `validate` từ cùng thư mục gốc repo.
- Baseline là multiset: `Count: 1` mà hiện có 2 finding cùng fingerprint => 1 finding mới được báo.
- Hai `SELECT *` khác nhau, hoặc DG004 thiếu `[A..E]` và `[A..F]`, là hai finding khác nhau (message vẫn cắt 5 cột để hiển thị, `Properties` giữ đủ danh sách).
- Entry cũ không có `Fingerprint` vẫn khớp theo `RuleId:Message`; chạy lại `dataguard baseline` để ghi lại thành v2 (lệnh luôn đánh giá mọi finding, không lọc qua baseline cũ).
- SARIF: mỗi result có `partialFingerprints: { "dataguard/v2": <fingerprint> }`, cùng giá trị với baseline.

**Khi nào dùng / When to use**:
- Dùng khi cần chấp nhận finding đã biết, sau review. Không bắt buộc cho mọi project.
- Review metadata và quyền chia sẻ trước khi commit baseline; thay đổi baseline có thể suppress regression.

---

### 3. `dataguard snapshot` - Quản Lý Snapshot

```bash
# Làm mới snapshot từ DB hiện tại
dataguard snapshot refresh --connection-env ORACLE_CI

# Xem thông tin snapshot
dataguard snapshot show

# So sánh schema hiện tại vs snapshot
dataguard snapshot diff --connection-env ORACLE_CI
```

**Định dạng snapshot v4** (plan gọi là "snapshot v3"; trên đĩa `Version: 4`): `refresh` ghi bảng (kèm `Schema` và `Charset` của cột), `StoredProcedures` (tham số với direction/độ dài/`HasDefault`/overload, cột kết quả, ref cursor, `ReturnType`), `LengthSemantics`, `Charset`, `Provider`, `DatabaseVersion`. `SchemaHash` (`canonical-schema-v2`) phủ toàn bộ nội dung đó. `show` in số bảng/cột/procedure/tham số và kết quả kiểm tra integrity. `diff` so sánh cả procedure (thêm/xoá/đổi tham số), in danh sách bảng/procedure thay đổi, và cảnh báo khi major.minor của database khác snapshot.

**Snapshot vs Baseline**:
| | Snapshot | Baseline |
|---|---|---|
| **Mục đích** | Schema hiện tại (giống Jest snapshot) | Drift hiện tại (freeze) |
| **Cập nhật** | `snapshot refresh` sau review/cho phép | Chạy `baseline` có chủ đích rồi review suppression; không tự cập nhật |
| **So sánh** | `snapshot diff` | Tự động trong `validate` |

---

### 4. `dataguard init` - Khởi Tạo

```bash
# Wizard tương tác (khuyên dùng)
dataguard init --wizard

# Tự động với provider
dataguard init --provider oracle --output .dataguard.yml
```

**Wizard Steps**:
1. Detect provider (auto/nhập tay)
2. Nhập connection string (hoặc dùng env var)
3. Detect EF Core / Dapper tự động
4. Chọn naming convention
4. Chọn baseline mode (Snapshot khuyên dùng)
5. Generate `.dataguard.yml`

---

### 5. `dataguard config` - Quản Lý Config

```bash
# Xem config hiện tại
dataguard config show

# Xác thực config file
dataguard config validate --config .dataguard.yml
```

---

### 6. `dataguard oracle-check` - Kiểm Tra Oracle

```bash
# Chạy dialect + length checks
dataguard oracle-check --connection-env ORACLE_CI --format sarif
```

**Implemented scope / Phạm vi thực tế:** `OracleCheckCommand.RunOracleValidationAsync` reads NLS length semantics and owner column metadata, then calls `CheckRawSqlUnmappedTypeUsage` on the catalog type/name text. It does not run every DG007–DG014 analyzer/source rule. Set the owner/schema and inspect catalog access on the intended Oracle host. The command currently accepts `--format` but emits console diagnostics plus SARIF when `--output` is supplied; other requested formats are not implemented here.

Lệnh đọc catalog Oracle, không phải toàn bộ rule source/analyzer. `--format` hiện không đổi writer; muốn file SARIF phải chỉ rõ `--output`. Khả năng đọc catalog/NLS và phát hiện type trên host Oracle đích cần test live riêng.

---

### 6. `dataguard version` - Xem Phiên Bản

```bash
dataguard version
```

Output reports the actual CLI/component informational versions, runtime and OS. These values come from the selected binary/host, not a promised release version. / Output lấy version/runtime/OS thực tế, không chứng minh bản release 1.0.0 tồn tại.

---

### 7. `dataguard hook` - Pre-commit Hooks

```bash
# Cài đặt (auto-detect: Husky/Lefthook/Native)
dataguard hook install

# Cài đặt force (ghi đè)
dataguard hook install --force

# Xem trạng thái
dataguard hook status

# Gỡ cài đặt
dataguard hook uninstall
```

**Supported Hook Types**:
| Type | File | Auto-detect |
|------|------|-------------|
| **Native Git** | `.git/hooks/pre-commit` | Luôn có |
| **Husky** | `.husky/pre-commit` | Nếu folder `.husky/` tồn tại |
| **Lefthook** | `lefthook.yml` | Nếu file tồn tại |

**Generated hook behavior (excerpt) / Trích hành vi hook**:
```bash
#!/bin/sh
# DataGuard pre-commit hook
echo "🔍 Running DataGuard pre-commit validation..."

if command -v dataguard >/dev/null 2>&1; then
    dataguard validate --offline --format text
    exit_code=$?
    if [ $exit_code -ne 0 ]; then
        echo "❌ DataGuard validation failed. Fix issues before committing."
        exit 1
    fi
    echo "✅ DataGuard validation passed."
else
    echo "⚠ DataGuard CLI not found. Skipping validation."
fi
exit 0
```

---

## Workflow Đề Xuất / Recommended Workflows

### 1. Legacy onboarding / Project cũ

Provision the reviewed CLI, initialize config without overwriting existing files, acquire an authorized snapshot and validate. Create a baseline only after reviewing the findings you intend to suppress. Review generated metadata before committing it. See [quickstart](01-overview/quickstart.md).

Cài CLI đã kiểm tra, tạo config, lấy snapshot được cho phép và validate. Baseline là quyết định chấp nhận finding, không bắt buộc và không tự sửa schema.

### 2. Greenfield / Project mới

Choose the required library/analyzer packages from a verified local feed or independently verified release. The CLI does not require adding Core/adapters to your application. Test the analyzer against the actual IDE/build host; never add skip attributes merely to remove a warning. Install hooks only deliberately and inspect their generated content: missing CLI currently prints a warning and skips validation, so this hook is not a fail-closed CI gate.

Chọn package đã xác minh; không suy ra registry có sẵn. Analyzer cần nghiệm thu trên host thật. Hook là opt-in; thiếu CLI thì hook hiện bỏ qua validation, không tương đương CI gate bắt buộc.

### 3. Oracle Length Mismatch Prevention

```csharp
// Entity với MaxLength
public class Customer {
    public int Id { get; set; }
    
    [MaxLength(100)]  // DataGuard validate vs DB CHAR_LENGTH
    public string FullName { get; set; }
    
    [MaxLength(255)]
    public string Email { get; set; }
    
    // Unicode = true (mặc định string) → check byte semantics
    public string Description { get; set; }  // DG009 nếu không MaxLength
}

// Nếu DB: VARCHAR2(50 BYTE) + Unicode data
// → DG008 Warning: Byte overflow risk
// Giải pháp: [MaxLength(50)] hoặc đổi DB sang CHAR semantics
```

### 4. Oracle Dialect Safety

```sql
-- ❌ DG010: Oracle syntax trong non-Oracle context
SELECT NVL(col, 'default') FROM table;
SELECT col1 (+) col2 FROM table;

-- ✅ ANSI SQL (cross-platform)
SELECT COALESCE(col, 'default') FROM table;
SELECT * FROM t1 LEFT JOIN t2 ON t1.id = t2.id;

-- ❌ DG011: SQL Server syntax trong Oracle
SELECT ISNULL(col, 'default') FROM table;
SELECT TOP 10 * FROM table;

-- ✅ Oracle equivalents
SELECT NVL(col, 'default') FROM table;
SELECT * FROM table FETCH FIRST 10 ROWS ONLY;

-- ❌ DG013: EXEC dbo.Proc trong Oracle
EXEC dbo.GetOrders @CustomerId;

-- ✅ Oracle block
BEGIN
  pkg_orders.get_orders(:customerId, :cursor);
END;
```

---

## IDE Integration / Tích Hợp IDE

### Roslyn Analyzers (Visual Studio / Rider / VS Code)

```xml
<!-- .csproj -->
<PackageReference Include="DataGuard.Analyzers" Version="<verified-package-version>" />
```

**Dòng compiler / Compiler line**: analyzer được build với Roslyn 4.12 (`Microsoft.CodeAnalysis` 4.12.0) — dòng compiler mà .NET SDK 9.0.1xx và Visual Studio 2022 17.12+ dùng. Compiler cũ hơn (SDK 8.x, VS 2022 17.0–17.11) từ chối analyzer với `warning CS9057` và không hiện diagnostic nào. / The analyzer targets Roslyn 4.12, the compiler line of the .NET 9.0.1xx SDK and Visual Studio 2022 17.12+; an older compiler refuses it with `warning CS9057` and shows no diagnostics (checked in CI by `scripts/verify-analyzer-packaging.sh`).

**Diagnostics Hiển Thị / Diagnostics Shown**:
| ID | Mô Tả | Severity | Quick Fix |
|----|-------|----------|-----------|
| DG001 | Unvalidated SQL call | Warning | Add `[SkipContractCheck]` / Add params |
| DG002 | Parameter count/type mismatch | Error | Add `[ExpectedSpParameter]` |
| DG003 | Parameter direction mismatch | Error | Add `out`/`ref` |
| DG004 | Column shape mismatch | Error | - |
| DG005 | Nullable mismatch | Warning | Add `[Required]` / fix DB |
| DG006 | Naming convention | Warning | Auto-fix rename |
| DG007 | Length exceeds column | Error | Add `[MaxLength]` / Suggest CLOB |
| DG008 | Byte overflow risk | Warning | Add MaxLength / Change semantics |
| DG009 | NVARCHAR2(2000) fallback | Warning | Add MaxLength / Use CLOB |
| DG010 | Oracle syntax in non-Oracle | Warning | Convert to ANSI |
| DG011 | SQL Server syntax in Oracle | Warning | Convert to Oracle |
| DG012 | Missing UseOracle() | Error | Add `.UseOracle()` |
| DG013 | EXEC dbo. in Oracle | Warning | Use BEGIN/END block |
| DG014 | Unmapped type | Warning | Map type / Add to DbContext |

**Quick Fixes (Lightbulb 💡)**:
- `💡 Add [SkipContractCheck("Dynamic SQL")]`
- `💡 Add [ExpectedSpParameter("@Id", "INT", "IN")]`
- `💡 Add [ExpectedColumn("CUSTOMER_ID", "int")]`
- `💡 Auto-fix naming convention`
- `💡 Convert to ANSI SQL`
- `💡 Add [MaxLength(100)]`
- `💡 Add .UseOracle() to DbContext`

### Visual Studio 2022 Extension (`DataGuard.VisualStudio`)

Menu **Tools → DataGuard**: Run Validation, Cancel, Assess Workspace, View Diagnostic Logs, Validation Rules, Forget Solution Consent, **Refresh Snapshot**, **Verify SQL Shapes Against Database**. Kết quả SARIF của `validate`/`assess` được nạp vào **Error List** (double-click nhảy đúng dòng/cột), tối đa 2 000 mục mỗi lần chạy.

**Yêu cầu phiên bản / Version floor**: squiggles và quick fix trong editor (DG001, DG017, …) đến từ analyzer đóng gói trong VSIX (`DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll` cùng `DataGuard.Contracts.dll`, `DataGuard.SqlClassification.dll`) và **cần VS 2022 17.12+** (manifest cài đặt `[17.12,19.0)`): VS 17.0–17.11 chạy Roslyn < 4.12 và từ chối analyzer (`CS9057`). / In-editor squiggles need VS 2022 17.12+; the VSIX installs only on 17.12 and later.

**Mô hình tin cậy / Trust model**:
- Lần chạy đầu tiên cho mỗi solution, extension hỏi xác nhận (modal) và lưu đồng ý theo bộ ba *(thư mục solution, đường dẫn `.sln`, SHA-256 của `.dataguard.yml`)* trong user settings của VS. Khi repo đổi `.dataguard.yml`, extension hỏi lại.
- "Run Validation on Build" **không bao giờ** hỏi; nếu solution chưa được đồng ý thì bỏ qua và ghi một dòng vào Output pane.
- `validate`/`assess` luôn chạy với `--ide-safe` (không nạp assembly, không kết nối DB/secret manager/network) và không truyền `--allow-env-connection`; hai lệnh database (**Refresh Snapshot**, **Verify SQL Shapes Against Database**) là lệnh live riêng, xem bên dưới. Kết quả chỉ được nạp vào Error List khi CLI in `ide-safe: active`; thiếu dòng này thì kết quả bị huỷ, và CLI ≤ 0.2.2 (từ chối cờ, exit 1) được báo là quá cũ.
- Đồng ý được khoá theo *(thư mục solution, đường dẫn `.sln`, SHA-256 của `.dataguard.yml`)*; lệnh **Forget Solution Consent** xoá đồng ý của solution hiện tại.
- CLI được bundle trong VSIX (`cli\dataguard.exe`); có thể chỉ định đường dẫn tuyệt đối khác tới một `dataguard.exe` tải từ GitHub Releases (kiểm tra SHA-256) trong Tools → Options → DataGuard → General. Extension **không** tự cài và **không** hướng dẫn `dotnet tool install` nữa.
- Dòng `ide-safe:`/`baseline:` của CLI được hiển thị trong Output pane.
- **Ground truth được báo trung thực**: Output pane in các dòng chính sách của CLI (`Warning:`, `UNEVALUATED:`, `ACQUISITION:`, `Rule <id> not evaluated:`) dưới dạng `[DataGuard] [WARN] …`; dòng Summary thêm `ground truth: <none|entities|schema|schema+entities>`, `unevaluated N`, `unavailable rules N` khi CLI cung cấp. Một lần `validate` **không nạp ground truth** (không snapshot, không EF model, không kết nối) kết thúc bằng `[WARN] Ran syntactic rules only: … the database contract was NOT compared …` thay vì `[OK] No issues found.`, status bar hiện `DataGuard: syntactic only (N errors, N warnings)`, và khối Result có dòng `Ground truth: not loaded (syntactic rules only)`. Contract không đánh giá được sau khi đã nạp ground truth được báo riêng (`[WARN] N contract(s) could not be evaluated …`). `validate` exit 4 được giải thích là CLI từ chối ghi file kết quả.
- Solution EF Core có đúng một `*ModelSnapshot.cs` (design-time) dưới thư mục solution: CLI tự parse nó làm ground truth (xem "EF ground truth tự phát hiện" ở `--ide-safe`), Output pane in `ide-safe: ef model <path> (source parse)`, DG006/DG004 từ entity có vị trí trong `ModelSnapshot.cs` để Error List hiển thị. Không cần cấu hình; tắt bằng `AutoDetectEFContext: false`.
- Lần chạy chạm giới hạn timeout: process tree của CLI bị dừng; nếu file SARIF đã được ghi xong thì kết quả vẫn được nạp vào Error List và Output pane in `[WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List.` (exit code của tiến trình bị dừng là của `taskkill`, không được diễn giải), nếu chưa có SARIF thì lần chạy bị huỷ và Output pane nói rõ.
- Không có `.dataguard.yml` trong solution: CLI chỉ chạy các rule source-only và extension cảnh báo rõ trong Output pane.
- **Lệnh database (live, không ide-safe)**: Tools → Options → DataGuard → General, mục *Live Database*: **Connection Environment Variable** (chỉ *tên* biến môi trường, ví dụ `DATAGUARD_CONNECTION_STRING`; VS không bao giờ lưu hay ghi log giá trị), **Live Provider** (`sqlserver` mặc định, `oracle`, `mysql`, `postgresql`), **Live Schema** (tuỳ chọn). **Refresh Snapshot** chạy `snapshot refresh --connection-env NAME --provider P [--schema S]` với cwd là thư mục solution nên file `.dataguard-snapshot.json` nằm đúng chỗ `validate` tìm; **Verify SQL Shapes Against Database** chạy `verify-shape --project <slnDir> --connection-env NAME --provider P --format json --output <tmp>\summary.json`. Cả hai **không bao giờ** truyền `--config` (một `.dataguard.yml` độc hại không thể đổi đường ghi snapshot, bật `DescribeRefCursors` hay chỉ định `ManualAssemblyPath`), `--ide-safe` hay `--connection`. Lần đầu mỗi solution hỏi một *consent live* riêng (khoá khác với consent của validate, nêu rõ "not IDE-safe", tên biến và đường ghi snapshot); mỗi lần chạy còn hỏi modal `DataGuard will connect to <host[:port]> (environment variable NAME, provider P) …` với host **không che** (để phân biệt prod/dev), nút mặc định là No. Output pane in banner `Mode: live database (not ide-safe; reads environment variable NAME; .dataguard.yml is not read)`; kết quả hiển thị theo exit code (`[OK] Snapshot refreshed: <path>` chỉ khi CLI in `snapshot: wrote …` và file tồn tại, mới hơn lúc chạy; verify-shape in `Verify shapes: N verified, N mismatches, N undetermined, N untyped` kèm một dòng `file:line "sql" → type` cho mỗi query chưa verified). Error List không thay đổi; "Run Validation on Build" không bao giờ gọi hai lệnh này. CLI phía server từ chối (exit 4, `Refusing to write snapshot …`) nếu `SnapshotFilePath` nằm ngoài thư mục làm việc/thư mục config, trước khi mở kết nối. Nếu timeout giết CLI giữa lúc ghi atomic, có thể còn lại `.dataguard-snapshot.json.<guid>.tmp` trong thư mục solution — thêm `*.tmp` (hoặc `.dataguard-snapshot.json.*.tmp`) vào `.gitignore` của repo.

### VS Code Extension (`DataGuard.VSCode`)

- `validate`/`assess` chạy với `--ide-safe`; kết quả bị huỷ nếu dòng stderr đầu tiên không phải `ide-safe: active` (thông báo `DataGuard CLI did not confirm IDE-safe mode; results were discarded`, kèm gợi ý `Update the dataguard CLI (0.3.0 or later) or set dataguard.cliPath` khi CLI cũ).
- Khi có credential trong VS Code SecretStorage, `validate` truyền thêm `--allow-env-connection`: CLI chỉ giữ credential đó (không bao giờ giữ connection string trong `.dataguard.yml`) và chỉ dùng nó để đọc catalog ground-truth (schema, stored procedure); `validate` **không** gửi hay describe SQL của repo lên database — việc đó chỉ xảy ra trong **Verify SQL Shapes Against Database** sau khi bạn xác nhận.
- **Refresh Snapshot**, **Create Baseline**, **Verify SQL Shapes Against Database** (`snapshot`, `baseline`, `verify-shape`) là lệnh kết nối database thật: dùng credential của bạn (hoặc kết nối trong `.dataguard.yml`/`DATAGUARD_CONNECTION_STRING`) và luôn hỏi xác nhận (modal) nêu host đích đã che trước khi chạy; không truyền `--ide-safe`.
- Dòng xác nhận `ide-safe: active` hiển thị trong DataGuard output channel dưới dạng `[INFO]`; các dòng `ide-safe:`/`baseline:` khác của CLI (suppressed, kept, baseline suppression) hiển thị dưới dạng `[WARN]`.

---

## CI/CD Integration / Tích Hợp CI/CD

Provision an exact reviewed CLI artifact and its required runtime before validation; see [installation](05-operations/installation-guide.md). Supply a reviewed snapshot for untrusted PRs. A portable validation step is:

```bash
dataguard validate --offline --project ./MyApp.csproj --format sarif --output dataguard.sarif
```

Replace the project path. Preserve the CLI exit status and retain the report on failure. In an explicitly authorized live job, supply `ORACLE_CONNECTION` through the CI secret mechanism and use `--connection-env ORACLE_CONNECTION --provider oracle`, never interpolate the secret into shell source or argv.

GitHub Code Scanning needs a valid SARIF file and appropriate upload permissions. Azure DevOps/GitLab can retain it as a generic artifact; GitLab's native `artifacts:reports:sast` expects its own schema, not arbitrary SARIF. Hosted upload/integration on each platform remains an acceptance test, not evidence provided by these CLI examples.

CI cần cung cấp CLI/runtime và snapshot đã kiểm tra. Giữ exit status và artifact khi lỗi; chỉ job live được cho phép mới nhận secret. Upload SARIF là bước riêng cần quyền và kiểm thử trên host. Không đưa SARIF trực tiếp vào GitLab SAST report schema.

---

## Troubleshooting / Xử Lý Sự Cố

### Lỗi Thường Gặp / Common Errors

| Lỗi | Nguyên Nhân | Giải Pháp |
|-----|------------|-----------|
| `DG001: Unvalidated SQL call` | Gọi SP/Raw SQL chưa validate | Chạy `dataguard validate` hoặc thêm `[SkipContractCheck]` |
| `DG007: Length exceeds column` | Entity MaxLength > DB column | Thêm `[MaxLength]` hoặc sửa DB schema |
| `DG008: Byte overflow risk` | Unicode data trong BYTE column | Thêm `MaxLength` hoặc đổi DB sang CHAR semantics |
| `DG009: NVARCHAR2(2000) fallback` | String không MaxLength + Unicode | Thêm `[MaxLength]` hoặc đổi DB sang CLOB |
| `DG010: Oracle syntax in non-Oracle` | Dùng NVL/DECODE trong SQL Server | Dùng COALESCE/CASE ANSI SQL |
| `DG012: Missing UseOracle()` | EF Core Oracle chưa đăng ký | Thêm `.UseOracle()` trong `OnConfiguring` |
| `Connection string not found` | Thiếu connection string | Set env `DATAGUARD_CONNECTION_STRING` hoặc config file |
| `Baseline file not found` | Chưa chạy baseline | Chạy `dataguard baseline` trước |

### Debug Mode

```bash
# Verbose logging
dataguard validate --connection "..." --verbose

# Chỉ output text (không SARIF)
dataguard validate --connection "..." --format text

# Offline mode (không cần DB)
dataguard validate --offline --format text
```

---

## Best Practices / Thực Hành Tốt

### 1. Baseline Management
```bash
# Chạy baseline MỘT LẦN khi onboarding
dataguard baseline --connection-env ORACLE_CI

# Khi DBA thay đổi schema hợp lệ
dataguard snapshot refresh --connection-env ORACLE_CI
# → Review diff trong PR → Approve → Merge

# KHÔNG sửa baseline file thủ công
# → Dùng `snapshot refresh` hoặc `baseline` command
```

### 2. Naming Convention
```csharp
// Entity properties: PascalCase
public string CustomerName { get; set; }

// DB columns: SNAKE_CASE (Oracle default)
[Column("CUSTOMER_NAME")]
public string CustomerName { get; set; }

// DataGuard tự động map: CustomerName ↔ CUSTOMER_NAME
```

### 3. MaxLength Best Practices
```csharp
// ✅ Luôn thêm MaxLength cho string
[MaxLength(100)]
public string Name { get; set; }

// ✅ Unicode = true (mặc định) → check byte semantics
[MaxLength(255)]
public string Description { get; set; }

// ❌ Không MaxLength → DG009 fallback NVARCHAR2(2000)
public string BadExample { get; set; }
```

### 4. Oracle Specific
```csharp
// DbContext Oracle config
protected override void OnConfiguring(DbContextOptionsBuilder options)
{
    options.UseOracle(connectionString, opts => 
    {
        opts.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19);
    });
}
```

---

## Environment Variables / Biến Môi Trường

| Variable | Mô Tả | Ví Dụ |
|----------|-------|-------|
| `DATAGUARD_CONNECTION_STRING` | Connection string chính (sau `--connection-env`) | `Server=...;Database=...` |
| `DATAGUARD_DATABASECONNECTION` | Connection string đọc bởi credential provider | `Server=...;Database=...` |
| `DATAGUARD_AUDIT_KEY` | Khoá HMAC (≥ 16 byte) cho audit hash chain; thắng `AuditKeyFile` | (secret) |
| `DATAGUARD_PROVIDER` | Gợi ý cho AutoDetectionEngine; không ghi đè `--provider`/`DefaultProvider` trong CLI validation | `Oracle` / `SqlServer` |

---

## Migration Guide / Hướng Dẫn Nâng Cấp

### Upgrade a selected version / Nâng cấp version đã chọn

Do not assume a v1.0 release exists. Review the selected version's changes and exact packages, preserve existing config, then run synthetic offline fixtures and the intended provider/editor tests. `init` refuses to overwrite config; create a separate output if comparing defaults. `migrate --baseline <path>` handles legacy v1 baseline format, not arbitrary schema or product upgrades. Review baseline changes instead of blindly recreating them.

Không coi số version minh họa là release thật. Sao lưu/review config, kiểm thử offline và provider/editor đích; migration baseline v1→v2 không phải migration mọi schema hay mọi version sản phẩm.

---

## FAQ / Câu Hỏi Thường Gặp

**Q: DataGuard có thay thế EF Core Power Tools không?**
A: Không. EFCorePowerTools scaffold code. DataGuard **validate** hợp đồng tại build/CI. Cùng dùng được.

**Q: Có hỗ trợ PostgreSQL/MySQL không?**
A: Có adapter MySQL và PostgreSQL trong source, cùng SQL Server/Oracle. Hỗ trợ phụ thuộc metadata và SQL shape; operation chưa đánh giá phải được báo, không coi như đã validate. Test database/version cụ thể vẫn cần evidence riêng.

**Q: Có cần DB connection trong IDE không?**
A: Analyzer/generator IDE kiểm tra local; chưa có số đo latency cho mọi host. Validation theo ground-truth mode; Full dùng DB, Snapshot/Manual không cần live DB. Các lệnh live của extension có consent riêng.

**Q: Baseline file có an toàn để commit không?**
A: Không tự động an toàn. Baseline chứa message/properties/path và snapshot có thể chứa schema/procedure metadata. Review nội dung, secret và quyền chia sẻ trước khi commit.

**Q: Có thể custom rules không?**
A: Có contract plugin qua `validate --plugins-dir`; cần manifest, SHA-256, dependency và rule ID hợp lệ. CLI chưa có signed-provenance verifier mặc định nên từ chối nếu không có opt-in unsigned local; xem option ở trên. Không tự nạp từ `~/.dataguard/plugins/`; IDE-safe cấm plugin.

---

## Links Hữu Ích / Useful Links

- [Repository](https://github.com/thanhnt-sm/eco_support_net_oracle)
- [Issues](https://github.com/thanhnt-sm/eco_support_net_oracle/issues)
- [Documentation index](README.md)
- [Installation](05-operations/installation-guide.md)
- [Rule source overview](03-components/core/rules-engine.md)

Source-level reconciliation: SA-DOC-015, baseline `ca68a3159425495f771da866188df75a8237ab4b`. Commands and contracts were inspected; public releases, live database operations, OS credential backends and hosted CI/editor execution were not exercised by this review.
