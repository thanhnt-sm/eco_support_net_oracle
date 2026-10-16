# Integrate merged main / Tích hợp main mới merge — 2026-10-16

Verified main: `5927edd852a71177bf1b068da66972153e3a9af3`.
PR64 is merged at `b2745bae75688215430ab1d5c097a370f22246ad`, head cc79f64;
GitHub read-only metadata reports merged=true, merged_at2026-10-16T02:24:00Z.
Read-only fetch and ancestry confirm the head and merge commit are in main.
Main also contains PR65 branch-normalizer changes. Relative to cc79f64 those are
seven non-overlapping normalizer/docs/fixture files; no extension source overlap.

New isolated worktree `/workspace/ide-main-update` starts at exact verified main.
The preserved cumulative extension patch SHA256
`2c185310f953686ec71588577373abcebffc0b6904130933bfaf42c7f7e2225c`
applies cleanly without replaying merged PR64/provider or PR65/normalizer changes.
Every previous patch/worktree remains unchanged. SDK9.0.319 and Node22.23.3 verified.
AGENTS and new main normalization docs/plan read; hooks and gates remain unchanged.

| Requirement | Source | Acceptance evidence |
| --- | --- | --- |
| Update from actual merged source | origin/main + PR64 metadata/ancestry | exact SHAs; no duplicate merged hunks |
| Preserve local maintenance | prior cumulative patch + all previous snapshots | hashes unchanged; source byte comparison |
| Actual regressions | lifecycle/edge/controller/provider tests | fresh main baseline then integrated npm test/coverage |
| Normalizer compatibility | merged coverage_manifest.py and tests | Python full suite and focused normalizer tests; no script modifications |
| Full local gate | existing verify_local_gates.sh SKIP_ACT=1 | Release/analyzers/format/C#coverage/audit/docs/actionlint |
| Review and bilingual docs | independent reviewer + EN/VI extension guides | no unresolved blockers; exact measured claims |

Initial work is local integration. Separately verified explicit user approval permits
one publication attempt only after gates/review pass: push a separate maintenance
branch and create a draft PR; never modify PR64, merge, release, change security,
bypass an API denial or retry a rejected approval through another route. Historical
local-only batch plans remain records of their original validation, not new runtime
claims. No external test uploads are part of local verification.

Runtime limits persist: real editor/Windows/.NET LSP integration is unverified;
synthetic owned Node child exit is observed with a client double. Unexposed child
or refused kill can remain unresolved. All16 TS files retained; no coverage exclusion,
threshold, normalizer, hook, lock or workflow changes in this maintenance diff.

## Integration results / Kết quả tích hợp

Fresh merged-main baseline:117 TypeScript tests passed, all16 files, lines2737/4228
(64.73%), raw branches430/500(86.00%). Integrated:189 passed, no failures/skips,
lines4146/4326(95.83%), branches1053/1126(93.51%). Source/branch denominators reflect
the preserved lifecycle fixes and newly executed branches, not normalizer changes.
Independent reviewer verified no duplicated/lost merged changes, byte-identical
maintenance source/tests/package and prior plans; focused72/72passed, no blockers.
Python full suite:143 total,142 passed/1existing skip; focused normalizer24passed.
Unchanged local gate passed:2213 C#passed/5Windows-only junction skips; union lines
17016/20978(81.11%). Build/analyzers0warnings/errors; formatting emits workspace-loading
warnings but returns0. Restore/audit, docs, topology, actionlint and whitespace checks
pass. Fresh five-report coverage normalization succeeds locally and raw input hashes
remain unchanged; no aggregate C#branch claim or external upload.

Evidence: `/workspace/.tmp/ide-main-update-evidence/`, including before/after summary
JSON, npm/Python/gate logs, raw/normalized C#reports, PR64/main metadata, patch and
manifest. Reproduce with `source /workspace/.setup/activate.sh`; under VSCode run
`npm test` and `npm run test:coverage`; root: `python3 -m unittest discover -s scripts/tests`,
`PATH=/workspace/.tools/actionlint-1.7.12:$PATH SKIP_ACT=1 ./scripts/verify_local_gates.sh`,
`./scripts/verify_docs_sync.sh`, `git diff --check`. SKIP_ACT=1 omits act/Docker and
remote secret verification under the existing local mode, not claiming they passed.
