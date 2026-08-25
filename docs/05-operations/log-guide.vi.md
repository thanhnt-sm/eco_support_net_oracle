# Hướng Dẫn Đọc Log

## Nguồn Log

### 1. Console Output (Mặc định)

DataGuard xuất kết quả validation ra stdout theo mặc định.

```bash
# Output chuẩn
dataguard validate

# Output chi tiết (bao gồm timing, thông tin provider)
dataguard validate --verbose
```

### 2. SARIF Output

Định dạng máy đọc được cho tích hợp CI.

```bash
dataguard validate --format sarif --output results.sarif
```

Cấu trúc SARIF:
```json
{
  "version": "2.1.0",
  "runs": [{
    "tool": { "driver": { "name": "DataGuard", "version": "0.1.0" } },
    "results": [{
      "ruleId": "DG001",
      "level": "error",
      "message": { "text": "Số lượng tham số không khớp..." },
      "locations": [{ "physicalLocation": { "artifactLocation": { "uri": "..." } } }]
    }]
  }]
}
```

### 3. Evidence Artifact

Evidence đã version, đã redact cho CI.

```bash
dataguard validate --format evidence --output evidence.json
```

### 4. Audit Log

Luồng audit bảo mật với chuỗi hash.

```jsonl
{"timestamp":"2026-01-15T10:30:00Z","eventType":"DatabaseOperation","operation":"Validate","provider":"oracle","connectionStringHash":"abc123","details":"Full validation","success":true,"hash":"sha256:...","previousHash":"sha256:..."}
{"timestamp":"2026-01-15T10:30:05Z","eventType":"CredentialAccess","operation":"GetConnection","provider":"oracle","connectionStringHash":"abc123","hash":"sha256:...","previousHash":"sha256:..."}
```

## Hiểu Output

### Exit Codes

| Code | Ý nghĩa | Hành động |
|------|---------|-----------|
| `0` | Thành công — không violations, không drift | Không cần làm gì |
| `1` | Tìm thấy violations hoặc drift | Xem violations, sửa hoặc baseline |
| `2` | Lỗi cấu hình/cách dùng | Sửa args lệnh hoặc file config |

### Định Dạng Violation

```
DG001: Số lượng tham số không khớp cho 'GetCustomer': kỳ vọng 3, nhận 2
  at MyApp.Data.CustomerRepository.GetCustomer(int id) (CustomerRepository.cs:45)
```

Thành phần:
- **Rule ID**: `DG001` (xem Tham Chiếu Rules)
- **Message**: Mô tả dễ đọc
- **Location**: File và số dòng (khi có)

### Tham Chiếu Rule IDs

| ID | Rule | Mức độ |
|----|------|--------|
| DG001 | Khớp số lượng tham số | Error |
| DG002 | Khớp kiểu tham số | Error |
| DG003 | Khớp hướng tham số | Error |
| DG004 | Khớp hình dạng cột kết quả | Error |
| DG005 | Khớp nullability | Warning |
| DG006 | Quy ước đặt tên | Warning |
| DG007 | Độ dài vượt cột (Oracle) | Error |
| DG008 | Tràn byte-length (Oracle) | Error |
| DG009 | Fallback size suy luận (Oracle) | Warning |
| DG010 | Cú pháp Oracle trong ngữ cảnh không phải Oracle | Warning |
| DG011 | Hàm không phải Oracle trong ngữ cảnh Oracle | Warning |
| DG012 | Tùy chọn provider không khớp | Warning |
| DG013 | Rò rỉ cú pháp SQL Server trong Oracle | Error |
| DG014 | Kiểu chưa ánh xạ trong Raw SQL | Warning |
| DG015 | Bảng ma (AI hallucination) | Error |
| DG016 | Cột ma (AI hallucination) | Error |
| MY001 | Kiểm tra cú pháp MySQL | Warning |
| MY002 | Kiểm tra độ dài MySQL | Warning |
| MY003 | Kiểm tra kiểu MySQL | Warning |
| PG001 | Kiểm tra cú pháp PostgreSQL | Warning |
| PG002 | Kiểm tra độ dài PostgreSQL | Warning |
| PG003 | Kiểm tra kiểu PostgreSQL | Warning |

## Mẫu Phân Tích Log

### Tìm Tất Cả Lỗi

```bash
dataguard validate 2>&1 | grep "error"
```

### Đếm Violations Theo Rule

```bash
dataguard validate --format sarif --output /dev/stdout | \
  jq '.runs[0].results | group_by(.ruleId) | map({rule: .[0].ruleId, count: length})'
```

### Tìm Vấn Đề Oracle

```bash
dataguard oracle-check --verbose 2>&1 | grep "DG0[0-9][0-9]"
```

### Kiểm Tra Tính Toàn Vẹn Audit Log

```bash
cat audit-log.jsonl | jq -r '.hash' | head -20
```

### Theo Dõi Thời Gian Validation

```bash
time dataguard validate --verbose 2>&1 | grep "Duration"
```

## Tích Hợp Log CI

### GitHub Actions

```yaml
- name: Validate contracts
  run: dataguard validate --format sarif --output results.sarif
  continue-on-error: true

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```

### Azure DevOps

```yaml
- script: dataguard validate --format sarif --output $(Build.SourcesDirectory)/results.sarif
  displayName: 'Validate contracts'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: '$(Build.SourcesDirectory)/results.sarif'
    artifactName: 'sarif-results'
```
