# ADR-0004: Health host (`src/DataGuard.Host`) and `DataGuard.Core.Health`

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4, recommendation 22

## Context

`src/DataGuard.Host` is an ASP.NET Core project (`Microsoft.NET.Sdk.Web`, `net9.0`) that references
`DataGuard.Core`. It does nothing unless `DataGuardHealth:EnableHost` is set (`Program.cs:6-11`). When
enabled, it binds only to loopback (`HealthHostBinding.IsLoopbackOnly`). It runs a background refresh
of four local probes: snapshot file readable, baseline file readable, free disk, managed memory. It
maps `/health` routes only when `ExposeEndpoints` is also set. Both switches default to `false`
(`docs/observability/adr.md` ADR-015).

`src/DataGuard.Core/Health` (`HealthContracts`, `HealthProbeCoordinator`, `HealthStateStore`,
`LocalHealthProbes`) holds the probe model. Its only consumer in `src/` is `DataGuard.Host`. The CLI,
the analyzers and the adapters do not use it.

## Original goal said

The original goals describe a build-time and CI-time tool, not a long-running service:

- `research/muc_tieu/5.md:55`: "`Cli` đóng gói thành `dotnet tool` để chạy CI/CD không phụ thuộc IDE."
  (The CLI is packaged as a `dotnet tool` to run in CI/CD.)
- `research/muc_tieu/2.md:9`: "Tầng CI (nặng, chạy theo lịch/PR): toàn bộ diff-engine + kết nối DB
  chạy như một `dotnet tool` riêng biệt". (The heavy tier runs as a separate `dotnet tool` on a
  schedule or per PR.)

No goal document mentions an HTTP host, health endpoints or readiness probes.

## What exists

| Item | Measurement |
|---|---|
| Host code | 191 LOC, 5 files (`Program.cs`, `HealthEndpoints.cs`, `HealthHostBinding.cs`, `HealthHostOptions.cs`, `HealthRefreshService.cs`) |
| Core/Health code | 190 LOC, 4 files |
| Tests | In `DataGuard.Core.Tests`, which has a `ProjectReference` to `DataGuard.Host`: `HealthProbeTests` (9), `HealthHostBindingTests` (5), `HealthHostIntegrationTests` (4). Together 475 LOC, 18 tests. |
| Dependencies | ASP.NET Core shared framework (Web SDK). `DataGuard.Core.csproj` also declares `Microsoft.Extensions.Diagnostics.HealthChecks`, but no `.cs` file in `src/` uses that namespace, because `Core/Health` has its own probe model. The package still flows into the CLI and every adapter lock file. |
| Solution membership | Host is in `DataGuard.sln` and `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test` builds Host and runs the tests above. `scripts/verify_host_publish.sh` (publish plus loopback readiness smoke) exists, but no workflow calls it. |
| Release | No workflow publishes Host as an artifact. It has no `PackageId`. |
| Docs | `docs/03-components/core/health.md` (+ `.vi.md`), `docs/observability/adr.md` ADR-015 |

## Risks it adds

- **Attack surface:** an HTTP server (Kestrel) inside the product solution. It is loopback-only and
  off by default, but it is still a listening socket that someone can turn on.
- **Maintenance:** about 860 LOC including tests, built and tested on every CI run, for a deployment
  mode with no user and no release artifact.
- **Coupling:** `DataGuard.Core.Tests` depends on an ASP.NET Core project. `DataGuard.Core`, the
  package every consumer takes, declares a health-checks package that no source file uses.
- **Licence and release size:** no direct effect today, because Host is not shipped. The Core package
  still carries the unused health-checks dependency.

## Options

1. **Keep:** make the Host a supported deployment mode with a release artifact.
2. **Freeze:** leave it as it is, opt-in and unreleased, and add no features.
3. **Extract:** move `DataGuard.Host` and `DataGuard.Core/Health`, with their tests and
   `scripts/verify_host_publish.sh`, to the same separate repository or branch as the observability
   reference (ADR-0003). Both serve a hypothetical long-running host.
4. **Remove:** not available in this plan.

## Recommendation

**`extract`** (Host and Core/Health together)

- The goals define DataGuard as an analyzer plus a `dotnet tool`. A health host answers a question,
  "is the DataGuard service up?", that the product does not raise.
- Health has exactly one consumer, the Host. Moving both together keeps Core smaller. Dropping the
  unused `Microsoft.Extensions.Diagnostics.HealthChecks` reference from the published Core package can
  happen at the same time, or earlier on its own.
- Interim state until the owner picks a destination: `freeze`. It stays opt-in, loopback-only and
  unreleased.

## Owner decision

`pending`. (Fill in: decision, destination repository or branch, date, name.)

## Consequences

- When extracted, `DataGuard.Host` leaves `DataGuard.sln` and `DataGuard.CrossPlatform.slnf`. The 18
  health tests move with it, and `DataGuard.Core.Tests` drops its Host `ProjectReference`.
- Removing `DataGuard.Core.Health` changes the public API of `DataGuard.Core`. This ADR does not
  measure external consumers. `docs/decisions/ADR-20260930-dataguard-license-after-mit.md` recorded 0
  packages on nuget.org on 2026-09-30, but that needs re-checking at extraction time.
- `docs/03-components/core/health.md` and ADR-015 in `docs/observability/adr.md` move or are updated in
  the same change.

## Tóm tắt (VI)

`DataGuard.Host` (191 LOC, ASP.NET Core) và `DataGuard.Core.Health` (190 LOC) tạo một HTTP host kiểm tra
sức khỏe, chỉ bind loopback và mặc định tắt. Health chỉ có đúng một consumer là Host. Không workflow nào
phát hành Host, và mục tiêu gốc chỉ nói DataGuard là analyzer cộng `dotnet tool` chạy trong CI. Chúng
vẫn được build và test (18 test) ở mọi lần CI. Core còn khai báo package health-checks mà không file nguồn nào dùng. Khuyến
nghị: **extract** cả Host và Core/Health sang cùng repository hoặc branch riêng với bộ observability
(ADR-0003). Trong lúc chờ thì freeze. Quyết định của owner: pending.
