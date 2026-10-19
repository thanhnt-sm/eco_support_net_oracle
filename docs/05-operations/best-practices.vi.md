# Best Practices

Các ví dụ dưới đây giả định CLI đã được build/cài từ artifact chính xác đã kiểm tra và có trong PATH; `dataguard version` chỉ kiểm tra khả năng gọi lệnh, không cài tool. Xem [hướng dẫn cài đặt](installation-guide.vi.md). Không giả định package NuGet công khai tồn tại.

Các ví dụ live dùng `DATAGUARD_DB` do cơ chế secret session/CI được duyệt cung cấp. Không ghi giá trị thật vào đối số. Đối chiếu source tại `ca68a3159425495f771da866188df75a8237ab4b`: YAML dùng PascalCase theo `ConfigDocument`; test runtime còn cần kiểm tra database/schema cụ thể, backend audit/secret và CI upload trên host đích.

## Cài Đặt & Thiết Lập

### 1. Dùng Chế Độ Snapshot Cho CI

```yaml
# .dataguard.yml cho CI
GroundTruthMode: Snapshot
SnapshotFilePath: ".dataguard-snapshot.json"
```

**Tại sao:** Không cần credentials database trong CI. Xem xét và cung cấp snapshot đã được duyệt, rồi validate offline.

### 2. Review metadata trước khi chia sẻ

Kiểm tra config, snapshot, baseline để tìm credential, định nghĩa schema/procedure, đường dẫn và nội dung finding. Chỉ commit metadata được phép đưa vào repository; dùng artifact có kiểm soát truy cập khi cần. Baseline suppress finding cũ nên mọi thay đổi cần review.

### 3. Cố định CLI độc lập với dependency ứng dụng

```bash
dataguard version
```

**Tại sao:** Artifact/version được chọn rõ giúp tái lập mà không thêm CLI làm dependency ứng dụng.

## Cấu Hình

### 4. Không Bao Giờ Hardcode Connection Strings

```yaml
# ❌ Sai
ConnectionString: "Server=prod-db;Password=secret123"

# ✅ Đúng - dùng biến môi trường
ConnectionString: null  # Đặt biến môi trường DATAGUARD_CONNECTION_STRING
```

### 5. Bật Audit Logging Trong Production

```yaml
EnableAuditLogging: true
AuditLogPath: "/var/log/dataguard/audit.jsonl"
```

**Tại sao:** Audit hỗ trợ điều tra. HMAC cần `DATAGUARD_AUDIT_KEY` hoặc `AuditKeyFile`; bật logging không tự chứng minh compliance.

### 6. Tắt Fallback Plaintext

```yaml
AllowPlaintextConfigFallback: false  # Mặc định, không bao giờ đổi trong production
```

**Tại sao:** Ngăn chặn downgrade credentials thầm lặng. Chỉ bật trong development local.

## Quy Trình Validation

### 7. Review baseline trước khi áp gate

```bash
dataguard baseline --connection-env DATAGUARD_DB --provider oracle
dataguard validate --offline
```

Baseline suppress finding đã biết khi được bật; không suppress lỗi acquisition hay chứng minh coverage. Đọc stderr, contract chưa đánh giá và exit status. Tạo lại baseline có thể chấp nhận regression; không làm chỉ để CI xanh.

### 8. Dùng `--fail-on-drift` Trong CI

```bash
dataguard snapshot diff --fail-on-drift
```

**Tại sao:** Bắt thay đổi schema phá vỡ contracts trước khi chúng đến production.

### 9. Chạy Oracle Check Riêng Biệt

```bash
dataguard oracle-check --format sarif --output oracle-results.sarif
```

**Tại sao:** Lệnh live đọc catalog cột Oracle, không chạy mọi rule source/analyzer. Output console và SARIF tùy chọn; `--format` được nhận nhưng chưa đổi writer.

## Thực Hành Code

### 10. Dùng `[SkipContractCheck]` Ít

```csharp
[SkipContractCheck]  // Chỉ cho SQL thực sự động
public IQueryable<T> DynamicQuery<T>(string whereClause) { ... }
```

**Tại sao:** Mỗi skip là một điểm mù. Ghi lý do trong attribute hoặc comment.

### 11. Khai báo ground truth thủ công có chủ đích

```csharp
[ExpectedColumn("CUSTOMER_ID", "System.Int32", IsNullable = false)]
public int CustomerId { get; set; }
```

Attribute cung cấp kỳ vọng Manual mode qua `ManualContractSource`; không tự xác minh schema live hay bảo đảm check lúc compile. Review assembly đáng tin cậy và mode CLI trong [usage](../USAGE.md).

### 12. Giữ Snapshot Tươi Mới

```bash
# Sau mỗi thay đổi schema
dataguard snapshot refresh --connection-env DATAGUARD_DB --provider oracle
```

**Tại sao:** Snapshot cũ tạo niềm tin sai. Làm mới sau mỗi thay đổi schema từ DBA.

## Tích Hợp CI/CD

### 13. Upload SARIF Lên GitHub

```yaml
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```

**Tại sao:** Violations xuất hiện trong GitHub Security tab, liên kết đến vị trí code.

### 14. Giữ nguyên mọi trạng thái lỗi

```bash
dataguard validate --offline
```

Không coi mọi mã khác 0 là finding mới: config sai, bằng chứng thiếu và lỗi thực thi có kết quả riêng. Xem [exit code theo lệnh](../USAGE.md).

### 15. Chạy Assessment Định Kỳ

```yaml
on:
  schedule:
    - cron: '0 6 * * 1'  # Thứ 2 hàng tuần 6h sáng
jobs:
  assess:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dataguard version
      - run: dataguard assess --format sarif --output assess.sarif
```

**Tại sao:** Assessment báo finding dependency/framework/config được hỗ trợ, không bảo đảm phát hiện mọi lỗ hổng hay secret. Cần cung cấp CLI trước khi workflow fragment chạy.

## Bảo Mật

### 16. Dùng Secret Managers

```yaml
# Production: dùng Key Vault / Secrets Manager
KeyVaultUri: "https://my-vault.vault.azure.net/"
# hoặc
AwsRegion: "us-east-1"
```

### 17. Rotate Credentials Định Kỳ

```yaml
EnableCredentialRotationDetection: true
CredentialRotationWarningDays: 30
```

### 18. Xem Xét Audit Logs

```bash
# Kiểm tra các lần truy cập thất bại
cat audit-log.jsonl | jq 'select(.Success == false)'
```

## Hiệu Suất

### 19. Tinh Chỉnh Song Song

```yaml
# Cho codebase lớn
MaxDegreeOfParallelism: 4  # Giới hạn để tránh quá tải DB
MaxViolationQueueSize: 50000  # Giảm cho CI thiếu bộ nhớ
```

### 20. Đo hiệu năng snapshot

Snapshot tránh round-trip database live nhưng chưa có mức tăng tốc cố định được chứng minh ở đây. So sánh cùng corpus source/snapshot, rule, phần cứng và điều kiện process nóng/lạnh; ghi thời gian, bộ nhớ và số contract chưa đánh giá.

## Anti-Patterns

| ❌ Đừng | ✅ Nên làm thay thế |
|---------|-------------------|
| Hardcode connection strings | Dùng biến môi trường hoặc secret managers |
| Bỏ qua baseline cho code cũ | Baseline trước, ép buộc từng bước |
| Bỏ qua finding/contract chưa đánh giá | Review rule và bằng chứng hiện có |
| Chạy Full mode trong CI | Dùng Snapshot mode |
| Tắt audit logging | Giữ bật, rotate logs |
| Dùng `[SkipContractCheck]` everywhere | Sửa mismatch contract thực tế |
