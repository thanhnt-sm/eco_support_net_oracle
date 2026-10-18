# Source, maturity and documentation map / Bản đồ nguồn và mức trưởng thành

Baseline `ca68a3159425495f771da866188df75a8237ab4b`, inspected 2026-10-18. Paths below are repository-relative. This mapping is source inspection, not a new test run. All evidence must be refreshed when the source SHA changes.

## Source authority

| Surface | Authority | Limitation |
|---|---|---|
| Product entrypoints | `src/DataGuard.Cli/Program.cs:15–29`; `src/DataGuard.Cli/ProviderRuleCatalog.cs`; adapter projects | Actual commands/provider rules, not proof every grammar/type is supported |
| CLI/IDE boundary | `src/DataGuard.Cli/IdeSafePolicy.cs`; VS/VSCode process coordinators | Explicit modes and restrictions; do not promise live validation on every keystroke |
| Public claims | Root `README.md`, `docs/USAGE.md`, actual manifests | These remain subject to evidence reconciliation; prose cannot override source |
| Governance | `AGENTS.md`, `rules/workspace_governance.md`, `rules/git_workflow.md` | Preserve existing work, release authorization and technical identities |
| ADRs | `docs/adr/README.md`, ADR-0001..0010 | Proposed freeze/extract/preview recommendations have pending owner decisions |
| License | `LICENSE`, `docs/legal/`, `Directory.Build.props`, Contracts project override | Canonical current text; historical releases through v0.3.0 retain their historical terms |
| Release proof | Exact workflow SHA, run, package digest, manifest, host acceptance | Source compilation or a green unit suite is not Marketplace installation acceptance |

## Initial capability ledger

Statuses: **I** implemented source; **P** partial scope or unresolved acceptance; **R** reference/starter; **N** proposed/not established. Test paths prove test presence only. No certification or new execution claim is implied.

| ID | Capability and status | Source / symbol | Existing test evidence | Release qualification |
|---|---|---|---|---|
| SA-CAP-001 | .NET/SQL contract checks: I/P | `src/DataGuard.Cli/ProviderRuleCatalog.cs`; `src/DataGuard.Core/Rules/`; four `*.Adapter` projects | `tests/DataGuard.Core.Tests/UnknownTypeEvaluationTests.cs`, `tests/DataGuard.Core.Tests/SqlServerLiveDescribeCommandTests.cs`, `tests/DataGuard.GoldenCorpus.Tests/` | Provider/type/grammar and Unknown/unevaluated behavior must be explicit; no blanket four-provider parity |
| SA-CAP-002 | Snapshot/baseline drift: I/P | `src/DataGuard.Core/Baseline/SnapshotComparer.cs`; `src/DataGuard.Core/Baseline/BaselineManager.cs` | `tests/DataGuard.Core.Tests/SnapshotV3Tests.cs`, `tests/DataGuard.Core.Tests/SnapshotRefreshContainmentTests.cs` | Snapshot age/source and compared contracts must be disclosed; no live guarantee from stale data |
| SA-CAP-003 | EF/source extraction: I/P | `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs`; CLI EF locator | `tests/DataGuard.Core.Tests/ModelSnapshotCSharpParserTests.cs`, `tests/DataGuard.Core.Tests/EfModelSnapshotLocatorTests.cs`, `tests/DataGuard.Core.Tests/IdeSafeEfAutoDetectEndToEndTests.cs` under Core tests | Unsupported semantic/assembly-loading cases remain explicit; no generic language coverage |
| SA-CAP-004 | SARIF/evidence + IDE rendering: I/P | `src/DataGuard.Core/Reporting/`; `src/DataGuard.VisualStudio/`; `src/DataGuard.VSCode/src/` | `tests/DataGuard.Core.Tests/ContractEvidenceTests.cs`; VSCode `src/DataGuard.VSCode/src/extension-lifecycle.test.ts`; `tests/DataGuard.VisualStudio.Tests/` | Installed-host, multiline mapping, cancellation and lifecycle evidence are separate gates |
| SA-CAP-005 | TypeScript DTO export: I/P | `src/DataGuard.Core/Reporting/ContractExport.cs:TypeScriptContractWriter`; `src/DataGuard.Cli/Commands/ValidateCommand.cs:347–365` | `tests/DataGuard.Core.Tests/ContractExportTests.cs` | Export branches return before later validation; not a validated end-to-end API guarantee. Unknown CLR types map to unknown |
| SA-CAP-006 | Assessment/upgrade advice: I/P | `src/DataGuard.Core/Assessment/AssessmentEngine.cs`, `src/DataGuard.Core/Assessment/UpgradePlanner.cs` | `tests/DataGuard.Core.Tests/UpgradePlannerTests.cs` | Analysis-only, curated support data; not autonomous migration. ADR-0005 recommendation pending |
| SA-CAP-007 | Credentials and bounded IDE operation: I/P | `src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs`; `src/DataGuard.Cli/IdeSafePolicy.cs` | `tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs`, `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs`, `tests/DataGuard.Core.Tests/IdeSafeWritePathEndToEndTests.cs`; VSCode redaction tests | Threat-model specific, not a general security certification; synthetic fixtures only |
| SA-CAP-008 | Performance measurement: I/P | `tools/benchmarks/DataGuard.Benchmarks/` and `scripts/validate_benchmark_claim.py` | Benchmark harnesses and metadata validation | No fresh benchmark in this doc task. Results must include SHA, hardware, corpus, warm/cold mode and variance |
| SA-CAP-009 | Observability integrations: R | `docs/observability/README.md:1–14`; `src/DataGuard.Observability*` | Observability tests exist | Reference/starter; not production-hosted service. Core telemetry and audit are distinct |
| SA-CAP-010 | React/API boundary compatibility: N | Marketplace plan persona/phase2 intent; TS export is only an existing seam | No end-to-end React/API contract acceptance suite established by this audit | Design serializer/nullability/enum/date/decimal/error-shape semantics before claiming support |
| SA-CAP-011 | AI-agent action protocol: N | Existing CLI/output/exit-code seams; historical Rust/MCP vision is not implementation | No current agent permission/action conformance suite established | Begin read-only evidence consumption; no new MCP/autonomous-edit claim |
| SA-CAP-012 | Enterprise/banking deployment assurance: N/P | Existing offline/policy/provenance seams and release docs | Local gates support components, not organization certification | Retention, access review, regulated-host rollout (and tenant isolation only for a future hosted control plane) and independent assurance need scoped evidence |

## Contradictions and disposition

| ID | Exact evidence | Conflict | Proposed disposition |
|---|---|---|---|
| SA-DOC-001 | `brainstorm/product_vision_and_niche_strategy.md:3–14` vs `rules/workspace_governance.md:20` | Old EcoSupport Rust/MCP product; Rust workspace removed | Historical banner; preserve content; exclude from current brand feature claims |
| SA-DOC-002 | `docs/06-roadmap/future-directions.md:3–16` and VI equivalent | v0.1-era inventory/counts and future release labels no longer match source | Dated planning banner; move current priorities to the canonical draft roadmap |
| SA-DOC-003 | `plans/260820-marketplace-extensions/plan.md:37` vs current `LICENSE` and legal notices | Legacy license paragraph conflicts with current shipped legal surfaces | Add scope warning; retain historical paragraph and legal history; do not rewrite license text |
| SA-DOC-004 | `docs/PRODUCT.md:5,39–49` vs root README quickstart | Package/tool distribution claims do not establish registry availability | Label legacy overview; reconcile install instructions only against verified channel evidence |
| SA-DOC-005 | `docs/PRODUCT.md:21,126,132,142` | Unscoped speed/Kubernetes readiness/quickfix-count claims | Replace later with measured claims and capability IDs; do not copy into Marketplace |
| SA-DOC-006 | `docs/product-discovery/code-capabilities.md` Program.cs symbol references vs current command classes | Old seam inventory names moved | Retain dated evidence; new capability ledger uses current paths and named symbols |
| SA-DOC-007 | `docs/product-discovery/baseline.md:3,25–27` | 2026-08-23 counts are historical, not current coverage | Preserve raw logs; attach date/SHA to every result; prohibit whole-product inference |
| SA-DOC-008 | `docs/adr/README.md` recommendation table vs hub summaries | Proposed freeze/extract can be mistaken for an accepted decision | Expose owner-pending status in strategy; expansion requires explicit ADR disposition |
| SA-DOC-009 | TypeScript writer summary vs CLI export early return | “Validated contracts” wording overstates pipeline guarantee | Prioritize semantic clarification and future validation-before-export requirement; no code fix in doc task |
| SA-DOC-010 | Original metadata mismatch resolved in source by merged PR #74: manifests 0.4.0 / engine ^1.91.0 | Source readiness is not release or installed-host acceptance | Validate manual release-identity path and exact package/editor evidence separately |

## Separate local work

The original source review predates PR #73. Integration now includes main `121b05d166c359c1d520d035dca40e86ea9cca1f`, preserving PR #73 optional-input normalization and merged PR #74 release source checks. Metadata is 0.4.0 and engine ^1.91.0; ordinary CI packages may retain development binary versions without the explicit manual override. Earlier test numbers and assembly probes remain branch-specific evidence, not new runtime acceptance of this documentation branch. Manual release-identity validation passed in run 39176474395; installed-editor acceptance remains pending. See [preparation evidence](release-claims.md). Marketplace publication remains paused; no repository or technical-identity rename is implied by StackAccord planning.
