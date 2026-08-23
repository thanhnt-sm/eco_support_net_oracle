# Release evidence — .NET Developer Platform assessment capability

Ngày: 2026-08-23. Commit baseline: `ea17a05`. Mọi lệnh chạy từ repository root, macOS arm64, .NET SDK 9.0.310.

## 1. Commands và kết quả

| Gate | Lệnh | Kết quả |
|---|---|---|
| Restore | `dotnet restore DataGuard.sln --locked-mode` | exit 0 |
| Build | `dotnet build DataGuard.sln --configuration Release --no-restore` | exit 0, 0 warnings, 0 errors |
| Analyzers | `dotnet build DataGuard.sln --configuration Release --no-restore /p:RunAnalyzers=true` | exit 0, 0 warnings, 0 errors |
| Format gate | `dotnet format DataGuard.sln --verify-no-changes --no-restore` | exit 0 (`Formatted 0 of N`) |
| Tests | `dotnet test DataGuard.sln --configuration Release --no-build` | Analyzers 5 passed; GoldenCorpus 25 passed; Core 278 passed — tổng **308 passed, 0 failed** (baseline trước thay đổi: 291; +17 assessment/planner/regression tests) |
| Coverage gate (CI parity) | Python parser trên cobertura output | **60.76%** (6.852/11.278) ≥ 60% → PASS |

## 2. Smoke test trên fixture thật

Chạy bằng binary build Release `src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli`:

| Scenario | Fixture | Kết quả quan sát |
|---|---|---|
| Legacy EOL TFM | net462 project | `DG1103` warning với evidence path+value, exit 1 |
| Invalid metadata | XML cụt | `[DG1003] Broken/Bad.csproj: invalid project metadata`; sibling `Good.csproj` vẫn được assess |
| Unknown TFM | net999.9 | `DG1101` Information "support status Unknown" — không suy đoán |
| Missing workspace | `/nonexistent-dir-xyz` | `[DG1000]`, exit 1 |
| Invalid format | `--format yaml` | Usage error, exit 2 |
| JSON output | `--format json --output ...` | File có đủ `schemaVersion/toolVersion/target/generatedAt/findings/errors/summary`, camelCase ổn định |
| SARIF output | `--format sarif --output ...` | JSON parse OK: `version 2.1.0`, 3 results với đúng ruleIds |
| Read-only | sha256 trước/sau assess trên fixture chain | byte-for-byte unchanged: PASS |
| Secret redaction | Web.config chứa `DbPassword=TOPSECRET-VALUE-42` | Report chứa `[redacted]` ×2; chuỗi secret xuất hiện **0 lần** trong JSON output |
| Lock file dependencies-only | lock có `targets:{}` rỗng, chỉ `dependencies` populated | Không phát DG1202 (regression test `LockFileDependenciesOnlySection_NoFalsePositive`) |
| Lock file .NET Framework key | `.NETFramework,Version=v4.7.2` khớp project `net472` | Không phát DG1202 (regression test `LockFileNetFrameworkKey_NoFalsePositive`) |

## 3. Schema version

`AssessmentReport.CurrentSchemaVersion = "1.0"` (`src/DataGuard.Core/Assessment/AssessmentContracts.cs`). Bump bắt buộc khi đổi serialized shape.

## 4. Shipped capabilities

| Pack | Vị trí | Rule IDs |
|---|---|---|
| Environment inventory + legacy compatibility | `src/DataGuard.Core/Assessment/Internal/InventoryPack.cs`, `LegacySupportTable.cs` | DG1101–DG1103, DG1004, DG1201 |
| Dependency health | `src/DataGuard.Core/Assessment/Internal/DependencyHealthPack.cs` | DG1202, DG1203 |
| Build/CI diagnosis | `src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs` | DG1301–DG1303 |
| Configuration and secrets | `src/DataGuard.Core/Assessment/Internal/SecretsPack.cs` | DG1401, DG1402 |
| Upgrade planning (analysis-only) | `src/DataGuard.Core/Assessment/UpgradePlanner.cs` | steps + blockers, không edit file |
| CLI surface | `src/DataGuard.Cli/Program.cs` `assessCommand` | text/json/sarif, exit semantics documented |

## 5. Known unverified cells

- Chưa chạy trên máy Windows/Linux thật; chỉ verified trên macOS arm64.
- `packages.config` legacy format: reader có sẵn (`PackagesConfigReader.cs`) nhưng chưa có rule pack riêng dùng nó trong release này.
- Remote advisory lookup (opt-in): chưa implement trong release đầu; matrix cell ghi rõ là không ship.
- Visual Studio / VS Code extension surfaces chưa expose assess command; chỉ CLI + programmatic API.

## 6. Cách tái lập

```bash
dotnet restore DataGuard.sln --locked-mode
dotnet build DataGuard.sln --configuration Release --no-restore
dotnet build DataGuard.sln --configuration Release --no-restore /p:RunAnalyzers=true
dotnet format DataGuard.sln --verify-no-changes --no-restore
dotnet test DataGuard.sln --configuration Release --no-build --collect:"XPlat Code Coverage" --logger "trx;LogFileName=test_results.trx"
# smoke:
printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net462</TargetFramework></PropertyGroup></Project>' > /tmp/f.csproj-dir/App.csproj
src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli assess --workspace <dir> --verbose
```

Release gate: **PASS**.
