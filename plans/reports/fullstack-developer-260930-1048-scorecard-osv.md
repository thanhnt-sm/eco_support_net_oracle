# Scorecard alert #45 — 17 OSV advisories closed (npm, VS Code extension)

- Date: 2026-09-30
- Branch: `fix/scorecard-osv-vulnerabilities` (from `origin/main` @ a866d56)
- Commit: c03bdc4 `fix(deps): close 17 OSV advisories flagged by Scorecard in VS Code extension npm lock`
- PR: https://github.com/thanhnt-sm/eco_support_net_oracle/pull/30 (OPEN, base `main`, not merged)
- Coordinator asked for filename `…-1048-scorecard-osv.md`; injected Naming pattern was `…-1055-{slug}` — used the coordinator's.

## Resolution (OSV API `api.osv.dev/v1/vulns/<id>`)

All 17 advisories are **npm**. None map to NuGet (`**/packages.lock.json`) or the `Dockerfile` base image. Every affected package is a **transitive** dependency in `src/DataGuard.VSCode/package-lock.json` (lockfileVersion 3). Dependabot PR #29 (NuGet) did not touch any of them; all 17 were still present on current `main`.

| Advisory | Package | Was | Fixed at (OSV) | Now | Parent chain (declared range) | Shipped in VSIX? |
|---|---|---|---|---|---|---|
| GHSA-2gqq-gqf2-x968 CVE-2026-84947 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | @vscode/vsce -> cheerio (`^7.19.0`) | no (dev) |
| GHSA-2jfj-6hjv-fm6j CVE-2026-84933 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | same | no |
| GHSA-3wwx-pv8p-q78v CVE-2026-85024 | undici | 7.29.0 | >=7.29.1 (6.x: >=6.28.1; no 6.x copy in repo) | 7.30.0 | same | no |
| GHSA-3xpg-4rpp-hhhm CVE-2026-84890 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | same | no |
| GHSA-8436-99hf-9mmv CVE-2026-85008 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | same | no |
| GHSA-pmjh-fq2x-6v4x CVE-2026-18149 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | same | no |
| GHSA-r53p-7pc4-xj5r CVE-2026-18540 | undici | 7.29.0 | >=7.29.1 (6.x: >=6.28.1) | 7.30.0 | same | no |
| GHSA-rfgv-xxqx-mfg5 CVE-2026-19534 | undici | 7.29.0 | >=7.29.1 (6.x: >=6.28.1) | 7.30.0 | same | no |
| GHSA-rx4f-c7p8-82vq CVE-2026-85014 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | same | no |
| GHSA-w293-vg96-wgc3 CVE-2026-84961 | undici | 7.29.0 | >=7.29.1 | 7.30.0 | same | no |
| GHSA-6j4f-fj2g-mc7p CVE-2026-102276 | brace-expansion | 5.0.9 / 2.1.4 | >=5.0.10 / >=2.1.5 | 5.0.12 / 2.1.7 | @vscode/vsce -> minimatch@10 (`^5.0.8`) / vscode-languageclient -> minimatch@5 (`^2.0.1`) | 2.x copy **yes** |
| GHSA-q2hr-2g5m-vwhr CVE-2026-102277 | brace-expansion | 5.0.9 / 2.1.4 | >=5.0.12 / >=2.1.7 | 5.0.12 / 2.1.7 | same | 2.x copy **yes** |
| GHSA-qhr7-859c-m2p7 CVE-2026-102278 | brace-expansion | 5.0.9 / 2.1.4 | >=5.0.11 / >=2.1.6 | 5.0.12 / 2.1.7 | same | 2.x copy **yes** |
| GHSA-58mr-gqgx-xq4g CVE-2026-84394 | fast-uri | 3.1.6 (`overrides` pin) | >=3.1.7 | 3.1.8 | @vscode/vsce -> @secretlint/config-loader -> ajv (`^3.0.1`) | no |
| GHSA-hrr3-gc8f-f4qj CVE-2026-86472 | fast-uri | 3.1.6 (`overrides` pin) | >=3.1.8 | 3.1.8 | same | no |
| GHSA-qw65-cvwx-89v3 CVE-2026-84292 | fast-uri | 3.1.6 (`overrides` pin) | >=3.1.7 | 3.1.8 | same | no |
| GHSA-253c-mchw-3w2r (no CVE) | markdown-it | 14.3.0 | >=14.3.1 (and 15.0.0 -> 15.0.1) | 14.3.2 | @vscode/vsce (`^14.1.0`) | no |

Discrepancy vs coordinator mapping: OSV lists markdown-it `[0, 14.3.1)` as affected in addition to `15.0.0`; the repo had 14.3.0, so it was in range. Fixed inside 14.x; no forced major bump to 15.

## Per-advisory action

- **fast-uri (3)**: `src/DataGuard.VSCode/package.json` `overrides.fast-uri` `3.1.6` -> `3.1.8`. The existing CVE pin had become vulnerable itself; this is the only manual edit.
- **undici (10), brace-expansion (3), markdown-it (1)**: `npm audit fix` (no `--force`) then `npm update undici brace-expansion markdown-it fast-uri`. All fixed versions sit inside the parents' declared semver ranges, so no `overrides`, no direct-dependency bump and no build-tool major bump (@vscode/vsce stays 3.9.2, typescript unchanged). Lock diff is exactly the five `version/resolved/integrity` triples (30 lines).
- **NuGet / Docker**: nothing to do for this alert. NuGet excluded by OSV ecosystem evidence (all 17 are `npm`) and by osv-scanner over all 27 `packages.lock.json` (0 findings). Docker excluded by OSV ecosystem evidence only — osv-scanner source mode does not read the `Dockerfile` base-image digests (0 Dockerfile entries in its scan log); the pinned `mcr.microsoft.com/dotnet/sdk:9.0` / `runtime:9.0` digests were not touched. No `dotnet` command was run by hand; the 4 pre-existing, unrelated modified NuGet lock files in the working tree were left untouched and not committed.
- **CHANGELOG**: `CHANGELOG.md` `[Unreleased] -> ### Security` first bullet; `src/DataGuard.VSCode/CHANGELOG.md` new `### Security` under `[Unreleased]`.

Nothing left unfixable.

## Gate outputs (verbatim tails)

`npm audit fix`
```
changed 5 packages, and audited 324 packages in 4s
found 0 vulnerabilities
```
Resulting lock versions (node script over `package-lock.json`):
```
lockfileVersion 3
brace-expansion 5.0.12 (dev)
fast-uri 3.1.8 (dev)
markdown-it 14.3.2 (dev)
undici 7.30.0 (dev)
vscode-languageclient/<nm>/brace-expansion 2.1.7 (prod)
```
`npm ci && npm test`
```
found 0 vulnerabilities
ℹ tests 95
ℹ suites 0
ℹ pass 95
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 1039.0797
```
`npm run compile` -> `tsc -p ./` exit 0.

`npm run package`
```
 DONE  Packaged: D:\100.Software\Github\eco_support_net_oracle\src\DataGuard.VSCode\dataguard-vscode-0.2.3.vsix (346 files, 611.83 KB)
```
VSIX content check (`unzip -p dataguard-vscode-0.2.3.vsix extension/<nm>/vscode-languageclient/<nm>/brace-expansion/package.json`):
```
  "version": "2.1.7",
```
undici / fast-uri / markdown-it entries inside the VSIX: 0 (dev-only, not shipped).

`osv-scanner 2.6.0 scan source --lockfile src/DataGuard.VSCode/package-lock.json`
```
Scanned D:\100.Software\Github\eco_support_net_oracle\src\DataGuard.VSCode\package-lock.json file and found 325 packages
No issues found
```
`osv-scanner 2.6.0 scan source -r .` (repo root; 55 lock/csproj files incl. every NuGet lock)
```
End status: 392 dirs visited, 1567 inodes visited, 55 Extract calls, 377.3929ms elapsed
Filtered 62 local/unscannable package/s from the scan.
No issues found
```
Pre-commit gate (`dotnet format whitespace`, workspace-guard, DocSync)
```
[workspace-guard] All staged paths are allowed.
[pre-commit] Passed.
```
Pre-push gate (`.githooks/pre-push` -> `scripts/verify_local_gates.sh`: restore `--locked-mode --force-evaluate`, build Release, analyzers, `dotnet format --verify-no-changes`, `dotnet test` + coverage threshold, NuGet audit): **passed** — hook exit 0 and the branch was pushed. Its verbose log was cut by the `| tail -5` on my push command; only the tail survived:
```
 * [new branch]      fix/scorecard-osv-vulnerabilities -> fix/scorecard-osv-vulnerabilities
branch 'fix/scorecard-osv-vulnerabilities' set up to track 'origin/fix/scorecard-osv-vulnerabilities'.
https://github.com/thanhnt-sm/eco_support_net_oracle/pull/30
```

PR #30 CI at report time (`gh pr checks 30`): **pass** — Package VS Code Extension, Security Scan, Visual Studio Build and Test, Generate SBOM, Golden standard checklist, Benchmark; **pending** — Build and Test, CodeQL Analysis, Package Visual Studio Extension, Visual Studio VSIX Packaging Gate. The two jobs that exercise this change (Package VS Code Extension, Security Scan) are already green.

Authoritative close: Scorecard (`.github/workflows/scorecard.yml`, `ossf/scorecard-action` v2.4.4) re-runs on the next push to `main`; alert #45 should auto-close after merge.

## Files changed (committed)

- `src/DataGuard.VSCode/package.json` (overrides.fast-uri 3.1.6 -> 3.1.8)
- `src/DataGuard.VSCode/package-lock.json`
- `CHANGELOG.md`
- `src/DataGuard.VSCode/CHANGELOG.md`

## Unresolved questions

- None for this alert. Observation only: the `overrides` block pins `fast-uri`, `js-yaml`, `qs` to exact versions; Dependabot npm does not raise `overrides`, so each pin needs the same manual bump whenever a new advisory lands. Consider `>=` ranges or dropping the pins once parents catch up.

**Status:** DONE
**Summary:** All 17 Scorecard/OSV advisories were transitive npm packages in the VS Code extension lock file; fixed via a raised `fast-uri` override plus in-range `npm audit fix`/`npm update` (undici 7.30.0, brace-expansion 5.0.12 + 2.1.7, fast-uri 3.1.8, markdown-it 14.3.2). 95/95 tests, compile, VSIX package, `npm audit` 0, osv-scanner 0 across the repo; PR #30 open against main, not merged.
**Concerns/Blockers:** None blocking. Pre-push gate log truncated by my `tail -5` (hook passed, exit 0). Coordinator's markdown-it mapping (15.0.0 only) was incomplete; OSV also flags <14.3.1, which is what the repo had.
