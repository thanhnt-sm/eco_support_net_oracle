# StackAccord — Scoped capabilities

[Tiếng Việt](feature-showcase.vi.md) · [Overview](product-overview.md) · [Capability ledger](../product/capabilities.md)

Source reconciliation on 2026-10-18, baseline `ca68a3159425495f771da866188df75a8237ab4b`. This describes implementation, not a new test run or release acceptance. DataGuard technical identities and the Marketplace pause remain.

| Capability | Present in source | Limits |
|---|---|---|
| Validation | Rules selected by provider/mode/configuration | No 100% guarantee; unknown and missing metadata do not prove correctness |
| Snapshot/baseline | Persist/compare evidence and drift | Snapshots may be stale/incomplete; a baseline does not prove a defect was fixed |
| CLI/IDE | CLI pipeline, analyzers/generators and extensions | Different scopes; host/artifact acceptance remains separate; latency unmeasured |
| Assessment | Inventory and upgrade advice | Analysis only; no automatic edit or migration execution |
| Reporting | Text, SARIF and exports | Output does not replace checking completion status, coverage and provenance |
| Security | Credential provider, policies and configurable audit | Not certification; audit depends on configuration; rotation warning is not rotation |
| Observability | Components and reference/starter | Not proof of a production service or K8s readiness |

## Providers

[Oracle](../03-components/adapters/oracle-adapter.md), [SQL Server](../03-components/adapters/sqlserver-adapter.md), [MySQL](../03-components/adapters/mysql-adapter.md) and [PostgreSQL](../03-components/adapters/postgresql-adapter.md) have separate adapters. Metadata/result-set capabilities differ; an adapter name does not imply parity or general procedure-body parsing. Use the [source-generated rule matrix](../../README.md) and individual adapter documentation for scope.

## Actual TypeScript export

`TypeScriptContractWriter` consumes `EntityDescriptor`. For example, a DateTime descriptor property `CreatedAt` and nullable string property `Email` produce:

```typescript
export interface CustomerDto {
  "CreatedAt": string;
  "Email"?: string;
}
```

Properties are ordered by name; property names come from descriptors (with collision/escaping handling), without a camelCase/PascalCase switch. Nullable becomes `?`, not `| null`. DateTime/DateTimeOffset/DateOnly/TimeOnly map to string; unrecognized types become `unknown`; decimal/int64 map to number without proving JavaScript precision preservation.

`ValidateCommand` selects entity descriptors and returns after export, before the later validation pipeline. Export does not verify serializers, converters, required/presence semantics or React consumers. M3/M4 are planned, not released runtime capabilities.

## Evidence-based use

Credential resolution checks environment before configured backends. Plaintext fallback requires permission; audit is configuration-dependent. See [security](../03-components/core/security.md). Use the actual binary's help and [CLI reference](../03-components/tooling/cli.md); avoid commands copied from old plans.

Quickfixes require diff/input review and post-checks. Performance claims need workload/hardware/SHA/run evidence; [evaluation](../product/evaluation.md) currently defines targets pending baseline. The [roadmap](../product/roadmap.md) owns React, agent and enterprise scope; no automatic-safe-repair or banking-certification claim is made.
