# Phase Implementation Report — Scorecard alert #109 (pip not pinned by hash)

## Executed Phase
Single-step CI hardening; no plan dir. Repo: `D:\100.Software\Github\eco_support_net_oracle`. Status: **completed**.
PR: https://github.com/thanhnt-sm/eco_support_net_oracle/pull/31 (branch `fix/scorecard-pin-pip-by-hash`, commit `6708bb0`, base `main` @ `a866d56`). Not merged.

## Files Modified
- `.github/workflows/ci.yml` — "Workflow policy check" step only: fallback is now
  `python3 -m pip install --user --require-hashes --only-binary=:all: -r scripts/requirements-ci.txt`; comment block updated. `import yaml` short-circuit unchanged.
- `scripts/requirements-ci.txt` — new. `pyyaml==6.0.2` + 13 `--hash=sha256:` (cp310–cp313 manylinux2014 x86_64 + aarch64 = 8, win_amd64 = 4, sdist = 1). Header documents purpose + regen command.
- `CHANGELOG.md` — `[Unreleased] → Changed`, `**CI (2026-09-30)**` line naming alert #109.
- Not touched: lock files, `scripts/check-workflow-policy.py`, `scripts/tests` (policy script has no assertion on the pip step; 18/18 tests pass unchanged).

## Design decision
Requirements-file route over `actions/setup-python` + `cache: pip`: zero new actions to SHA-pin / Dependabot-track, no cache key, keeps the no-network common path; setup-python would still end in the identical hashed `pip install`. Scorecard keys on `--require-hashes` in the `run:` text, which it now is. Sdist hash is inert under `--only-binary=:all:` (included as instructed); win_amd64 hashes exist only so the local Windows check is real.

## Tests Status
- `pip download --require-hashes --only-binary=:all: -r scripts/requirements-ci.txt`: native win cp313 OK; `--platform manylinux2014_x86_64` cp310/cp312/cp313 OK; `manylinux2014_aarch64` cp312 OK (cp312 x86_64 = ubuntu-24.04 system python wheel). Each resolved to the expected wheel and passed hash verification.
- `pip install --help` (pip 25.3): `--require-hashes`, `--user`, `--only-binary` accepted.
- YAML parse OK; `actionlint .github/workflows/ci.yml` clean; `python scripts/check-workflow-policy.py` OK; `unittest discover -s scripts/tests` 18/18 OK.
- EOL: all three files `i/lf w/lf`; `git diff --check` clean.
- Hooks ran unmodified: pre-commit (dotnet format whitespace, topology guard, docs sync) passed; pre-push `verify_local_gates.sh` passed twice (coverage 62.53%, NuGet audit clean) — second run in a clean worktree of `6708bb0` only.

## Issues Encountered
1. **Shared working-tree collision with a sibling agent (recovered, no data loss).** Reflog: I created my branch at 10:55:21; at 10:55:48 another session ran `git switch -c fix/scorecard-osv-vulnerabilities origin/main` in the same checkout; my 11:00:05 commit therefore landed on *their* branch, and my first `git push -u origin fix/scorecard-pin-pip-by-hash` pushed my branch still at `a866d56` (`gh pr create` → "No commits between main and branch"). Repair: `git branch -f fix/scorecard-pin-pip-by-hash 6708bb0`; `git reset --mixed a866d56` on their branch (working tree + their unstaged `src/DataGuard.VSCode/package*.json` untouched); verified my three files carried only my hunks, then `git checkout -- ci.yml CHANGELOG.md` + `rm scripts/requirements-ci.txt`. Sibling has since committed `c03bdc4`: parent = `a866d56`, contains my commit = **no**. Orchestrator: parallel executors on one checkout need `isolation: "worktree"` (or serialize git work).
2. Worktree push retry #1 failed: scratchpad path + repo's long `plans/…` filenames exceed Windows MAX_PATH ("Filename too long"). Used `C:\Users\thant\AppData\Local\Temp\ck-wt-pin` instead; removed after push.
3. Pre-existing `packages.lock.json` " M" flags in the shared tree are CRLF-only (`i/lf w/crlf`, empty content diff) — left alone per "no lock-file edits".
4. Report path: parent asked for `…-1048-scorecard-pip-hash.md`; Naming hook computed `…-1055-{slug}.md`. Written to the parent's explicit path.

## Next Steps
- Orchestrator: wait for hosted CI + Scorecard re-run on PR #31; alert #109 should close on merge. Do not merge from this session (per instruction).
- Content-level leak check: `git grep -n requirements-ci c03bdc4 -- CHANGELOG.md .github/workflows/ci.yml` returns nothing — the sibling's tree carries neither my commit nor my text.
- Merge-order note: the sibling's OSV PR also edits root `CHANGELOG.md` `[Unreleased]`; whichever of PR #31 / the OSV PR merges second will need a trivial `CHANGELOG.md` rebase. Merge one, rebase the other, don't hit it cold.
- Optional follow-up (not in scope): extend `scripts/check-workflow-policy.py` to assert every `pip install` in `.github/workflows` carries `--require-hashes`, so the alert cannot regress silently.

**Status:** DONE_WITH_CONCERNS
**Summary:** Alert #109 fix implemented, verified (hash-pinned PyYAML via `scripts/requirements-ci.txt`, `--require-hashes` in the run line), committed `6708bb0`, pushed, PR #31 open against main. Concern is operational, not correctness: a sibling agent shares this checkout and briefly absorbed my commit; repaired without loss, verified.
**Concerns/Blockers:** Shared working tree between parallel executors (see Issue 1) — recommend worktree isolation for concurrent git work. No blockers.
**PR:** https://github.com/thanhnt-sm/eco_support_net_oracle/pull/31
