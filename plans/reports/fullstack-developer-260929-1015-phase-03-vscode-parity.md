# Phase 3 — VS Code parity: credential carve-out and old-CLI detection (TDD)

Plan: `plans/260929-0952-vs-hardening-redteam-tdd-followup/phase-03-vs-code-parity-credential-carve-out-and-old-cli-detection.md`
Branch: `feat/vs-extension-hardening` (uncommitted). Scope respected: only `src/DataGuard.VSCode/` touched. `packages.lock.json` / `package-lock.json` untouched (pre-existing `M` on the nine `packages.lock.json` files predates this session, mtime 10:05).
Report path: parent-requested name (`…-1015-phase-03-vscode-parity.md`); the hook's injected naming would have been `…-1103-…`. Kept the parent's.
Status: **completed**.

## 1. Baseline + Tests Before (RED)

Baseline: `cd src/DataGuard.VSCode && npm test` → `tests 76, pass 76, fail 0` (plan's 76 confirmed).

Stubs so the RED run is a runtime red, not a compile red: `buildCliArguments(..., options: CliArgumentOptions = {})` accepted-but-ignored; `ide-safe-contract.ts` exporting `MIN_CLI_VERSION = ""`, `hasIdeSafeAck → false`, `isOldCliRejection → false`, `buildIdeSafeFailureMessage → ""`, `formatProgressLine → undefined`. `out/ide-safe-contract.test.js` added to the `test` script before the RED run.

RED run (same command) → `tests 87, pass 78, fail 9`; new file confirmed executing (its 9 failures listed with `out\ide-safe-contract.test.js:<line>`):
- `validate carries --allow-env-connection only when the host supplies a user credential` (command-args)
- `hasIdeSafeAck accepts only an exact ack as the first non-empty stderr line`
- `isOldCliRejection is anchored at line start and recognises both new flags`
- `MIN_CLI_VERSION names the first CLI that acknowledges ide-safe`
- `buildIdeSafeFailureMessage adds the update hint only for an old-CLI rejection`
- `formatProgressLine surfaces ide-safe: and baseline: lines as [WARN] channel lines`
- `formatProgressLine renders a BaselineApplied progress event as a [WARN] line`
- `formatProgressLine keeps the existing rendering for other progress events and prefixed lines`
- `no source or README line recommends a NuGet global-tool install of the CLI` (offenders: `src/extension.ts:865`, `README.md:20`)

Already-green pins written in the same pass (regression guards): `assess never carries --allow-env-connection even with a user credential`, `snapshot, baseline and verify-shape never carry --ide-safe or --allow-env-connection`.

## 2. Files

Modified
- `src/DataGuard.VSCode/src/command-args.ts` — `CliArgumentOptions { hasUserCredential? }`, `ALLOW_ENV_CONNECTION_FLAG`, appended after `--ide-safe` on `validate` only.
- `src/DataGuard.VSCode/src/command-args.test.ts` — 3 tests above.
- `src/DataGuard.VSCode/src/extension.ts` (+~50/-~35) — imports; `ChildExit.stderrHead` (first 4 non-empty stderr lines) + `startFailed`; `runConfirmedOperation` replaced by `confirmLiveDatabaseCommand` inside `runCliCommand` (after SecretStorage read, before `nextReservation()`); snapshot/baseline register as plain `runCliCommand`; `hasUserCredential` passed to `buildCliArguments`; ack gate after the timed-out/cancelled checks and before `loadDiagnostics`; `processProgressText` delegates to `formatProgressLine` and fills the stderr head; CLI-not-found message replaced.
- `src/DataGuard.VSCode/README.md` — install from GitHub Releases + SHA-256, CLI ≥ 0.3.0 / ≤ 0.2.2 rejected, `--allow-env-connection` (SecretStorage only), ack requirement, `[WARN]` echo, live-database commands + confirmation; line 13/14 ("does not connect to a database itself" / "open a database") reworded since they became false with a stored credential; commands table gained the assess and live-database rows.
- `src/DataGuard.VSCode/package.json` — `test` script only: `out/ide-safe-contract.test.js`, `out/live-database-confirmation.test.js`.

Created
- `src/DataGuard.VSCode/src/ide-safe-contract.ts` (111 lines) — `MIN_CLI_VERSION`, `IDE_SAFE_ACK_LINE`, `hasIdeSafeAck`, `isOldCliRejection`, `buildIdeSafeFailureMessage`, `formatProgressLine` (pure; imports only `./security`).
- `src/DataGuard.VSCode/src/ide-safe-contract.test.ts` (103 lines).
- `src/DataGuard.VSCode/src/live-database-confirmation.ts` (92 lines) — `isLiveDatabaseCommand`, `maskConnectionHost`, `buildLiveDatabaseConfirmation`. **Deviation from the create list**: a second module rather than growing `ide-safe-contract.ts` past ~200 lines / mixing the confirmation concern into the handshake module.
- `src/DataGuard.VSCode/src/live-database-confirmation.test.ts` (54 lines) — Tests After.

## 3. Design decisions

- **Ack detection** reads a separate stderr head (stdout+stderr are merged in `output`, so "first stderr line" is unrecoverable from it). Gate applies to `validate`/`assess` regardless of exit code; on failure: `[DataGuard] <message> (exit code n)` in the channel, `setStatus("error")`, error notification, early return — SARIF never read, no findings notification (an old CLI exits 1, which would otherwise show "found findings" for zero findings). No retry path exists.
- **Old-CLI regex** `^Unrecognized command or argument '--(ide-safe|allow-env-connection)'` — anchored (stricter than the VS extension's mid-line `IndexOf`, intended per phase); includes the carve-out flag because the PR #24 intermediate CLI accepts `--ide-safe` but not `--allow-env-connection`, and the remedy is identical. Hint text built from `MIN_CLI_VERSION` so that constant is load-bearing.
- **`startFailed`**: `waitForExit`'s catch already reports the spawn error; without the flag the ack gate would stack a second "did not confirm" error on ENOENT. Node emits `error` (from the spawn `onexit` path) before `close`, so the catch branch is the one reached and the GitHub-Releases message is what the user sees.
- **Confirmation count guard**: `latestScanReport.queries` is a bare cast of CLI `summary.json`; the read-query filter checks `typeof operation === "string"` first so a malformed element cannot throw inside the modal gate.
- **`hasUserCredential`** = SecretStorage returned a non-blank string (blank normalised to `undefined` so no empty env var is exported). Repo config can never set it.
- **`formatProgressLine`** returns channel text without the `[DataGuard] ` prefix (caller adds it, as before). `BaselineApplied` → `[WARN] baseline: n suppressed by <Detail>`; a non-integer/negative `SuppressedCount` (external data) is never echoed (`… violations suppressed by <path>`). Plain `ide-safe:`/`baseline:` lines → `[WARN] <line>`; existing Detail/Phase/`[INFO|WARN|ERROR]` rendering unchanged; all redacted. `ProgressEventPayload` typing moved with it (`unknown` fields, narrowed).
- **Confirmation moved into `runCliCommand`** for snapshot/baseline/verify-shape: it now runs after the trust check and secret read (needed for the host) and before `nextReservation()` (declining must not cancel a running job). One code path, `runConfirmedOperation` deleted.
- **Literal count**: not cheaply available before the run (the CLI extracts SQL during `verify-shape`). Used the in-memory `latestScanReport` (read-operation queries from the last `scan` in this session), worded as "N read SQL queries from the last scan"; when no scan ran → "every read SQL query discovered in this workspace", host only. Never fabricated.
- **Host masking**: ADO.NET keys (`Server`, `Data Source`, `Host`, `Address`, …) incl. `tcp:host,port`, `host\instance`, EZConnect, `(HOST=…)`, and URI form; masked as `db-***.com`, `10.***.40`, `loc***` (labels ≤ 3 chars keep 1 char: `pg` → `p***`). No SecretStorage credential → the text names `.dataguard.yml`/`DATAGUARD_CONNECTION_STRING` explicitly instead of a host.

## 4. Tests After + regression gate

Tests After (`live-database-confirmation.test.ts`, 6 tests): command gating; masking across 7 shapes; credential never leaks + `undefined` cases; verify-shape text with count; no fabricated count/host (negative count ignored); snapshot/baseline wording. One expectation corrected after GREEN (`pg` → `p***.org` per the ≤ 3-char rule; code unchanged).

Gate:
- `npm test` → `tests 93, pass 93, fail 0` (76 → 93; ≥ 82 met).
- `npm run compile` → clean (`tsc -p ./`, strict).
- `npm run package` → `dataguard-vscode-0.2.3.vsix` (345 files, 609.06 KB reported by vsce; 623,673 bytes on disk). `server/` and `*.vsix` are gitignored. Version left at 0.2.3 (bump is Phase 4/5).
- `git status --short src/DataGuard.VSCode` → exactly the 5 modified + 4 new files above.

## 5. Deviations / skips

- Second module + test file (`live-database-confirmation.*`) beyond the create list; `package.json` test list gained two entries instead of one.
- Old-CLI regex is a one-token superset of the spec (`--allow-env-connection`), see §3.
- README edits went slightly beyond the listed bullets (two now-false sentences reworded; commands table rows) to keep the doc consistent with the shipped behaviour.
- Existing pre-Phase behaviour left as is: after any completed run the `finally` block resets the status bar to idle (also after the new error path); notifications carry the outcome. Not changed to keep the diff minimal.
- No extension-host test was run (`npm run test:extension-host` not in the gate); ack/confirmation wiring in `extension.ts` is covered by the pure-module tests, not by an integration test.

## 6. Unresolved questions

1. Should the VS extension's `IsIdeSafeUnsupportedMessage` (mid-line `IndexOf`) be aligned with the anchored VS Code check, or is the divergence acceptable (Phase 2 owner)?
2. Status-bar reset to idle after an ack failure (pre-existing `finally` logic) — acceptable, or should error/warning states persist until the next run?
3. `MIN_CLI_VERSION = "0.3.0"` / README "0.3.0 or later" are per spec, but no `<Version>` exists in the CLI csproj or `Directory.Build.props` (tag-driven versioning): Phase 4/5 must actually cut the CLI as 0.3.0 or the hint names a version that does not exist.
4. Marketplace changelog (Phase 4) must state: CLI ≤ 0.2.2 (and the PR #24 intermediate build when a SecretStorage credential exists) is rejected for validate/assess with an update hint.
