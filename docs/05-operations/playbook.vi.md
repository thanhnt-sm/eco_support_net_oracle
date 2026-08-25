# Sổ Tay Vận Hành

## Vận Hành Hàng Ngày

### Kiểm Tra Sáng (CI Pipeline)

```bash
# Kiểm tra trạng thái CI mới nhất
gh run list --limit 5

# Nếu validation thất bại, xem chi tiết
gh run view <run-id> --log-failed
```

### Quy Trình Thay Đổi Schema

```mermaid
flowchart TD
    A["DBA áp dụng thay đổi schema"] B["Developer pull code mới"]
    B --> C["dataguard snapshot diff --fail-on-drift"]
    C --> D{"Phát hiện drift?"}
    D -->|Có| E["Xem xét thay đổi"]
    E --> F["dataguard snapshot refresh"]
    F --> G["dataguard validate"]
    G --> H{"Có violations mới?"}
    H -->|Có| I["Sửa code hoặc cập nhật baseline"]
    H -->|Không| J["✅ Sẵn sàng commit"]
    I --> G
    D -->|Không| J
```

### Quản Lý Baseline

```bash
# Tạo baseline cho codebase cũ
dataguard baseline --connection "..." --provider oracle

# Kiểm tra có bao nhiêu violations đã baseline
cat .dataguard-baseline.json | jq '.violations | length'

# Baseline lại sau khi sửa violations
dataguard baseline --connection "..." --provider oracle

# Di chuyển baseline v1 sang v2
dataguard migrate --baseline .dataguard-baseline.json
```

### Quản Lý Snapshot

```bash
# Làm mới snapshot từ database
dataguard snapshot refresh --connection "..." --provider oracle

# Hiển thị thông tin snapshot
dataguard snapshot show

# Kiểm tra drift
dataguard snapshot diff --connection "..." --provider oracle --fail-on-drift
```

## Quy Trình Nhóm

### Onboard Project Mới

```bash
# 1. Cài DataGuard
dotnet tool install -g DataGuard.Cli

# 2. Khởi tạo cấu hình
dataguard init --provider oracle

# 3. Tạo snapshot ban đầu
dataguard snapshot refresh --connection "..." --provider oracle

# 4. Chạy validation lần đầu
dataguard validate --verbose

# 5. Tạo baseline cho violations hiện có
dataguard baseline --connection "..." --provider oracle

# 6. Commit các file cấu hình
git add .dataguard.yml .dataguard-snapshot.json .dataguard-baseline.json
git commit -m "chore: khởi tạo DataGuard contract validation"
```

### Checklist Review PR

- [ ] `dataguard validate` pass (exit code 0)
- [ ] Không có violations mới ngoài baseline
- [ ] Snapshot cập nhật nếu schema thay đổi
- [ ] Entity mới có attributes `[ExpectedColumn]` (chế độ Manual)
- [ ] SP call mới có attributes `[ExpectedSpParameter]`

### Quy Trình Release

```bash
# 1. Validation đầy đủ
dataguard validate --format sarif --output release-validation.sarif

# 2. Kiểm tra drift snapshot
dataguard snapshot diff --fail-on-drift

# 3. Assessment
dataguard assess --format json --output assessment.json

# 4. Commit artifacts
git add release-validation.sarif assessment.json
git commit -m "chore: release validation artifacts"
```

## Cấu Hình Theo Môi Trường

### Development

```yaml
groundTruthMode: Manual
allowPlaintextConfigFallback: true
enableAuditLogging: false
```

### Staging

```yaml
groundTruthMode: Full
connectionString: null  # Dùng biến môi trường CONNECTION_STRING
enableAuditLogging: true
auditLogPath: "/var/log/dataguard/audit.jsonl"
```

### Production CI

```yaml
groundTruthMode: Snapshot
snapshotFilePath: ".dataguard-snapshot.json"
enableAuditLogging: true
enableCredentialRotationDetection: true
```

## Giám Sát

### Chỉ Số Chính

| Chỉ số | Nguồn | Ngưỡng cảnh báo |
|--------|-------|-----------------|
| Thời gian validation | TelemetryCollector | > 60s |
| Số violations | Validation output | Violations mới ngoài baseline |
| Schema drift | snapshot diff | Bất kỳ drift nào |
| Rotation credentials | CredentialManager | < 30 ngày còn lại |
| Tính toàn vẹn audit log | FileAuditLogger | Chuỗi bị đứt |

### Kiểm Tra Sức Khỏe

```bash
# Xác minh cài đặt DataGuard
dataguard version

# Xác minh cấu hình
dataguard config validate

# Xác minh kết nối database
dataguard validate --verbose 2>&1 | head -20
```
