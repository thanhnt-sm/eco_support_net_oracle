# Sổ Tay Xử Lý Sự Cố

## Sự Cố Thường Gặp

### INC-001: Validation Thất Bại Trong CI

**Triệu chứng:** CI pipeline thoát với code 1 ở `dataguard validate`

**Chẩn đoán:**
```bash
# Kiểm tra violations tìm thấy
dataguard validate --verbose 2>&1 | grep "DG[0-9]"

# So sánh với baseline
dataguard validate 2>&1 | diff - .dataguard-baseline.json
```

**Giải quyết:**
1. Nếu violations mới: sửa code hoặc cập nhật baseline
2. Nếu schema drift: `dataguard snapshot refresh`
3. Nếu false positive: thêm attribute `[SkipContractCheck]`

**Leo thang:** Nếu violations trong code bên thứ ba, thêm vào `excludedProcedures` trong config.

---

### INC-002: Kết Nối Database Thất Bại

**Triệu chứng:** `CredentialError` hoặc `ExtractionError` exit code

**Chẩn đoán:**
```bash
# Test kết nối trực tiếp
dataguard validate --verbose 2>&1 | grep -i "connection\|credential\|timeout"

# Kiểm tra resolve credentials
echo $CONNECTION_STRING
dataguard config show | grep -i connection
```

**Giải quyết:**
1. Xác minh biến môi trường `CONNECTION_STRING` đã đặt
2. Kiểm tra kết nối mạng đến database
3. Xác minh credentials chưa hết hạn
4. Với Oracle: kiểm tra TNS names / service name
5. Với SQL Server: kiểm tra instance name và port

---

### INC-003: Phát Hiện Drift Snapshot

**Triệu chứng:** `dataguard snapshot diff` báo cáo drift

**Chẩn đoán:**
```bash
# Xem những gì thay đổi
dataguard snapshot diff --verbose

# So sánh snapshot với hiện tại
dataguard snapshot show
```

**Giải quyết:**
1. Xem xét thay đổi drift với DBA
2. Nếu cố ý: `dataguard snapshot refresh`
3. Nếu không cố ý: cảnh báo DBA, điều tra thay đổi schema
4. Cập nhật baseline nếu có violations mới

---

### INC-004: Cảnh Báo Rotation Credentials

**Triệu chứng:** Cảnh báo về rotation credentials trong output

**Chẩn đoán:**
```bash
# Kiểm tra tuổi credentials
dataguard config show | grep -i rotation

# Kiểm tra audit log
cat audit-log.jsonl | grep "credential" | tail -5
```

**Giải quyết:**
1. Rotate credentials trong secret manager
2. Cập nhật biến môi trường `CONNECTION_STRING`
3. Xác minh credentials mới hoạt động: `dataguard validate --verbose`

---

### INC-005: Assessment Tìm Thấy Vấn Đề Nghiêm Trọng

**Triệu chứng:** `dataguard assess` báo cáo findings mức Critical

**Chẩn đoán:**
```bash
# Lấy báo cáo assessment đầy đủ
dataguard assess --format json --output assessment.json
cat assessment.json | jq '.findings[] | select(.severity == "Critical")'
```

**Giải quyết:**
1. Xem xét từng finding Critical
2. Với TFM không được hỗ trợ: lên kế hoạch nâng cấp
3. Với secrets trong config: chuyển sang secret manager
4. Với thiếu lock file: `dotnet restore --lock-file`

---

### INC-006: Analyzer Không Hoạt Động Trong IDE

**Triệu chứng:** Không có gạch chân squiggly cho SQL calls trong VS Code / VS 2022

**Chẩn đoán:**
```bash
# Kiểm tra package analyzer đã cài
dotnet list package | grep DataGuard.Analyzers

# Kiểm tra .csproj có tham chiếu analyzer
grep -r "DataGuard.Analyzers" *.csproj
```

**Giải quyết:**
1. Cài package analyzer: `dotnet add package DataGuard.Analyzers`
2. Khởi động lại IDE
3. Kiểm tra Output panel tìm lỗi analyzer
4. Xác minh project target netstandard2.0+ hoặc net6.0+

---

### INC-007: Docker Build Thất Bại

**Triệu chứng:** Build Docker image thất bại trong CI

**Chẩn đoán:**
```bash
# Build local
docker build -t dataguard:test .

# Kiểm tra log build
docker build -t dataguard:test . 2>&1 | tail -50
```

**Giải quyết:**
1. Xác minh phiên bản .NET SDK trong Dockerfile khớp project
2. Kiểm tra `packages.lock.json` đã commit
3. Đảm bảo `DataGuard.Cli.csproj` trong build context
4. Với multi-arch: đảm bảo QEMU đã cài cho arm64

---

### INC-008: Tính Toàn Vẹn Audit Log Thất Bại

**Triệu chứng:** Xác minh chuỗi hash audit log thất bại

**Chẩn đoán:**
```bash
# Kiểm tra audit log
cat audit-log.jsonl | jq '.hash' | head -20

# Xác minh chuỗi
dataguard validate --verbose 2>&1 | grep -i "audit\|hash\|integrity"
```

**Giải quyết:**
1. **NGHIÊM TRỌNG:** Audit log có thể đã bị giả mạo
2. Bảo toàn log hiện tại: `cp audit-log.jsonl audit-log.jsonl.bak`
3. Điều tra truy cập file audit log
4. Cân nhắc rotate sang audit log mới
5. Báo cáo sự cố bảo mật theo SECURITY.md

## Ma Trận Leo Thang

| Mức độ | Thời gian phản hồi | Hành động |
|--------|-------------------|-----------|
| Critical (INC-008) | Ngay lập tức | Phản ứng sự cố bảo mật |
| High (INC-001, INC-002) | 1 giờ | Chặn CI, sửa hoặc bypass |
| Medium (INC-003, INC-004) | 4 giờ | Lên lịch sửa |
| Low (INC-005, INC-006) | Sprint tiếp theo | Lên kế hoạch khắc phục |
