# Instrumentation decision matrix

The platform deliberately uses more than one technique: a native signal source covers
framework/dependency telemetry, while an explicit boundary adapter covers the business
operation. “Deferred” means the repository did not contain the client package or workload
needed to verify a concrete adapter; it is not a claim that the workload is unsupported.

| Technique | HTTP MVC | Minimal API | gRPC | Worker/job | Kafka/RabbitMQ | Async/cancel | Generic/interface constraints | Reflection/AOT/trimming | Debug/build safety | Runtime/ops cost | Decision |
|---|---:|---:|---:|---:|---:|---:|---|---|---|---|---|
| OTel SDK instrumentation | Yes | Yes | Only when client/server emits native `Activity` | Runtime only | Client-specific | Native async | No business interface | Strong; no proxy | Compile-time package, visible spans | Low–medium; one provider | Primary baseline |
| OTel automatic instrumentation | Yes | Yes | Package/profiler dependent | Runtime dependent | Package/profiler dependent | Usually, but verify context | No interface; profiler startup | Native AOT/trimming and profiler conflicts | Hidden control flow; version matrix required | Medium–high; startup/profiler | Optional canary |
| ASP.NET middleware | Yes | Yes (pipeline) | No | No | No | Yes; request token passed | No interface; one request boundary | AOT-friendly | Explicit control flow/testable | Low | Use for endpoint metadata |
| MVC action filter | Yes | No | No | No | No | Yes; `RequestAborted` | MVC action/controller metadata | AOT-friendly; reflection for attribute lookup | Framework-specific, visible | Low | Use for selected MVC actions |
| Minimal API endpoint filter | No | Yes | No | No | No | Yes; `RequestAborted` | `IEndpointFilter` and endpoint metadata | AOT-friendly | Explicit and testable | Low | Use for selected endpoints |
| gRPC interceptor | No | No | Yes | No | No | Yes; streaming/cancel must be propagated | gRPC interceptor contracts; client version required | Generally AOT-friendly | Explicit, but streaming tests required | Low–medium | Add after package discovery |
| Kafka producer/consumer decorator/interceptor | No | No | No | Consumer worker | Yes | Yes; ack/retry/DLQ are client-specific | Concrete producer/consumer API required | Depends on client/proxy | Header/retry semantics must be visible | Low–medium | Add after client discovery |
| RabbitMQ producer/consumer decorator/interceptor | No | No | No | Consumer worker | Yes | Yes; ack/nack/redelivery are client-specific | Concrete channel/consumer API required | Depends on client/proxy | Poison/DLQ behavior must be visible | Low–medium | Add after client discovery |
| DI decorator / generic operation wrapper | Via application service | Via application service | Via application service | Yes | Via producer/consumer adapter | Yes; supports `Task`/`ValueTask` when wrapped | Interface or explicit delegate; no private/static interception | Strong; no runtime proxy | Best testability and visible flow | Low; caller opt-in | `IBusinessOperationObserver` baseline |
| `DispatchProxy` / Castle DynamicProxy | Interface/controller dependent | No | Interface dependent | Interface dependent | Interface dependent | Usually; verify `ValueTask`/streaming | Requires virtual/interface methods; cannot cover private/static | Reflection/proxy trimming/AOT gaps | Hidden flow and stack complexity | Medium–high | Defer; no universal base class |
| Source generator/analyzer | Metadata validation | Metadata validation | Metadata validation | Metadata validation | Metadata validation | Build-time shape checks | Requires generator package and supported syntax | Strong after generated code is AOT-safe | Best naming/build safety; generator complexity | Build-time cost | Future hardening |
| Native .NET profiler / eBPF | Process-wide | Process-wide | Process-wide | Process-wide | Process-wide | N/A | No source API; deployment/runtime/OS contract | Profiler, kernel, symbols and privilege constraints | Separate data plane; can obscure inlining | Medium–high; measure overhead | Separate Pyroscope evaluation |

Coverage rules:

- One selected business boundary per request/message prevents duplicate business spans; the
  recursion guard is a safety net, not a license to enable every adapter simultaneously.
- Minimal APIs may choose either `WithObservedOperation` plus `UseObservedOperations` middleware
  or `AddObservedOperationEndpointFilter` on a `RouteHandlerBuilder`; do not register both for
  the same route.
- The generic observer preserves the original exception and cancellation token and never reads
  arguments or return values. A host-provided result classifier may inspect a result locally but
  can emit only a finite allowlisted result label.
- Private/static methods, DTO mappers, getters/setters and repository helpers are intentionally
  not intercepted. Dependency instrumentation, when verified for the actual client, owns the
  database/cache/network span.
- Automatic instrumentation and profiler injection require a canary because they can conflict
  with an existing CLR profiler, Native AOT/trimming, or a service mesh's native spans.

## Continuous profiling options

Profiling is a separate data plane and is not created by `AddCoreObservability`.

| Option | Capability and constraints | Production decision |
|---|---|---|
| A — native Pyroscope/.NET profiler | CLR profiler injection can collect CPU/wall-time/allocation/lock/exception profiles according to the exact profiler/runtime build. It requires deployment-level environment variables, a compatible Linux/architecture/runtime image, a symbol/PDB policy, an authenticated export path and an overhead canary. It may conflict with another CLR auto-instrumentation profiler. Source-line attribution is conditional on symbols and backend mapping. | Candidate for a controlled canary only; no default until workload, OS/architecture, profiler version, symbols and overhead budget are approved. |
| B — OpenTelemetry/eBPF profile pipeline | Host-wide CPU sampling through the profiling Collector/eBPF path is separate from traces/metrics/logs. The Profiles signal is Alpha, eBPF requires Linux and privileged host access, and symbolization/protocol compatibility can change. It needs a restricted DaemonSet, host mounts/PID namespace, kernel review and a rollback switch. | Development/integration evaluation first; do not place on a banking production critical path without security and performance approval. |

The reference default is **defer**. A selected option must publish exact profiler/Collector/
Pyroscope versions, OS/architecture support, sampling rate, CPU/allocation overhead, symbol
retention and tenant controls. Trace-profile links require matching service/version/time labels
and backend support; otherwise the runbook reports only method/frame-level diagnosis.
