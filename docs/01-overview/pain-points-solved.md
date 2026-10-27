# StackAccord — Problems addressed within supported scope

[Tiếng Việt](pain-points-solved.vi.md) · [Capabilities](feature-showcase.md)

Source-reviewed on 2026-10-18 at `ca68a3159425495f771da866188df75a8237ab4b`. These are evaluation scenarios, not customer incident statistics or promises to prevent every failure. DataGuard identities and the Marketplace pause remain. Source was read; databases/editors were not run in this documentation batch.

| Problem | Behavior present in source | Limit to test |
|---|---|---|
| 1. Procedure contract mismatch | DG101/DG002/DG003 resolve calls against catalog signatures/types/directions | Depends on extraction, catalog, overload resolution and type tables; `StrictProcedureContracts` affects severity; unknown is not pass |
| 2. Result-shape drift | DG004 compares projection names with properties/ColumnName; DG018 is a separate live-shape path | DG004 is not type comparison; unextractable projections are skipped; extras are reported only above half the accepted property/column-name count |
| 3. Oracle CHAR/BYTE | DG007 compares length; DG008 estimates worst-case bytes from charset, UTF-16 units and capacity | Metadata/assumption-based risk, not actual payload-byte measurement or guaranteed ORA-12899 prevention |
| 4. NVARCHAR2 fallback | DG009 currently warns for string-like properties without MaxLength against CLOB/NCLOB | Not every NVARCHAR2 column/provider setting; the current DG009 branch does not consult Unicode annotations |
| 5. Missing SQL objects | DG015/DG016 compare supported parsed references against schema | Does not identify AI authorship; permissions/snapshot age/syntax constrain evidence; not proof of all SQL |
| 6. Name mapping | DG006 honors configured naming in CLI/API: ExactMatch compares names without transformation, ignoring case; conversion modes retain snake/Pascal matching | No arbitrary custom naming policy or provider-specific quoted-identifier case semantics; full validation remains default |
| 7. Nullability | DG005 resolves exactly one TableName match and compares property/column nullability; Required overrides nullable | Missing/ambiguous table or absent column skips comparison; not API/serializer validation |
| 8. IDE and CI feedback | Local generators/analyzers; `dataguard validate` selects its provider/mode pipeline | Not an analyzer connecting to a database for every rule; CLI marks DG012 unavailable without DbContext registration metadata |
| 9. Legacy upgrades | Assessment reads inventory/dependency/build/config patterns; UpgradePlanner emits steps from a curated support table | No automatic edit/migration or effort estimate; remote advisories require opt-in/policy; review blockers |
| 10. Dialect confusion | ProviderRuleCatalog selects provider-specific rules | Scoped heuristics/parsers, not a complete multi-dialect compiler |
| 11. CI evidence | CLI SARIF/reporting | Inspect exit/status/unknown; annotations/uploads depend on artifacts, permissions and workflows; no automatic PR approval |
| 12. CI credentials | Environment precedes configured stores, local storage and gated plaintext fallback | Audit is configurable; rotation detection does not rotate secrets; Core hash comparison does not establish dependency provenance |

## Reproducible evaluation approach

Use synthetic descriptors/fixtures with matching, mismatching and missing-evidence pairs: missing call argument; projection missing a property; small BYTE column; string without MaxLength against CLOB; identifier absent from schema; different ColumnName; opposite nullability. Fix provider, mode, SHA and metadata. Check violations and skipped/unknown behavior, not just warning counts.

For DG004, expected properties `Id, Name`: `SELECT Id FROM t` should report missing Name; `SELECT * FROM t` supplies no projection names to this static branch; `SELECT Id, Name, Extra FROM t` does not exceed the extra threshold when there are only two accepted names. This is source-derived rationale, not a newly executed test result.

For DG006, the same `CustomerName`→`customer_name` descriptor under different convention enum values currently reaches the same comparison. This is recorded as source backlog; no implementation change is made here.

## Sources and acceptance limits

- [DG004](../../src/DataGuard.Core/Rules/ColumnShapeMatchRule.cs), [DG005](../../src/DataGuard.Core/Rules/NullableMismatchRule.cs), [DG006](../../src/DataGuard.Core/Rules/NamingConventionRule.cs)
- [Procedure count/resolution](../../src/DataGuard.Core/Rules/ParameterCountRule.cs), [Oracle length/fallback](../../src/DataGuard.Oracle.Adapter/LengthMismatch.cs), [CLI rule catalog](../../src/DataGuard.Cli/ProviderRuleCatalog.cs)
- [Assessment](../../src/DataGuard.Core/Assessment/AssessmentEngine.cs), [UpgradePlanner](../../src/DataGuard.Core/Assessment/UpgradePlanner.cs), [security](../03-components/core/security.md)

Separate runtime tests remain: disposable databases by provider/permissions/charset; editor hosts for locations and quickfixes; authorized CI for SARIF ingestion. None was run live in this batch. React/agent/enterprise proposals remain in the [roadmap](../product/roadmap.md); these scenarios do not turn them into released capabilities.
