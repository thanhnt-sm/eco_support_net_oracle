# Hướng Dẫn Sử Dụng / Usage Guide / Hướng Dẫn Sử Dụng

## Cài Đặt Nhanh / Quick Start / Cài Đặt Nhanh

### 1. Cài Đặt Package / Install Packages

```bash
# Core (bắt buộc / required)
dotnet add package DataGuard.Core

# SQL Server (nếu dùng SQL Server)
dotnet add package DataGuard.SqlServer.Adapter

# Oracle (nếu dùng Oracle) - cần chấp nhận Oracle License
dotnet add package DataGuard.Oracle.Adapter

# Analyzers cho IDE (khuyên dùng / recommended)
dotnet add package DataGuard.Analyzers
```

### 2. Cài Đặt CLI Tool

```bash
# Cài global / Global install
dotnet tool install -g DataGuard.Cli

# Hoặc cài local cho project
dotnet tool install --local DataGuard.Cli
```

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

```yaml
# DataGuard Configuration
GroundTruthMode: Snapshot          # Snapshot | Full | Manual
EnableSmartDefaults: true          # Tự động phát hiện provider/EF/Dapper
EnableBaseline: true               # Bật baseline cho legacy
NamingConvention: SnakeCaseToPascalCase  # SnakeCaseToPascalCase | PascalCaseToSnakeCase | ExactMatch
DefaultProvider: SqlServer         # SqlServer | Oracle
SnapshotFilePath: .dataguard-snapshot.json
BaselineFilePath: .dataguard-baseline.json
EnableTelemetry: false             # Opt-in telemetry

# Oracle-specific (nếu dùng Oracle)
Oracle:
  Owner: MY_SCHEMA                 # Schema owner
  UseRefCursorDescribe: true       # Dùng DBMS_SQL.DESCRIBE_COLUMNS
  UseAllArguments: true            # Dùng ALL_ARGUMENTS
  UseAllTabColumns: true           # Dùng ALL_TAB_COLUMNS

# SQL Server-specific
SqlServer:
  Schema: dbo                      # Schema mặc định
  UseFirstResultSet: true          # Dùng sp_describe_first_result_set

# Bảo mật / Security
EnableCredentialRotationDetection: true
CredentialRotationWarningDays: 30
EncryptConnectionStringAtRest: false
KeyVaultUri: ""                    # Azure Key Vault URI (nếu có)
EnableAuditLogging: true
AuditLogPath: ""                   # Tùy chọn custom path
```

---

## Các Lệnh CLI / CLI Commands / Các Lệnh CLI

### 1. `dataguard validate` - Xác Thực Hợp Đồng

```bash
# Cơ bản
dataguard validate --connection "Server=...;Database=...;"

# Chế độ offline (nhanh, không cần DB)
dataguard validate --offline --format text

# Output SARIF cho GitHub Code Scanning
dataguard validate --connection "..." --format sarif --output results.sarif

# Chỉ định provider
dataguard validate --connection "..." --provider Oracle

# Verbose logging
dataguard validate --connection "..." --verbose
```

**Options / Tùy Chọn**:
| Option | Mô Tả | Mặc Định |
|--------|-------|----------|
| `--connection` | Connection string | Từ config/env |
| `--config` | Đường dẫn .dataguard.yml | `.dataguard.yml` |
| `--output` | File output SARIF/JSON | Stdout |
| `--format` | `sarif` \| `json` \| `text` | `sarif` |
| `--offline` | Chế độ offline (không DB) | `false` |
| `--verbose` | Log chi tiết | `false` |
| `--provider` | `SqlServer` \| `Oracle` | Từ config |
| `--schema` | Schema/owner name | Từ config |
| `--package` | Oracle package name | Từ config |

**Exit Codes**:
- `0` = Pass (không violation mới)
- `1` = Fail (có violation mới)

---

### 2. `dataguard baseline` - Tạo Baseline

```bash
# Tạo baseline từ validation hiện tại
dataguard baseline --connection "Oracle CI Schema" --output .dataguard-baseline.json

# Baseline sẽ chứa:
# - Version: 2
# - SchemaVersion: "1.0"
# - GroundTruthMode: "Snapshot"
# - DatabaseVersion: "Oracle Database 19c..."
# - SchemaHash: "a1b2c3d4e5f67890" (SHA256-64bit)
# - Violations: [] (tất cả violations hiện tại)
```

**Khi nào dùng / When to use**:
- ✅ **Bắt buộc** khi onboarding legacy codebase
- ✅ Chạy **một lần** trước khi bật CI gate
- ✅ Commit file `.dataguard-baseline.json` vào repo

---

### 3. `dataguard snapshot` - Quản Lý Snapshot

```bash
# Làm mới snapshot từ DB hiện tại
dataguard snapshot refresh --connection "CI Schema"

# Xem thông tin snapshot
dataguard snapshot show

# So sánh schema hiện tại vs snapshot
dataguard snapshot diff --connection "CI Schema"
```

**Snapshot vs Baseline**:
| | Snapshot | Baseline |
|---|---|---|
| **Mục đích** | Schema hiện tại (giống Jest snapshot) | Drift hiện tại (freeze) |
| **Cập nhật** | `snapshot refresh` (DBA approval) | Tự động khi schema thay đổi hợp lệ |
| **So sánh** | `snapshot diff` | Tự động trong `validate` |

---

### 4. `dataguard init` - Khởi Tạo

```bash
# Wizard tương tác (khuyên dùng)
dataguard init --wizard

# Tự động với provider
dataguard init --provider Oracle --output .dataguard.yml
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
dataguard oracle-check --connection "Oracle CI" --format sarif
```

**Kiểm tra**:
- DG010: Oracle syntax trong non-Oracle context
- DG011: SQL Server syntax trong Oracle context
- DG012: Missing `UseOracle()` registration
- DG013: SQL Server EXEC syntax trong Oracle
- DG014: Unmapped type usage (Oracle EF Core 8+)
- DG007/DG008/DG009: Length mismatch checks

---

### 6. `dataguard version` - Xem Phiên Bản

```bash
dataguard version
```

**Output**:
```
DataGuard CLI version 1.0.0
Runtime: 9.0.12
OS: Linux 6.8.0
DataGuard.Core: 1.0.0.0
DataGuard.Oracle.Adapter: 1.0.0.0
DataGuard.SqlServer.Adapter: 1.0.0.0
DataGuard.Analyzers: 1.0.0.0
```

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

**Generated Hook Content**:
```bash
#!/bin/sh
# DataGuard pre-commit hook
echo "🔍 Running DataGuard pre-commit validation..."

if command -v dataguard &> /dev/null; then
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

### 1. Legacy Codebase Onboarding (Onboarding Dự Án Cũ)

```bash
# 1. Cài đặt packages
dotnet add package DataGuard.Core
dotnet add package DataGuard.Oracle.Adapter  # nếu Oracle
dotnet tool install -g DataGuard.Cli

# 2. Interactive setup
dataguard init --wizard

# 3. Tạo baseline (MỘT LẦN - bắt buộc)
dataguard baseline --connection "Oracle CI Schema"

# 4. Commit baseline
git add .dataguard-baseline.json .dataguard.yml
git commit -m "chore: add DataGuard baseline for legacy drift"

# 4. CI Pipeline (thêm vào .github/workflows/ci.yml)
# - name: DataGuard Validate
#   run: dataguard validate --connection ${{ secrets.ORACLE_CI }} --format sarif --output dataguard.sarif
# - name: Upload SARIF
#   uses: github/codeql-action/upload-sarif@v3
#   with: { sarif_file: dataguard.sarif }

# 5. Pre-commit hook (local dev)
dataguard hook install
```

### 2. Greenfield Project (Dự Án Mới)

```bash
# 1. Cài đặt
dotnet add package DataGuard.Core
dotnet add package DataGuard.Analyzers
dotnet add package DataGuard.SqlServer.Adapter  # hoặc Oracle.Adapter

# 2. Analyzer tự động hoạt động trong IDE
# - DG001: "Unvalidated SQL call" warning trên FromSqlRaw
# - Click lightbulb → "Add [SkipContractCheck]" hoặc "Add expected params"

# 3. Pre-commit hook
dataguard hook install

# 4. CI Gate
# GitHub Actions: dataguard validate --connection "${{ secrets.SQL_CONNECTION }}" --format sarif
```

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
<PackageReference Include="DataGuard.Analyzers" Version="1.0.0" />
```

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

---

## CI/CD Integration / Tích Hợp CI/CD

### GitHub Actions

```yaml
# .github/workflows/dataguard.yml
name: DataGuard Validation

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]

jobs:
  dataguard-validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      
      - name: Restore
        run: dotnet restore
      
      - name: Install DataGuard CLI
        run: dotnet tool install -g DataGuard.Cli
      
      - name: DataGuard Validate
        run: |
          dataguard validate \
            --connection "${{ secrets.ORACLE_CONNECTION }}" \
            --format sarif \
            --output dataguard.sarif
        env:
          ORACLE_CONNECTION: ${{ secrets.ORACLE_CONNECTION }}
      
      - name: Upload SARIF to GitHub Code Scanning
        uses: github/codeql-action/upload-sarif@v3
        with:
          sarif_file: dataguard.sarif
          category: dataguard
```

### Azure DevOps

```yaml
# azure-pipelines.yml
trigger:
- main

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '9.0.x'

- script: dotnet tool install -g DataGuard.Cli
  displayName: 'Install DataGuard CLI'

- script: |
    dataguard validate \
      --connection "$(ORACLE_CONNECTION)" \
      --format sarif \
      --output $(Build.ArtifactStagingDirectory)/dataguard.sarif
  displayName: 'DataGuard Validate'
  env:
    ORACLE_CONNECTION: $(ORACLE_CONNECTION)

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: 'dataguard.sarif'
    artifactName: 'DataGuard-SARIF'
```

### GitLab CI

```yaml
# .gitlab-ci.yml
stages:
  - validate

dataguard_validate:
  stage: validate
  image: mcr.microsoft.com/dotnet/sdk:9.0
  before_script:
    - dotnet tool install -g DataGuard.Cli
  script:
    - dataguard validate --connection "$ORACLE_CONNECTION" --format sarif --output dataguard.sarif
  artifacts:
    reports:
      sast: dataguard.sarif
    when: always
```

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
dataguard baseline --connection "CI"

# Khi DBA thay đổi schema hợp lệ
dataguard snapshot refresh --connection "CI"
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
| `DATAGUARD_CONNECTION_STRING` | Connection string chính | `Server=...;Database=...` |
| `DATAGUARD_PROVIDER` | Provider mặc định | `Oracle` / `SqlServer` |
| `DATAGUARD_CONFIG` | Config file path | `.dataguard.yml` |
| `DATAGUARD_BASELINE_PATH` | Baseline file path | `.dataguard-baseline.json` |
| `DATAGUARD_SNAPSHOT_PATH` | Snapshot file path | `.dataguard-snapshot.json` |
| `DATAGUARD_KEY_VAULT_URI` | Azure Key Vault URI | `https://myvault.vault.azure.net` |

---

## Migration Guide / Hướng Dẫn Nâng Cấp

### Từ v0.x → v1.0

```bash
# 1. Update packages
dotnet add package DataGuard.Core --version 1.0.0
dotnet add package DataGuard.Oracle.Adapter --version 1.0.0

# 2. Regenerate config (nếu có breaking changes)
dataguard init --wizard

# 3. Recreate baseline (schema hash format changed)
dataguard baseline --connection "CI"

# 4. Update CI pipeline (nếu format SARIF thay đổi)
# Kiểm tra SARIF 2.1.0 compatibility
```

---

## FAQ / Câu Hỏi Thường Gặp

**Q: DataGuard có thay thế EF Core Power Tools không?**
A: Không. EFCorePowerTools scaffold code. DataGuard **validate** hợp đồng tại build/CI. Cùng dùng được.

**Q: Có hỗ trợ PostgreSQL/MySQL không?**
A: Chưa. Roadmap v1.1. Hiện tại: SQL Server + Oracle.

**Q: Có cần DB connection trong IDE không?**
A: Không. IDE layer chỉ syntax-only (~ms). CI layer mới cần DB.

**Q: Baseline file có an toàn để commit không?**
A: Có. Chỉ chứa violation metadata + hash. Không chứa connection string/data nhạy cảm.

**Q: Có thể custom rules không?**
A: Có. Tạo class implement `IContractRule`, build thành DLL, drop vào `~/.dataguard/plugins/` hoặc config plugin directory.

---

## Links Hữu Ích / Useful Links

- **GitHub**: https://github.com/DataGuard/DataGuard
- **NuGet**: https://www.nuget.org/packages/DataGuard.Core
- **Documentation**: https://dataguard.github.io/docs
- **Issues**: https://github.com/DataGuard/DataGuard/issues
- **Discussions**: https://github.com/DataGuard/DataGuard/discussions
- **Changelog**: https://github.com/DataGuard/DataGuard/blob/main/CHANGELOG.md

---

*Phiên bản: 1.0.0 | Cập nhật: 2025-01-19*