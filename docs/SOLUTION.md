# Tổng Quan Giải Pháp / Solution Overview / Tổng Quan Giải Pháp

## Cấu Trúc Dự Án / Project Structure / Cấu Trúc Dự Án

```
DataGuard.sln
├── src/
│   ├── DataGuard.Contracts/                # Shared attribute contracts (netstandard2.0)
│   ├── DataGuard.Core/                     # Động cơ xác thực cốt lõi (net9.0)
│   ├── DataGuard.SqlServer.Adapter/        # Metadata SQL Server (ScriptDOM)
│   ├── DataGuard.Oracle.Adapter/           # Metadata Oracle (catalog views)
│   ├── DataGuard.MySql.Adapter/            # Metadata MySQL
│   ├── DataGuard.PostgreSql.Adapter/       # Metadata PostgreSQL
│   ├── DataGuard.Analyzers/                # Roslyn analyzer + generator (IDE)
│   ├── DataGuard.CodeFixes/                # Roslyn code fixes (quick actions)
│   ├── DataGuard.LanguageServer/           # stdio language-server entrypoint
│   ├── DataGuard.Host/                     # Optional loopback health host
│   ├── DataGuard.Build/                    # MSBuild integration
│   ├── DataGuard.SqlClassification/        # Shared SQL-call classification
│   ├── DataGuard.Observability*/           # Runtime observability projects
│   ├── DataGuard.Cli/                      # dotnet tool CLI
│   ├── DataGuard.VSCode/                   # VS Code extension (TypeScript)
│   └── DataGuard.VisualStudio/             # Visual Studio 2022 VSIX (net472)
├── tests/
│   ├── DataGuard.Core.Tests/
│   ├── DataGuard.GoldenCorpus.Tests/
│   ├── DataGuard.Analyzers.Tests/
│   ├── DataGuard.CodeFixes.Tests/
│   ├── DataGuard.Observability.Tests/
│   ├── DataGuard.VisualStudio.Tests/       # Windows
│   └── DataGuard.BinaryCompatibilityFixture/ # Compile-only consumer fixture
├── docs/                                   # Tài liệu
├── .github/workflows/                      # CI/release/Marketplace
├── Directory.Build.props
├── Dockerfile
└── .editorconfig                           # StyleCop/C# analyzer policy
```

---

## Chi Tiết Dự Án / Project Details

### DataGuard.Core
**Mục Đích / Purpose**: Động cơ xác thực cốt lõi, không phụ thuộc driver DB / Core validation engine, no database-driver dependency; EF Core/Roslyn dependencies remain

**Thành Phần Chính / Key Components**:
- `Abstractions/` - Contracts, Rules, Models
- `Sources/` - EfModelSource, project/manual contract sources; SQL Server parsers live in its adapter
- `Rules/` - provider-neutral rules and dependency graph; provider composition lives in CLI `ProviderRuleCatalog`
- `Reporting/` - DiagnosticEmitter, SARIF sinks
- `Baseline/` - violation baselines, v4 schema snapshots, integrity and structural comparison
- `Security/` - CredentialManager, ZeroTrust, hash verification, Audit
- `Plugins/` - RulePluginManager (MEF)
- `Telemetry/` - TelemetryCollector (opt-in)
- `AutoDetection/` - AutoDetectionEngine + Interactive Wizard
- `PublicApi/` - DataGuardApi, ValidationPipeline

**Phụ Thuộc / Dependencies**: 
- Microsoft.CodeAnalysis.CSharp 5.9.0 (Core; analyzers pin 4.12.0)
- Microsoft.EntityFrameworkCore 9.0.21
- System.Composition.Hosting 10.0.13
- System.Text.Json 10.0.13

---

### DataGuard.SqlServer.Adapter
**Mục Đích / Purpose**: Trích xuất metadata SQL Server sử dụng ScriptDOM / SQL Server metadata extraction using ScriptDOM

**Thành Phần Chính / Key Components**:
- `SqlServerStoredProcedureParser` - `sys.parameters` + `sp_describe_first_result_set`
- `RawSqlParser` - ScriptDOM TSql160Parser

**Phụ Thuộc / Dependencies**:
- Microsoft.Data.SqlClient 7.1.1
- Microsoft.SqlServer.TransactSql.ScriptDom 180.117.0

---

### DataGuard.Oracle.Adapter
**Mục Đích / Purpose**: Trích xuất metadata Oracle qua các view catalog / Oracle metadata extraction via catalog views

**Thành Phần Chính / Key Components**:
- `AllArgumentsReader` - `ALL_ARGUMENTS` với SEQUENCE/OVERLOAD cho overloads
- `AllTabColumnsReader` - `ALL_TAB_COLUMNS` với CHAR_USED (B/C)
- `NlsSessionReader` - `NLS_LENGTH_SEMANTICS` + Phiên Bản DB
- `RefCursorDescriber` - `DBMS_SQL.DESCRIBE_COLUMNS3` (explicit execution opt-in; see Oracle adapter guide)
- `LengthMismatchDetector` - 3 loại mismatch + EfCoreInferenceSimulator
- `OracleDialectChecker` - 5 quy tắc dialect

**Phụ Thuộc / Dependencies**:
- Oracle.ManagedDataAccess.Core 23.26.301 (Oracle Distribution License)

---

### DataGuard.Analyzers
**Mục Đích / Purpose**: Roslyn analyzer + generator cho tích hợp IDE / Roslyn analyzers and generator for IDE integration

**Thành Phần Chính / Key Components**:
- `UnvalidatedSqlCallGenerator` - IIncrementalGenerator (lớp IDE nhẹ, DG001)
- `ContractValidationAnalyzer` - DiagnosticAnalyzer (lớp semantic)

**Phụ Thuộc / Dependencies**: Microsoft.CodeAnalysis.CSharp (KHÔNG reference Workspaces — analyzer/generator được `csc` load)

---

### DataGuard.CodeFixes
**Mục Đích / Purpose**: Roslyn code fixes (quick actions) cho IDE / Roslyn code fixes for IDE

**Thành Phần Chính / Key Components**:
- `DataGuardCodeFixProvider` - quick fixes cho nhiều diagnostic IDs
- `AddMaxLengthAttributeFixProvider` - DG007, DG009
- `SkipContractCheckFixProvider` - DG001

**Phụ Thuộc / Dependencies**: Microsoft.CodeAnalysis.CSharp.Workspaces (chỉ VS IDE load; tách khỏi analyzer để tránh RS1038)

### DataGuard.Cli
**Mục Đích / Purpose**: dotnet tool cho CI/CD và xác thực local / dotnet tool for CI/CD and local validation

**Lệnh / Commands**: the authoritative registration is [Program.cs](../src/DataGuard.Cli/Program.cs); this table highlights the primary commands.
| Command / Lệnh | Mô Tả / Description |
|---------|-------------|
| `validate` | Validation with text/SARIF/evidence; contracts/yaml/typescript are acquisition exports, not validation results |
| `baseline` | Tạo baseline từ vi phạm hiện tại |
| `snapshot refresh` | Explicitly acquire and persist a schema snapshot |
| `snapshot show` | Hiển thị metadata snapshot |
| `snapshot diff` | So sánh schema hiện tại vs snapshot |
| `init` | Tạo `.dataguard.yml` config |
| `config show/validate` | Xem/xác thực cấu hình |
| `oracle-check` | Kiểm tra dialect + length Oracle |
| `version` | Hiển thị thông tin phiên bản |
| `preflight`, `scan`, `verify-shape` | Additional acquisition/validation entrypoints; use command `--help` |
| `migrate`, `assess`, `hook` | Migration, assessment and hook commands; use command `--help` |

**Cài Đặt Hook / Hook Installer**:
- `PreCommitHookInstaller` - Husky, lefthook, native git hooks

---

## Cấu Hình Build / Build Configuration

Project-specific target frameworks override shared defaults. `DataGuard.sln` is the complete solution; `DataGuard.CrossPlatform.slnf` is the non-Windows build surface. VS Code has its own Node toolchain and is not built by `dotnet build`. See the checked-in project files, [global.json](../global.json) and [Directory.Build.props](../Directory.Build.props); the snippets below show selected settings.

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
    "version": "9.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

---

## Các Gói NuGet / NuGet Packages

Selected package identities below are not a complete publication inventory. Contracts, CodeFixes, MySQL/PostgreSQL adapters, Build and observability projects have their own packaging metadata. A project/package ID does not establish current feed availability.

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

The executable definitions are [ci.yml](../.github/workflows/ci.yml) and [release.yml](../.github/workflows/release.yml). CI has separate Linux, live-database, VS Code, script, Windows/VSIX, security, CodeQL, SBOM, benchmark and Docker jobs; it is not one linear restore→security→publish sequence.

CI chạy các job riêng; `Category!=LiveDb` của bộ test thông thường không thay thế job integration. Release có kiểm tra version/CI, build/test và các điều kiện `needs`/`if` riêng cho ký, đóng gói, draft release, NuGet, attestation, Docker và publish release. Workflow dispatch có chế độ dry-run. Không suy ra đã publish hoặc tất cả job đã chạy từ việc có file workflow.

Marketplace publication remains paused; see [publishing policy](marketplace-publishing.md). This source-only reconciliation did not run workflows or publish artifacts.

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

### .dataguard.yml (Illustrative subset / Tập cấu hình minh họa)
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
git clone https://github.com/thanhnt-sm/eco_support_net_oracle
cd eco_support_net_oracle

# Restore & Build / Khôi Phục & Build
dotnet restore DataGuard.CrossPlatform.slnf --locked-mode
dotnet build DataGuard.CrossPlatform.slnf --configuration Release --no-restore

# Run Tests / Chạy Test
dotnet test DataGuard.CrossPlatform.slnf --configuration Release --no-build --filter "Category!=LiveDb"

# Chạy CLI từ source / Run CLI from source
dotnet run --project src/DataGuard.Cli --no-launch-profile -- validate --offline --format text
```

---

## Hỗ Trợ Docker / Docker Support

The checked-in [Dockerfile](../Dockerfile) restores `src/DataGuard.Cli/DataGuard.Cli.csproj` in locked mode, publishes for the target architecture, and runs `dotnet DataGuard.Cli.dll` as a non-root user on the .NET runtime image. Its entrypoint already selects the executable, so container arguments start with `validate`, not another `dataguard` executable name.

Dockerfile thực tế là nguồn chuẩn cho digest image, project closure và publish flags. Không dùng bản Dockerfile rút gọn cũ làm bản triển khai tương đương. Container smoke/build was not run for this documentation audit.

---

## Checklist Release / Release Checklist

- [ ] Tất cả test pass (CI green)
- [ ] Release version agrees with tag/workflow inputs and MinVer metadata
- [ ] CHANGELOG.md cập nhật
- [ ] Owner-authorized release tag matches intended version
- [ ] GitHub Release tạo
- [ ] Intended packages published according to the actual release workflow
- [ ] Docker image push lên GHCR
- [ ] SBOMs tạo và upload
- [ ] Sigstore attestations publish
- [ ] Documentation cập nhật

---

*Historical document date / Ngày ghi trên tài liệu trước đối chiếu: 2025-01-19.*
Source review: 2026-10-18. Historical diagrams were previously dated 2025-01-19. Test targets are goals, not measured accuracy or coverage; no runtime, Windows/editor-host, database or release checks were performed in this documentation-only pass.
