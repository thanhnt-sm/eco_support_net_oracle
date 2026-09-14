# Data classification and allowlist

The default is allowlist/fail-closed. “Yes” means the value may appear only in the narrowly
defined form shown in Treatment; it does not authorize raw customer or security data. The
application SDK and Collector are both enforcement points, and profiles carry metadata only.

| Data element | Classification | Logs | Traces | Metrics | Baggage | Profiles metadata | Treatment |
|---|---|---:|---:|---:|---:|---:|---|
| Transaction ID | Restricted PII | No | No | No | No | No | Keep in the transaction store; use an opaque support-case reference outside telemetry. |
| Account ID | Restricted PII | No | No | No | No | No | Never emit raw or reversible values. |
| Customer ID | Restricted PII | No | No | No | No | No | Never emit raw or reversible values. |
| Correlation ID | Operational | Yes | Yes | No | No | Metadata only | Use only for correlation; bounded length and generated at the trust boundary. |
| Trace ID | Operational | Yes | Yes | No | No | Metadata only | W3C context field; never a metric label or business identifier. |
| Span ID | Operational | Yes | Yes | No | No | Metadata only | Correlation field only; never a metric label. |
| Tenant ID | Confidential | Allowlisted opaque token | Allowlisted opaque token | Bounded tenant class only | Strip at untrusted boundary | No | Hash/tokenize for cross-tenant routing; owner-approved tenant isolation required. |
| Branch ID | Confidential | Allowlisted opaque token | Allowlisted opaque token | No | Strip by default | No | Allow only when an owner-approved bounded list exists. |
| Workload identity | Confidential | Allowlisted opaque value | Allowlisted opaque value | No | Strip at untrusted boundary | No | Use only a bounded deployment identity class; never export a token, subject claim or credential. |
| Product code | Internal | Yes | Yes | Bounded enum | No | No | Normalize to an enumerated code set. |
| Error code | Internal | Yes | Yes | Bounded enum | No | No | Use normalized taxonomy, never raw exception text. |
| HTTP route | Internal | Template only | Template only | Template only | No | No | `/accounts/{accountId}`-style route template; reject raw URL. |
| Raw URL/query | Restricted | No | No | No | No | No | Drop path values, query strings and fragments unless separately reviewed. |
| SQL statement | Restricted | No | No | No | No | No | Record only normalized dependency name; no text or parameters. |
| SQL parameters | Secret/PII | No | No | No | No | No | Never collect, including in exception/debug processors. |
| Message key | Confidential | No | No | No | No | No | Use bounded topic/operation metadata; key contents stay in the broker/domain. |
| Message ID | Confidential | No | No | No | No | No | Correlate with a one-way hash only in a controlled support workflow. |
| Exception type | Internal | Type only | Type only by default | Bounded normalized type | No | No | Allow type/category; do not use as an unbounded metric label. |
| Exception message | Restricted | No | No | No | No | No | Default event omits it; `CaptureExceptionDetails` is non-production-only and gateway-blocked. |
| Authentication claims | Secret/PII | No | No | No | No | No | Derive only a bounded authorization result (`allowed|denied`). |
| User ID | Restricted PII | No | No | No | No | No | Use role/result metadata, not the principal identifier. |
| Device ID | Restricted PII | No | No | No | No | No | Never export; retain in approved fraud/security systems. |
| IP address | Personal data | No | No | No | No | No | Do not export by default; use coarse, owner-approved network class only. |

Synthetic canary tests seed PAN-like, account, customer, email, phone, authorization-token,
SQL-parameter and message-payload values and assert absence from activity events/tags and
bounded metrics. A deployed test must repeat the search across Loki, Tempo, Mimir and
Pyroscope metadata before any production rollout.
