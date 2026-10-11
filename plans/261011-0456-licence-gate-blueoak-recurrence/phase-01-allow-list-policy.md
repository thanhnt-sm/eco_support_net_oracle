---
phase: 1
title: "Allow-list policy + truthful notices"
status: completed
priority: P1
effort: "1h"
dependencies: []
---

# Phase 1: Allow-list policy + truthful notices

## Overview
Add `BlueOak-1.0.0` (plus `Zlib` if validation Q2 approves it) to the `[spdx]` allow-list, and record the admission criteria in the allow-list header so the next licence review follows the same rule. Make `THIRD-PARTY-NOTICES.md` §3 truthful now that a Blue Oak component ships inside the VSIX.

## Requirements
- Functional: the real gate CLI passes on PR #49's lockfile and still fails on an unreviewed licence.
- Non-functional: `[exceptions]` stays untouched. Exceptions are reserved for pre-SPDX file/URL markers (`allowed-licences.txt:5-9`), so no glob is added for minimatch.

## Architecture
`allowed-licences.txt` → `parse_allow_list` (`check-nuget-licences.py:34`) → `spdx_allowed` (`:59`) → `evaluate` (`:152`). Only data changes. No code changes in this phase.

## Related Code Files
- Modify: `scripts/allowed-licences.txt` — `[spdx]` (after line 23 `MPL-2.0`), and header lines 1-11 (admission rule).
- Modify: `docs/legal/THIRD-PARTY-NOTICES.md:149` and its byte-identical copies `src/DataGuard.VisualStudio/THIRD-PARTY-NOTICES.md`, `src/DataGuard.VSCode/THIRD-PARTY-NOTICES.md` (identity enforced by `scripts/check-license-consistency.py:59-60`).
- Create (throwaway, outside the repo): `/tmp/pr49-package-lock.json`, `/tmp/allow-without-blueoak.txt`.

## Tests first (RED) — acceptance harness, not a permanent unit test
A permanent test asserting "the allow-list contains BlueOak" would pin config data (repo rule: no tests of copies or incidental data). The regression guard is the CI gate itself, which runs on every PR. RED/GREEN therefore use the real CLI:

```bash
git show origin/dependabot/npm_and_yarn/src/DataGuard.VSCode/npm-eac8022a8b:src/DataGuard.VSCode/package-lock.json > /tmp/pr49-package-lock.json
python3 scripts/check-nuget-licences.py --npm-lock /tmp/pr49-package-lock.json; echo "exit=$?"
```
Expected RED: `- npm minimatch 10.2.6: [expression] BlueOak-1.0.0`, `9 production npm packages`, `exit=1`. Precondition: verify `dotnet --version` outputs a 9.0.x SDK satisfying `global.json`. Exact restore sequence required on non-Windows hosts before running the CLI: `dotnet restore DataGuard.CrossPlatform.slnf --locked-mode` followed by `dotnet msbuild tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -restore -p:RestoreLockedMode=true`. Never run unlocked `dotnet restore DataGuard.sln`, which would mutate tracked `packages.lock.json` files.
## Implementation Steps
<!-- Updated: Validation Session 1 - Q2 (Admit Zlib) and Q3 (Exclude Artistic-2.0) confirmed -->
1. Insert `BlueOak-1.0.0` and `Zlib` on new lines directly following `MPL-2.0` (line 23). Tokens are case-sensitive.
2. Extend the header comment (after line 11) with the explicit admission rule and decision record:
   `# [spdx] admission rule: Permissive licences with explicit redistribution rights (attribution only); or explicit steward/FSF statement of GPL-3.0 compatibility. MS-PL and MPL-2.0 are grandfathered. Copyleft (GPL, AGPL, LGPL), non-standard/source-available (SSPL, BSL), and Artistic-2.0 are excluded by policy.`
   `# Decision recorded 2026-10-11: BlueOak-1.0.0 (minimatch >=10, sax; Blue Oak Council FAQ https://blueoakcouncil.org/license-faq states GPLv3 compatibility) and Zlib (pako; FSF GPL-compatible) admitted; Artistic-2.0 and "SEE LICENSE IN" stay excluded.`
3. Edit `docs/legal/THIRD-PARTY-NOTICES.md:149`: change `(MIT, Apache-2.0, the PostgreSQL licence, ISC)` to `(MIT, Apache-2.0, the PostgreSQL licence, ISC, the Blue Oak Model License 1.0.0)`. Note: `Zlib` is NOT added to NOTICES because no Zlib component ships in DataGuard (pako is dev-only tooling); NOTICES §3 lists components actually redistributed. Then copy the file byte-for-byte to both mirrors (`src/DataGuard.VisualStudio/THIRD-PARTY-NOTICES.md` and `src/DataGuard.VSCode/THIRD-PARTY-NOTICES.md`).
4. GREEN: rerun the RED command. Expect `licence gate: OK`, `exit=0`.
5. Negative control: write `/tmp/allow-without-blueoak.txt` = the allow-list with `BlueOak-1.0.0` removed, and run with `--allow-list /tmp/allow-without-blueoak.txt --npm-lock /tmp/pr49-package-lock.json`. Expect the same offender and `exit=1`. This proves the pass comes from the new line and not from some other leak.
6. `python3 scripts/check-license-consistency.py`. Expect `6 copies identical`, exit 0.

## Success Criteria
- [x] RED evidence captured (exit 1, exact offender line) before the edit.
- [x] GREEN: the real CLI with the PR #49 lockfile → `licence gate: OK`, exit 0.
- [x] Negative control → exit 1 with the minimatch offender.
- [x] Current `main` lockfile still passes: `python3 scripts/check-nuget-licences.py` → OK.
- [x] Consistency script exit 0, 6 copies identical.
- [ ] Deterministic gate J1 (exit 0, 0 offenders on real CLI) + advisor A1 passed (J1 satisfied by deterministic CLI evidence; A1 not run).

## Risk Assessment
- Typo in the SPDX id → the gate stays RED. Steps 4 and 5 catch it.
- The NOTICES mirrors drift → caught by the consistency script.
- Legal claim "GPL-3.0 compatible": BlueOak-1.0.0 is OSI-approved and permissive. The Blue Oak Council publishes explicit analysis affirming compatibility with GPLv3. The owner formally confirmed admission at validation Q5.
