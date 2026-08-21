# Sản Phẩm DataGuard / DataGuard Product / Sản Phẩm DataGuard

## Tổng Quan Sản Phẩm / Product Overview / Tổng Quan Sản Phẩm

**DataGuard** là công cụ xác thực hợp đồng (contract validation) **Entity ↔ Stored Procedure/Raw SQL** được phân phối qua NuGet, dành cho các .NET developer sử dụng EF Core/Dapper với Oracle và SQL Server.

**Vấn đề giải quyết / Problem Solved**: Microsoft đã xác nhận gap này từ 2014 (issue #245) - EF Core không kiểm tra hợp đồng giữa Entity và Stored Procedure/Raw SQL tại design-time, dẫn đến lỗi runtime như ORA-12899 "value too large for column".

---

## Giá Trị Cốt Lõi / Core Value Proposition / Giá Trị Cốt Lõi

| Pain Point / Nỗi Đau | Giải Pháp DataGuard / DataGuard Solution |
|---|---|
| **Lỗi runtime ORA-12899** | Phát hiện length mismatch tại design-time / Detect length mismatch at design-time |
| **Parameter mismatch SP ↔ Entity** | Validate count, type, direction tại build / Validate count, type, direction at build |
| **Column missing từ SP result** | So khớp column shape với Entity properties |
| **Dialect confusion (MySQL → Oracle)** | 5 dialect rules phát hiện syntax sai |
| **Legacy codebase = noise CI** | Baseline mechanism "stop the bleeding" |
| **Credential leak trong CI** | Snapshot mode = zero CI credentials |
| **IDE chậm do analyzer nặng** | IncrementalGenerator syntax-only ~ms |
| **Team không đồng bộ config** | Interactive wizard + Smart defaults |

---

## Sản Phẩm Đầu Ra / Deliverables / Sản Phẩm Đầu Ra

### 1. 9 Gói NuGet / 9 NuGet Packages

| Package | Mô Tả / Description | License |
|---------|-------------|---------|
| `DataGuard.Contracts` | Attribute contracts dùng chung (netstandard2.0) | MIT |
| `DataGuard.Core` | Động cơ xác thực cốt lõi | MIT |
| `DataGuard.SqlServer.Adapter` | Adapter SQL Server (ScriptDOM) | MIT |
| `DataGuard.Oracle.Adapter` | Adapter Oracle (Catalog-based) | MIT + Oracle License |
| `DataGuard.MySql.Adapter` | Adapter MySQL | MIT |
| `DataGuard.PostgreSql.Adapter` | Adapter PostgreSQL | MIT |
| `DataGuard.Analyzers` | Roslyn analyzer + generator | MIT |
| `DataGuard.CodeFixes` | Roslyn code fixes (quick actions) | MIT |
| `DataGuard.Cli` | dotnet tool CLI | MIT |

### 2. CLI Tool / Công Cụ CLI

```bash
# Cài đặt / Install
dotnet tool install -g DataGuard.Cli

# Lệnh / Commands
dataguard validate          # Xác thực hợp đồng
dataguard baseline          # Tạo baseline legacy
dataguard snapshot refresh  # Làm mới snapshot (giống Jest)
dataguard init --wizard     # Wizard tương tác
dataguard oracle-check      # Kiểm tra Oracle dialect + length
dataguard config show       # Xem config
dataguard hook install      # Cài pre-commit hook
dataguard version           # Xem phiên bản
```

### 3. Roslyn Analyzers (IDE Integration)

```xml
<PackageReference Include="DataGuard.Analyzers" Version="1.0.0" />
```

- **DG001**: Unvalidated SQL call (IDE light layer)
- **DG002-DG006**: Parameter/Column/Nullable/Naming/Direction checks
- **DG007-DG009**: Length mismatch (Oracle byte/char, NVARCHAR2(2000) fallback)
- **DG010-DG016**: Dialect + phantom identifier checks (Oracle/SQL Server/MySQL/PostgreSQL)
- **DG098-DG099**: Missing FROM clause + SQL injection pattern
- **DG101**: Parameter count (engine-only)

### 4. Pre-commit Hooks

```bash
dataguard hook install  # Auto-detect: Husky, lefthook, native git
```

---

## Tính Năng Chính / Key Features / Tính Năng Chính

### ✅ Xác Thực Hợp Đồng / Contract Validation
- **Parameter**: Count, Type, Direction (IN/OUT/INOUT)
- **Column**: Shape match Entity ↔ SP Result Set
- **Nullable**: NOT NULL ↔ Non-nullable property
- **Naming**: snake_case ↔ PascalCase configurable

### ✅ Length Mismatch Oracle (ORA-12899 Prevention)
- **DG007**: Entity MaxLength > Column CharLength
- **DG008**: Byte semantics overflow (Vietnamese Unicode 3 bytes/char)
- **DG009**: EF Core NVARCHAR2(2000) fallback (#33218)

### ✅ Dialect Checks (5 Rules)
- Oracle syntax in non-Oracle context (DECODE, NVL, (+), DUAL)
- SQL Server syntax in Oracle (ISNULL, TOP, GETDATE)
- Provider option mismatch (missing UseOracle)
- SQL Server EXEC leak in Oracle
- Unmapped type usage (Oracle EF Core 8+)

### ✅ Baseline Mechanism (MVP Mandatory)
- `dataguard baseline` → Freeze existing drift
- CI chỉ fail trên **drift mới** sau baseline
- SchemaHash + DatabaseVersion tracking

### ✅ 3 Chế Độ Ground Truth / 3 Ground Truth Modes
| Mode | Mô Tả | CI Credentials | Default |
|------|-------|----------------|---------|
| **Full** | Live DB connection | Required | No |
| **Snapshot** | Offline JSON file | **None** | **Yes** |
| **Manual** | Attributes on DTOs | None | No |

### ✅ Zero-Config / Smart Defaults
- Auto-detect provider từ connection string
- Auto-detect EF Core / Dapper từ csproj + source
- Auto-detect naming convention từ code
- Interactive wizard: `dataguard init --wizard`

### ✅ Security Hardened
- **Zero-trust credential**: Env var → KeyVault → AWS → Vault → Local DPAPI → Config (warning)
- **Credential rotation detection**: 30-day warning
- **DPAPI encryption** at rest
- **Supply chain SLSA**: cosign keyless signing, SBOM, provenance
- **Audit logging**: JSON Lines format

### ✅ Performance Optimized
- **IncrementalGenerator**: Syntax-only IDE layer ~ms
- **ConcurrentValidationEngine**: Parallel rules, configurable parallelism
- **Streaming SARIF**: Utf8JsonWriter, no full object graph in memory
- **Baseline v2**: Memory-mapped I/O >1MB, SchemaHash (SHA256-64bit)
- **SchemaHash Caching**: Memory + File cache, 1hr TTL

### ✅ Health Checks (K8s Ready)
- `/health/live` - Liveness probe
- `/health/ready` - Readiness (credentials, baseline, supply chain, disk, memory)
- `/health/startup` - Startup probe

### ✅ Plugin Architecture (MEF)
- Custom rules via `ExportRuleAttribute`
- Version compatibility checking
- Metadata: RuleId, Category, Severity, Author, Tags

### ✅ 12 CodeFixProviders (IDE Quick Fixes)
- Add `[SkipContractCheck]` attribute
- Add `[ExpectedSpParameter]` / `[ExpectedColumn]` attributes
- Auto-fix naming convention
- Convert dialect syntax (Oracle ↔ ANSI SQL)
- Add `[MaxLength]` attribute
- Add `.UseOracle()` to DbContext

---

## So Sánh / Comparison / So Sánh

| Feature | DataGuard | EFCorePowerTools | EF Core Built-in | Dapper | Manual |
|---------|-----------|------------------|------------------|--------|--------|
| **Design-time validation** | ✅ | ❌ (scaffold only) | ❌ | ❌ | ❌ |
| **SP parameter validation** | ✅ | ⚠️ scaffold only | ❌ | ❌ | Manual |
| **Column shape validation** | ✅ | ❌ | ❌ | ❌ | Manual |
| **Length mismatch (ORA-12899)** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Dialect detection** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Baseline legacy support** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **CI/CD integration** | ✅ SARIF | ❌ | ❌ | ❌ | Manual |
| **IDE integration** | ✅ Roslyn + Fixes | VS Extension only | ❌ | ❌ | ❌ |
| **Oracle support** | ✅ Catalog-based | ⚠️ SET FMTONLY | ❌ | ❌ | Manual |
| **Credential security** | ✅ Zero-trust | N/A | N/A | N/A | Manual |
| **License** | MIT (Core) | MIT | MIT | MIT | - |

---

## Kịch Bản Sử Dụng / Use Cases / Kịch Bản Sử Dụng

### 1. Legacy Codebase Onboarding
```bash
# 1. Cài đặt
dotnet add package DataGuard.Core
dotnet add package DataGuard.Oracle.Adapter  # nếu Oracle
dotnet tool install -g DataGuard.Cli

# 2. Wizard setup
dataguard init --wizard

# 3. Baseline (một lần)
dataguard baseline --connection "Oracle CI Schema"

# 4. CI Gate
dataguard validate --connection "CI" --format sarif
```

### 2. Greenfield Project
```bash
# Cài đặt analyzer
dotnet add package DataGuard.Analyzers
dotnet add package DataGuard.Core

# Code bình thường - IDE warning nếu chưa validate
# Pre-commit hook tự động validate offline
# CI Gate: dataguard validate --connection "CI" --format sarif
```

### 3. Oracle Length Mismatch Prevention
```csharp
// Entity
public class Customer {
    [MaxLength(100)]  // DataGuard sẽ validate vs DB CHAR_LENGTH
    public string FullName { get; set; }
}

// Nếu DB: VARCHAR2(50 BYTE) + Unicode data → DG008 Warning
// Nếu EF Core infer NVARCHAR2(2000) → DG009 Warning
```

### 4. Oracle Dialect Safety
```sql
-- ❌ DG010: Oracle syntax in non-Oracle context
SELECT NVL(col, 'default') FROM table;

-- ✅ ANSI SQL
SELECT COALESCE(col, 'default') FROM table;

-- ❌ DG011: SQL Server syntax in Oracle
SELECT ISNULL(col, 'default') FROM table;

-- ✅ Oracle
SELECT NVL(col, 'default') FROM table;
```

---

## Hiệu Suất / Performance / Hiệu Suất

| Metric | Target | Typical |
|--------|--------|---------|
| **IDE Latency** (per keystroke) | < 10ms | ~2-5ms |
| **Full Validation** (100 contracts) | < 5s | ~2-3s |
| **Offline Validation** | < 1s | ~200ms |
| **Baseline Create** (1000 violations) | < 500ms | ~200ms |
| **SARIF Streaming** (10k violations) | < 1s | ~500ms |
| **Memory Peak** (10k violations) | < 200MB | ~80MB |
| **SchemaHash Compute** | < 50ms | ~10ms (cached) |

---

## Bảo Mật & Tuân Thủ / Security & Compliance

| Control | Implementation |
|---------|----------------|
| **Credential Handling** | Zero-trust: Env → KeyVault → AWS → Vault → DPAPI → Config (warn) |
| **Encryption at Rest** | DPAPI (Windows) / libsecret (Linux) via `ProtectedData` |
| **Rotation Detection** | 30-day warning, hash comparison |
| **Supply Chain** | Sigstore keyless signing, SBOM (SPDX), Provenance |
| **Audit Logging** | JSON Lines, Machine/User/Process/Details |
| **Package Signing** | Sigstore keyless, `cosign sign-blob --bundle` |
| **SBOM** | SPDX 2.3, `Microsoft.Sbom.Tool` |
| **Attestations** | `gh attestation upload` to GitHub |

---

## Licensing / Giấy Phép

| Package | License | Vendor Dependencies |
|---------|---------|-------------------|
| `DataGuard.Core` | MIT | None |
| `DataGuard.SqlServer.Adapter` | MIT | ScriptDOM (MIT) |
| `DataGuard.Oracle.Adapter` | MIT + Oracle License | Oracle.ManagedDataAccess.Core |
| `DataGuard.Analyzers` | MIT | Roslyn (MIT) |
| `DataGuard.Cli` | MIT | Core + Adapters |

**Core = MIT thuần** → Eligible for Anthropic Grant / OSI-approved

---

## Hỗ Trợ / Support

| Channel | Mô Tả |
|---------|-------|
| **GitHub Issues** | Bug reports, feature requests |
| **GitHub Discussions** | Q&A, best practices |
| **Documentation** | `/docs` folder + GitHub Pages |
| **NuGet** | Package downloads, version history |

---

## Roadmap / Lộ Trình

| Version | Focus | Timeline |
|---------|-------|----------|
| **1.0** | Core + SQL Server + Oracle + Baseline + CLI + Analyzers | Current |
| **1.1** | PostgreSQL Adapter, MySQL Adapter | Q2 2025 |
| **1.2** | VS Code Extension, Rider Plugin | Q3 2025 |
| **1.3** | AI-assisted fix suggestions | Q4 2025 |
| **2.0** | Multi-repo monorepo support, Policy-as-Code | 2026 |

---

## So Sánh Chi Tiết / Detailed Comparison

| Aspect | DataGuard | Alternatives |
|--------|-----------|--------------|
| **Architecture** | Separated IDE/CI | Monolithic |
| **Oracle Parsing** | Catalog-based (no parser) | ScriptDOM/ANTLR |
| **Baseline** | SchemaHash + DB Version | None / Manual |
| **Security** | Zero-trust, SLSA, DPAPI | Config file only |
| **Performance** | Incremental + Streaming + Parallel | Single-threaded |
| **Extensibility** | MEF Plugins + Custom Rules | Hardcoded / None |
| **IDE Experience** | 12 Code Fixes | None / Basic |
| **Pre-commit** | Husky/Lefthook/Native | Manual setup |
| **Observability** | Health Checks + Telemetry | None |

---

## Kết Luận / Conclusion

**DataGuard** giải quyết **gap 10 năm** của EF Core (issue #245) bằng kiến trúc hiện đại:
- **Separation of Concerns**: IDE light / CI heavy
- **Security First**: Zero-trust, SLSA, Audit
- **Legacy Friendly**: Baseline mechanism
- **Developer Experience**: Zero-config, Wizard, Quick Fixes
- **Performance**: Incremental, Streaming, Parallel
- **Extensibility**: MEF Plugins, Custom Rules

**Sẵn sàng production** cho team .NET sử dụng EF Core/Dapper với Oracle/SQL Server.

---

*Phiên bản: 1.0.0 | Cập nhật: 2025-01-19*