# Simplify — applied: scripts + CI workflow files (260929-1911)

Branch `feat/vs-extension-hardening`, HEAD `8a2df13`, no commit. Behaviour identical; quality-only edits.
Source findings: `simplify-260929-1541-simplification.md` #9, `simplify-260929-1541-efficiency.md` #5, #1.

## Applied / skipped

| # | Finding | Decision | Reason |
|---|---|---|---|
| 1 | Simplification #9 — move inline self-test out of `scripts/check-workflow-policy.py` | **APPLIED** | Fixtures (`REAL_GUARD`, `fixture()`, `RELEASE_FIXTURE`, 14 cases) moved to `scripts/tests/test_check_workflow_policy.py` (stdlib `unittest`, no pytest) — fixture content verbatim; two case labels sanitized (`if: false` -> `if false`, `if: ${{ false }}` -> `if expression false`) so they form valid `test_*` method names. Script loaded by path via `importlib.util.spec_from_file_location` (hyphenated filename). One `test_*` method generated per case so `-v` lists 14 named tests. `--self-test` branch + docstring line dropped; expression parser (`split_top_level`/`strip_outer_parentheses`) untouched. Assertion logic mirrors old `self_test()`: `expected is None -> failures == []`, else `any(f.startswith(expected))`. |
| 2 | Efficiency #5 — nested `pwsh -NoProfile -File scripts/assert-vsix.ps1` inside a pwsh step | **APPLIED** | `ci.yml:180`, `release.yml:509` -> `& ./scripts/assert-vsix.ps1 ...`. Verified locally (pwsh 7): `exit 1` from inside the script's `Fail` function, through `try/finally`, sets `$LASTEXITCODE=1` in the caller; `exit 0` resets to 0. Script does not rely on `-File` semantics (`$ErrorActionPreference='Stop'` is script-scoped; no `throw`, only `Fail` -> `exit 1`). Kept the following `if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }`. Policy rule (a) still matches: `./scripts/assert-vsix.ps1` contains the `scripts/assert-vsix.ps1` substring. |
| 3 | Efficiency #1 — drop `fetch-depth: 0` on `cli-package` checkout | **SKIPPED** | `-p:Version` does NOT neutralise MinVer. `Directory.Build.props:40` references MinVer 8.0.0 for every project. `MinVer.targets` (v8.0.0, lines 80-85) sets `AssemblyVersion`, `FileVersion`, `InformationalVersion`, `PackageVersion` and `Version` from the git-derived `MinVerVersion`; `-p:Version` is a global property so only `Version` is overridden — `FileVersion`/`InformationalVersion` in the published `dataguard.dll`/`.exe` still come from the tag walk. With a depth-1 clone MinVer falls back to `0.0.0-alpha.0.N` -> `FileVersion 0.0.0.0`. Also the `cli-package` checkout has no `ref:`; on the `workflow_dispatch` path it checks out the branch head, not the tag, so depth 1 would not even see `vX.Y.Z`. The existing `build-and-test` pack job (`release.yml:74`) uses `fetch-depth: 0` for the same reason. Cheaper alternative would be `-p:MinVerVersionOverride=$RELEASE_VERSION` (MinVer then skips the git walk) — a behaviour-affecting versioning change, out of scope for a simplify pass; flagged below. |

## Line counts

| File | Before | After |
|---|---|---|
| `scripts/check-workflow-policy.py` | 292 | 209 (target <= 210) |
| `scripts/tests/test_check_workflow_policy.py` | — | 118 (new) |
| `.github/workflows/ci.yml` | 369 | 369 (3 lines changed: comment, unittest invocation, assert invocation) |
| `.github/workflows/release.yml` | 754 | 754 (1 line changed: assert invocation) |

## Files touched (ownership respected)

- `scripts/check-workflow-policy.py` — removed self-test block (old lines 192-270) and the `--self-test` branch in `main()`; docstring now points at the unit tests.
- `scripts/tests/test_check_workflow_policy.py` — new.
- `.github/workflows/ci.yml` — only the "Workflow policy check" step (comment line + `python3 -m unittest discover -s scripts/tests -v` replaces `--self-test`) and the "Assert VSIX contents" invocation line. "Package VSIX" msbuild step, setup-dotnet, restore: untouched.
- `.github/workflows/release.yml` — only the assert-vsix invocation line. `cli-package` checkout untouched (see #3).

Not mine (sibling simplify agents, present in the working tree): `src/DataGuard.VSCode/src/*`, `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs`, `PhantomIdentifierRuleRegexTests.cs`, `src/**/packages.lock.json`.

## Gate outputs

```
$ python -m unittest discover -s scripts/tests -v
test_assert_script_only_in_a_run_comment_does_not_count ... ok
test_assert_with_continue_on_error_does_not_count ... ok
test_assert_with_if_expression_false_does_not_count ... ok
test_assert_with_if_false_does_not_count ... ok
test_both_PR_triggers_fail_closed ... ok
test_event_clause_alone_is_not_enough ... ok
test_guard_OR_ed_with_true_is_not_a_guard ... ok
test_pull_request_guard_does_not_cover_pull_request_target ... ok
test_pull_request_target_guard_passes ... ok
test_real_shape_passes ... ok
test_release_VS_test_step_with_continue_on_error_does_not_count ... ok
test_same_repo_clause_alone_is_not_enough ... ok
test_unguarded_upload_on_pull_request_target ... ok
test_unguarded_upload_without_a_PR_trigger_is_allowed ... ok
Ran 14 tests in 0.029s  OK

$ python scripts/check-workflow-policy.py
check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)   (exit 0)

yaml.safe_load: ci.yml jobs: 8 / release.yml jobs: 13 (both parse)
actionlint ci.yml release.yml: exit 0, no output (baseline before edits: also 0)
bash ./scripts/verify_docs_sync.sh: "All bilingual documentation & rule artifacts are synchronized and present!" exit 0
LF check (CR bytes): check-workflow-policy.py 0, test_check_workflow_policy.py 0, ci.yml 0, release.yml 0
release.yml has no trailing newline — pre-existing at HEAD (git show HEAD ends "mode=max"), not introduced here.
pwsh in-process exit probe: Fail -> exit 1 inside try/finally -> $LASTEXITCODE=1 (finally ran); exit 0 -> 0.
```

## Follow-ups (outside ownership / scope)

- `docs/journals/260929-1524-vs-hardening-redteam-tdd-followup-cook.md:64` and `CHANGELOG.md:17` mention the policy script; neither mentions `--self-test`, so no doc drift. `ci.yml:32` comment "See scripts/check-workflow-policy.py" is still accurate.
- If the `cli-package` clone cost matters, the correct lever is `-p:MinVerVersionOverride="$RELEASE_VERSION"` (plus `fetch-depth: 1`), which pins all five version properties to the release version and skips the git walk. That changes the source of published assembly metadata and needs an owner decision; not applied.
- Report path: task specified `simplify-260929-1911-applied-scripts-ci.md`; the injected Naming pattern (`fullstack-developer-260929-1912-{slug}.md`) differs — the task path was used.

**Status:** DONE
**Summary:** Applied #1 (self-test -> `scripts/tests/`, script 292 -> 209 lines, ci.yml runs unittest) and #2 (in-process assert-vsix in ci.yml + release.yml); skipped #3 with MinVer evidence (fetch-depth 0 still needed for FileVersion/InformationalVersion). All gates pass; only owned files touched; no commit.
**Concerns/Blockers:** None blocking. Working tree also carries sibling agents' VSCode/test edits — not mine, left untouched.
