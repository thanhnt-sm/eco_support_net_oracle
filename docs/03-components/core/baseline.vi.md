# Quản Lý Baseline

> Nguồn: `src/DataGuard.Core/Baseline/BaselineManager.cs`

Quản lý baseline cho phép DataGuard làm việc với codebase legacy đã có các violations đã biết. Thay vì thất bại trên mọi vấn đề hiện có, baseline chụp trạng thái hiện tại và chỉ báo cáo các violations **mới** (drift).

## Vòng Đời Baseline

```mermaid
stateDiagram-v2
    [*] --> NoBaseline: Trạng thái ban đầu
    NoBaseline --> Creating: dataguard baseline create
    Creating --> V2Active: Baseline đã tạo (v2)

    V2Active --> Validating: dataguard validate
    Validating --> V2Active: Chỉ lọc violations mới

    V2Active --> DriftDetected: Hash schema thay đổi
    DriftDetected --> V2Active: dataguard baseline update

    V2Active --> Migrating: Phát hiện legacy v1
    Migrating --> V2Active: Tự động migrate lên v2

    V2Active --> [*]: dataguard baseline delete
```

## BaselineManager

Lớp cốt lõi để tạo, tải, kiểm tra, và migrate baseline files.

```csharp
public class BaselineManager
{
    private readonly string _baselineFilePath;

    public BaselineManager(string baselineFilePath) { ... }

    public async Task<BaselineFile> CreateBaselineAsync(
        IEnumerable<ContractViolation> violations,
        string schemaVersion,
        string groundTruthMode,
        string? databaseVersion = null,
        string? schemaHash = null,
        IReadOnlyList<SnapshotTable>? schema = null,
        CancellationToken cancellationToken = default) { ... }

    public async Task<BaselineFile?> LoadAsync(CancellationToken ct = default) { ... }

    public IEnumerable<ContractViolation> FilterNewViolations(
        IEnumerable<ContractViolation> current,
        BaselineFile baseline) { ... }
}
```

## Định Dạng BaselineFile v2

Định dạng baseline hiện tại (version 2) thêm theo dõi phiên bản database và schema hash để phát hiện drift.

```json
{
  "version": 2,
  "createdAt": "2026-08-25T10:30:00Z",
  "schemaVersion": "1.0",
  "groundTruthMode": "Snapshot",
  "databaseVersion": "19c",
  "schemaHash": "A1B2C3D4E5F67890",
  "violations": [
    {
      "ruleId": "DG002",
      "message": "Parameter 'P_ID' has CLR type 'int' but database type 'VARCHAR2' is not compatible",
      "severity": "Error",
      "location": {
        "filePath": "src/Models/Order.cs",
        "startLine": 42,
        "startColumn": 8,
        "endLine": 42,
        "endColumn": 30
      },
      "properties": null
    }
  ],
  "schema": [
    {
      "name": "ORDERS",
      "columns": [
        {
          "name": "ORDER_ID",
          "dataType": "NUMBER",
          "maxLength": null,
          "precision": 10,
          "scale": 0,
          "isNullable": false,
          "charUsed": null
        }
      ]
    }
  ]
}
```

### Tham Chiếu Trường

| Trường | Kiểu | Mô tả |
|--------|------|-------|
| `version` | `int` | Phiên bản định dạng (luôn 2) |
| `createdAt` | `DateTimeOffset` | Thời gian tạo UTC |
| `schemaVersion` | `string` | Phiên bản schema do người dùng định nghĩa |
| `groundTruthMode` | `string` | `"Full"`, `"Snapshot"`, hoặc `"Manual"` |
| `databaseVersion` | `string` | Phiên bản database (vd: `"19c"`, `"2022"`) |
| `schemaHash` | `string` | Hash SHA256 để phát hiện drift |
| `violations` | `BaselineViolation[]` | Các violations đã biết tại thời điểm baseline |
| `schema` | `SnapshotTable[]?` | Snapshot schema offline tùy chọn |

## SnapshotTable / SnapshotColumn

Schema ground-truth có thể serialize cho kiểm tra offline (không cần kết nối database).

```csharp
public record SnapshotTable(
    string Name,
    IReadOnlyList<SnapshotColumn> Columns);

public record SnapshotColumn(
    string Name,
    string DataType,
    int? MaxLength,
    int? CharLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    string? CharUsed);
```

Khi baseline bao gồm `schema`, DataGuard có thể kiểm tra với snapshot mà không cần kết nối database — hữu ích cho CI/CD pipeline không có quyền truy cập database.

## Tính Toán Schema Hash

Hai chiến lược tính toán hash:

### Hash Dựa Trên Violation (Legacy)

```csharp
public static string ComputeSchemaHash(IEnumerable<ContractViolation> violations)
{
    var data = string.Join("|", violations
        .OrderBy(v => v.RuleId).ThenBy(v => v.Message)
        .Select(v => $"{v.RuleId}:{v.Message}"));
    return SHA256(data)[..16]; // Tiền tố 16-hex
}
```

### Hash Dựa Trên Schema (Đầy Đủ)

```csharp
public static string ComputeSchemaHash(IReadOnlyList<SnapshotTable> schema)
{
    var canonical = string.Join("|", schema
        .OrderBy(t => t.Name)
        .Select(t => $"{t.Name}{{{columns_canonicalized}}}"));
    return SHA256(canonical); // Đầy đủ 64-hex
}
```

Hash dựa trên schema phát hiện thay đổi ngay cả khi chúng không tạo violations (vd: thêm cột nullable).

## Phát Hiện Drift

```mermaid
flowchart LR
    subgraph Baseline
        BH[Baseline Hash]
        BV[Baseline Violations]
    end

    subgraph Current
        CH[Current Hash]
        CV[Current Violations]
    end

    BH --> COMP{So sánh}
    CH --> COMP
    COMP --> |hash khớp| OK[Không drift]
    COMP --> |hash khác| DRIFT[Phát hiện drift]

    BV --> FILTER{Lọc}
    CV --> FILTER
    FILTER --> NEW[Chỉ violations mới]
```

**Quy trình phát hiện drift:**
1. Tải baseline file
2. Tính hash schema hiện tại
3. So sánh với `SchemaHash` baseline
4. Nếu khác → phát hiện drift
5. Lọc violations hiện tại với chữ ký baseline (`RuleId:Message`)
6. Chỉ báo cáo violations mới

## Migration Legacy v1 → v2

Tự động migrate từ định dạng baseline legacy:

```csharp
private static BaselineFile MigrateFromLegacy(LegacyBaselineFile legacy)
{
    return new BaselineFile(
        Version: 2,
        CreatedAt: legacy.CreatedAt,
        SchemaVersion: legacy.SchemaVersion,
        GroundTruthMode: legacy.GroundTruthMode,
        DatabaseVersion: "unknown",
        SchemaHash: ComputeSchemaHashFromLegacy(legacy),
        Violations: legacy.Violations);
}
```

**Quy trình migration:**
1. `LoadAsync()` thử deserialize v2
2. Khi `JsonException`, fallback về định dạng v1
3. `MigrateFromLegacy()` chuyển đổi v1 → v2
4. `MigrateBaselineAsync()` thực hiện migration tại chỗ và lưu

### Định Dạng Legacy v1

```csharp
internal record LegacyBaselineFile(
    int Version,           // 1
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    IReadOnlyList<BaselineViolation> Violations);
```

Các trường thiếu trong v1: `DatabaseVersion`, `SchemaHash`, `Schema`.

## Tối Ưu Hiệu Suất

### Memory-Mapped Files

Cho baseline files > 1MB, sử dụng `MemoryMappedFile` cho I/O hiệu quả:

```csharp
private async Task<BaselineFile?> LoadWithMemoryMappedFileAsync(CancellationToken ct)
{
    using var mmf = MemoryMappedFile.CreateFromFile(_baselineFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
    using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    // Đọc trực tiếp từ vùng memory-mapped
}
```

### Bộ Nhớ Đệm Schema Hash

Sử dụng `MemoryCache` cho kết quả tính toán schema hash:

```csharp
private static readonly MemoryCache _schemaHashCache = new MemoryCache(new MemoryCacheOptions
{
    SizeLimit = 10000,
    ExpirationScanFrequency = TimeSpan.FromMinutes(5),
});
```

### Ghi Nguyên Tử

Baseline files lớn sử dụng mẫu ghi nguyên tử:

```csharp
private async Task SaveWithMemoryMappedFileAsync(byte[] data)
{
    var tempPath = _baselineFilePath + ".tmp";
    await File.WriteAllBytesAsync(tempPath, data);
    File.Replace(tempPath, _baselineFilePath, null, false);
}
```

## Mẫu Sử Dụng

### CI/CD Pipeline

```bash
# Lần chạy đầu: tạo baseline
dataguard baseline create --schema-version 1.0

# Các lần sau: chỉ lỗi trên violations mới
dataguard validate --baseline .dataguard-baseline.json

# Sau thay đổi schema: cập nhật baseline
dataguard baseline update
```

### Sử Dụng Chương Trình

```csharp
var manager = new BaselineManager(".dataguard-baseline.json");
var baseline = await manager.LoadAsync();

if (baseline != null)
{
    var newViolations = manager.FilterNewViolations(currentViolations, baseline);
    if (newViolations.Any())
    {
        // Lỗi CI: phát hiện violations mới
    }
}
```
