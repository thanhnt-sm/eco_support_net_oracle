# Review Handoff — DataGuard (2026-08-21)

Tài liệu tổng hợp để chuyên gia red-team/architecture review một chỗ: trạng thái todo, backlog đề xuất, kiến trúc hiện trạng và các câu hỏi mở.

## 1. Trạng thái tổng quan

| Hạng mục | Giá trị |
|---|---|
| Commit hiện tại | `main` = `10586ca` (đã push) |
| Warnings build | **0** (đã giảm 6770 → 0) |
| CodeQL open alerts | **0** |
| Tests | 69 (Core 39, GoldenCorpus 25, Analyzers 5) |
| Test coverage | ~20% (đo coverlet) |
| NuGet packages | 9 (Contracts, Core, 4 adapters, Analyzers, CodeFixes, Cli) |
| Editor extensions | VS Code (TS) + Visual Studio 2022 (VSIX) |

## 2. Todo state (session memory — không persist)

### Hoàn tất (32/34)

**Repair PR3** — 3/3
- Align package dependency versions
- Regenerate locked dependency graph
- Verify PR3 branch checks

**Merge PRs** — 3/3
- Verify PR4 updated checks
- Merge green pull requests (squash #3 NuGet deps + #4 GitHub Actions)
- Delete merged remote branches (chỉ còn `main`)

**CI cleanup** — 2/2
- Resolve remaining workflow warnings (0 warnings)
- Confirm zero CodeQL alerts

**VS Marketplace** — 24/26 done
- Research publishing requirements, Visual Studio VSIX requirements
- Red-team extension design + Visual Studio integration risks
- Plan extension product architecture
- Implement + build VS Code extension (VSIX + SHA-256 + SBOM + provenance)
- Implement + build Visual Studio extension (VSIX + SHA-256 + SBOM + provenance)
- Research role pain points, banking compliance, competitors, discovery, procurement, positioning
- Define enterprise outcomes, architecture, backend/full-stack workflows, policy evidence
- Test enterprise journeys; prioritize backlog; update roadmap; reconcile license (MIT)

### Blocked (2/34 — cần owner/external)

| Task | Lý do block |
|---|---|
| Publish VS Code extension | Cần publisher verify + secret `VSCE_PAT` + Extension Development Host smoke |
| Publish Visual Studio extension | Cần secret `VS_MARKETPLACE_PAT` + VS 2022 Experimental Instance smoke + `VsixPublisher.exe` |

## 3. Kiến trúc hiện trạng

```
DataGuard.sln
├── DataGuard.Contracts/      netstandard2.0 — attribute dùng chung (ExpectedColumn, ExpectedSpParameter, SkipContractCheck)
├── DataGuard.Core/           net9.0 — rules engine (14 rules), baseline, security, reporting, sources, public API
├── DataGuard.SqlServer.Adapter/   ScriptDOM + sys.parameters/sp_describe_first_result_set
├── DataGuard.Oracle.Adapter/      ALL_ARGUMENTS/ALL_TAB_COLUMNS/NLS + RefCursorDescriber
├── DataGuard.MySql.Adapter/       MySqlConnector
├── DataGuard.PostgreSql.Adapter/  Npgsql
├── DataGuard.Analyzers/      netstandard2.0 — Roslyn analyzer + generator (KHÔNG reference Workspaces)
├── DataGuard.CodeFixes/      netstandard2.0 — Roslyn code fixes (reference Workspaces, tách khỏi analyzer)
├── DataGuard.Cli/            dotnet tool — validate/baseline/snapshot/init/config/oracle-check/migrate/version
├── DataGuard.VSCode/         TypeScript — trusted workspace, private SARIF → Problems, no raw output
└── DataGuard.VisualStudio/   net472 VSIX — Tools command, SARIF → Error List, taskkill tree
```

### Quyết định kiến trúc quan trọng (đã thực hiện)

1. **Tách analyzer/codefix** (RS1038): generator/analyzer không được nằm chung assembly reference `Microsoft.CodeAnalysis.Workspaces` (csc không cung cấp). `DataGuard.CodeFixes` pack `codefixes/dotnet/cs`.
2. **Contract evidence/output**: CLI hỗ trợ `--format text|sarif|evidence|contracts|typescript`; machine-readable output bắt buộc `--output` (không bao giờ qua stdout).
3. **Redaction fail-closed**: SARIF/evidence chỉ giữ scalar properties allowlist; sensitive marker (password/token/bearer/connection string) bị `[REDACTED]`; editor host không hiển thị stdout/stderr CLI thô.
4. **DPAPI Windows-only**: `CredentialManager` annotate `[SupportedOSPlatform("windows")]` + guard `OperatingSystem.IsWindows()`.

## 4. Backlog đề xuất (theo ưu tiên)

| Ưu tiên | Việc | Lý do |
|---|---|---|
| **P0** | Test coverage → 60%+ | gap lớn nhất: `BaselineManager`, `ZeroTrustCredentialProvider`, `Sources/` (EfModelSource, SqlServerParsers, OracleReaders), `AutoDetection`, `ConcurrentValidationEngine`, `Telemetry` chưa có test trực tiếp |
| **P0** | Verify claims chưa chứng minh | `init --wizard`, `AutoDetectionEngine` (auto-detect provider/EF/Dapper), `RulePluginManager` (MEF), `TelemetryCollector` |
| **P1** | `version` hiển thị `InformationalVersion` thay `Version` (assemblies phụ đang in `0.0.0.0`) | UX |
| **P1** | `snapshot show` exit 0 (informational) khi chưa có snapshot | UX |
| **P1** | Chuẩn hóa docs 1 ngôn ngữ (bỏ format 3-ngôn-ngữ `.vi.md` + EN lặp) | debt docs |
| **P2** | Full marketplace publish (sau khi owner cấp credentials) | runbook: `docs/marketplace-publishing.md` |
| **P2** | Breaking-change classification cho contract export | full-stack roadmap |

## 5. Câu hỏi cho chuyên gia red-team

1. **Rules engine**: `ParameterTypeMatchRule` dùng `InferClrType` rồi `IsTypeCompatible` — có tồn tại type không bao giờ mismatch (logic no-op)? Nếu có, rule nào vô dụng?
2. **Security boundary**: editor extension đã "no raw output + private SARIF + trusted workspace" — còn vector leak nào từ CLI process (argv, temp file, env)?
3. **Supply chain**: NuGet analyzer/codefix 2-package model + VSIX SBOM/provenance — còn khoảng trống nào cho SLSA level 3?
4. **PublicApi**: `ValidationPipeline` + `DataGuardFactory` — có surface nào vi phạm SemVer 1.0.0 hoặc là dead code?
5. **Ground truth**: 3 mode (Full/Snapshot/Manual) — Snapshot mode có thể bị drift/stale không được phát hiện? Cần thêm hash/version check gì?
6. **Performance**: `ConcurrentValidationEngine` + `IncrementalGenerator` — có claim ~ms nào chưa benchmark?

## 6. Link tài liệu chính

- SSOT: `plans/ACTIVE_SESSION_REGISTER.md`
- Red-team tổng: `plans/2026-08-21-redteam-review.md`
- Red-team Marketplace: `plans/260820-marketplace-extensions/reports/marketplace-redteam.md`
- Marketplace plan: `plans/260820-marketplace-extensions/plan.md`
- ADR: `plans/adr/001-v4-architecture.md`, `plans/adr/002-core-dependency-scope.md`
- Warnings plan: `plans/2026-08-21-warnings-plan.md`
- Risks/Gaps: `docs/RISKS_GAPS.md`, `docs/FIX_PLAN.md`
- Marketplace runbook: `docs/marketplace-publishing.md`
