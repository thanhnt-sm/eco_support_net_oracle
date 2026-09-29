# Simplify — applied fixes, VS Code extension (rows 10 and 15)

Source: `plans/reports/simplify-260929-1541-simplification.md` rows 10 and 15. Branch `feat/vs-extension-hardening` at 8a2df13, uncommitted. Ownership respected: only `src/DataGuard.VSCode/src/*.ts` touched (no `node_modules`, `out`, `package-lock.json`).

## Applied

| Row | Status | What changed |
|---|---|---|
| 10 | applied | `ChildExit.stderrHead: readonly string[]` + `STDERR_HEAD_LINES = 4` → `firstStderrLine?: string`; `ProgressTextState.head?: string[]` → `firstLine?: string`, captured with `state.firstLine ??= line` after the existing blank-line skip in `processProgressText` (so it is still the first NON-EMPTY trimmed line). `hasIdeSafeAck(firstLine: string \| undefined)` and `buildIdeSafeFailureMessage(firstLine: string \| undefined)` now mirror `isOldCliRejection`'s signature; `hasIdeSafeAck` keeps `.trim()` and the exact-equality rule against `ide-safe: active`. `startFailed` kept. Tests converted in place inside the two existing `test()` blocks (no test added or removed); the array-only assertion "only the first line is consulted" is gone because the helper no longer sees a second line — the ordering rule now lives in `processProgressText`. |
| 15 | applied (3 of 4 sites) | New `reportRunError(channel, message, channelLine?)` in `extension.ts`: optional channel line → `setStatus("error")` → `void showErrorMessage(message)`, same order as before. Used at the timeout site, the ide-safe handshake-failure site, and the generic non-0/1 exit site. Strings unchanged: timeout computes the sentence once and passes `DataGuard ${...}` / `\n[DataGuard] ${...}`; handshake passes its `(exit code ...)` channel line; the generic site passes no channel line because `exited with code` is written before the branch and shared with the idle/warning branches. |

## Skipped

- Row 15, 4th site `showStartError` (:880): it has no `setStatus("error")` of its own. Its two callers differ: the `waitForExit` catch (:731) has the caller set `error` afterwards (:488-489, left as is), but the synchronous `spawn` catch (:461) runs before `setStatus("running")` and today leaves the status bar untouched. Folding `setStatus("error")` into it would change that path, so it stays a two-line function. Behaviour identical was the constraint.

## Line delta (git diff --numstat)

| file | added | removed |
|---|---|---|
| `src/DataGuard.VSCode/src/extension.ts` | +26 | -22 |
| `src/DataGuard.VSCode/src/ide-safe-contract.test.ts` | +12 | -11 |
| `src/DataGuard.VSCode/src/ide-safe-contract.ts` | +5 | -6 |

Net: 1 array + 1 constant + 1 dead `?? []` pair removed; 3 triples (12 lines) → 3 one-liners + 1 helper (12 lines incl. its 4-line doc comment). `extension.ts` is net +4 lines: the win is state (array + cap constant gone) and one place to change the error triple, not raw line count. Contract helper and tests net -1 each.

## Gate output

- `npm run compile` (tsc -p ./): exit 0, no diagnostics.
- `npm test`: compile + `test:typescript` + node --test → `tests 95 / pass 95 / fail 0 / cancelled 0 / skipped 0`.
- `grep -c $'\r'` on the three edited files: 0, 0, 0 (LF only).
- `git status --short src/DataGuard.VSCode`: only `src/extension.ts`, `src/ide-safe-contract.ts`, `src/ide-safe-contract.test.ts` modified.
- Repo-wide grep for `stderrHead|STDERR_HEAD` and "first four/4 stderr lines" prose (excluding node_modules/out/bin/obj): no hits in `docs/`, the extension README, or any source; only two historical `plans/reports/*` entries (`fullstack-developer-260929-1015-phase-03-vscode-parity.md:32`, `simplify-260929-1541-efficiency.md:26`) mention the old 4-line array. Historical reports are not living docs, so no doc update is owed.

## Unresolved questions

1. Should the sync-spawn-throw path (:461) set the status bar to `error`? If yes, that is a behaviour change to make deliberately, after which `showStartError` can also delegate to `reportRunError(channel, message, "\n" + message)` and the `setStatus("error")` at :488 can go.
