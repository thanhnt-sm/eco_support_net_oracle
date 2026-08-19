# Tổng Quan Giải Pháp / Solution Overview / Tổng Quan Giải Pháp

## Cấu Trúc Dự Án / Project Structure / Cấu Trúc Dự Án

```
DataGuard.sln
├── src/
│   ├── DataGuard.Core/                    # Động cơ xác thực cốt lõi / Core validation engine
│   ├── DataGuard.SqlServer.Adapter/       # Trích xuất metadata SQL Server / SQL Server metadata extraction
│   ├── DataGuard.Oracle.Adapter/          # Trích xuất metadata Oracle / Oracle metadata extraction
│   ├── DataGuard.Analyzers/               # Roslyn analyzers + code fixes
│   └── DataGuard.Cli/                     # dotnet tool CLI
├── tests/
│   ├── DataGuard.Core.Tests/              # Unit tests
│   └── DataGuard.GoldenCorpus.Tests/      # Golden corpus regression tests
├── docs/                                  # Documentation / Tài liệu
├── .github/workflows/                     # CI/CD pipelines
├── Directory.Build.props                  # Common build properties / Thuộc tính build chung
├── global.json                           # SDK version pinning / Ghim phiên bản SDK
├── Dockerfile                            # Container image
├── docker-compose.yml                    # Local development stack / Stack phát triển local
└── wrangler.toml                         # Cloudflare Workers config
```

---

## Chi Tiết Dự Án / Project Details

### DataGuard.Core
**Mục Đích / Purpose**: Động cơ xác thực cốt lõi, không phụ thuộc vendor / Core validation engine, zero vendor dependencies

**Thành Phần Chính / Key Components**:
- `Abstractions/` - Contracts, Rules, Models
- `Sources/` - EfModelSource, SqlServerParsers, RawSqlParser
- `Rules/` - 6 quy tắc xác thực built-in / 6 built-in validation rules
- `Reporting/` - DiagnosticEmitter, SARIF sinks
- `Baseline/` - BaselineManager v2 (SchemaHash + DB Version)
- `Security/` - CredentialManager, ZeroTrust, SLSA, Audit
- `Plugins/` - RulePluginManager (MEF)
- `Telemetry/` - TelemetryCollector (opt-in)
- `Health/` - HealthChecks (Liveness/Readiness/Startup)
- `AutoDetection/` - AutoDetectionEngine + Interactive Wizard
- `PublicApi/` - DataGuardApi, ValidationPipeline

**Phụ Thuộc / Dependencies**: 
- Microsoft.CodeAnalysis.CSharp 4.11.0
- Microsoft.EntityFrameworkCore 8.0.0
- System.Composition.Hosting 8.0.0
- System.Text.Json 8.0.5

---

### DataGuard.SqlServer.Adapter
**Mục Đích / Purpose**: Trích xuất metadata SQL Server sử dụng ScriptDOM / SQL Server metadata extraction using ScriptDOM

**Thành Phần Chính / Key Components**:
- `SqlServerStoredProcedureParser` - `sys.parameters` + `sp_describe_first_result_set`
- `RawSqlParser` - ScriptDOM TSql160Parser

**Phụ Thuộc / Dependencies**:
- Microsoft.Data.SqlClient 5.2.0
- Microsoft.SqlServer.TransactSql.ScriptDom 170.3.0

---

### DataGuard.Oracle.Adapter
**Mục Đích / Purpose**: Trích xuất metadata Oracle qua các view catalog / Oracle metadata extraction via catalog views

**Thành Phần Chính / Key Components**:
- `AllArgumentsReader` - `ALL_ARGUMENTS` với SEQUENCE/OVERLOAD cho overloads
- `AllTabColumnsReader` - `ALL_TAB_COLUMNS` với CHAR_USED (B/C)
- `NlsSessionReader` - `NLS_LENGTH_SEMANTICS` + Phiên Bản DB
- `RefCursorDescriber` - `DBMS_SQL.DESCRIBE_COLUMNS`
- `LengthMismatchDetector` - 3 loại mismatch + EfCoreInferenceSimulator
- `OracleDialectChecker` - 5 quy tắc dialect

**Phụ Thuộc / Dependencies**:
- Oracle.ManagedDataAccess.Core 23.6.0 (Oracle Distribution License)

---

### DataGuard.Analyzers
**Mục Đích / Purpose**: Roslyn analyzers cho tích hợp IDE / Roslyn analyzers for IDE integration

**Thành Phần Chính / Key Components**:
- `UnvalidatedSqlCallGenerator` - IIncrementalGenerator (lớp IDE nhẹ)
- `ContractValidationAnalyzer` - DiagnosticAnalyzer (lớp CI nặng)
- `CodeFixProviders` - 12 CodeFixProviders cho sửa nhanh

**Code Fix Providers / Cung Cấp Sửa Nhanh**:
1. `DataGuardCodeFixProvider` - Cung cấp chính (12 diagnostic IDs)
2. `AddMaxLengthAttributeFixProvider` - DG007, DG009
3. `AddUseOracleFixProvider` - DG012
4. `SkipContractCheckFixProvider` - DG001

**Phụ Thuộc / Dependencies**:
- Microsoft.CodeAnalysis.CSharp 4.11.0
- Microsoft.CodeAnalysis.CSharp.IncrementalGenerators 4.11.0
- Microsoft.CodeAnalysis.Operations

---

### DataGuard.Cli
**Mục Đích / Purpose**: dotnet tool cho CI/CD và xác thực local / dotnet tool for CI/CD and local validation

**Lệnh / Commands** (9 lệnh / 9 commands):
| Command / Lệnh | Mô Tả / Description |
|---------|-------------|
| `validate` | Xác thực hợp đồng đầy đủ với output SARIF/JSON/text |
| `baseline` | Tạo baseline từ vi phạm hiện tại |
| `snapshot refresh` | Làm mới snapshot schema (giống Jest) |
| `snapshot show` | Hiển thị metadata snapshot |
| `snapshot diff` | So sánh schema hiện tại vs snapshot |
| `init` | Tạo `.dataguard.yml` config |
| `config show/validate` | Xem/xác thực cấu hình |
| `oracle-check` | Kiểm tra dialect + length Oracle |
| `version` | Hiển thị phiên bản + assemblies đã load |

**Cài Đặt Hook / Hook Installer**:
- `PreCommitHookInstaller` - Husky, lefthook, native git hooks

---

## Cấu Hình Build / Build Configuration

### Directory.Build.props
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
</Project>
```

### global.json
```json
{
  "sdk": {
    "version": "9.0.310",
    "rollForward": "latestFeature"
  }
}
```

---

## Các Gói NuGet / NuGet Packages

| Package / Gói | ID | Mô Tả / Description |
|---------|----|-------------|
| Core | `DataGuard.Core` | Động cơ xác thực cốt lõi / Core validation engine |
| SQL Server | `DataGuard.SqlServer.Adapter` | Adapter SQL Server |
| Oracle | `DataGuard.Oracle.Adapter` | Adapter Oracle |
| Analyzers | `DataGuard.Analyzers` | Roslyn analyzers |
| CLI | `DataGuard.Cli` | dotnet tool |

**Loại Package / Package Type**: 
- Core/Adapters/Analyzers: `library`
- CLI: `DotnetTool`

---

## Pipeline CI/CD

> Pipeline được rewrite toàn diện (2026-08): SDK `9.0.x`, permissions tối thiểu,
> pin SHA toàn bộ actions, supply-chain (cosign v3.1.3 keyless + SBOM + attestations),
> Trusted Publishing cho NuGet. Chi tiết: `plans/2026-08-20-ci-cd-upgrade.md`.

### Build & Test (`.github/workflows/ci.yml`)
```yaml
jobs:
  build-and-test:   # SDK 9.0.x, NuGet cache, fail-fast
    - dotnet restore DataGuard.sln
    - dotnet build --configuration Release --no-restore
    - dotnet test DataGuard.sln --configuration Release --no-build
    - dotnet list DataGuard.sln package --vulnerable --include-transitive (gate, parse JSON)

  security-scan:
    - TruffleHog (pin SHA, only_verified) — push + PR
    - CodeQL Analysis (default csharp + custom queries .github/codeql)

  generate-sbom:
    - Microsoft.Sbom.Tool 4.1.5 (pin global tool) — sbom-tool generate
      (-bc directory, -m output dir) → upload sboms artifact

  docker-smoke:     # chỉ push → main
    - build image qua Dockerfile → chạy `DataGuard.Cli.dll --help`
```

### Release (`.github/workflows/release.yml`)
```yaml
jobs:
  build-and-test: (same as CI) + pack NuGet (version từ git tag, strip prefix v)
  sign-packages:
    - cosign v3.1.3 keyless signing + `--bundle` (nupkgs) → signed-nupkgs artifact
    - SBOM per-package (download nupkgs thật → sbom-tool generate) → sboms artifact
  publish-nuget:
    - Trusted Publishing (NuGet/login OIDC, secret NUGET_USER) fallback NUGET_API_KEY
    - dotnet nuget push từ artifacts (không skip-duplicate)
  publish-attestations:
    - actions/attest@v4 (build provenance, id-token) — thay `gh attestation upload`
  create-github-release:
    - gh CLI: draft → attach nupkg + .sigstore.json + SBOM → publish
      (guard: xóa draft cũ khi re-run, fail nếu đã published)
  docker:
    - buildx + QEMU: linux/amd64 + linux/arm64 → push GHCR
    - tags: semver (từ git tag) + raw version + latest (workflow_dispatch)
```

---

## Chiến Lược Kiểm Thử / Testing Strategy

### Kim Tháp Test / Test Pyramid
| Layer / Lớp | Coverage / Phủ | Tool / Công Cụ |
|-------|----------|---------|
| Unit | Rules, diff logic, naming | xUnit, Moq, FluentAssertions |
| Integration | SQL Server (Testcontainers), Oracle (Testcontainers + gvenzl/oracle-xe) | xUnit |
| Golden Corpus | H1/H2/H3/Length regression (periodic) | xUnit |

### Mục Tiêu Golden Corpus / Golden Corpus Targets
| Category / Danh Mục | Target Detection Rate / Tỷ Lệ Phát Hiện Mục Tiêu |
|----------|----------------------|
| H1 Phantom Identifiers | >95% |
| H2 Column/Table Mismatch | >90% |
| H3 Dialect Confusion | >80% |
| Length Mismatch | >95% |

---

## Các File Cấu Hình / Configuration Files

### .dataguard.yml (Được sinh bởi `dataguard init`)
```yaml
# DataGuard Configuration
GroundTruthMode: Snapshot
EnableSmartDefaults: true
EnableBaseline: true
NamingConvention: SnakeCaseToPascalCase
DefaultProvider: SqlServer
SnapshotFilePath: .dataguard-snapshot.json
BaselineFilePath: .dataguard-baseline.json
EnableTelemetry: false
```

### launchSettings.json (Debug)
```json
{
  "profiles": {
    "DataGuard.Cli": {
      "commandName": "Project",
      "commandLineArgs": "validate --offline --format text"
    }
  }
}
```

---

## Thiết Lập Phát Triển / Development Setup

```bash
# Clone
git clone https://github.com/DataGuard/DataGuard
cd DataGuard

# Restore & Build / Khôi Phục & Build
dotnet restore DataGuard.sln
dotnet build DataGuard.sln

# Run Tests / Chạy Test
dotnet test DataGuard.sln

# Cài đặt CLI local
dotnet tool install --local --add-source ./artifacts DataGuard.Cli

# Chạy CLI
dataguard validate --offline --format text

# Cài đặt pre-commit hook
dataguard hook install
```

---

## Hỗ Trợ Docker / Docker Support

### Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish DataGuard.Cli -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "DataGuard.Cli.dll"]
```

### docker-compose.yml
```yaml
services:
  dataguard:
    build: .
    volumes:
      - .:/workspace
    working_dir: /workspace
    command: dataguard validate --offline
```

---

## Checklist Release / Release Checklist

- [ ] Tất cả test pass (CI green)
- [ ] Version bump trong `Directory.Build.props`
- [ ] CHANGELOG.md cập nhật
- [ ] Git tag pushed (`v1.0.0`)
- [ ] GitHub Release tạo
- [ ] 5 gói NuGet publish
- [ ] Docker image push lên GHCR
- [ ] SBOMs tạo và upload
- [ ] Sigstore attestations publish
- [ ] Documentation cập nhật

---

*Cập nhật lần cuối: 2025-01-19 / Last updated: 2025-01-19*