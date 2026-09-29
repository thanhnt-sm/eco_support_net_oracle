---
phase: 3
title: "VS Code parity: credential carve-out and old-CLI detection"
status: pending
priority: P1
effort: "3h"
dependencies: [1]
---

# Phase 3: VS Code parity: credential carve-out and old-CLI detection

## Overview
Restore the documented SecretStorage credential flow under `--ide-safe`, detect an old or non-acknowledging CLI, surface `ide-safe:`/`baseline:` lines, gate `verify-shape` behind a confirmation, and remove the NuGet install guidance. Closes findings 1, 2, 4 (VS Code half), 5 (VS Code half), 9 (VS Code half).

## Requirements
- Functional:
  - `buildCliArguments("validate", …)` appends `--allow-env-connection` **only** when the caller passes `hasUserCredential: true` (set when `SecretStorage` returned a connection string, `extension.ts:509-511`). Repo config never sets this.
  - `processProgressText` echoes lines starting with `ide-safe:` and `baseline:` to the channel as `[WARN]` (never dropped); `BaselineApplied` progress events render as `[WARN] baseline: n suppressed by <path>`.
  - Run result handling: if stderr never contained the `ide-safe: active` line (for `validate`/`assess`), discard the SARIF, show an error notification "DataGuard CLI did not confirm IDE-safe mode; results were discarded" and, when the first stderr line starts with `Unrecognized command or argument '--ide-safe'`, add "Update the dataguard CLI (0.3.0 or later) or set dataguard.cliPath". Never retry without the flag.
  - `verify-shape` command shows the same modal confirmation as `snapshot`/`baseline` (`extension.ts:317-325` pattern), naming the target database host (masked) and the number of SQL literals that will be described.
  - Remove `dotnet tool install -g DataGuard.Cli` from `extension.ts:864-865` and `README.md`; replacement: "Install the dataguard CLI from GitHub Releases (verify SHA-256) and set dataguard.cliPath in User Settings".
  - `README.md` security bullets: ide-safe applies to `validate`/`assess`; `snapshot`, `baseline`, `verify-shape` are live-database commands that use your credential and ask for confirmation.
- Non-functional: `npm test` (76 → ≥ 82 tests) green; no new dependencies.

## Architecture
Detection logic lives in a new pure module `src/ide-safe-contract.ts` (`hasIdeSafeAck(stderrLines)`, `isOldCliRejection(firstLine)`, `MIN_CLI_VERSION`) so it is unit-testable without the VS Code API; `extension.ts` consumes it after the child exits.

## Related Code Files
- Modify: `src/DataGuard.VSCode/src/command-args.ts`, `src/DataGuard.VSCode/src/command-args.test.ts`, `src/DataGuard.VSCode/src/extension.ts` (`:317-325`, `:430-438`, `:471-480`, `:509-511`, `:561-563`, `:644-661`, `:864-865`), `src/DataGuard.VSCode/README.md`, `src/DataGuard.VSCode/package.json` (`test` script list)
- Create: `src/DataGuard.VSCode/src/ide-safe-contract.ts`, `src/DataGuard.VSCode/src/ide-safe-contract.test.ts`

## Implementation Steps (tests first)
1. **Tests Before** (fail on current code):
   - `command-args.test.ts`: validate argv contains `--allow-env-connection` iff `hasUserCredential`; assess never does; `snapshot`/`baseline`/`verify-shape` never carry `--ide-safe`.
   - `ide-safe-contract.test.ts`: ack detection (exact line, first non-empty), old-CLI rejection detection (anchored), spoofed path line with the quoted flag → not old CLI.
   - Progress text test: `ide-safe: suppressed …` and `baseline: 3 violations suppressed by .dataguard-baseline.json` produce channel lines.
   - String guard test: no source or README line matches `/dotnet tool (install|update)/`.
2. **Implement** the requirements; keep `extension.ts` changes minimal by delegating to the new module.
3. **Tests After**: modal-gate decision helper for `verify-shape` (pure function returning the confirmation text) tested.
4. **Regression Gate**: `cd src/DataGuard.VSCode && npm test`; `npm run package` produces the VSIX.

## Success Criteria
- [ ] With a SecretStorage credential, validate runs against the DB under ide-safe; without one, the credential path is inert
- [ ] Old CLI → clear error + update hint, zero fake "findings"
- [ ] `ide-safe:`/`baseline:` lines visible in the DataGuard channel
- [ ] verify-shape asks before touching the database
- [ ] No NuGet install guidance remains

## Risk Assessment
- Users on CLI ≤ 0.2.2 lose validate until they update — acceptable and now explained; the Marketplace changelog must say so (Phase 4).
- `--allow-env-connection` in a trusted-but-hostile workspace: the credential is the user's, the repo config still cannot load code; documented in README.
