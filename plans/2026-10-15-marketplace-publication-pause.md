# Temporary marketplace publication pause — 2026-10-15

## Scope and baseline

User requested a separate draft PR pausing only Publish Visual Studio Marketplace and
Publish VS Code Marketplace until product readiness. No release, merge, protection change,
secret deletion, hook change, or coverage change is authorized by this plan.
Baseline: freshly fetched main `0fb5f3f91078ba0df508f41cc822d6192e1be748` (PR61 merge).
PR60 and PR61 were merged externally during startup; their branches were not modified.
The earlier `d1aeffcdb949703a782eb9896a508fa935128301` baseline was superseded before branching.
Real tooling: `/workspace/.tools/dotnet/dotnet`, SDK 9.0.319 / runtime 9.0.21;
activation `/workspace/.setup/activate.sh`; actionlint 1.7.12. Hooks remain `.githooks`.

## Bypass evidence (read-only GitHub API, 2026-10-15)

- [Ruleset 24366111](https://github.com/thanhnt-sm/eco_support_net_oracle/rules/24366111)
  is active for the default branch. Required checks are exactly `Build and Test`,
  `Standards audit`, and `Security Scan`, with strict freshness. Neither publish name is required.
- Requires one approving review, code-owner review, latest-push approval, resolved review
  threads, stale-review dismissal, and extra approval for unattributed changes.
- GET `/pulls/60/reviews` and `/pulls/61/reviews` each returned `[]`. Required approval
  was unmet; deleting publication checks cannot remove that approval requirement.
- Both PRs now return `merged=true`, `state=closed`, `mergeable_state=unknown`;
  current responses cannot reconstruct the historical merge UI or every pre-merge blocker.
  Heads: PR60 `4aa40946ad1b5a3551f166c68148490368d9f9ca`, PR61
  `c8156a215a71ecaa50b4696efdcc1aef6948cbc8`.
- GET `/branches/main/protection` returned 403 `Resource not accessible by integration`.
  Legacy protection configuration remains an explicit evidence gap. No protection was changed.
- [Source run 38698569025](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38698569025):
  jobs 116303334661 (Visual Studio publication) and 116303334693 (VS Code publication)
  were skipped; C# test and both packaging jobs succeeded. Existing publication conditions
  allowed only manual dispatch with `publish=true`, never a PR publication.

## Implementation and reversal

Remove exactly the two publication jobs and the publish input, rather than adding false
conditions or synthetic success jobs. Remove publish-only tag preconditions, retaining
explicit tag-to-checkout validation. Preserve all packaging steps, tests, SBOMs, checksums,
artifact names/guards, non-PR attestations, and permissions on retained jobs.

The release helper defaults to no marketplaces and fails on explicit flag/config requests
before tag/push/dispatch, including dry-run. The interactive prompt no longer offers the
paused option. Historical dispatch implementation remains unreachable behind the preflight
for a focused, reversible pause. `.release.env.example` and EN/VI operator docs are synchronized.
Other release channels and workflows are untouched.

[Re-enablement procedure](../docs/marketplace-publishing.md) requires product approval and a
reviewed PR restoring a consistent workflow/helper contract, preferably in a manual-only
publication workflow. It records old source SHA, immutable-tag/tests/artifact verification,
pinned tools, environment approval and Windows publishing gaps. Existing tags still contain
old workflows; this change is not credential revocation.

## Validation log

- TDD: new topology test failed on the original two publish jobs; release-helper regression
  failed with `paused marketplace request succeeded` before implementation.
- After implementation: 119 Python tests, 118 passed / 1 skipped (environment-dependent test);
  `bash tests/git-tools/test_dg_release.sh` passed including CLI/config rejection and override.
- `python3 scripts/check-workflow-policy.py` passed across five workflows.
- Final local gates, independent review and hosted final-head CI: results recorded below
  and in the draft PR; a pending result is not a pass.
- Independent read-only review found no production blockers. Fixed its regression-fixture
  issue (`--yes` now uses `DRY_RUN=false`) and stale help text; reviewer rechecked and passed.
- actionlint 1.7.12 passed all workflows; docs-sync and whitespace checks passed.
- Local Docker smoke attempted with the available daemon: build blocked fetching pinned
  Microsoft SDK base-image metadata (MCR blob endpoint returned Forbidden). No Dockerfile
  change was made; hosted Docker smoke remains necessary.
- The one Python skip is the existing live GitHub alert-resolution test: local `gh` token
  authentication is unavailable. Connector read access succeeded except legacy protection.
- Unchanged pre-commit and pre-push hooks passed for implementation commit
  `8bd558ee3ce499473cf46d02f309fd0ed46c75c1`. The sandboxed run had four documented
  extractor fixture path failures caused by synthetic `/tmp/.git`; the authorized run
  outside that sandbox passed unchanged: 2,204 .NET tests / 5 Windows-only skips,
  coverage 71.18% (14,851/20,863), Release build zero warnings/errors, formatting,
  analyzers, locked restore, NuGet audit, topology, preflight and documentation gates passed.
  The hook's default `SKIP_ACT=1` leaves hosted secret scanning and matrix jobs required.
- A structural comparison against baseline confirmed all three retained job definitions
  are identical except removal of publish-only tag prerequisites; retained permissions match.
- Draft [PR62](https://github.com/thanhnt-sm/eco_support_net_oracle/pull/62) opened without
  modifying PR60/61. Hosted final-head run URLs/results are maintained in its description
  after completion, rather than claiming pending runs passed or recursively committing CI IDs.
