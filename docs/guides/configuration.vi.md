# Cấu hình phân tích snapshot có chủ đích

Dùng [mẫu Oracle tối thiểu](oracle-snapshot.minimal.yml) hoặc [mẫu đầy đủ có chú thích từng trường](oracle-snapshot.full.yml). Mẫu đầy đủ do `dataguard init --provider oracle` sinh ra. Cả hai được kiểm thử bằng `ConfigLoader`/`ConfigDocument` thực tế — schema cấu hình có thể thực thi. `config validate` kiểm tra kiểu/provider, không chứng minh snapshot tồn tại, còn mới hay phản ánh đúng database.

## Lỗi cấu hình đã tái hiện

`Oracle: true` và `SqlServer: false` sai kiểu: hai khóa này là **mapping**, không phải công tắc provider. Trước bản sửa, binding có kiểu bị lỗi, fallback âm thầm bỏ các trường này (và có thể mất danh sách/block lồng) nhưng giữ `DefaultProvider: sqlserver`. `config validate` vẫn trả exit 0. Vì vậy T-SQL được chọn và tái hiện DG019 tại alias UPDATE/DELETE kiểu Oracle. Loader mới trả exit 2 với hướng dẫn khi sai kiểu, không tự đoán provider.

Thứ tự chọn: `--provider` → `DefaultProvider` → `sqlserver`. Snapshot và AutoDetectProvider không ghi đè. `config validate` hiển thị provider hiệu lực, cảnh báo block provider không được chọn. `Oracle: {}` hợp lệ cùng `DefaultProvider: sqlserver` vẫn chọn SQL Server. Wizard có bước dò bằng chứng project riêng. Generator hiện tại serialize object/null; chưa có bằng chứng CLI hoặc extension Visual Studio hiện tại tạo ra dạng boolean đã báo cáo.

Với snapshot đã xác nhận là Oracle:

```yaml
GroundTruthMode: Snapshot # Nguồn schema, không phải phương ngữ.
DefaultProvider: oracle # --provider trên CLI có ưu tiên cao hơn.
SnapshotFilePath: .dataguard-snapshot.json # Snapshot Oracle đã có và được rà soát.
EnableBaseline: false # Rà soát finding trước khi chủ động tạo baseline.
AllowPlaintextConfigFallback: false # Không lưu credential trong YAML.
EnableTelemetry: false # Observability local chỉ bật khi có chủ đích.
StrictConfig: true # Khóa top-level không biết sẽ gây lỗi.
```

Chạy từ thư mục gốc solution:

```sh
dataguard config validate --config .dataguard.yml
dataguard validate --offline --config .dataguard.yml --project .
```

Snapshot đọc schema lưu sẵn khi không có connection đã resolve; **không đồng nghĩa offline**. Dùng `--offline`; IDE áp dụng `--ide-safe` riêng. Metadata provider snapshot phải khớp provider hiệu lực. `init` chỉ tạo YAML, không tạo schema/baseline. Lấy snapshot đã rà soát theo quy trình hiện hành; refresh từ database là hành động live riêng, cần được cho phép. Không tự dựng snapshot để vượt kiểm tra.

Chưa có parser ngữ pháp Oracle. Cấu hình đúng tránh dùng sai T-SQL nhưng raw SQL Oracle vẫn **chưa đánh giá**, thường exit 3. `--allow-unevaluated` chấp nhận kết quả chưa đầy đủ, không biến thành đã xác thực. Các rule Oracle được hỗ trợ khác vẫn chạy. Xem [DG019](dg019-dialect-and-locations.vi.md). Không thêm regex alias, đổi SQL, thực thi live hay cam kết kiểm tra toàn bộ Oracle.

## Ý nghĩa trường và thực hành tốt

| Trường | Ý nghĩa và giới hạn |
|---|---|
| `Oracle`, `SqlServer` | Bỏ block không dùng hoặc để null. Ví dụ `Oracle: { Owner: APP, DescribeRefCursors: false }`, `SqlServer: { Schema: dbo }`. Không chọn provider bằng block/boolean. REF CURSOR execution là opt-in, giữ false cho snapshot. |
| Baseline | EnableBaseline áp dụng BaselineFilePath đã rà soát nếu tồn tại. Suppression khác snapshot schema; không tự tạo file. Không baseline lỗi provider. |
| Exclusions | ExcludedEntities là tên CLR chính xác trong nguồn EF, không phải glob/suppression SQL toàn cục. ExcludedProcedures hiện là trường dự trữ, CLI acquisition chưa dùng. Viết danh sách YAML (`[]` hoặc block list). |
| Song song/hàng đợi | MaxDegreeOfParallelism 0 = theo số processor, không phải tắt/vô hạn. EnableConcurrentValidation false chạy tuần tự. Vượt MaxViolationQueueSize làm kết quả chưa đầy đủ. IDE-safe giới hạn tài nguyên quá lớn. |
| Timeout | ValidationTimeoutSeconds được lưu nhưng CLI chưa thực thi như deadline. Visual Studio dùng tùy chọn timeout riêng. Không coi YAML này là ranh giới an toàn. |
| Auto-detect | AutoDetectProvider/EnableSmartDefaults là gợi ý library, không ghi đè provider CLI. AutoDetectDapper hiện là placeholder; extractor tự nhận diện tên lời gọi hỗ trợ. AutoDetectEFContext có thể đọc ModelSnapshot.cs trong IDE-safe, không chạy DbContext/nạp assembly. |
| Tên/schema/package | DefaultSchema/DefaultPackage resolve procedure chưa qualify; NamingConvention đối chiếu tên. Không chép dbo sang Oracle nếu không đúng schema thực tế. |
| Credentials | Snapshot không cần credential trong YAML. Để trống ConnectionString, KeyVaultUri, AwsRegion, VaultAddress, ManualAssemblyPath nếu không có workflow riêng cần chúng. Live đã cho phép thì ưu tiên --connection-env NAME; không ghi secret vào repo/log. Backend hỗ trợ tùy môi trường. |
| Mã hóa | EncryptConnectionStringAtRest bảo vệ credential store local, **không mã hóa YAML**. Mặc định bật bảo vệ OS; RequireEncryptedCredentialStore quyết định fallback khi backend thiếu. Giữ AllowPlaintextConfigFallback false. Không cần tắt mã hóa cho snapshot offline. |
| Rotation/audit | Áp dụng cho quản lý credential; audit riêng với telemetry. AuditLogPath trống dùng mặc định application-data của logger. Key toàn vẹn audit ở ngoài repo. Offline/IDE-safe không khởi tạo chuỗi provider credential live. |
| Telemetry | Observability local opt-in, nhãn service/version và chi tiết đã redacted/giới hạn. Giữ false và details false nếu chưa cần. IDE-safe chặn output telemetry do repo chỉ định. |

`init` và `init --wizard` dùng chung writer có chú thích, từ chối ghi đè file đã có để giữ comment thủ công. Sửa tại chỗ hoặc sinh sang --output mới rồi xem diff. config validate/show và validate không ghi lại YAML. API serialize tái tạo chú thích chuẩn; object không giữ comment tùy ý nên không roundtrip comment thủ công qua API đó.

## Tùy chọn Visual Studio là luồng riêng

**Live Provider = oracle** chỉ áp dụng cho Refresh Snapshot và Verify SQL Shapes Against Database. Run Validation truyền `.dataguard.yml` của solution cùng `--ide-safe`, không truyền `--provider`, nên dùng DefaultProvider trong YAML. Tên biến môi trường live để trống sẽ chặn lệnh live, không chọn phương ngữ validate. Live Schema cũng riêng. **Custom CLI Path** có ưu tiên hơn CLI kèm VSIX; cài VSIX mới không chứng minh validate đã chạy CLI mới. Kiểm tra executable được ghi trong DataGuard output và phiên bản của nó; không cần giá trị credential.
