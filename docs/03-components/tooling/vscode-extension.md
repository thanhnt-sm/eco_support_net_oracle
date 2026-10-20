# VS Code Extension

DataGuard combines CLI batch commands, a packaged language server and editor UI. These are separate execution paths: a successful CLI run does not prove that the language server started or that a database contract was fully evaluated.

## Requirements and activation

The `0.4.0` manifest requires VS Code `^1.91.0`, declares a workspace extension, and does not support untrusted or virtual workspaces. Activation events are `onLanguage:csharp` and `onStartupFinished`; activation does not depend on finding `.dataguard.yml` or a DataGuard package reference.

CLI commands use `dataguard.cliPath` (default `dataguard`). The language server instead runs the packaged `server/DataGuard.LanguageServer.dll` through `dotnet`; it needs a compatible .NET runtime. Before starting it, the extension checks the artifact against its SHA-256 manifest. This checks bundle consistency, not an independent publisher signature. See the [extension build guide](../../05-operations/extension-build-guide.md) for packaging prerequisites.

## Commands and results

Open a trusted workspace, save source files, then use the DataGuard commands in the Command Palette. In a multi-root workspace, select the folder for the run.

| Command | Actual CLI behavior |
|---|---|
| Run Validation (`dataguard.runValidation`) | `validate` with SARIF output, progress and `--ide-safe`; configured provider and an existing config file are passed. |
| Assess Workspace (`dataguard.assess`) | `assess --ide-safe` with SARIF output; no remote advisory opt-in. |
| Refresh Snapshot (`dataguard.refreshSnapshot`) | `snapshot refresh`, with provider and an existing config file; live command. |
| Create Baseline (`dataguard.createBaseline`) | `baseline`, with provider and an existing config file; live command. |
| Scan Project / Refresh Queries | `scan` with JSON output and progress; extracts saved source for the SQL Queries view. |
| Verify SQL Shapes (`dataguard.verifyShape`) | `verify-shape` with provider and JSON output; live command, without a config argument. |
| Cancel Validation (`dataguard.cancelValidation`) | Cancels the active CLI command, including commands other than validation. |
| Configure Connection | Stores or removes a connection string in workspace-scoped VS Code SecretStorage. |
| Open Dashboard / Refresh Findings / Clear Findings | Displays, reruns validation for, or clears the findings UI. |

Snapshot, baseline and verify-shape ask for live-database confirmation. They do not pass `--ide-safe`; scan also does not pass that flag. The confirmation displays the masked target derived from the stored connection string, which is not an independent verification of a target later resolved by CLI configuration or environment fallback.

Batch commands read saved files. A newer command replaces the active run after workspace selection, credential lookup and any live confirmation finish. Request and run identifiers discard stale completions. When the new run reaches startup, it clears Problems, findings, quick-fix state, query results, CodeLens, hover, decorations and dashboard data together. These views should not be interpreted as a persistent history of previous successful runs.

Read **Output > DataGuard**, Problems and the status tooltip together. The status bar uses idle/running/warning/error icons; clicking it runs validation. A syntactic-only result means no database ground truth was compared. Zero findings does not prove schema compatibility, and unsupported or unevaluated contracts must remain visible in the summary. `--ide-safe` implies allowance for unevaluated contracts at the CLI exit-code layer; do not infer completeness from exit code zero.

## Configuration and credential boundaries

| Setting | Default and scope |
|---|---|
| `dataguard.enabled` | `true`; command execution checks it. |
| `dataguard.configPath` | `.dataguard.yml`, relative to the selected workspace. If absent, validate/snapshot/baseline omit `--config`. |
| `dataguard.provider` | `sqlserver`; accepted values also include `postgresql`, `mysql`, `oracle`. |
| `dataguard.cliPath` | `dataguard`, machine scope. |
| `dataguard.timeoutSeconds` | 60 seconds; clamped to 5–900. |
| `dataguard.assessmentTimeoutSeconds` | 60 seconds; clamped to 5–900. |

The extension spawns the CLI with an argument array, `shell: false`, and the selected workspace as its working directory. It passes a stored credential through the child environment, not command-line arguments. Only validation adds `--allow-env-connection` when that workspace has a stored credential. Without this opt-in, IDE-safe child-environment construction removes `DATAGUARD_CONNECTION_STRING` and the configured connection-variable name. Output redaction covers recognized patterns, not arbitrary secret formats.

IDE-safe is a CLI policy, not an OS sandbox. It suppresses configured assembly/plugin loading and selected credentials, secret-manager and output settings; current policy disables both Oracle REF CURSOR describe switches even with a retained environment credential. Validate/assess results require the CLI acknowledgement `ide-safe: active`. Missing acknowledgement causes their results to be discarded; select a compatible CLI rather than interpreting discarded results as clean.

## Editor UI and safety limits

SARIF becomes Problems diagnostics; query summaries feed the SQL Queries tree, CodeLens and hover. CodeLens limits display to 50 entries and removes stale lenses for dirty documents. Hover content disables trusted commands and HTML. The dashboard and findings tree are presentation surfaces, not additional SQL validation engines.

Quick-fix actions currently provide manual remediation guidance. `applyRemediation` returns false and does not apply a source edit. Review and edit source yourself, save, then rerun. See [code fixes](code-fixes.md) for the distinction from Roslyn code fixes.

Config and SARIF paths receive lexical workspace-containment checks. Those helpers do not resolve symlinks; do not treat them as a filesystem sandbox. Some later tree/dashboard navigation paths additionally check real paths, but that is not a universal property of every diagnostic path.

The loader checks SARIF size against 50 MiB and summary size against 20 MiB before reading and parsing whole files. These are preflight checks, not bounded streaming reads or a guarantee against concurrent file growth. CLI text and progress buffers have separate limits; these do not bound all report-processing memory.

## Cancellation and shutdown

CLI cancellation uses `taskkill /T /F` on Windows, with a direct-child fallback. POSIX launches use a detached process group, request SIGTERM, and schedule SIGKILL after three seconds. The fallback and state checks do not establish that every descendant exited: in particular, a successful direct-child signal sets `child.killed`, which the later escalation guard checks even if exit has not been observed.

Deactivation invalidates pending requests, cancels the active CLI session, attempts output-directory cleanup and stops the owned language client with bounded waits. Language-server shutdown also attempts forced cleanup of an exposed owned child when graceful stop does not complete. These are lifecycle safeguards, not proof of orphan-free shutdown across every platform or language-client failure. Inspect lifecycle messages when cancellation or reload behaves unexpectedly.

## Source and verification

The behavior above is grounded in `src/DataGuard.VSCode/package.json`, `src/extension.ts`, `src/command-args.ts`, `src/ide-safe-contract.ts`, `src/security.ts`, `src/ui/quick-fix-provider.ts` and the language-client lifecycle helpers beneath `src/DataGuard.VSCode/`.

From `src/DataGuard.VSCode`, use `npm ci`, `npm run compile`, `npm test` and `npm run test:coverage` for local compilation and automated tests. Mocked VS Code API/process tests and synthetic fixtures do not establish real editor-host, Windows process-tree, VSIX installation or live-database behavior. Run `npm run test:extension-host` separately with `VSCODE_EXECUTABLE_PATH` pointing to a local VS Code executable; the script fails if none is available.

This documentation reconciliation inspected source only and did not run editor-host or platform tests. Existing execution records are in the [IDE behavior plan](../../../plans/261015-ide-behavior/plan.md) and [extension lifecycle plan](../../../plans/261016-extension-lifecycle/plan.md); they are historical evidence, not a new test result or a publication authorization.

## Language-server protocol regression scope

The full-text language server treats an empty `didChange.contentChanges` array as no content replacement and preserves queued diagnostics. It does not publish document versions or negotiate non-UTF-16 position encodings. Diagnostic line indexing accepts CR, LF and CRLF, with UTF-16 character offsets (including surrogate pairs); this does not expand the C# literal scanner's supported newline grammar.

`LanguageServerLifecycleTests` exercises these behaviors through a real local subprocess using synthetic SQL, including mixed line endings, tabs and an emoji. It does not establish editor-host compatibility, race-free diagnostic publication or complete LSP conformance. See the [protocol regression plan](../../../plans/2026-10-20-lsp-protocol-regressions.md) for evidence and deferred findings.
## Classifier handling of escaped identifiers and malformed prefixes

The shared syntax classifier keeps doubled closing delimiters inside bracket, double-quoted and backtick identifiers, decodes them for displayed names. Existing trimming of quoted names remains because C# escape masking introduces padding; preserving significant leading/trailing identifier whitespace needs a separate source-mapping change. Quoted keyword text therefore remains part of the identifier. SQL strings and comments remain excluded from statement detection. This lexer does not resolve dialect/session settings or establish that a statement is valid SQL.

The C# literal scanner skips an unsuccessful dollar prefix once, including inside interpolation holes, while preserving later literal offsets. It still recognizes regular, verbatim, interpolated and raw literals; multiple dollars remain supported for raw interpolation. This bounds the repaired prefix-retry path, not every possible malformed/nested input. See the [classifier hardening plan](../../../plans/2026-10-20-classifier-hardening.md) for regression and operation-count evidence.
