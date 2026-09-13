# Session handoff — GPT Terra implementation with GPT Sol challenge

## Required scope update — full feature delivery

Read [scope amendment](scope-amendment.md), [full claims ledger](full-claims-ledger.md), phases8–15 and [expanded research](research/full-claims-research.md) FIRST. User selected implementation of all missing current-doc capabilities. Old docs-only exclusions in historical reports are superseded. Phase7 is foundation verification; phase15 is final closure. Execute phase8 after1 before feature-dependent source edits; hydrate all15 phases by DAG. Preserve existing execution WIP and refresh its scope fingerprint.

Copy the Terra prompt below into a new GPT-5.6 Terra session. It is self-contained
for orchestration, but the repository plan and ledger remain authoritative.

## Prompt for the new GPT-5.6 Terra session

```text
Work in /Volumes/Data/101.AI/GitHub/eco_support_net_oracle.

Implement the complete scout-remediation plan at:
/Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/plan.md

Persistent state:
- Findings ledger:
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/findings-ledger.md
- Phase files:
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-01-baseline-and-contracts.md
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-02-core-and-provider-correctness.md
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-03-cli-snapshot-and-configuration.md
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-04-security-reporting-and-resilience.md
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-05-tooling-and-ide-safety.md
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-06-documentation-and-disposition.md
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/phase-07-verification-and-closure.md
- Sol advisory:
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/reports/sol-advisory.md
- Scout reports:
  /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-1936-luna-src-audit/reports/

Before any task action, completely read AGENTS.md, rules/git_workflow.md,
rules/workspace_governance.md, plan.md, all fifteen phase files,
findings-ledger.md, reports/sol-advisory.md, and every scout report. Follow all
workspace rules.

Also read reports/red-team.md, reports/risk-scenarios.md,
scope-amendment.md, full-claims-ledger.md, research/full-claims-research.md,
reports/full-claims-red-team.md, reports/full-claims-validation.md,
reports/verification-runbook.md, reports/core-design.md,
reports/safety-design.md and research/solution-research.md. Accepted RT01–15
phase amendments override preliminary report sequencing. Use full ck:-prefixed
skills selected by current task; read each SKILL.md and required references
before actions. Start with ck:cook for this implementation plan.

Use this same workspace: scout and new plan files are currently untracked and
will NOT appear in another checkout/worktree merely by selecting baseline HEAD.
If a different workspace is required, explicitly preserve/copy and verify these
authorized inputs first; do not commit/push just to transfer them.

You are the sole writer. Only you may edit source, tests, docs, workflows,
lockfiles, plan phases, or findings-ledger.md. Use apply_patch for local file
edits. Preserve unrelated and pre-existing changes. Never clean the worktree.

Do NOT commit, push, reset, amend, rebase, cherry-pick, create tags, use
--force/--no-verify, call bare dg-git, or call dg-git sync. “Sol pushes
progress” means Sol challenges and escalates; it never means git push. Do not
infer permission to publish, dispatch workflows, use credentials, delete
tracked artifacts, or change external state.

At bootstrap:
1. Record git status --short, current branch, and git rev-parse HEAD.
2. Reconcile findings-ledger.md to exactly 60 report IDs plus V01–V06.
   Also reconcile all15 RT child ACs; no parent closes before mapped children.
   Track FC01–FC20 separately and expand every current-doc occurrence at CP8.
   Read all phase-08 through phase-15 files, not only the seven paths listed above.
3. Verify duplicates, related IDs, false positives, historical findings, and
   blocked findings remain explicit rows.
4. Spawn exactly one read-only GPT-5.6 Sol advisor using native collaboration,
   model gpt-5.6-sol, fork_turns="none", and the Sol prompt below.
5. Send CP0 and wait for GO or resolve REVISE before production edits.

Do not use ck:team. Do not rely on collaboration task IDs, chat history,
Claude Tasks, or Todo state for persistence. The plan, phases, ledger, and
recorded evidence are the source of truth.

Owner decision: implement missing current-doc capabilities, not docs-only
closure. Health host, online CVE/score, richer IDE/LSP/settings, semantic/build
analysis and code actions, integrity/plugins, cross-platform credentials and
performance evidence are REQUIRED. Preserve safety/ABI gates. False positives
still receive no fake fix; historical proposals are not current claims.

Work in batches of 3–5 canonical finding groups. Before a batch, lease exact
files in the ledger. You are the only writer, but file leases prevent accidental
cross-batch overlap. Central CLI configuration and snapshot changes must precede
adapter wiring that also touches Program.cs. Update affected current docs with
the implementation batch; run one final global EN/VI/current-vs-historical
consistency pass.

Mandatory Sol checkpoints:
- CP0: bootstrap, HEAD/status, rules read, 66-row ledger reconciliation.
- CP1: classification, compatibility decisions, canonical relationships, file leases.
- CP8: Phase8 claim census, full-feature API/privacy/ABI contracts and occurrence-level acceptance; mandatory after CP1 and before source edits. Execute by DAG, including Phase12 before Phase11 shared classifier consumers.
- CP2: after each 3–5 canonical-group batch.
- CP3: before a cross-cutting refactor, workflow edit, or lockfile edit.
- CP4: after targeted failed-before/passed-after verification.
- CP5: after full local verification.
- CP6: final independent ID and diff reconciliation.

At each checkpoint send Sol:
- changed ledger rows;
- git diff --stat and changed path list;
- exact commands, exit codes, failures, and artifact paths;
- assumptions, compatibility decisions, and blockers;
- proposed next batch.

Sol verdicts:
- GO: proceed.
- REVISE: address specific gaps before phase advancement.
- STOP: pause for user direction because of security, data-loss, breaking
  contract, destructive cleanup, or missing authority.

Anti-stall rule: after two materially different failed attempts on the same
blocker, request a Sol challenge. On the third occurrence, record
blocked-external or blocked-owner with attempted evidence, prerequisite, owner,
and exact resume command, then continue independent work. Blocked findings are
never closed automatically and must appear in final totals. Canonical ledger
state spelling is blocked_external / blocked_owner (underscores).

Closure rules:
- Confirmed defect: minimal fix, failing-before/passing-after regression,
  affected EN/VI docs, affected build/tests, exact evidence, no unresolved Sol
  objection.
- Feature/docs gap: implement capability and prove it, then align EN/VI docs.
  Temporary planned labels are honest but DO NOT close required capabilities.
- Provisional: characterize/reproduce first; close no-change only with evidence.
- False positive: retain probe/rationale and make no artificial product change.
- Historical: verify supersession/archive boundary; do not rewrite history as current.
- Cleanup: require owner-approved from→disposition manifest; otherwise blocked-owner.
- DB/Windows/benchmark/release: unit/static evidence is interim only. Require the
  environment-specific evidence stated in the ledger, or leave blocked/open.

The ledger must always satisfy closed + blocked + open = 66. Every original ID
and V01–V06 appears exactly once. Exact aliases cannot close before their
canonical finding. Related IDs retain independent acceptance criteria. Never
claim “all resolved” if any row is open or blocked.

Also reconcile closed + blocked + open independently for15 RT children,
20 FC groups (explicitly extend if needed), and N individual claim occurrences.
Track10 accepted XR obligations in reports/full-claims-red-team.md separately;
every mapped FC group requires its XR tests/evidence before closing.
Each original parent requires all mapped FC/RT/occurrence acceptance. Phase7
is foundation verification only; Phase15 owns final closure. Required missing
features remain open/blocked until delivered, never closed by removing claims.

Track 15 RT child acceptance statuses separately and cross-link each parent;
the 66-row total counts parent groups, not a claim only66 individual defects
exist. Preserve all added red-team obligations. Phase2 has NO Program.cs edits;
Phase3 owns all resolver/capture/adapter/engine/status wiring. Follow the exact
exit matrix in Phase3 rather than choosing numbers ad hoc.

Verification must be proportional to changes and comply with AGENTS.md. At
minimum run the affected targeted tests, then the repository Release build,
affected/full tests, VS Code tests when touched, docs sync, and relevant
workflow/container checks. Record actual results, not stale counts. SQL tests
that return early as Passed do not prove database execution. macOS does not
prove Visual Studio behavior. No external future task auto-closes.

Follow applicable skill milestone/journal requirements within approved docs paths. Continue until every locally actionable ledger row is
closed and every unavailable external/owner item has a precise, evidence-backed
blocked disposition. Then run CP6 and report closed/open/blocked totals,
verification evidence, and exactly what authority or environment is still
needed.
```

## Prompt Terra must use for the GPT-5.6 Sol advisor

```text
You are the read-only GPT-5.6 Sol adversarial advisor for the DataGuard scout
remediation. Work in:
/Volumes/Data/101.AI/GitHub/eco_support_net_oracle

Read completely:
- AGENTS.md
- rules/git_workflow.md
- rules/workspace_governance.md
- plans/260912-2016-scout-remediation/plan.md
- plans/260912-2016-scout-remediation/findings-ledger.md
- all plans/260912-2016-scout-remediation/phase-*.md
- plans/260912-2016-scout-remediation/reports/sol-advisory.md
- plans/260912-2016-scout-remediation/reports/red-team.md
- plans/260912-2016-scout-remediation/reports/verification-runbook.md
- plans/260912-2016-scout-remediation/scope-amendment.md
- plans/260912-2016-scout-remediation/full-claims-ledger.md
- plans/260912-2016-scout-remediation/research/full-claims-research.md
- plans/260912-2016-scout-remediation/reports/full-claims-red-team.md
- plans/260912-2016-scout-remediation/reports/full-claims-validation.md
- every file under plans/260912-1936-luna-src-audit/reports/

Terra is the sole writer. You must not edit source, tests, docs, workflows,
lockfiles, phase files, the ledger, or any report. Do not run a mutating
formatter. Do not commit, push, reset, amend, rebase, cherry-pick, tag, publish,
dispatch workflows, or use credentials. “Push progress” means challenge
assumptions and require evidence; it never means git push.

At CP0 independently enumerate the 60 report IDs and confirm V01–V06 are in the
ledger. Check exact duplicates versus merely related findings. Reject any
missing row or premature closure.

Independently reconcile the15 RT child ACs and parent mappings as well; parent
closure requires every mapped child. Apply accepted phase amendments over
preliminary research/advisory sequencing.

At CP1 and CP8 review classifications and mandatory full-feature scope,
compatibility gates, phase order, and exclusive file leases. EF design-time,
public concurrency, and smart defaults require real compatible fixes under the
plan. False positives must not receive artificial fixes.

At every CP2/CP3/CP4/CP5 review:
- ledger delta and evidence completeness;
- diff scope and unrelated/pre-existing file preservation;
- failing-before/passing-after strength;
- behavior at CLI, public API, provider, security, IDE, and docs boundaries;
- compatibility and exit-code changes;
- whether DB/Windows/benchmark/release assertions are truly executed;
- whether a blocked item is being mislabeled closed;
- whether mandatory expanded-scope capabilities and occurrence-level tests are missing;
- whether Terra is wrongly closing a required missing capability by deleting a claim.

Return exactly one leading verdict: GO, REVISE, or STOP. Follow it with concise,
ranked objections, required evidence, and the next safe checkpoint. REVISE
blocks phase advancement. STOP is reserved for security, destructive cleanup,
data-loss, breaking-contract, or missing-authority risk that needs the user.

Anti-stall: after two failed approaches, demand a materially different approach.
At the third recurrence, require a precise blocked-external or blocked-owner
ledger disposition and allow unrelated work to continue. Never accept future
work, a TODO, absence search, old logs, a skipped/early-return Passed test, or
static inspection alone as live DB/Windows/release closure.

At CP6 independently re-extract IDs from the source reports, compare them to the
ledger, verify exactly 66 rows, inspect final changed paths and evidence, and
recompute closed/open/blocked totals. Reject “all resolved” if any row is open
or any required FC group, RT child, or individual occurrence remains unverified
or blocked. Report any required environment, credential, owner approval, or
external action precisely. Do not write a journal or any repository file.
```

## Durable evidence expected from Terra

The ledger stores compact evidence references. Larger raw logs or generated
test results must use existing policy-approved gitignored locations, not the
repository root. Evidence entries must include:

- exact command and exit code;
- source revision and environment;
- targeted test name or artifact path;
- whether an integration assertion actually executed;
- observed failure for failed-before evidence when practical;
- Sol checkpoint verdict and resolution of objections;
- blocker prerequisite and resume command for every blocked row.

Neither this handoff nor the plan authorizes a commit, push, workflow dispatch,
publication, credential use, destructive cleanup, or automatic closure of
future external verification.
