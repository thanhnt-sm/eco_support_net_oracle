---
type: advisory
reviewer: gpt-5.6-sol
date: 2026-09-12
source_audit: ../../260912-1936-luna-src-audit
status: completed
---

# Sol advisory — scout remediation orchestration and closure

## Outcome

Use a new GPT-5.6 Terra session as the only implementation writer and one
GPT-5.6 Sol sub-agent as a read-only adversarial reviewer. Persistent Markdown
files, not chat context or collaboration task IDs, are the source of truth.

The remediation ledger must account for all 60 report IDs:

- `CE-01`–`CE-09`: 9
- `CS-01`–`CS-14`: 14
- `AD-01`–`AD-06`: 6
- `TL-01`–`TL-06`: 6
- `CI-01`–`CI-10`: 10
- `DOC-01`–`DOC-08`: 8
- `F1`–`F7`: 7

It must also account for `V01`–`V06` from the verification and advisory pass.
No row may disappear because it is duplicated, historical, disproved,
provisional, or blocked by a database, Windows, credentials, publishing, or
owner approval.

## Model roles and portability constraints

### GPT-5.6 Terra — sole writer

- Reads the plan, every phase, the complete findings ledger, all Luna reports,
  `AGENTS.md`, `rules/git_workflow.md`, and `rules/workspace_governance.md`.
- Is the only agent allowed to modify production source, tests, documentation,
  workflows, lockfiles, plan phases, or the findings ledger.
- Runs verification and records exact commands, exit codes, environment, and
  evidence paths.
- Uses `apply_patch` for deliberate local edits and preserves unrelated user
  work.
- Does not commit, push, reset, amend, rebase, cherry-pick, tag, run bare
  `dg-git`, or run `dg-git sync` unless the user later gives explicit authority.

### GPT-5.6 Sol — read-only challenger

- Reads code, diffs, ledger rows, reports, and test evidence.
- Challenges missing acceptance criteria, compatibility regressions, false
  closure, weak tests, and unnecessary feature expansion.
- Returns one checkpoint verdict: `GO`, `REVISE`, or `STOP`.
- Does not edit any file, run formatting that mutates files, or perform git
  mutation.
- “Push progress” means challenge assumptions and force evidence-backed
  decisions. It never means `git push`.

### Tool portability

- Use native Codex collaboration tools. Do not use `ck:team`: its Claude/Opus
  and Claude Tasks assumptions are not the contract for this Terra/Sol flow.
- Spawn Sol directly with model `gpt-5.6-sol` and `fork_turns: "none"`; provide
  absolute plan and ledger paths in its prompt.
- Collaboration agent IDs and mailbox contents are session-ephemeral. Do not
  write an agent ID into the plan or ledger.
- Claude Tasks, Todo state, and old session messages are not durable handoff
  state. The plan, phase files, ledger, and evidence files are durable state.
- Skill syntax differs across harnesses. The handoff must describe the required
  behavior and paths even if a named skill is unavailable.

## Session bootstrap protocol

1. Confirm the workspace root is
   `/Volumes/Data/101.AI/GitHub/eco_support_net_oracle`.
2. Read workspace rules and the whole remediation plan before acting.
3. Capture `git status --short`, current branch, and `git rev-parse HEAD`.
4. Treat all pre-existing modified or untracked files as user-owned. In
   particular, do not clean, replace, or absorb the scout plan accidentally.
5. Reconcile the ledger count: 60 source IDs plus exactly `V01`–`V06`.
6. Spawn one read-only Sol reviewer using the prompt in `session-handoff.md`.
7. Send checkpoint `CP0` to Sol before making production changes.

If the repository state differs from the ledger baseline, Terra must classify
the difference before editing. It must not reset or erase it.

## Persistent state ledger

The ledger is `findings-ledger.md` in the remediation plan directory. Each row
should contain, at minimum:

| Field | Meaning |
|---|---|
| `source_id` | Original report ID; never rewritten or omitted |
| `canonical_id` | Canonical remediation group |
| `relation` | `canonical`, `exact_duplicate`, `related`, or `split` |
| `class` | defect, docs gap, test gap, risk, provisional, false positive, historical, cleanup, or external verification |
| `priority` / `confidence` | Preserved from the source report |
| `decision` | code, docs, no-change, or pending-owner |
| `state` | open, in-progress, implemented, verified, closed, blocked-external, or blocked-owner |
| `owner` | Terra, owner, or named external environment |
| `exclusive_files` | Current file lease for conflict prevention |
| `acceptance_ids` | Stable acceptance checks for the row |
| `verification_level` | static, unit, integration, live-db, Windows, benchmark, or release |
| `evidence` | Command, exit code, artifact, test name, and source/doc location |
| `blocker_prerequisite` | Exact resource or authority required to resume |
| `sol_verdict` | Latest checkpoint verdict affecting the row |
| `updated_at` | Timestamp of last material state change |

Ledger invariants:

- Every source ID appears exactly once.
- `V01`–`V06` each appear exactly once.
- An exact duplicate points to one canonical issue; a merely related finding
  retains independent acceptance checks.
- A duplicate alias cannot close before its canonical issue is verified.
- False-positive, historical, and no-change closure still require evidence.
- `blocked-external` and `blocked-owner` are open terminal dispositions for the
  current environment, not successful closure.
- No row can be `closed` with an acceptance check marked not-run or failed.
- Totals must satisfy `closed + blocked + open = 66`.
- Final reporting must show all three totals. It must never call the effort
  complete while blocked or open rows are hidden.

## Duplicate and relationship rules

- `CI-01` and `CS-01` are exact duplicates of the snapshot self-comparison
  defect.
- `CS-02` and `F4` are related hash-version/fallback semantics, not exact
  duplicates of the live-drift defect.
- `CS-05` and `DOC-03` share a capability/docs canonical group.
- `CI-10` and the health-endpoint part of `DOC-04` overlap. The code-fix count
  part of `DOC-04` relates to `TL-02` and must retain separate acceptance.
- The performance-claim part of `DOC-08` relates to `F7`; its queue semantics
  part relates to `CE-09`.
- `TL-06` supports `TL-01` and `TL-04` as a test gap; it is not a duplicate.
- `DOC-07` relates to `CI-02`, `CI-03`, and `CS-07`; it is not interchangeable
  with any of them.

## Definition of closed by finding class

### Confirmed defect

A defect closes only when all are true:

1. The minimal implementation is present.
2. A targeted test would fail on the old behavior and passes on the new one.
3. Affected current English and Vietnamese documentation is synchronized.
4. The affected project build and tests pass.
5. The ledger contains command, exit code, test/artifact, and source evidence.
6. Sol has no unresolved `REVISE` or `STOP` objection.

### Documentation overclaim

The default policy is honest documentation, not speculative feature creation.
Closure requires removing or qualifying the shipped/current claim, stating the
actual supported boundary, and attaching current source or verification
evidence. Historical measurements must be labeled with commit and date.

Exceptions already mandated by this remediation are real fixes for EF
design-time behavior, public concurrency, and smart defaults. Those must use
compatibility gates and cannot be closed by documentation downgrade alone.

### Contract choice

Record the selected behavior and compatibility implications before editing.
Where no explicit user choice exists, retain current compatible behavior and
make documentation honest. Do not silently redefine CLI exit codes, offline
mode, rule IDs, default precedence, or security behavior.

### Provisional or medium-confidence issue

First create a characterization or reproduction test. It closes as a defect
only after reproduction and correction. It may close as `not-reproduced` or a
supported assumption only with fixture/provider-version evidence and a clear
documented boundary. Absence search alone is insufficient.

### False positive

Do not change product behavior merely to satisfy the report. Preserve the
probe, test, or source reasoning. `CS-03` already has a successful sibling
lockfile probe and should close as disproved/no-change.

`CS-06` is not yet a proven defect. Characterize wizard mapping and make the UI
wording explicit; do not invent a Baseline enum that does not exist.

### Historical material

Verify that the material is explicitly archived or superseded and that current
documents do not cite it as live behavior. Do not rewrite historical text as
if it were current product implementation. `F3` normally requires no source
change.

### Cleanup

`F6` cannot authorize deletion. It requires an owner-approved
`from -> disposition` manifest and preservation of WIP. Until approval exists,
the row remains `blocked-owner`. Do not create an `archive/` or `legacy/`
directory in the production repository.

### Database, Windows, benchmark, and release evidence

Static and unit tests are interim evidence, not live-environment closure.

- Oracle, PostgreSQL, MySQL, and SQL Server rows need a live database command
  and evidence that assertions executed. Early return reported as `Passed`
  does not qualify.
- Windows rows need a `windows-latest` or supported Windows/MSBuild build log
  and VSIX artifact. Runtime/UI claims need Windows/Visual Studio runtime
  evidence or an honest docs downgrade.
- Performance numbers need a repeatable benchmark definition, environment,
  baseline, results artifact, and threshold.
- Release/publish claims need immutable artifact or marketplace evidence.

If credentials, Windows, database infrastructure, publishing authority, or
permission to push is unavailable, record the exact blocker and resume command.
Do not auto-close future work.

## Checkpoints and anti-stall policy

| Checkpoint | Trigger | Required evidence |
|---|---|---|
| `CP0` | Bootstrap complete | HEAD/status, rules read, ledger count and baseline |
| `CP1` | Classification and compatibility decisions complete | Canonical mappings, contract decisions, file leases |
| `CP2` | Each batch of 3–5 canonical groups | Ledger delta, changed files, targeted test results, next batch |
| `CP3` | Before cross-cutting refactor, workflow, or lockfile edit | Proposed design, compatibility risks, rollback scope |
| `CP4` | Targeted implementation tests complete | Exact commands/exits and failed-before/passed-after evidence |
| `CP5` | Full local verification complete | Restore/build/test/docs/Node/security summaries |
| `CP6` | Final reconciliation | Independent ID enumeration, final diff, closed/open/blocked totals |

At every checkpoint Terra sends Sol:

- ledger rows changed since the prior checkpoint;
- `git diff --stat` and changed-path list;
- commands, exit codes, failures, and evidence locations;
- assumptions and blockers;
- proposed next batch.

Sol responds:

- `GO`: evidence is sufficient to proceed;
- `REVISE`: specific gaps must be addressed before phase advancement;
- `STOP`: safety, data-loss, security, or breaking-contract risk requires user
  direction.

For anti-stall, Terra makes at most two materially different attempts before a
mandatory Sol challenge. A third occurrence of the same blocker becomes
`blocked-external` or `blocked-owner`, including attempts made, prerequisite,
owner, and exact resume action. Independent work proceeds; the blocked item is
never counted as closed.

## Conflict prevention

- Terra is the only writer; Sol is read-only.
- Terra leases exact files for one active batch in the ledger before editing.
- No other batch starts if a leased file overlaps an in-progress batch.
- Central CLI configuration/snapshot changes precede adapter CLI wiring to
  avoid repeated edits to `Program.cs`.
- Documentation changes occur with their canonical implementation batch, then
  receive one final global consistency pass.
- Do not run concurrent formatters, restore commands, or package operations
  that can rewrite the same lockfile or generated asset.
- Before and after each batch, compare status and diff against the recorded
  baseline. Unrelated changes are reported and preserved.

## Recommended phase order

1. Reconcile the ledger, duplicate mapping, contracts, and compatibility gates.
2. Correct P1 foundations: CLI configuration precedence, snapshot semantics,
   credential fail-closed behavior.
3. Correct provider adapters and CLI reachability with provider-scoped seams.
4. Correct Core: EF design time, public concurrency, smart defaults, graph,
   nullability, truncation, exports, containment, cancellation, telemetry.
5. Correct analyzers/code fixes with a diagnostic-to-action behavior matrix.
6. Correct hooks and IDE boundaries; reconcile current EN/VI documentation.
7. Resolve dependency advisory and cleanup decisions.
8. Run local gates, then external DB/Windows/benchmark/release gates and final
   reconciliation.

## Evidence-backed additional obligations

### V01 — npm development dependency advisories

The verification report found three transitive development-only advisories via
`@vscode/vsce`, while production-only audit was clean. Research the current
fixed dependency path and lockfile impact. Do not run blind `npm audit fix`.
Retain separate production and development audit evidence.

### V02 — SQL Server live/skip evidence

`SqlServerIntegrationTests` and `SqlServerParserIntegrationTests` return from
`[Fact]` when a container is unavailable, which VSTest reports as Passed.
Require explicit skip/fail/marker semantics and proof that database assertions
executed in the live gate.

### V03 — Oracle/PostgreSQL/MySQL live evidence

Static fixes and mock tests do not establish provider compatibility. Require
live bind, package null/non-null, reader-to-rule, schema acquisition, and result
shape evidence for affected providers. Missing infrastructure remains blocked.

### V04 — Windows/Visual Studio evidence

The repository already defines Windows packaging jobs in build/release and
marketplace workflows. Local macOS cannot validate the VS project. A remote job
for the exact implementation revision or a supported Windows host is required.
No commit/push/workflow dispatch may be inferred from this plan.

### V05 — coverage, benchmark, and release evidence

Do not reuse old test counts, coverage, performance, publish, or release
artifacts as current proof. Each claim needs evidence tied to the exact source
revision. Unavailable benchmark/release prerequisites remain blocked or the
current docs claim is downgraded.

### V06 — additional snapshot, cancellation, and hook obligations

These are related to existing report IDs rather than independent feature
expansion:

- Snapshot refresh persists a schema only for Oracle, while offline Snapshot
  validation consumes persisted `Schema`. SQL Server/MySQL/PostgreSQL can fall
  back to violation hashes and then provide no offline schema contracts.
  Acceptance requires provider-neutral schema capture or an explicit supported
  provider boundary, tested as a provider-by-mode matrix.
- CLI root invocation and several downstream calls use
  `CancellationToken.None`, discarding command cancellation. Fold this into
  `CS-14` and prove propagation through CLI, provider, and rule paths.
- Hook installation writes the wrong Husky bytes, treats read-only attributes
  as executable mode, emits `/bin/sh` scripts containing `&>`, and invokes
  `validate --offline` without the assembly required by current CLI behavior.
  Uninstall also deletes hook/config files wholesale. Fold this into
  `CI-04`/`CI-05`; require byte-content, executable-mode, POSIX execution,
  managed ownership/backup, uninstall safety, and end-to-end CLI tests.

## Family-level acceptance map

### Snapshot and CLI configuration

- `CI-01`/`CS-01`, with related `CS-02`/`F4`: acquire the actual current schema,
  version hash kinds, test DDL add/drop/type/nullability, and distinguish offline
  consistency from live drift.
- `CI-02`/`CI-03`/`DOC-07`: one resolver shared by validate, baseline, refresh,
  diff, and oracle-check. Test explicit CLI > environment > config > product
  default for connection, provider, schema, and package without logging secrets.
  The provider option currently has an implicit sqlserver default, so the
  implementation must distinguish an explicit option from its default.
- `CI-04`/`CI-05`: satisfy the complete V06 hook matrix and record the chosen
  offline-mode contract.

### Providers and Core

- `AD-01`: both Oracle queries use valid bind syntax; cover package null and
  non-null with live evidence.
- `AD-02`–`AD-04`: intended rules/readers are reachable through CLI and produce
  actual schema/result-shape contracts.
- `AD-05`: test reader-to-detector byte/character semantics.
- `AD-06`: current dependency versions and capability wording match source.
- `CE-01`: a real C# `ModelSnapshot` fixture works without a build and has a
  compatibility fallback/error contract.
- `CE-02`: public concurrency is implemented behind compatibility gates. Do not
  flatten rule dependencies; execute safe dependency levels or preserve ordered
  semantics explicitly.
- `CE-08`: smart defaults are applied through a compatibility-safe API path and
  do not silently overwrite explicit configuration.
- `CE-03`: package metadata and current ADR narrative no longer claim zero
  vendor dependencies.
- `CE-04`: distinguish dependency placeholders from registered rules and test
  the selected warning/error policy.
- `CE-05`: characterize supported SQL Server result metadata live before
  changing hidden-column behavior.
- `CE-06`: cover CLR nullable reference, EF nullability, explicit annotations,
  and duplicate column names across tables.
- `CE-07`: expose malformed SQL diagnostics and state declaration/call-site
  limits; do not claim unavailable CLR direction/type validation.
- `CE-09`: expose truncation/dropped count and define exit/error semantics.

### Security, reporting, and resilience

- `CS-03`: retain the disproving probe; no product fix.
- `CS-04`: cover sibling-prefix paths and explicitly decide symlink policy.
- `CS-05`: default to honest local-only assessment docs unless remote lookup is
  separately authorized and fully implemented.
- `CS-06`: characterize wizard behavior and clarify wording without inventing a
  false defect.
- `CS-07`: fail closed or use a real platform credential backend; never label
  plaintext encrypted.
- `CS-08`/`CS-09`: hostile secret-shaped values are redacted through every
  buffered and streaming path.
- `CS-10`/`CS-11`: add hostile plugin/prefix tests and describe plugin loading as
  not sandboxing and prefix trust as not provenance unless those features are
  actually implemented.
- `CS-12`: nested export order is deterministic; TypeScript identifiers are
  valid, keyword-safe, and collision-safe.
- `CS-13`: exclude generated trees, cap scans, and preserve a clear credential
  boundary.
- `CS-14`: propagate cancellation, bound baseline loads, and define telemetry
  retry/drop behavior.

### Tooling, IDE, documentation, and history

- `TL-01`/`TL-06`: test diagnostic ID to action count and the resulting document
  for every advertised action.
- `TL-02`: document exact exported-provider, action, and diagnostic-ID taxonomy.
- `TL-03`: state that analyzers are light/static and Core/CLI owns ground truth.
- `TL-04`: use unambiguous diagnostic semantics and test reachable control flow.
- `TL-05`: never emit unusable empty type/direction attributes.
- `CI-06`: enforce workspace containment including sibling-prefix and the chosen
  symlink policy.
- `CI-07`/`CI-08`: default to honest shipped Run/Cancel boundaries; extra IDE
  features require separate implementation and runtime evidence.
- `CI-09`: provider selection must be persisted and consumed, or the cosmetic
  option must be removed/documented honestly.
- `CI-10`/`DOC-04`: remove current health endpoint claims unless an actual host,
  routes, and tests are implemented. Document the real code-fix provider count.
- `DOC-01`: count rules separately by analyzer, Core, provider, and reachable
  CLI layer.
- `DOC-02`: synchronize material EN/VI behavior, not merely line counts.
- `DOC-03`: describe DependencyHealth as local-only.
- `DOC-05`: stamp measurements with source revision and date.
- `DOC-06`: label development and banking security profiles explicitly.
- `DOC-07`: reflect the tested resolver precedence.
- `DOC-08`: remove or qualify unmeasured speed and allocation claims; describe
  truncation behavior accurately.
- `F1`: distinguish verified-at-commit from current-unverified status.
- `F2`: add supersession notice and repair ADR links.
- `F3`: retain historical/research separation.
- `F5`: keep live DB and marketplace evidence open until it exists.
- `F6`: require owner-approved cleanup manifest.
- `F7`: remove or benchmark performance claims.

## Final adversarial gate

At `CP6`, Sol independently extracts the IDs from the source reports rather
than trusting Terra's count. Sol compares those IDs with the ledger, checks that
`V01`–`V06` exist, inspects the final diff for unrelated or unowned files, and
rejects any statement that all issues are resolved while an open or blocked row
remains.
