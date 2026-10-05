# ADR-0010: One telemetry stack: `DataGuard.Core.Telemetry` vs `DataGuard.Observability*`

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4, recommendation 22
- **Related:** ADR-0003 (Observability packages), ADR-0006 (Telemetry HTTP export),
  `docs/observability/adr.md` ADR-011, ADR-014 and ADR-015

## Context

The repository has two independent telemetry stacks:

| Aspect | Stack A: `DataGuard.Core.Telemetry` | Stack B: `DataGuard.Observability*` |
|---|---|---|
| Location | Inside `DataGuard.Core` (2 files, 1,448 LOC) | Three separate projects (4 files, 921 LOC) |
| Instrumentation API | BCL `System.Diagnostics.Metrics`, Meter `DataGuard.Core` | OpenTelemetry SDK 1.19, Meter `DataGuard.Observability`, ActivitySources `DataGuard.Observability.Business` / `.Messaging` |
| Default output | Local NDJSON archive per UTC day (`FileObservabilitySink`) | None. Exporters are configured by the host. |
| Remote output | NDJSON over HTTP POST (`RemoteExportEnabled`, default off) | OTLP exporter (`RemoteExportEnabled`, default off) |
| Configuration | `TelemetryConfig` record | `CoreObservabilityOptions` class, `configuration.schema.json` |
| Extra package dependencies | None | Six OpenTelemetry packages and the ASP.NET Core framework |
| Consumers in `src/` | `ValidationPipeline` (public library API) | None |
| Tests | 27 (`TelemetryTests`, `ObservabilityFileSinkTests`) | 38 (`DataGuard.Observability.Tests`) |

The stacks are not connected. `AddCoreObservability` subscribes only to its own meters and sources
(`AddMeter("DataGuard.Observability")`, `AddMeter("DataGuard.Observability.Messaging")`). It does not
subscribe to `DataGuard.Core`. Wiring stack B into a host therefore collects nothing that DataGuard's
validation emits.

## Original goal said

The goals do not mention telemetry. Two constraints apply:

- `research/muc_tieu/3.md:27`: "`DataGuard.Core` ... Không phụ thuộc bất kỳ driver vendor nào." The
  goals intend Core to stay small and dependency-light.
- `research/muc_tieu/5.md:55`: "`Cli` đóng gói thành `dotnet tool` để chạy CI/CD không phụ thuộc IDE."
  The product runs as a short-lived CLI process in CI, not as a service with a telemetry backend.

The repository's own observability ADRs agree. ADR-014 says "DataGuard is a CLI/library with no
required backend" and that the local text archive is the product primary. ADR-011 says "do not enable
both duplicate producers" (for span metrics).

## What exists

See the table above and the measurements in ADR-0003 and ADR-0006. Both stacks are in
`DataGuard.CrossPlatform.slnf` and tested in `ci.yml` `build-and-test`. Stack A ships inside
`DataGuard.Core`. Stack B ships as three NuGet packages through `dotnet pack` of the slnf in
`release.yml`.

## Risks of keeping both

- **Two answers to one question:** contributors and users cannot tell which stack is canonical. Docs
  under `docs/observability/` describe stack B as a reference and stack A as the product path, but
  both are packaged and released.
- **Double maintenance:** two meters, two exporter paths, two `RemoteExportEnabled` flags, two
  configuration schemas, two test suites.
- **Two egress paths:** NDJSON HTTP in Core plus OTLP in stack B, each needing review against
  `docs/observability/data-classification.md`.
- **False integration:** a host that adds `AddCoreObservability` gets no DataGuard validation
  metrics, because the meters differ.

## Options

1. **Stack A stays (reduced), stack B is extracted.** Core keeps the BCL Meter `DataGuard.Core`, the
   bounded queue, the local file sink and the injectable export delegate. The HTTP sink leaves Core
   (ADR-0006). Stack B leaves the repository (ADR-0003). A host that wants OpenTelemetry calls the
   standard SDK, `AddMeter("DataGuard.Core")`, in its own code.
2. **Stack B becomes canonical.** Core instruments through the OpenTelemetry API, and stack B becomes
   the official integration package. Stack A's exporters are removed.
3. **Keep both, but connect them.** Stack B also subscribes to `DataGuard.Core`, and both keep
   shipping.

## Recommendation

**Option 1.** Stack A stays (`keep`, reduced as in ADR-0006). Stack B is extracted (`extract`, as in
ADR-0003).

- A BCL `Meter` is the dependency-free integration contract. Any OpenTelemetry, Prometheus or
  `dotnet-counters` consumer can read `DataGuard.Core` without DataGuard shipping an OpenTelemetry
  package. This keeps Core in line with `3.md:27`.
- The CLI is a short-lived CI process with no backend (`5.md:55`, ADR-014). A local file archive fits
  it, and an OpenTelemetry SDK pipeline does not.
- Option 2 would put six OpenTelemetry packages into the product dependency graph. The gateway
  topology it needs is still "Blocked until topology/backend/tenant contract is supplied"
  (`docs/observability/adr.md` ADR-004).
- Option 3 keeps all the duplication and only fixes the false integration.

## Owner decision

`pending`. (Fill in: decision, date, name. Should match the decisions on ADR-0003 and ADR-0006.)

## Consequences

- After ADR-0003 and ADR-0006 are carried out, the product has one telemetry stack: Meter
  `DataGuard.Core` plus the local file sink, with no built-in network export.
- `docs/03-components/core/telemetry.md` documents `DataGuard.Core` as the meter name for hosts to
  subscribe to. The extracted observability repository documents `AddMeter("DataGuard.Core")` as its
  integration point.
- `docs/observability/adr.md` ADR-014 stays true. ADR-015's "legacy OTLP exporter remains compiled"
  becomes historical once the extraction happens.

## Tóm tắt (VI)

Repo đang có hai bộ telemetry độc lập. Bộ A là `DataGuard.Core.Telemetry`: Meter BCL `DataGuard.Core`,
file sink cục bộ, xuất NDJSON qua HTTP, và có consumer là `ValidationPipeline`. Bộ B là
`DataGuard.Observability*`: OpenTelemetry SDK, Meter riêng, xuất OTLP, và không có consumer. Hai bộ
không nối với nhau, nên host dùng `AddCoreObservability` cũng không thu được metric validate nào của
DataGuard. Khuyến nghị: giữ bộ A làm stack duy nhất, thu gọn về Meter, hàng đợi, file sink và delegate
xuất (bỏ HTTP theo ADR-0006). Bộ B thì extract theo ADR-0003. Host nào cần OpenTelemetry tự gọi
`AddMeter("DataGuard.Core")`. Cách này giữ Core nhẹ như 3.md yêu cầu và hợp với bản chất CLI chạy ngắn
trong CI. Quyết định của owner: pending.
