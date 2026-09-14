# Gaps, Unknowns and Blocking Questions

| Topic | Status | Why it matters | Where to find / who can answer | Blocking? |
|---|---|---|---|---|
| Production topology, environments, ingress and service identity | UNKNOWN | Determines collection path, trust boundary, network controls and deployment instrumentation | Platform/SRE deployment manifests, environment owners | Yes |
| Collector/backend (OTLP, Prometheus, log store, vendor) | UNKNOWN | Chooses SDK/export protocol, labels, auth and failure semantics | Observability platform owner/architecture decision | Yes |
| SLO/SLI, alert rules and on-call | UNKNOWN | Defines useful signals, burn-rate alerts and escalation | SRE/product owner/runbooks | Yes |
| Traffic, concurrency, payload/DB operation volume | UNKNOWN | Needed for queue sizing, sampling, cardinality and cost | Production telemetry/DB owners | Yes |
| Retention, residency, PII/PCI/customer-data classification | UNKNOWN | Governs fields, redaction, encryption, access and deletion | Security/privacy/compliance owners | Yes |
| RTO/RPO and failure budget | UNKNOWN | Determines telemetry durability and backpressure policy | Business continuity/service owner | Yes |
| Ownership and permission to change each surface | UNKNOWN | Prevents unauthorized instrumentation or deployment changes | CODEOWNERS, team registry, release owner | Yes |
| Auth/TLS contract for health endpoints | UNKNOWN (remote explicitly rejected in WIP Host) | Required before any remote probe exposure | Host/platform owner | Yes |
| Correlation/request/tenant IDs | UNKNOWN | Needed to join logs, metrics, traces without sensitive identifiers | Core/API owners and data policy | Yes |
| Runtime logging provider and sink | UNKNOWN | Determines structured log schema and transport | Host/CLI deployment configuration | Medium |
| Test/build status of dirty tree and WIP projects | UNKNOWN | Static inventory cannot establish deployability | CI owner; requires approved build/test | Medium |
| External package lifecycle/CVE/license status | UNVERIFIED_EXTERNAL | Affects supportability and security posture | Approved dependency scanner/advisory process | Medium |
| Supply-chain expected hash anchor and independent plugin verifier | UNKNOWN | Admission intentionally fails closed without these controls | Release/signing owner | Medium |
| Duplicate benchmark and solution omissions | CONFLICT | Scope affects build matrix and observability coverage | Repository owner/maintainers | Medium |
| Audit log single-writer/integrity contract | CONFLICT | Two append paths may produce different verification semantics | Security/data governance owner | Yes |

No SLO, traffic, compliance, retention, cost, topology, RTO/RPO, business criticality or ownership value was invented in this report.
