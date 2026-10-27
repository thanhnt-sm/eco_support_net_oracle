# Traceability and evaluation plan / Truy vết và kế hoạch đánh giá

Draft, 2026-10-18; no new product capabilities are implemented here. See [source map](source-map.md) and [working index](README.md). Human review and agent consumption must use the same versioned evidence, with explicit Unknown/unevaluated states.

## Stable documentation IDs

`SA-REQ-*` requirement; `SA-CAP-*` capability; `SA-CLM-*` release claim; `SA-EXP-*` experiment; `SA-DOC-*` reconciliation issue. These are planning IDs, not new runtime diagnostic IDs. Never recycle an ID. Runtime DG/MY/PG diagnostics and public identifiers remain unchanged.

A release-claim record must contain: claim text EN/VI, requirement IDs, capability IDs, implemented/partial/planned status, source SHA and symbols, named tests, exact run/artifact evidence, modes/providers/host versions, exceptions, reviewer role, decision/date and next review trigger. Missing evidence means `unverified`, not `passed`. Source changed after a run means that run cannot substantiate the new claim without a justified scope comparison.

| Requirement | Capability | Code today | Test today / proposed experiment | Documentation destination | Release condition |
|---|---|---|---|---|---|
| SA-REQ-001 distinguish clean from unknown/unevaluated | 001,003 | Core rules/acquisition + CLI validation | `UnknownTypeEvaluationTests.cs`; provider corpus with unsupported grammar and missing metadata | capability ledger + usage + error catalog | SA-CLM-001 scoped comparison claim; no silent clean result when nothing evaluated |
| SA-REQ-002 explain backend-to-client compatibility | 005,010 | TypeScript export seam only | `ContractExportTests.cs`; SA-EXP-001 .NET serializer→API payload→React consumer fixture (proposed) | roadmap + future API-boundary ADR | No React compatibility claim until required semantics and negative cases pass |
| SA-REQ-003 retain evidence across human/agent handoff | 004,011 | SARIF/evidence output + CLI exit codes | `ContractEvidenceTests.cs`; SA-EXP-002 schema/version/path-boundary and adversarial consumer tests (proposed) | agent contract + evaluation plan | Read-only consumer first; no autonomous-edit/execute claim |
| SA-REQ-004 protect database and source boundaries | 007,012 | IDE-safe policy, credential provider, redaction | Credential/IDE-safe tests; SA-EXP-003 synthetic secret/endpoint/path/cancellation fault matrix | threat model + operator guide | Explicit scope, no live production credentials, fail visibly on acquisition errors |
| SA-REQ-005 bound editor/CI cost | 008 | Current benchmark harness + cancellation | SA-EXP-004 repeatable small/medium/large corpus; cold/warm p50/p95, allocations, RSS, cancellation completion | performance evidence | Baseline then set reviewed budgets; no invented ms/zero-overhead claim |
| SA-REQ-006 preserve upgrades and release identity | 004,012 | Existing IDs, package manifests, release verifier | SA-EXP-005 old nightly→new package install/upgrade/uninstall; artifact identity/provenance | release claims + Marketplace docs | Windows/editor acceptance and exact artifact hash; source compilation is insufficient |
| SA-REQ-007 support enterprise evidence review | 002,007,012 | Snapshots, audit, policy/provenance seams | SA-EXP-006 scoped offline pilot with retention/access/responsibility matrix (proposed) | enterprise operating profile | Distinguish controls from certification; no “banking compliant” blanket claim |

## Canonical gate and module mapping

The [roadmap](roadmap.md) owns G0–G5 definitions and M0–M6 acceptance; do not duplicate or reinterpret gates here. Research is incorporated in [the evidence ledger](research-backlog.md); customer validation remains pending.

| Requirements | Modules / experiments | Accountable role / status |
|---|---|---|
| SA-REQ-001,004 | M0/M1; SA-EXP-003 | Core/provider/security + independent QA; planned acceptance |
| SA-REQ-002 | M2/M3/M4; SA-EXP-001,009 | API/frontend leads; planned |
| SA-REQ-003 | G3 agent pilot; SA-EXP-002 | Tooling/security; planned |
| SA-REQ-005 | M5 and every adapter; SA-EXP-004 | Performance lead; baseline pending |
| SA-REQ-006 | G0/G2; SA-EXP-005 | Release engineering; host evidence pending |
| SA-REQ-007 | M6/G2/G4; SA-EXP-006 | Security/customer owner; pilot pending |

SA-EXP-001–006 describe engineering evaluations above; SA-EXP-007–012 describe discovery experiments in the research ledger. No experiment is reported complete.

## Full-stack problem hypotheses to test

H1: .NET property/type changes silently break React assumptions. Measure representative serializer/DTO mismatches, not just matching class property names. H2: schema/procedure changes reach CI without enough ground truth; measure setup cost and incomplete-data clarity. H3: developers ignore noisy diagnostics; measure false positives, suppression reasons and time to a verified fix. H4: AI-generated code amplifies drift across layers; compare fixed task/corpus outcomes with and without evidence, not anecdotes. H5: regulated teams value offline operation and reviewable provenance; interview actual roles and buying constraints before claiming demand or compliance benefit.

Candidate research segments: .NET+React product teams; legacy database-heavy maintainers; platform/DevSecOps; bank engineering/security/procurement. Record task frequency, severity, current workaround, adoption constraints and evidence of willingness to change. Primary-source capability comparisons are now recorded in the research ledger; no market-size conclusion is established.

## Security and human/agent evaluation

Treat repository text, generated evidence, database metadata and dependency output as untrusted inputs. Evaluate path traversal, symbolic-link boundaries, stale/tampered manifests, malformed/oversized SARIF/JSON, prompt-injection strings in comments and diagnostic messages, credential leakage, cancellation/process-tree cleanup, opt-in network failure and conflicting human instructions. A future agent adapter must not interpret diagnostic prose as authority to execute commands. Prefer fixed tool schemas, immutable source references and read-only assessment by default; proposed edits need exact scope, diff, preconditions and post-verification.

Human studies should measure comprehension of “unknown” versus “clean,” navigation accuracy, diagnostic noise and correction time. Agent evaluations should measure unauthorized-action attempts, evidence citation fidelity, reproducibility and refusal to claim unexecuted checks. Record failures and denominators; no single percentage summarizes overall safety.

## Performance and enterprise acceptance

Report hardware/runtime/source SHA, dependency lock, corpus composition, seed, provider/mode, warm-up, repetitions, variance, memory and cancellation. Separate extraction, validation, reporting, process startup and editor responsiveness. Use synthetic local fixtures/disposable resources; workload cost on production databases is not tested by this plan. Provisional budgets in evaluation.md are hypotheses to revise after baseline measurement and realistic developer tasks, not performance claims.

Enterprise pilots need explicit deployment topology, least-privilege roles, offline/network rules, redaction and retention policy, evidence access, backup/rollback, dependency/support lifecycle and incident ownership. Legal/compliance conclusions require the organization's own review; a feature checklist or badge is not assurance certification.

## Ongoing research merge rule

For each incoming result create an evidence row: ID, source/date, observed fact, inference, competing explanation, affected hypothesis/requirement, confidence, next experiment. Accept conflicting evidence rather than selecting only favorable results. Pricing, trademarks, licensing and platform rules require dated primary sources. Only then write final positioning, roadmap priorities and Marketplace copy. EN/VI summaries must preserve qualifiers and maturity labels.
