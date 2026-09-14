# Threat model

Scope: application SDK, propagation carriers, Collector agent/gateway, Kubernetes control
plane, LGTM backends and the separate profiling plane. Owners must re-score these threats
against the real trust boundaries before rollout.

| Threat | Attack path | Impact | Mitigation | Detection | Residual risk / owner |
|---|---|---|---|---|---|
| PII leakage | Unbounded attributes or payloads enter a signal | Privacy/PCI breach | SDK allowlist, Collector redaction, canary tests, no body/parameter capture | Canary queries, redaction/drop counters | Runtime-generated fields; Security/privacy |
| Credential leakage | Authorization, cookie, token or secret reaches logs/traces | Account compromise | Never capture headers/claims; blocked keys/values; secret scanning | Pattern scan and backend access review | Third-party logger; Security |
| Baggage exfiltration | Caller injects customer/account data into W3C baggage | Cross-service PII spread | Allowlisted keys, URL escaping, strip at trust boundaries, size cap | Rejected-baggage counter and carrier tests | Misconfigured gateway; Platform |
| Log injection | Newline/control characters or forged event fields | Misleading incident evidence | Structured logging, bounded fields, sanitizer and collector redaction | Parser errors, event-shape tests | Sink-specific parser behavior; Security |
| Trace-context spoofing | Forged `traceparent`/`tracestate` crosses ingress | Trace confusion or tenant pivot | W3C parser, remote-context policy, trust-boundary reset | Invalid-context metric and gateway logs | Trusted gateway contract; Platform |
| Malformed propagation header | Invalid version, IDs or tracestate sent to consumer | Parser faults or dropped context | `TryParse`, fail-new-root, bounded carrier parsing | Malformed-header test and rate alert | Client library variation; Messaging |
| Oversized header DoS | Huge baggage/tracestate consumes parser memory | CPU/memory exhaustion | 8 KiB carrier cap, per-key/value caps, bounded queues | Rejection count, request saturation | Upstream proxy limits; SRE |
| Cardinality denial of service | IDs or raw URLs become span names/metric labels | Backend series/storage exhaustion | Stable operation names, finite allowlists, budgets and tests | Top-N names/series dashboards | Unknown traffic mix; SRE |
| Telemetry amplification | Retry loops, duplicate instrumentation or fan-out multiply signals | Cost and queue exhaustion | One provider/source/meter, recursion guard, bounded retries, sampling | Duplicate-span and bytes-ingested panels | Host composition mistakes; Platform |
| Log storm | Exception loop logs at every layer | Storage/ingestion outage and hidden signals | Source-generated/stable events, level policy, rate limits, no duplicate logging | Events/minute and exporter queue alerts | Incident debug bursts; Service owner |
| Collector compromise | Vulnerable or exposed collector pod reads all signals | Data exfiltration or pivot | Pinned digest, non-root, read-only FS, mTLS, NetworkPolicy, minimal RBAC | Image/SBOM scan, RBAC audit, pod security alerts | Cluster admin boundary; Platform |
| Backend credential theft | Secret refs or tenant headers exposed to workload | Cross-tenant data access | Secret manager/rotation, no inline secrets, least-privilege backend role | Secret access audit, cert/credential expiry alerts | Backend IAM implementation; Platform |
| Cross-tenant leakage | Routing or labels mix tenant streams | Regulatory breach | Tenant isolation, opaque bounded routing, per-tenant RBAC and residency | Cross-tenant query tests and access audit | Backend topology unknown; Compliance |
| Untrusted message headers | Broker producer controls propagation/baggage values | Spoofed correlation or injection | Parse/size/allowlist validation; never trust payload metadata | Header rejection/redelivery metrics | Broker ACLs; Messaging owner |
| Replay/duplicate message | Retry, replay or forged context reprocesses a command | Double effect or false SLO failure | Domain idempotency, span links per attempt, explicit duplicate result | Duplicate/replay metric and ledger check | Domain implementation; Payments owner |
| Supply-chain compromise | NuGet/image/dependency altered | Code execution or data theft | Exact versions/lock files, vulnerability/SBOM/signing policy, provenance review | Restore audit, signature/SBOM scan | Registry compromise; Security/platform |
| Debug endpoint exposure | Health/metrics/config or profiler endpoint exposed publicly | Information disclosure or control | NetworkPolicy, auth, no config dump, internal-only scrape | Ingress scan and endpoint audit | Ingress/controller drift; Platform |
| Profiling data exposure | PDB, source path or raw stack exported to profile backend | Source/business disclosure | Separate profile plane, restricted tenant, symbol policy, kill switch | Access audit and profile metadata scan | Profiler capability unknown; Performance |
| Source-line overclaim | Missing symbols/inlining interpreted as exact source | Wrong remediation or evidence | Report frame/method-level unless PDB/source mapping verified | Correlation acceptance test | JIT/backend behavior; Performance |
| Excessive Collector privileges | ClusterRole broader than metadata/resolver needs | Namespace/workload discovery or pivot | Read-only scoped Role plus minimal agent ClusterRole; no writes | RBAC review and admission policy | CNI/control-plane policy; Platform |
| Cross-cluster/region misrouting | Load-balancing resolver sends a tenant to another region | Residency breach/latency | Explicit resolver/tenant routing and region labels; deny by default | Route/tenant integration test | DNS/service-mesh behavior; Platform |
| Retention violation | Backend defaults retain restricted signals too long | Compliance breach and cost | Per-signal retention/deletion workflow, legal hold process | Retention configuration audit | Backend policy unknown; Compliance |
| Data-residency violation | Global gateway/backend stores data outside approved region | Regulatory breach | Region-local gateway/backend, egress allowlist, residency tags | Query/access location audit | Cloud topology unknown; Compliance |
| Certificate expiration | mTLS/backend cert expires while queues fill | Telemetry blindness and delayed diagnosis | Secret rotation, expiry alerts, bounded queues and fallback | TLS handshake/expiry metrics | Rotation ownership; PKI |
| Backend throttling/partition | Loki/Tempo/Mimir returns 429/5xx or network fails | Bounded telemetry loss and retry storm | Timeout/retry limits, queue caps, sampling downgrade, fail-open business path | Export failure/refused/drop ratios | No zero-loss guarantee; Platform |
| Queue/disk exhaustion | Outage exceeds bounded memory/disk budget | Signal drops or pod OOM | Formula-based capacity, memory limiter, PDB, explicit drop policy | Queue utilization, refused/export metrics | RPO/disk class unknown; SRE |
| Diagnostic/audit confusion | Loki logs used as transaction ledger | Non-repudiation/regulatory failure | Separate immutable, integrity-protected audit store and IDs | Audit-store reconciliation | Owner approval required; Compliance |

The model deliberately treats diagnostic telemetry, security events and immutable audit records
as separate data products. Residual risks are not waived by a successful local build.
