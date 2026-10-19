# Sổ Tay Vận Hành

Các ví dụ dưới đây giả định CLI đã được build/cài từ artifact chính xác đã kiểm tra và có trong PATH; `dataguard version` chỉ kiểm tra khả năng gọi lệnh, không cài tool. Xem [hướng dẫn cài đặt](installation-guide.vi.md). Không giả định package NuGet công khai tồn tại.

Các ví dụ live dùng `DATAGUARD_DB` do cơ chế secret session/CI được duyệt cung cấp. Không ghi giá trị thật vào đối số. Đối chiếu source tại `ca68a3159425495f771da866188df75a8237ab4b`: YAML dùng PascalCase theo `ConfigDocument`; test runtime còn cần kiểm tra database/schema cụ thể, backend audit/secret và CI upload trên host đích.

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
    F --> G["dataguard validate --offline"]
    G --> H{"Có violations mới?"}
    H -->|Có| I["Sửa code hoặc cập nhật baseline"]
    H -->|Không| J["✅ Sẵn sàng commit"]
    I --> G
    D -->|Không| J
```

### Quản Lý Baseline

```bash
# Tạo baseline cho codebase cũ
dataguard baseline --connection-env DATAGUARD_DB --provider oracle

# Kiểm tra có bao nhiêu violations đã baseline
cat .dataguard-baseline.json | jq '.Violations | length'

# Baseline lại sau khi sửa violations
dataguard baseline --connection-env DATAGUARD_DB --provider oracle

# Di chuyển baseline v1 sang v2
dataguard migrate --baseline .dataguard-baseline.json
```

### Quản Lý Snapshot

```bash
# Làm mới snapshot từ database
dataguard snapshot refresh --connection-env DATAGUARD_DB --provider oracle

# Hiển thị thông tin snapshot
dataguard snapshot show

# Kiểm tra drift
dataguard snapshot diff --connection-env DATAGUARD_DB --provider oracle --fail-on-drift
```

## Quy Trình Nhóm

### Onboard Project Mới

```bash
# 1. Kiểm tra CLI đã được cung cấp
dataguard version

# 2. Khởi tạo cấu hình
dataguard init --provider oracle

# 3. Tạo snapshot ban đầu
dataguard snapshot refresh --connection-env DATAGUARD_DB --provider oracle

# 4. Chạy validation lần đầu
dataguard validate --offline --verbose

# 5. Tạo baseline cho violations hiện có
dataguard baseline --connection-env DATAGUARD_DB --provider oracle

# 6. Commit các file cấu hình
# Review metadata và quyền chia sẻ trước khi commit; không commit tự động.
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
# Lưu report đã duyệt tại nơi kiểm soát truy cập.
```

## Cấu Hình Theo Môi Trường

### Development

```yaml
GroundTruthMode: Manual
AllowPlaintextConfigFallback: true
EnableAuditLogging: false
```

### Staging

```yaml
GroundTruthMode: Full
ConnectionString: null  # Dùng biến môi trường DATAGUARD_CONNECTION_STRING
EnableAuditLogging: true
AuditLogPath: "/var/log/dataguard/audit.jsonl"
```

### Production CI

```yaml
GroundTruthMode: Snapshot
SnapshotFilePath: ".dataguard-snapshot.json"
EnableAuditLogging: true
EnableCredentialRotationDetection: true
```

## Giám Sát

### Kiểm tra giám sát đề xuất

| Chỉ số | Nguồn | Ngưỡng cảnh báo |
|--------|-------|-----------------|
| Thời gian validation | TelemetryCollector | Nhóm định nghĩa sau khi đo |
| Số violations | Validation output | Violations mới ngoài baseline |
| Schema drift | snapshot diff | Bất kỳ drift nào |
| Rotation credentials | CredentialManager | Review metadata credential hiện có |
| Tính toàn vẹn audit log | FileAuditLogger | Chuỗi bị đứt |

Đây là đề xuất, không phải alert đã cấu hình. Cờ rotation không chứng minh credential có ngày hết hạn từ issuer.

### Kiểm Tra Sức Khỏe

```bash
# Xác minh cài đặt DataGuard
dataguard version

# Xác minh cấu hình
dataguard config validate

# Xác minh kết nối database
dataguard validate --verbose --connection-env DATAGUARD_DB
```
