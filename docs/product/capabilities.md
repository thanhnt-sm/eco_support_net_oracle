# Capability and maturity ledger

Source baseline `ca68a3159425495f771da866188df75a8237ab4b`, 2026-10-18. Detailed symbols/tests and qualifications: [source map](source-map.md). Test presence is not a test run.

Integration baseline: `121b05d166c359c1d520d035dca40e86ea9cca1f` includes PR #73 and merged PR #74. Current source metadata is 0.4.0 with VS Code engine ^1.91.0; the original inspection baseline above remains provenance. The manual build-only release-identity gate passed for main 121b05d; installed-editor acceptance remains unverified. See the [bounded preparation evidence](release-claims.md). See [source reconciliation](source-map.md).

Four independent axes: **Implemented** = source exists; **Validated** = exact scoped run evidence exists; **Released** = accepted distributed artifact is evidenced; **Planned** = intended scope. “Partial” never implies provider parity. This documentation-only task does not establish new product validation or release evidence.

| Capability IDs | Implemented at baseline | Validated in this task | Released evidence here | Planned extension |
|---|---|---|---|---|
| SA-CAP-001–003 | Partial .NET/SQL checking, snapshots, extraction; four adapters | Not rerun | Not established | M0–M2 provider/grammar matrix and independent corpus |
| SA-CAP-004 | Reporting and CLI/IDE integrations | Windows preparation/identity checks passed for the [exact run](release-claims.md); installed-editor behavior remains unverified | Not established | Artifact-specific same-ID upgrades, shared findings |
| SA-CAP-005 | Descriptor-to-TypeScript export | Not rerun; export path returns before later validation | Not established | M3/M4 wire and runtime contracts |
| SA-CAP-006 | Analysis-only upgrade assessment | Not rerun | Not established | No mutation promise |
| SA-CAP-007 | Credential/IDE-safe policy mechanisms | Not rerun; not blanket security assurance | Not established | Threat-model controls and hostile corpus |
| SA-CAP-008–009 | Benchmark harness; observability reference/starter | No new performance measurement | Not established | Reviewed budgets and operating profile |
| SA-CAP-010–012 | React/agent absent as complete products; enterprise foundations partial | Not established | Not established | M3/M4, read-only agent, M6 |

Provider leads must publish an operation-by-operation Oracle/SQL Server/PostgreSQL/MySQL matrix: supported syntax/type mapping, metadata permissions, snapshot/live modes, unknown reasons, tests and artifact versions. Until then, adapter existence is the maximum defensible general claim. Existing ADR recommendations to retain PostgreSQL/MySQL preview remain Proposed, not accepted decisions.
