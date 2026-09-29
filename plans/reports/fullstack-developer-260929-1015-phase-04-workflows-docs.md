# Phase 4 — Workflows and docs: shared VSIX assert and corrected claims (TDD)

Plan: `plans/260929-0952-vs-hardening-redteam-tdd-followup/phase-04-workflows-and-docs-shared-vsix-assert-and-corrected-claims.md`
Branch: `feat/vs-extension-hardening` (uncommitted). Untouched as instructed: `packages.lock.json`, `.claude/`, `src/DataGuard.VisualStudio`, `tests/DataGuard.VisualStudio.Tests`, `src/DataGuard.VSCode`.
Report path: parent-requested (`…-1015-phase-04-…`); hook naming would have been `…-1341-…`. Kept the parent's, as Phase 3 did.
Status: **completed** (Phase 2 report absent at write time — see §4).

## 1. Policy check (Tests Before / After)

Command (system python 3.13 + PyYAML, from repo root):
```
python scripts/check-workflow-policy.py
```
Asserts: (a) every `run:` step building `DataGuard.VisualStudio.csproj` is followed in the same job by a step containing `scripts/assert-vsix.ps1`; (b) every `actions/upload-artifact` step whose `with.name`/`with.path` mentions `vsix`, in a workflow with a `pull_request` trigger, has `github.event.pull_request.head.repo.full_name == github.repository` in its `if:`; (c) `release.yml` `visual-studio-package` has a `dotnet test tests/DataGuard.VisualStudio.Tests… --configuration Release` step with a lower index than the MSBuild step.
Scope choice for (b): VSIX uploads only. test-results/coverage/sbom/benchmark artifacts are not installables; guarding them would strip fork PRs of debug evidence for no security gain. `release.yml` has no `pull_request` trigger, so (b) is computed vacuous there from the `on:` block (PyYAML reads `on:` as boolean `True`; handled).

RED (before any workflow change) — exit 1:
```
check-workflow-policy: FAIL
  - (a) .github/workflows/ci.yml job 'visual-studio-vsix-package' step 4 (Package VSIX (CreateVsixContainer=true)): MSBuild of DataGuard.VisualStudio.csproj is not followed by scripts/assert-vsix.ps1
  - (b) .github/workflows/ci.yml job 'visual-studio-vsix-package' step 6 (Upload VSIX artifact): VSIX upload lacks the same-repo guard `github.event.pull_request.head.repo.full_name == github.repository`
  - (a) .github/workflows/release.yml job 'visual-studio-package' step 4 (Restore and package VSIX): MSBuild of DataGuard.VisualStudio.csproj is not followed by scripts/assert-vsix.ps1
  - (c) .github/workflows/release.yml job 'visual-studio-package': no `dotnet test tests/DataGuard.VisualStudio.Tests --configuration Release` step
```
(ci.yml (a) failed too because the inline assert did not reference the shared script yet.)

GREEN (after) — exit 0:
```
check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)
```
Not wired into CI (nothing under `tests/git-tools` runs in CI either); local command above.

## 2. Files

Created
- `scripts/assert-vsix.ps1` (72 lines) — `-VsixPath`, `-ExpectedVersion` (both mandatory); required entries `cli/dataguard.exe`, `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `extension.vsixmanifest`, `DataGuard.VisualStudio.dll`, `DataGuard.VisualStudio.pkgdef`; manifest `Identity/@Version` must equal `ExpectedVersion`; single `assert-vsix: FAIL - …` line + exit 1 on missing file / bad zip / missing entries / bad XML / missing or mismatched version; backslash entry names normalised.
- `scripts/check-workflow-policy.py` (129 lines) — see §1.

Modified
- `.github/workflows/ci.yml` — "Assert VSIX contents" now derives `$sourceVersion` from `ExtensionVersion.cs` (unchanged regex) and calls `pwsh -NoProfile -File scripts/assert-vsix.ps1 -VsixPath … -ExpectedVersion $sourceVersion` + `exit $LASTEXITCODE`; upload `if:` = `env.ACT != 'true' && (github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository)`; artifact name `dataguard-visualstudio-vsix-ci-${{ github.sha }}`. Build + assert still run on fork PRs.
- `.github/workflows/release.yml` `visual-studio-package` — added `Setup .NET` (same SHA-pinned action as ci.yml), `Restore Visual Studio tests` (`--locked-mode`), `Run Visual Studio tests` (`--configuration Release --no-restore`) **before** "Set VSIX manifest version" (tests run against committed source); MSBuild step now ends after the build; new "Assert VSIX contents" step (`-ExpectedVersion $env:VERSION` = release version, so it also proves the manifest patch took); copy + SHA-256 moved to "Stage VSIX and checksum". Upload unchanged (tag/dispatch only).
- `README.md` — Quickstart line 15 only: `dotnet tool install -g DataGuard.Cli` → GitHub Releases + SHA-256 comment, not-on-nuget note.
- `SECURITY.md`, `SECURITY.vi.md` — IDE-hosts bullet split into: ide-safe scoped to `validate`/`assess` + clamps + `ide-safe: active` handshake + CLI ≤ 0.2.2 rejected / ≥ 0.3.0 required + baseline warning/`BaselineApplied`; user-credential bullet (`--allow-env-connection`, config credential always stripped, VS Code SecretStorage only, live-database commands with confirmation in VS Code, VS has none); VS trust-gate bullet (consent key incl. `.sln` path); CLI distribution bullet (not on nuget.org, owner reserves ID, GitHub Releases + SHA-256, bundled `cli\dataguard.exe`, shared assert, fork VSIX never uploaded); hardening bullet (1 s regex timeout, 256 KiB literal cap / DG1291).
- `docs/USAGE.md` — §"Cài Đặt CLI Tool" rewritten (Releases + `sha256sum`/`Get-FileHash`, 0.3.0 requirement, no nuget); `--allow-env-connection` option row; `--ide-safe` paragraph scoped to validate/assess + 5 sub-bullets (handshake incl. old-CLI `Unrecognized command or argument '--ide-safe'` exit 1; `--allow-env-connection` semantics and `ide-safe: kept environment connection (--allow-env-connection)`; clamps `Name=value (clamped to bound)`; `baseline: <n> violations suppressed by <path>` / `BaselineApplied`; 1 s regex timeout + `[WARN] DG1291 SQL literal in <file>:<line> is <n> chars (cap 262144); skipped`); exit-code 2 note; VS 2022 section updated; new "VS Code Extension" section; the four CI/CD examples (`legacy onboarding`, GitHub Actions, Azure DevOps, GitLab) replaced `dotnet tool install` with a curl + `sha256sum -c` download step (`dataguard-linux-x64` asset name is illustrative — see §6).
- `CHANGELOG.md` — `## [Unreleased] — next release must be tagged v0.3.0` + blockquote (hosts reject ≤ 0.2.2, latest tag v0.2.2, nuget not published); new Security entries (CLI / VS / VS Code red-team follow-up, CI/Release shared assert + fork guard + policy script), Fixed entries (CLI baseline warning + junction/relativise; VS lifecycle fixes), Changed entry (docs). Existing entries preserved, one section per type.
- `plans/260929-0835-vs-extension-hardening/phase-02-vs-package-modularize-and-trust-gate.md` — Req 2 "exits 2" → "exits 1 (System.CommandLine unknown-option rejection)"; create list drops `ExitCodeExplainerTests` and notes the tests live in `CliArgumentBuilderTests.cs`.
- `plans/260929-0835-vs-extension-hardening/phase-03-ci-vsix-packaging-gate.md` — `timeout-minutes: 30` → `40`.

## 3. assert-vsix runs (Tests After)

No VSIX exists under `src/DataGuard.VisualStudio/bin/Release` (Phase 2 has not produced one), so a fake archive with the six required entries + `[Content_Types].xml` + a vsix-schema manifest (`Version="0.2.3"`) was built in the scratchpad with `System.IO.Compression.ZipFile` (exact forward-slash entry names), then a copy with `cli/dataguard.exe` deleted.
```
pwsh -File scripts/assert-vsix.ps1 -VsixPath fake-good.vsix -ExpectedVersion 0.2.3
assert-vsix: OK - fake-good.vsix (0 MB), version 0.2.3, 7 entries, all 6 required entries present
exit=0

pwsh -File scripts/assert-vsix.ps1 -VsixPath fake-tampered.vsix -ExpectedVersion 0.2.3
assert-vsix: FAIL - VSIX 'fake-tampered.vsix' is missing required entries: cli/dataguard.exe. Entries present: DataGuard.Analyzers.dll, DataGuard.CodeFixes.dll, DataGuard.VisualStudio.dll, DataGuard.VisualStudio.pkgdef, [Content_Types].xml, extension.vsixmanifest
exit=1

pwsh -File scripts/assert-vsix.ps1 -VsixPath fake-good.vsix -ExpectedVersion 9.9.9
assert-vsix: FAIL - manifest version '0.2.3' does not match expected version '9.9.9'
exit=1

pwsh -File scripts/assert-vsix.ps1 -VsixPath nope.vsix -ExpectedVersion 0.2.3
assert-vsix: FAIL - VSIX not found at '…/nope.vsix'
exit=1
```
Phase 5 should re-run the OK path against the real packaging-build VSIX.

Regression gate: `python -c "import yaml…"` both workflows parse; `actionlint` (on PATH) clean on both; policy check OK; `bash ./scripts/verify_docs_sync.sh` → "All bilingual documentation & rule artifacts are synchronized and present!".

## 4. VS-specific doc sentences written WITHOUT the Phase 2 report (verify against Phase 2)

Source: phase-02 Requirements + current (mid-edit) `src/DataGuard.VisualStudio/CliArgumentBuilder.cs` (has `--ide-safe`, no `--allow-env-connection`).
1. SECURITY.md/.vi: "The CLI prints `ide-safe: active` as its first stderr line; a host discards the results when that line is missing (an older CLI, 0.2.2 and below, rejects the flag and is reported as too old…)" — VS half.
2. SECURITY.md/.vi: "Visual Studio exposes no live-database command." / "Visual Studio không có lệnh kết nối database."
3. SECURITY.md/.vi: "one-time consent per solution file (keyed by the solution directory, the `.sln` path and the `.dataguard.yml` hash)".
4. docs/USAGE.md VS section: "extension không có lệnh kết nối database và không truyền `--allow-env-connection`. Kết quả chỉ được nạp vào Error List khi CLI in `ide-safe: active`; thiếu dòng này thì kết quả bị huỷ, và CLI ≤ 0.2.2 (từ chối cờ, exit 1) được báo là quá cũ."
5. docs/USAGE.md VS section: "Đồng ý được khoá theo (thư mục solution, đường dẫn `.sln`, SHA-256 của `.dataguard.yml`); lệnh **Forget Solution Consent** xoá đồng ý của solution hiện tại."
6. docs/USAGE.md VS section: "Extension **không** tự cài và **không** hướng dẫn `dotnet tool install` nữa." (current tree still has two `dotnet tool` strings in `DataGuardPackage.Commands.cs:151` / `DataGuardPackage.Publishing.cs:82` — true only once Phase 2 lands.)
7. docs/USAGE.md VS section: "Dòng `ide-safe:`/`baseline:` của CLI được hiển thị trong Output pane." (Phase 1 report Q1 says VS must learn `BaselineApplied`; not confirmed.)
8. CHANGELOG Security (VS) and Fixed (VS) entries — every clause restates a phase-02 requirement (termination classification, timeout publish, handshake gate, spoof-proof old-CLI detection, consent key + Forget command, inventory/Cancel, off-UI-thread start, progress pump, solution lifetime, ExitCodeExplainer `sarifExists`, exit 3 preserving Error List).

VS Code and CLI sentences are quoted from the shipped code/reports (`ide-safe-contract.ts`, `IdeSafePolicy.cs`, `Program.cs`, VS Code README).

## 5. Deviations

- Policy check is a standalone Python script (not `tests/git-tools`, which is bash with no CI runner) and is not added to CI — kept the workflow diff to the requested gates.
- `release.yml`: added a `Setup .NET` step (job had none; `dotnet test` needs it) and split the old "Restore and package VSIX" step into build / assert / stage. Tests are placed before the manifest patch (deliberate: test committed source).
- `README.md` line 15 replaced with a 3-line comment block instead of one line (needs the URL + the not-on-nuget note).
- USAGE.md CI/CD examples reference a `dataguard-linux-x64` release asset and `DATAGUARD_VERSION`/`DATAGUARD_SHA256` placeholders; the asset naming is not yet defined by `release.yml` (which publishes nupkg + VSIX + SBOM, no standalone CLI binary) — see §6.
- CHANGELOG stays under `[Unreleased]` (Keep a Changelog; nothing tagged) with the v0.3.0 requirement in the heading + blockquote rather than a `## [0.3.0]` section.
- Phase-04 file `status: pending` not flipped (plan file not in my ownership list; lead's call).

## 6. Unresolved questions

1. `release.yml` publishes no standalone CLI binary asset, yet all docs now say "download `dataguard` from GitHub Releases and verify SHA-256". Either add a CLI publish (per-RID zip + `.sha256`) to the release workflow (Phase 5?) or the guidance is aspirational until then.
2. `README.vi.md:18` still has `dotnet tool install -g DataGuard.Cli` — not in my ownership; same one-line replacement as README.md needed.
3. VS Code Marketplace changelog (`src/DataGuard.VSCode/CHANGELOG.md`, if any) must state CLI ≤ 0.2.2 rejection (Phase 3 Q4) — not owned here.
4. Should `scripts/check-workflow-policy.py` run in CI (e.g. a `python3` step in `build-and-test`; ubuntu runners ship PyYAML)? Cheap to add once the lead agrees.
5. Items in §4 need a pass against the Phase 2 report; #6 and #7 are false on the current tree until Phase 2 lands.
6. `SECURITY.md` "Supported versions" table still says `0.1.x (pre-release)`; left untouched (out of scope) but stale next to the v0.3.0 note.
7. `release.yml` `visual-studio-package` now runs `dotnet test` (builds the VS project with `CreateVsixContainer=false`) → manifest/`Fallback` patch → `msbuild /restore /t:Build` in one job. No workflow exercised that sequence before; incremental build should pick up the patched `ExtensionVersion.cs`, but the first `v0.3.0` tag run is the first real test — Phase 5 should watch it (a `workflow_dispatch` dry run against a throwaway tag would confirm).
8. CHANGELOG has no `[Unreleased]: <compare-url>` link reference, so the decorated `## [Unreleased] — …` heading is safe; if a link reference is added later, move the v0.3.0 requirement into the blockquote only.

**Status:** DONE_WITH_CONCERNS
**Summary:** Policy check RED (4 assertions) → GREEN; shared `scripts/assert-vsix.ps1` used by ci.yml and release.yml, release runs VS unit tests before packaging, CI VSIX upload guarded to same-repo PRs with SHA-named artifact; SECURITY (en/vi), USAGE, README Quickstart, CHANGELOG and the two plan files corrected; YAML/actionlint/docs-sync gates green.
**Concerns/Blockers:** VS-specific doc sentences (§4) are unverified pending the Phase 2 report; docs promise a GitHub Releases CLI binary that `release.yml` does not yet publish (§6.1); `README.vi.md` NuGet line outside ownership (§6.2).

## Follow-up (lead decisions on Q1–Q4, Q6)

### Changes
- **Q1 — `release.yml` `cli-package` job** (matrix `win-x64`, `linux-x64`, `osx-arm64`; ubuntu-latest; `needs: build-and-test`; `contents: read`): `dotnet publish src/DataGuard.Cli/DataGuard.Cli.csproj --configuration Release --runtime <rid> --self-contained false -p:PublishBundledCli=true -p:Version=<release_version> -p:NuGetLockFilePath=obj/cli-publish-<rid>.packages.lock.json -p:RestoreLockedMode=false`, then `zip -qr dataguard-<version>-<rid>.zip .` + `sha256sum > <asset>.sha256`, uploaded as `dataguard-cli-<rid>` (30 days). csproj check: `PackAsTool=true`, no RID/SingleFile settings; `PublishBundledCli=true` is the existing csproj switch that names the executable `dataguard` (otherwise `DataGuard.Cli`); the lock-file properties mirror the VSIX's bundled-CLI publish so committed `packages.lock.json` files are not rewritten (NU1004 regression in CHANGELOG). `create-github-release` now `needs` `cli-package`, downloads `pattern: dataguard-cli-*` with `merge-multiple: true`, and attaches `./artifacts/dataguard-*.zip` + `.sha256`; `publish-attestations` downloads the same and adds `./artifacts/dataguard-*.zip` to `subject-path`. Release notes text mentions the CLI packages. All actions reuse the existing SHA pins. Policy script needed no change (the new job has no VS MSBuild/VSIX upload).
  Local smoke (linux-x64 RID from Windows): `dotnet publish … --runtime linux-x64 --self-contained false …` → exit 0; output contains `dataguard` (apphost), `dataguard.dll`, `dataguard.runtimeconfig.json` (96 files); `git status` of `**/packages.lock.json` identical before/after.
- **Q2** — `README.vi.md` Quickstart: `dotnet tool install -g DataGuard.Cli` → GitHub Releases `dataguard-<version>-<rid>.zip` + `.sha256` guidance (3-line comment, LF preserved; diff 3/1). `README.md` Quickstart comment aligned to the same asset naming.
- **Q3** — created `src/DataGuard.VSCode/CHANGELOG.md` (Keep-a-Changelog style, `[Unreleased]` + `[0.2.3]`): validate/assess require the `ide-safe: active` handshake, CLI ≤ 0.2.2 rejected with the exact notification strings (`DataGuard CLI did not confirm IDE-safe mode; results were discarded` / `Update the dataguard CLI (0.3.0 or later) or set dataguard.cliPath`), `--allow-env-connection` with SecretStorage, verify-shape confirmation, `[WARN]` echo, Releases install guidance. Not excluded by `.vscodeignore`, so vsce will ship it to the Marketplace changelog tab.
- **Q4** — `ci.yml` `build-and-test` gained a "Workflow policy check" step right after checkout: `python3 -c "import yaml" 2>/dev/null || python3 -m pip install --user pyyaml` then `python3 scripts/check-workflow-policy.py`.
- **Q6** — `SECURITY.md` / `SECURITY.vi.md` supported-versions table: `0.2.x (latest tag v0.2.2)` best effort + rejected by IDE hosts for validate/assess; `0.3.x (upcoming, next tag v0.3.0)` will be the supported line, required by both extensions; `0.1.x` no longer supported. CLI-distribution bullet now names `dataguard-<version>-<rid>.zip` (`win-x64`, `linux-x64`, `osx-arm64`; framework-dependent, .NET 9 runtime) + `.sha256` + provenance attestation.
- Docs asset naming — `docs/USAGE.md` install section (`sha256sum -c <asset>.sha256` / `Get-FileHash … -ieq` on Windows, framework-dependent note) and the three CI/CD examples (GitHub Actions, Azure DevOps, GitLab) download `dataguard-${DATAGUARD_VERSION}-linux-x64.zip` + `.sha256` from `releases/download/v${DATAGUARD_VERSION}/`, run `sha256sum -c`, unzip and prepend to PATH. Root `CHANGELOG.md` CI/Release entry mentions the `cli-package` job and the CI policy step.

### Gate results (verbatim last lines)
Policy script before the follow-up edits (already GREEN from the main phase; the new job adds no VS MSBuild/VSIX-upload step, so no new RED was expected) and after:
```
check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)
policy exit=0
```
YAML parse:
```
.github/workflows/ci.yml parses
.github/workflows/release.yml parses
```
actionlint:
```
actionlint exit=0
```
(`actionlint` printed nothing — no findings.) Line endings: all touched files `i/lf w/lf`.

### Notes for Phase 5
- First `v0.3.0` tag run exercises `cli-package` for real (win-x64/osx-arm64 publishes are cross-RID from ubuntu — supported for framework-dependent output). Verify the three zips + `.sha256` land on the release and that `unzip`/`zip` are present on the runner (they are on ubuntu-latest).
- `-p:Version=<release_version>` overrides MinVer for the CLI assembly, same as the existing `dotnet pack` step.

**Status:** DONE_WITH_CONCERNS
**Summary:** Follow-up applied — `cli-package` release job (3 RIDs, zip + sha256, attached to the GitHub Release and attested), policy script wired into CI, `README.vi.md` fixed, VS Code Marketplace changelog created, SECURITY supported-versions tables made truthful, all docs aligned to `dataguard-<version>-<rid>.zip` + `.sha256`; policy/YAML/actionlint green; local RID publish smoke passed without touching lock files.
**Concerns/Blockers:** the eight VS-specific doc sentences in §4 remain unverified until the Phase 2 report exists (two are false on the current tree); the new release job has not run in GitHub Actions yet (first exercise is the `v0.3.0` tag).

## Review fixes (VS Code/scripts/docs)

Source: `plans/reports/code-reviewer-260929-1412-vs-hardening-followup.md` — M5, L1, L2, L7, L8, L9 and the doc halves of H1/M1/M2. Branch `feat/vs-extension-hardening` at c37a2ce, nothing committed. Ownership respected: VS Code `src/` (no `node_modules`/`out`/`.vsix`), `scripts/check-workflow-policy.py`, `ci.yml` policy step, SECURITY en/vi, `docs/USAGE.md`, root and VS Code CHANGELOG, VS Code README. CLI/Core/VS trees and lock files untouched by this pass (they carry the concurrent agents' edits).

### RED → GREEN

| Finding | RED (observed before the fix) | GREEN |
|---|---|---|
| L9 `ide-safe-contract.ts` | `formatProgressLine("ide-safe: active")` expected `[INFO] ide-safe: active`, actual `[WARN] ide-safe: active` (1 of 8 tests in `ide-safe-contract.test.js` failed) | exact trimmed ack → `[INFO] ide-safe: active`; `ide-safe: active!`, `ide-safe: kept …`, `baseline: …` stay `[WARN]`; JSDoc updated |
| L7 `extension.ts` / `live-database-confirmation.ts` | `tsc` error TS2305: no exported member `countReadQueries` (test written against the helper first). Honesty note: the old inline `latestScanReport?.queries.filter(...)` could not throw through any real path — `extension.ts:539` already normalises `parsedRecord.queries` to `[]` and `latestScanReport` is only ever that object or `null` — so this is hardening of a cast, not a reachable bug; both CHANGELOG bullets are worded that way | new pure `countReadQueries(report: unknown): number \| undefined` (`undefined` for no report / non-array `queries`, else a count incl. `0`; entries validated per item, no `any`); `confirmLiveDatabaseCommand` uses it; 2 new tests (7 malformed shapes + case-insensitive count) |
| L2 `scripts/check-workflow-policy.py` | new `--self-test` (14 inline YAML fixtures, one rule each) run against the unchanged checks: **10 of 14 FAIL** — assert step with `continue-on-error: true`, `if: false`, `if: ${{ false }}`, assert script only in a `#` comment, guard `${{ true \|\| (…) }}`, same-repo clause `\|\| true`, unguarded upload on `pull_request_target`, `pull_request` guard on a `pull_request_target` workflow, both PR triggers, release test step with `continue-on-error: true` | (a)/(c) only count an *effective* step (`continue-on-error` absent or literally `false`; `if` absent or not literally `false`; `${{ }}` stripped, fail closed on expressions) and `run` text drops full-line `#` comments; (b) parses the `if:` (depth-0 `&&` split, outer parens stripped, depth-0 `\|\|` split, quote-aware) and requires one AND-ed operand whose OR-set is exactly `{github.event_name != '<pr-event>', <same-repo clause>}`; `pull_request_target` is PR-reachable and needs its own `!=` clause; both PR triggers → fail closed. `--self-test`: 14/14 PASS; real workflows: `check-workflow-policy: OK` |
| L8 `ci.yml` policy step | unpinned `pip install --user pyyaml` fallback | kept, pinned: `python3 -m pip install --user --only-binary=:all: pyyaml==6.0.2`. Why keep: the ubuntu-24.04 runner-images README lists `libyaml-dev` and `yamllint` but no `python3-yaml`/PyYAML (how yamllint is installed is not stated), so the fallback cannot be proven dead; why it works: the image's `/etc/pip.conf` sets `break-system-packages = true` (`images/ubuntu/scripts/build/install-python.sh`), so PEP 668 does not block a user-scope install. Addition not asked for: the step now runs `--self-test` before the real check so the fixtures cannot rot |
| L1 `docs/USAGE.md` VS bullet 1 | pair *(thư mục solution, SHA-256)* contradicted bullet 3 | triple *(thư mục solution, đường dẫn `.sln`, SHA-256 của `.dataguard.yml`)* |

### Doc wording (H1/M5, M1, M2)

- **H1/M5** — SECURITY.md, SECURITY.vi.md, `docs/USAGE.md` (CLI `--allow-env-connection` bullet + VS Code section), VS Code README, VS Code CHANGELOG, root CHANGELOG (CLI + VS Code entries): the kept credential is used **only to read the ground-truth catalog** (schema and stored-procedure definitions); `validate` never sends or describes repository SQL against it; the live SQL-shape check (`sp_describe_first_result_set` over repository SQL) stays disabled under `--ide-safe` and runs only through `verify-shape` (VS Code: **Verify SQL Shapes Against Database**, after the modal). Confirmed against the concurrent CLI diff: `ProviderRuleCatalog.GetRulesForProvider(…, bool ideSafe)` registers the connection-less rule and `IdeSafePolicy.LiveShapeRuleDisabledNote = "live SQL shape rule disabled (use verify-shape)"` — quoted in the USAGE CLI bullet.
- **M1** — `docs/USAGE.md` regex bullet and root CHANGELOG CLI entry: a regex that exceeds the 1 s match timeout is "reported as a rule failure (the rule is not silently skipped)"; no exit code promised. The Core engine has no diff yet and phase-01 has no "Review fixes (CLI/Core)" section, so the neutral wording is used.
- **M2** — root CHANGELOG VS entry and a new `docs/USAGE.md` VS bullet: a run at the timeout boundary is published when its SARIF exists; the exit code is `taskkill`'s and is not interpreted; Output pane line quoted from the concurrent VS diff: `ExitCodeExplainer.TerminatedAtTimeoutExplanation = "[WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List."` — confirmed verbatim by phase-02 "Review fixes (VS)" → "Exact new explainer string (for docs)" (Output pane shows it with the `[DataGuard] ` channel prefix, after the existing "timed out … process tree was terminated" line).
- **L9 follow-through** — VS Code README, VS Code CHANGELOG, `docs/USAGE.md` VS Code bullet now say `ide-safe: active` → `[INFO]`, other `ide-safe:`/`baseline:` lines → `[WARN]`.

### Files
`src/DataGuard.VSCode/src/ide-safe-contract.ts`, `ide-safe-contract.test.ts`, `live-database-confirmation.ts`, `live-database-confirmation.test.ts`, `extension.ts`; `scripts/check-workflow-policy.py` (130 → 292 lines: parser + 14 fixtures; single-file by ownership, splitting the fixtures into a `-self-test` module is a follow-up if the 200-line rule must hold); `.github/workflows/ci.yml` (policy step only); `SECURITY.md`, `SECURITY.vi.md`, `docs/USAGE.md`, `CHANGELOG.md`, `src/DataGuard.VSCode/README.md`, `src/DataGuard.VSCode/CHANGELOG.md`.

### Gates
`npm test` (compile + `test:typescript` + node --test): 93 → **95 pass, 0 fail**; `npm run compile` clean. `python scripts/check-workflow-policy.py --self-test` OK (14/14); `python scripts/check-workflow-policy.py` OK; `ci.yml` parses (PyYAML 6.0.3 locally); `actionlint .github/workflows/ci.yml` exit 0, no findings; `bash ./scripts/verify_docs_sync.sh` green; every touched file LF. Vacuous-pass check on the real workflows: copies of `ci.yml` and `release.yml` with the `scripts/assert-vsix.ps1` invocation renamed, and copies with it commented out, each produce exactly one `(a)` failure (`ci.yml` step 4 "Package VSIX", `release.yml` step 7), so rule (a) still bites on the multi-line PowerShell steps after the comment-stripping change.

### Not confirmed against the other agents' reports
1. M1 sentence ("reported as a rule failure") — no Core diff and no "Review fixes (CLI/Core)" section at the time of writing; if the CLI agent settles on a specific DG code or exit code, the USAGE regex bullet and the CHANGELOG CLI entry need it appended.
2. Where the CLI prints `live SQL shape rule disabled (use verify-shape)` — read from the `IdeSafePolicy` constant's doc comment ("written alongside `KeptEnvironmentConnectionNote`"), assumed to be part of the `ide-safe: suppressed …` line; USAGE says "stderr liệt kê" without naming the exact line. Phase-01 has no "Review fixes (CLI/Core)" section yet.
3. (resolved) M2 explainer string confirmed against phase-02 "Review fixes (VS)".

**Status:** DONE_WITH_CONCERNS
**Summary:** L1/L2/L7/L8/L9 applied tests-first (policy self-test 10/14 RED → 14/14 GREEN, VS Code 93 → 95 tests green, compile clean); H1/M5, M1, M2 doc wording aligned across SECURITY en/vi, USAGE, both CHANGELOGs and the VS Code README; all gates green, nothing committed.
**Concerns/Blockers:** M1 wording is neutral and unconfirmed (no Core diff / no phase-01 review section yet); the exact stderr placement of `live SQL shape rule disabled (use verify-shape)` is inferred from a doc comment; `scripts/check-workflow-policy.py` is 292 lines (single-file by ownership).
