# Best Practices

## Cài Đặt & Thiết Lập

### 1. Dùng Chế Độ Snapshot Cho CI

```yaml
# .dataguard.yml cho CI
groundTruthMode: Snapshot
snapshotFilePath: ".dataguard-snapshot.json"
```

**Tại sao:** Không cần credentials database trong CI. Snapshot được commit vào repo, validate offline.

### 2. Commit Tất Cả File DataGuard

```bash
git add .dataguard.yml .dataguard-snapshot.json .dataguard-baseline.json
```

**Tại sao:** Nhóm chia sẻ cùng baseline validation. Phát hiện drift hoạt động so với snapshot đã commit.

### 3. Dùng Global Tool, Không Dùng Project Reference

```bash
dotnet tool install -g DataGuard.Cli
```

**Tại sao:** Phiên bản nhất quán across tất cả projects. Không phình to dependencies trong project files.

## Cấu Hình

### 4. Không Bao Giờ Hardcode Connection Strings

```yaml
# ❌ Sai
connectionString: "Server=prod-db;Password=secret123"

# ✅ Đúng - dùng biến môi trường
connectionString: null  # Đặt biến môi trường CONNECTION_STRING
```

### 5. Bật Audit Logging Trong Production

```yaml
enableAuditLogging: true
auditLogPath: "/var/log/dataguard/audit.jsonl"
```

**Tại sao:** Chuỗi audit hash-chain cho compliance và điều tra sự cố.

### 6. Tắt Fallback Plaintext

```yaml
allowPlaintextConfigFallback: false  # Mặc định, không bao giờ đổi trong production
```

**Tại sao:** Ngăn chặn downgrade credentials thầm lặng. Chỉ bật trong development local.

## Quy Trình Validation

### 7. Baseline Trước Khi Ép Buộc

```bash
# Đầu tiên: tạo baseline cho violations hiện có
dataguard baseline --connection "..." --provider oracle

# Sau đó: CI chỉ fail trên violations MỚI
dataguard validate
```

**Tại sao:** Codebase cũ có violations hiện có. Baseline cho phép ép buộc từng bước.

### 8. Dùng `--fail-on-drift` Trong CI

```bash
dataguard snapshot diff --fail-on-drift
```

**Tại sao:** Bắt thay đổi schema phá vỡ contracts trước khi chúng đến production.

### 9. Chạy Oracle Check Riêng Biệt

```bash
dataguard oracle-check --format sarif --output oracle-results.sarif
```

**Tại sao:** Rules Oracle (semantics CHAR/BYTE, kiểm tra dialect) tách biệt khỏi validation chung.

## Thực Hành Code

### 10. Dùng `[SkipContractCheck]` Ít

```csharp
[SkipContractCheck]  // Chỉ cho SQL thực sự động
public IQueryable<T> DynamicQuery<T>(string whereClause) { ... }
```

**Tại sao:** Mỗi skip là một điểm mù. Ghi lý do trong attribute hoặc comment.

### 11. Ưu Tiên Attributes Thủ Công Cho Code Mới

```csharp
[ExpectedColumn("CUSTOMER_ID", "int", IsNullable = false)]
public int CustomerId { get; set; }
```

**Tại sao:** Không cần truy cập database. Bắt lỗi mismatch tại compile time qua Roslyn analyzer.

### 12. Giữ Snapshot Tươi Mới

```bash
# Sau mỗi thay đổi schema
dataguard snapshot refresh --connection "..." --provider oracle
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

### 14. Chỉ Fail CI Trên Violations Mới

```bash
# Baseline lọc ra violations đã biết
dataguard validate  # Exit 1 chỉ cho violations mới
```

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
      - run: dotnet tool install -g DataGuard.Cli
      - run: dataguard assess --format sarif --output assess.sarif
```

**Tại sao:** Bắt dependency rot, TFM không được hỗ trợ, và rò rỉ secret trước khi trở thành sự cố.

## Bảo Mật

### 16. Dùng Secret Managers

```yaml
# Production: dùng Key Vault / Secrets Manager
keyVaultUri: "https://my-vault.vault.azure.net/"
# hoặc
awsRegion: "us-east-1"
```

### 17. Rotate Credentials Định Kỳ

```yaml
enableCredentialRotationDetection: true
credentialRotationWarningDays: 30
```

### 18. Xem Xét Audit Logs

```bash
# Kiểm tra các lần truy cập thất bại
cat audit-log.jsonl | jq 'select(.success == false)'
```

## Hiệu Suất

### 19. Tinh Chỉnh Song Song

```yaml
# Cho codebase lớn
maxDegreeOfParallelism: 4  # Giới hạn để tránh quá tải DB
maxViolationQueueSize: 50000  # Giảm cho CI thiếu bộ nhớ
```

### 20. Dùng Chế Độ Snapshot Cho Tốc Độ

Chế độ Snapshot nhanh hơn 10-100x so với Full mode (không có round-trip DB).

## Anti-Patterns

| ❌ Đừng | ✅ Nên làm thay thế |
|---------|-------------------|
| Hardcode connection strings | Dùng biến môi trường hoặc secret managers |
| Bỏ qua baseline cho code cũ | Baseline trước, ép buộc từng bước |
| Bỏ qua cảnh báo DG006 | Sửa quy ước đặt tên sớm |
| Chạy Full mode trong CI | Dùng Snapshot mode |
| Tắt audit logging | Giữ bật, rotate logs |
| Dùng `[SkipContractCheck]` everywhere | Sửa mismatch contract thực tế |
