# Gated roadmap

Proposed, not committed, 2026-10-18. [Tiếng Việt](roadmap.vi.md) · [Evaluation](evaluation.md) · [Traceability](traceability.md)

All new modules below are **planned**; M0/M1 extend existing partial capabilities. Owners are accountable roles awaiting assignment. Dependencies are acceptance dependencies, not merely code availability. Each expansion needs three design partners with relevant incidents and an explicit disposition of affected Proposed ADRs.

| Module / owner | Scope and dependencies | Acceptance evidence |
|---|---|---|
| M0 safe foundation / core lead + security | Deterministic IR, provider support matrix, unknown/coverage, SQL/source spans, config/baseline; first dependency for all modules | Independent unsupported/permissions/error corpus; acquisition failure cannot become clean; no implicit build execution; normalized full/incremental equivalence on fixtures |
| M1 .NET/database depth / provider leads + QA | M0; missing/renamed objects, fields, nullability versus presence/default, length, precision/scale, result shapes, typed parameters, delta CI and symbol evidence | 100+ independently labeled mutations across declared provider/dialect scope; per-rule precision/recall/unknown with denominators; real disposable integration evidence before any provider GA claim |
| M2 procedures and rollout / provider lead | M1; app N-1/N × schema N-1/N compatibility and declared consumer inventory | Dynamic SQL, multiple result sets and unsupported outer-join nullability stay unknown; expand–migrate–contract fixtures; unregistered consumers never assumed safe |
| M3 wire semantics / .NET API lead | M1 + serializer ADR; separate input/output contracts, JsonPropertyName/Ignore, required/nullable, enum converters, decimal/int64, DateOnly/instant/local time | Golden comparisons against actual System.Text.Json payloads; custom converters unknown unless modeled; negative rename/missing/null/enum/time/collection/error-payload cases |
| M4 TypeScript/Zod/React / frontend lead | M3; strict TypeScript and runtime schema generation; explicit input/output transforms | Generated/runtime schema agreement; JS precision lost during JSON.parse is not repaired by validation; assertions are not runtime checks; only provable tenant/filter cache-key cases may block; scoped async/form fixtures, not general React lint |
| M5 query and business contracts / performance lead | M1/M2 and declared semantics; query-risk/N+1, concurrency tokens, idempotency | Measured query counts, no static performance guarantee; declared token propagation; valid SaveChanges must not be flagged as missing transaction; idempotency needs a declared business contract |
| M6 enterprise operation / security + release leads | M0/M1 quality gates and customer scope; offline operation, provenance, support lifecycle, pilot controls | Scoped operating profile, independent security review and customer acceptance; banking requires jurisdiction/customer review, never a blanket certification |

## Gate sequence

- G0 truthful safe baseline: capability/claim ledger, threat model, independent corpus, release pause and current identities preserved.
- G1 accurate advisory preview: M0/M1; exploratory shadow pilot target precision ≥95%, recall ≥80%, with sample sizes and uncertainty. Advisory only.
- G2 enterprise pilot: M6 after G1; proposed blocking target precision ≥99%, recall ≥95% on held-out declared scope, with confidence intervals and unknown coverage. Insufficient evidence means advisory, not rounding up to pass.
- G3 read-only agent pilot: stable evidence schema and G1; enterprise use also requires G2 controls. M3/M4 are needed only for full-stack claims. Same findings for humans and agents; controlled task evaluation before tool release.
- G4 banking evaluation: G2 plus customer-specific controls, independent penetration test and AI/data policies. Agent use additionally requires G3. Passing a pilot is not regulatory certification.
- G5 selective expansion: evidence from discovery and maintenance capacity justifies M2–M5 or a hosted service; no automatic SaaS/RBAC commitment.

## Capacity and sequencing

Illustrative horizons only: 0–3 months M0 and discovery; 3–6 months M1 plus narrow M2; 6–12 months selected M3/M4 and enterprise pilots; 12–24 months selected M5/agent/banking evaluation. Assumption: at least two backend/provider engineers, one QA/security-capable engineer, part-time product/release support, and frontend capacity before M4. No staffing is committed. Gates and incident evidence override dates; do not parallelize all modules with a small team.

Discovery and stop criteria are in [SA-EXP-007–012](research-backlog.md). No interviews, pilots, outreach or paid commitments have occurred in this task.
