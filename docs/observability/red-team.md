# Executive red-team report

| Requirement | Decision | Rationale / replacement |
|---|---|---|
| Observability bảo đảm SLA 99.99% | MODIFIED | Observability đo SLI, giảm MTTD/MTTR; SLO thuộc service/business owner. |
| Mọi backend cùng OTLP/gRPC | REJECTED | Protocol phụ thuộc backend; Collector adapter theo signal. |
| IncludeScopes tự tạo trace IDs | REJECTED | IDs đến từ active `Activity`; scope chỉ bổ sung state. |
| Hard-code exception API | REJECTED | Dùng `Activity.AddException` của package đã pin; API cũ được loại khỏi code. |
| Profiling bằng IServiceCollection | REJECTED | Profiler/eBPF là data plane deployment riêng, có kill switch. |
| Base class mọi workload | REJECTED | Composition: middleware/filter/interceptor/decorator/wrapper. |
| Instrument mọi method/object | REJECTED | Chỉ business boundaries có metadata ổn định. |
| Package latest | REJECTED | Pin exact versions sau discovery. |
| Một Collector config mọi môi trường | REJECTED | Agent/gateway overlays theo trust boundary. |
| Mọi HTTP 4xx là outage | REJECTED | Business rejection/authz tách khỏi technical failure. |
| Diagnostic logs thay audit ledger | REJECTED | Audit cần immutability, integrity, retention và approval riêng. |
| Queue = zero loss | MODIFIED | Queue bounded; báo drop và data-loss model. |
| TraceId làm metric label | REJECTED | Cardinality denial-of-service. |
| Thu body/secret để debug | REJECTED | Allowlist fail-closed, redaction và synthetic canary tests. |
| AOP proxy luôn tốt hơn | REJECTED | Native AOT/debuggability/coverage hạn chế; dùng adapter phù hợp. |
| Flame graph luôn ra source line | MODIFIED | Chỉ khi symbols/source mapping/backend hỗ trợ. |

Assumptions: no production topology, traffic, tenant model, retention, RTO/RPO or backend supplied; all are explicitly `UNKNOWN` in discovery and must be owner-resolved before rollout.
