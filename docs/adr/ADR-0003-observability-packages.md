# ADR-0003: Observability packages (`DataGuard.Observability`, `.AspNetCore`, `.Messaging`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4 and §3.3 (dead/orphan), recommendation 22

## Context

Three class libraries form an OpenTelemetry SDK starter kit for a long-running .NET service:

- `DataGuard.Observability` (`CoreObservability.cs`): `AddCoreObservability`, `ObservedOperationAttribute`,
  a business-operation observer, Meter `DataGuard.Observability`, ActivitySource
  `DataGuard.Observability.Business`. An OTLP exporter is wired only when `RemoteExportEnabled`
  (default `false`) and `OtlpEndpoint` are set.
- `DataGuard.Observability.AspNetCore`: ASP.NET Core middleware and endpoint helpers.
- `DataGuard.Observability.Messaging`: W3C trace-context propagation and messaging metrics.

None of them references `DataGuard.Core`, and no project under `src/` references any of them
(`grep -rl DataGuard.Observability src --include=*.csproj` finds only their own files). Their own
documentation, `docs/observability/README.md`, describes them as a "compatibility/reference adapter
cho một host khác nếu sau này có owner contract" that the DataGuard CLI and library never start.

## Original goal said

The original goals say nothing about observability, OpenTelemetry or service hosting. The closest
constraint is the product shape:

- `research/muc_tieu/1.md:49-50`: "Gói NuGet chính chứa Roslyn analyzer ... Một CLI/MSBuild task đi
  kèm (`dotnet tool install`) chuyên trách lớp 2". (The product is a NuGet analyzer plus a companion
  CLI or MSBuild task for the DB layer.)
- `research/muc_tieu/2.md:27`: "Không bao giờ tự đọc connection string trực tiếp trong code chính ...
  tool không log giá trị." (Never read connection strings directly in the main code, and never log
  them.) This argues for the least telemetry egress code inside the product.

## What exists

| Item | Measurement |
|---|---|
| Production code | 921 LOC: `DataGuard.Observability` 533 (1 file), `.AspNetCore` 141 (1 file), `.Messaging` 247 (2 files) |
| Tests | `tests/DataGuard.Observability.Tests`: 1,027 LOC, 3 files (`AdapterTests` 11, `CoreObservabilityTests` 22, `SensitiveDataAndCardinalityTests` 5 = 38 tests) |
| Dependencies | `OpenTelemetry` 1.19.1, `OpenTelemetry.Extensions.Hosting` 1.19.1, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1, `OpenTelemetry.Instrumentation.AspNetCore` / `.Http` / `.Runtime` 1.19.0 |
| Solution membership | All four projects are in `DataGuard.sln` and `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test` builds them and runs the 38 tests. |
| Release | Each project sets a `PackageId`. `release.yml` `build-and-test` and `build_release.yml` `package-nuget` run `dotnet pack DataGuard.CrossPlatform.slnf`, so three `DataGuard.Observability*.nupkg` files are produced, signed and passed to `publish-nuget`. |
| Docs | `docs/observability/` (reference architecture, collector and Kubernetes samples, runbooks, `adr.md` ADR-001..015) |

## Risks it adds

- **Release surface:** three extra NuGet packages under the `DataGuard` prefix that no DataGuard user
  needs for contract validation. Publishing them advertises support for a service-hosting use case the
  product does not have.
- **Dependency churn:** six OpenTelemetry packages plus their ASP.NET Core and hosting dependencies.
  These packages bring Dependabot updates, licence allow-list entries in `ci.yml` and restore and CI
  time on every run.
- **Attack surface:** OTLP export code in the product repository. It is off by default, but it is a
  network egress path in the same solution as code that handles database credentials.
- **Confusion:** a second telemetry stack next to `DataGuard.Core.Telemetry`. See ADR-0010.
- **Maintenance:** about 1,950 LOC including tests with zero consumers.

## Options

1. **Keep:** treat the packages as part of the product.
2. **Freeze:** keep them in CI. Stop publishing them (`IsPackable=false`) and add no features.
3. **Extract:** move the three projects, their tests and `docs/observability/` to a separate
   repository or owner branch, as a reference implementation that can consume `DataGuard.Core` meters.
4. **Remove:** not available in this plan.

## Recommendation

**`extract`**

- No original goal and no `src/` consumer needs these packages. Their own docs call them a reference
  adapter for "another host".
- Extracting them removes three published packages and the OpenTelemetry dependency tree from the
  product release, without losing the work.
- If extraction is delayed, the interim state is `freeze` with `IsPackable=false`, so the packages are
  no longer published while they wait.

## Owner decision

`pending`. (Fill in: decision, destination repository or branch, date, name.)

## Consequences

- When extracted, the four projects leave `DataGuard.sln` and `DataGuard.CrossPlatform.slnf`, and
  `docs/observability/` moves with them. Inbound links, `docs/00-directory-tree/*` and the licence
  allow-list entries that only OpenTelemetry needs are updated in the same change (governance cleanup
  rule 2).
- A host that wants OpenTelemetry for DataGuard subscribes to the `DataGuard.Core` meter with the
  standard OpenTelemetry SDK (ADR-0010).

## Tóm tắt (VI)

Ba thư viện Observability (921 LOC, 1.027 LOC test với 38 test) là bộ khởi đầu OpenTelemetry cho một
service chạy lâu. Không project nào trong `src/` tham chiếu chúng, và mục tiêu gốc không nhắc gì đến
observability. Dù vậy chúng vẫn được `dotnet pack` và đẩy lên NuGet cùng release, kéo theo sáu gói
OpenTelemetry và một đường xuất OTLP ra mạng. Khuyến nghị: **extract** cả ba project, test và
`docs/observability/` sang repository hoặc branch riêng do owner chọn. Trong lúc chờ thì freeze và đặt
`IsPackable=false` để ngừng phát hành. Quyết định của owner: pending.
