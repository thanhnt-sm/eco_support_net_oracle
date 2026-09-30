# FluentAssertions 7.2.2 (Apache-2.0) migration + NuGet/npm licence allow-list gate

Date: 2026-09-30 | Branch: `fix/fluentassertions-7-apache-licence` (from `origin/main` @ `92aee89`) | Worktree: `.claude/worktrees/agent-ad377c5607dd04a2f`
Input: `plans/reports/researcher-260930-1156-fluentassertions-alternatives-compile-check.md` (FA 7.2.2 = zero-diff drop-in; AwesomeAssertions 9.x renamed the root namespace, 82 edits).

## Phase Implementation Report

### Executed Phase
Single-phase task (no plan dir). Status: **completed**.

### Files Modified
- `tests/DataGuard.{Core,CodeFixes,Analyzers,GoldenCorpus,Observability,VisualStudio}.Tests/*.csproj` — `FluentAssertions` → `7.2.2` (five from 8.11.0, VisualStudio.Tests from 6.12.2).
- Same six `packages.lock.json` — regenerated with `dotnet restore DataGuard.sln --force-evaluate` (+187/−25: net9.0 projects gain the transitive `System.Configuration.ConfigurationManager 6.0.0` chain; net472 project only swaps the FA hash and `System.Threading.Tasks.Extensions 4.5.0 → 4.5.4`). `--force-evaluate` also rewrote every `src/*/packages.lock.json` with CRLF only (no content change after normalisation); normalised back to LF, so they are not part of the diff.
- `.github/dependabot.yml` — FluentAssertions `semver-major` ignore kept; comment reworded per decision (2026-09-30) and points at the gate.
- `.github/workflows/ci.yml` — new step **NuGet/npm licence allow-list** in `build-and-test`, placed after "Linux evaluation parity (Dependabot)" (first point where the VSIX projects have assets on Linux; the script's implicit restore is then a no-op). `--no-restore` deliberately not passed (not verifiable on the 9.0.x runner from here).
- `CHANGELOG.md` — `[Unreleased]` → Changed (FA 7.2.2 + rationale) and Added (licence gate).
- `SECURITY.md`, `SECURITY.vi.md` — one supply-chain bullet each (the vi file mirrors the en posture section; kept in sync).

### Files Created
- `scripts/check-nuget-licences.py` (199 lines, stdlib only) — `dotnet list DataGuard.sln package --include-transitive --format json` → nuspec in the NuGet cache (`NUGET_PACKAGES` → `dotnet nuget locals global-packages --list` → `~/.nuget/packages`): `type="expression"` SPDX (compound expressions tokenised, every id must be allowed), `type="file"` → first non-empty line, legacy `licenseUrl` only when no `<license>` element; `aka.ms/deprecateLicenseUrl` never counts. npm: production entries of `src/DataGuard.VSCode/package-lock.json` (lockfile v3, `dev: true` skipped) and their `license` field. Fails closed (exit 1, offenders listed) on unknown licence, missing nuspec/licence file, missing `license` field, `dotnet list` non-zero exit, `problems` in the JSON, or an empty graph.
- `scripts/allowed-licences.txt` — `[spdx]` MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, MS-PL, 0BSD, ISC, Unlicense, PostgreSQL, MPL-2.0; `[exceptions]` as `<id glob> | <marker> | <justification>` — an entry matches only when the id matches the glob AND the marker is a substring of the observed licence text, so a glob is pinned by its licence text (vendor swaps the file → RED again). Populated from what the run found: `Microsoft.VisualStudio.*`, `Microsoft.VSSDK.BuildTools`, `Microsoft.VsSDK.CompatibilityAnalyzer`, `Microsoft.ServiceHub.Resources`, `VSLangProj*`, `envdte*`, `stdole`, `Microsoft.Data.SqlClient.SNI.runtime` (all "MICROSOFT SOFTWARE LICENSE TERMS" licence files); URL-only legacy nuspecs `Microsoft.VisualStudio.RemoteControl`/`Telemetry` (mt736442), `Utilities.Internal` (mt736439), `Setup.Configuration.Interop` (linkid=831109), `TaskRunnerExplorer.14.0`/`Web.BrowserLink.12.0` (.NET Library EULA), `Microsoft.NETCore.Platforms`/`Targets`, `System.Private.Uri` (LinkId=329770), `Microsoft.NETFramework.ReferenceAssemblies*`, `NETStandard.Library`, `xunit.abstractions`; `Oracle.ManagedDataAccess.Core` (Oracle Free Distribution, Hosting, and Use Terms).
- `scripts/tests/test_check_nuget_licences.py` — 10 stdlib unit tests (allow-list parsing/validation, compound SPDX, nuspec expression/file/url/placeholder/missing, npm dev-skip + scoped names + missing licence, evaluate offenders, glob pinned by marker). Picked up by the existing CI `python3 -m unittest discover -s scripts/tests` step.
- This report.

### Deviation from the task text (deliberate)
npm side reads `package-lock.json` `license` fields instead of `npm ls --omit=dev --json --all` + `node_modules/*/package.json`: `build-and-test` has no Node setup and no `npm ci`, so `npm ls` would fail in the job the gate is wired into; lockfile v3 carries the same per-package `license` field and needs no install. Same 8 production packages (semver ISC; vscode-jsonrpc, vscode-languageclient, vscode-languageserver-protocol/-types, balanced-match, brace-expansion MIT; minimatch ISC).

### RED / GREEN
RED — script run on the untouched tree (five projects on 8.11.0), verbatim:
```
licence gate: FAIL: 1 package(s) outside the allow-list:
  - nuget FluentAssertions 8.11.0: [file] XCEED SOFTWARE INC.
licence gate: checked 276 NuGet packages (DataGuard.sln) and 8 production npm packages (DataGuard.VSCode) against allowed-licences.txt
exit=1
```
GREEN — after the csproj bump + lock regeneration:
```
licence gate: checked 281 NuGet packages (DataGuard.sln) and 8 production npm packages (DataGuard.VSCode) against allowed-licences.txt
licence gate: OK
exit=0
```
(281 vs 276: the five new `System.Configuration.ConfigurationManager` chain packages, all MIT.)

### Tests Status
| Gate | Result |
|---|---|
| `dotnet restore DataGuard.sln --force-evaluate` then `--locked-mode` | OK, no NU warnings |
| `dotnet build DataGuard.sln -c Release -m:1` | 0 warnings, 0 errors (1m09s) |
| `dotnet test DataGuard.sln -c Release --no-build -m:1` | Core 898/898, GoldenCorpus 28/28, Analyzers 13/13, CodeFixes 24/24, Observability 38/38, VisualStudio 155 pass + 1 skip — identical to baseline |
| `dotnet restore DataGuard.sln --locked-mode -p:NuGetAuditMode=all` | no NU19xx |
| `python scripts/check-nuget-licences.py` | OK (GREEN above) |
| `python -m unittest discover -s scripts/tests -v` | 26 tests OK (16 policy + 10 licence gate) |
| YAML parse (`ci.yml`, `dependabot.yml`), `actionlint ci.yml` (1.7.12) | OK |
| `python scripts/check-workflow-policy.py` | OK |
| `dotnet format whitespace DataGuard.sln --verify-no-changes` | exit 0 |
| LF endings (`git ls-files --eol -m`, `file` on new files) | all LF |

Local SDK is 10.0.401 (CI: 9.0.x, no `global.json`); the lock files produced here differ from a 9.0.x restore only by CRLF (normalised), no SDK-specific entries were introduced (src lock files came back byte-identical after normalisation).

### Issues Encountered
- `--force-evaluate` rewrites every lock file with CRLF on Windows; `.gitattributes` would normalise on commit but the working tree was normalised explicitly so `git status` shows only the six real lock changes.
- Worktree guard rejects `for` loops / `$VAR` chains in Bash; commands were split. Bash hook blocks the literal `node_modules` in a command string, so the script/test were written via the Write tool.

### Next Steps
- Reviewer: confirm the `[exceptions]` justifications (VS SDK, Oracle FDHUT, legacy Microsoft URLs) reflect the intended licence policy; tightening is a one-line delete per entry.
- Xceed's maintenance policy for the FA 7.x line is still unverified (researcher's open item); if 7.x stops receiving fixes, AwesomeAssertions 9.x is the documented fallback (82 mechanical edits).

## Unresolved questions
- Should the exceptions file also pin package *versions* (currently id glob + licence-text marker)? Not done: a version pin would make every Dependabot bump of a VS SDK package touch the allow-list.
