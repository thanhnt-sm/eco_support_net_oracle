# ADR-0006: Telemetry HTTP export in `DataGuard.Core.Telemetry`

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4, recommendation 22

## Context

`src/DataGuard.Core/Telemetry` has two files:

- `TelemetryCollector.cs` (886 LOC): a `System.Diagnostics.Metrics` Meter named `DataGuard.Core`, a
  bounded in-process event queue, a circuit breaker and two export paths.
  - The **local file sink**, `FileObservabilitySink`, is the default whenever telemetry is enabled
    and no exporter is injected.
  - The **HTTP export**, `ExportEventsAsync`, POSTs NDJSON with a shared static `HttpClient` and a
    5 s timeout. It runs only when `RemoteExportEnabled` is `true`, or when a caller injects its own
    export delegate. `IsAllowedExportEndpoint` accepts only HTTPS, or HTTP on loopback, with no
    user-info, query or fragment.
- `ObservabilityFileSink.cs` (562 LOC): an allow-listed NDJSON archive per UTC day
  (`docs/observability/adr.md` ADR-014, "Local text archive is the product primary").

Who reaches this code:

- The only `src/` consumer is the public library API, `ValidationPipeline` in
  `PublicApi/PublicApiSurface.cs` (`config.EnableTelemetry`, `WithTelemetry`,
  `CreateTelemetryCollector`).
- The CLI reads the `EnableTelemetry` configuration key (`Program.cs:2386`), and `IdeSafePolicy`
  clears it (`IdeSafePolicy.cs:115-118`). No CLI code path creates a `TelemetryCollector`, so the key
  has no effect in the CLI. The red-team summary described this as "not consumed in src".
- The comment on `ExportEventsAsync` says the NDJSON is "compatible with OTLP/HTTP collectors".
  ADR-014 says the opposite: the envelope "is not OTLP wire data".

## Original goal said

The goals do not mention telemetry or metrics export. The relevant constraints are:

- `research/muc_tieu/2.md:27`: "Không bao giờ tự đọc connection string trực tiếp trong code chính —
  bắt buộc thông qua injection từ biến môi trường/secret vault do người dùng cấu hình, tool không log
  giá trị." (Secrets only by injection, and never logged.) The less a validator sends over the
  network, the easier this is to keep true.
- `research/muc_tieu/3.md:27`: "`DataGuard.Core` ... Không phụ thuộc bất kỳ driver vendor nào." (Core
  depends on no database vendor driver.) The intent is that Core stays the small, trusted part.

## What exists

| Item | Measurement |
|---|---|
| Production code | 1,448 LOC in 2 files (`TelemetryCollector.cs` 886, `ObservabilityFileSink.cs` 562). The HTTP-specific part (default sink, endpoint validation, static client, remote-flush branch) is a small fraction of `TelemetryCollector.cs`. |
| Tests | `TelemetryTests` (20 tests, 426 LOC) and `ObservabilityFileSinkTests` (7 tests, 246 LOC) in `DataGuard.Core.Tests`. Egress is tested through an injected sink, not a real network. |
| Solution membership | Part of `DataGuard.Core`, so it is in `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test`. |
| Release | Ships in the `DataGuard.Core` package and therefore in every CLI artifact. |
| Docs | `docs/03-components/core/telemetry.md` (+ `.vi.md`), `docs/observability/local-file-observability.md`, `docs/observability/adr.md` ADR-014 and ADR-015 |

## Risks it adds

- **Attack surface:** an HTTP POST path in Core, the package every consumer and every CLI build
  carries. It is off by default and endpoint-checked, but the code still ships everywhere.
- **Data classification:** event details can include workspace-derived values when
  `IncludeEventDetails` is on. Today the file sink is the only path most users see. A remote path
  makes the classification rules in `docs/observability/data-classification.md` a release-blocking
  concern.
- **Misleading configuration:** `EnableTelemetry` in `.dataguard.yml` does nothing in the CLI. The
  "OTLP-compatible" code comment contradicts ADR-014.
- **Duplication:** the HTTP export overlaps with the OTLP exporter in `DataGuard.Observability`
  (ADR-0003, ADR-0010).
- **Licence and release size:** no extra package. The cost is code size and review effort in Core.

## Options

1. **Keep:** keep the HTTP export as a supported Core feature and wire `EnableTelemetry` in the CLI.
2. **Freeze:** keep it as it is, off by default, library-only, with no new exporters.
3. **Extract:** remove the built-in HTTP sink from Core. Core keeps the Meter, the queue, the local file
   sink and the injectable `Func<string, string, Task>` export delegate. An HTTP or OTLP exporter lives
   outside Core, in the observability reference repository from ADR-0003 or in the host that wants it.
4. **Remove:** not available in this plan.

## Recommendation

**`extract`** (the HTTP export path only)

- The CLI never uses it, and the library already accepts an injected exporter. The built-in HTTP sink
  adds egress code to Core without adding a capability that the injection point does not already give.
- With the HTTP code gone, Core's telemetry has no network path at all. That is the simplest posture
  against expert 3's concern and matches ADR-014's "local text archive is the product primary".
- The rest of `Core/Telemetry` stays. See ADR-0010 for which telemetry stack remains.

## Owner decision

`pending`. (Fill in: decision, date, name.)

## Consequences

- `TelemetryConfig.RemoteExportEnabled` and `ExportEndpoint` become meaningful only with an injected
  exporter. The default `ExportEventsAsync` and the static `HttpClient` leave Core. The HTTP-specific
  tests move with the exporter.
- A separate follow-up decides what `EnableTelemetry` means in the CLI. Either it wires the local file
  sink, or the key is documented as library-only. This ADR does not decide that. Note that plan Phase
  4.2 routes the CLI's `ValidateContractsAsync` through `ValidationPipeline`. Once that lands,
  `EnableTelemetry` may start to take effect in the CLI through `PublicApiSurface.cs:68`, so this
  follow-up should be settled together with 4.2.
- Fix the "OTLP-compatible" comment when the code moves.

## Tóm tắt (VI)

`DataGuard.Core.Telemetry` (1.448 LOC, 27 test) có Meter `DataGuard.Core`, file sink cục bộ, và một
đường xuất NDJSON qua HTTP POST, mặc định tắt và chỉ cho HTTPS hoặc loopback. CLI không bao giờ tạo
`TelemetryCollector`, nên khóa `EnableTelemetry` trong CLI không có tác dụng. Chỉ API thư viện
`ValidationPipeline` dùng phần này. Mục tiêu gốc không nhắc telemetry và muốn Core nhỏ, ít bề mặt tấn
công. Khuyến nghị: **extract** riêng đường xuất HTTP ra khỏi Core, đưa sang repo observability
(ADR-0003) hoặc để host tự cung cấp qua delegate đã có. Core giữ Meter, hàng đợi và file sink cục bộ.
Quyết định của owner: pending.
