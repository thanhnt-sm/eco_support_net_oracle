# Visual Studio 2022 Extension

StackAccord runs its bundled CLI outside `devenv.exe` and publishes batch findings to the Error List. Bundled Roslyn analyzers provide in-editor diagnostics separately. Visual Studio 2022 **17.12 or later** is required by the Roslyn 4.12 analyzer; the VSIX manifest accepts `[17.12,19.0)` on Windows amd64. This is not a Visual Studio for Mac extension.

## Commands and everyday use

Open a solution, save the source/configuration files, then use **Tools > StackAccord (Beta)**:

| Command | Behavior |
|---|---|
| Run Validation | Validate saved files using the solution-root `.dataguard.yml`; publish accepted SARIF findings. |
| Cancel Validation | Cancel the active StackAccord command and request termination of its owned CLI process tree. |
| Assess Workspace | Run local-first `assess`, without remote advisory consent. |
| Export Support Summary | Preview an allowlisted summary, then explicitly save locally; no automatic upload. |
| View Diagnostic Logs | Open the diagnostic log location. |
| Validation Rules & Descriptions | Display the rule catalog. |
| Forget Solution Consent | Remove current validation consent and the separate live-command consent for this solution file. |
| Refresh Snapshot | Connect to the selected database and write `.dataguard-snapshot.json` in the solution directory. |
| Verify SQL Shapes Against Database | Describe read SQL queries against the selected database and report non-verified queries with `file:line`. |

Run Validation passes the solution-root `.dataguard.yml` path together with `--ide-safe`. If that file is missing, the CLI warns and uses defaults: `ValidateCommand` passes `allowMissingConfig: ideSafe` through the configuration resolver to `ConfigLoader`. A missing file therefore does not itself fail this IDE run. A present but invalid configuration can still fail validation.

Batch commands read files on disk. Save and rerun after edits; previous Error List results remain until accepted replacement results arrive or the solution closes. A second command while one is active is rejected with an “already running” message; it is not queued. `Run Validation on Build` is opt-in and skips solutions that have not granted consent.

Inspect **View > Output > StackAccord (Beta)**, the status bar and the Error List together. A syntactic-only result means no snapshot, EF model or database ground truth was loaded; zero findings then does not establish database compatibility. The summary reports ground truth and unevaluated contracts when available. Use a committed schema snapshot or supported EF ModelSnapshot source for offline comparison; compiled manual assemblies are not loaded by the IDE-safe path.

## CLI discovery

The VSIX includes a self-contained `cli\dataguard.exe`; installing a separate CLI on PATH is not required. `CliLocator` searches in this order:

1. **Tools > Options > DataGuard > General > Custom CLI Executable Path**: an explicit fully qualified executable path. An invalid explicit setting fails discovery instead of silently falling back.
2. The bundled `cli\dataguard.exe`.
3. `DATAGUARD_CLI_PATH`.
4. `%USERPROFILE%\.dotnet\tools`, then standard Program Files and LocalAppData installation directories.
5. PATH.

Restart Visual Studio after environment changes. If the bundled executable is missing, reinstall the VSIX or select a verified custom executable. The extension launches the executable directly without a shell.

## Consent and execution modes

**Run Validation** and **Assess Workspace** pass `--ide-safe`. Publication requires a positive IDE-safe handshake and an acceptable completed result. Validation consent is keyed by solution directory, solution file and the current `.dataguard.yml` hash; changing the configuration or opening a different solution file prompts again. Build-triggered runs do not prompt.

IDE-safe is a CLI policy, not an OS sandbox. It suppresses configured assembly/plugin loading and selected connection, secret-manager and output settings. The current `IdeSafePolicy` also disables both Oracle REF CURSOR describe switches (`DescribeRefCursors` and `UseRefCursorDescribe`), including when an environment credential is retained. Older audit notes about these switches remaining enabled are historical, not the current implementation. The handshake confirms policy selection; it does not establish complete process or network isolation.

**Refresh Snapshot** and **Verify SQL Shapes Against Database** are explicitly live commands. Set **Connection Environment Variable**, **Live Provider** and optional **Live Schema** under **Tools > Options > DataGuard > General**. The extension persists the variable name, not the credential, and shows the extracted target host for confirmation **every run**. These commands use `--connection-env`, omit `--config`, `--ide-safe` and `--connection`, and do not read the solution's `.dataguard.yml`.

Live consent is keyed by solution directory and solution file, independently of configuration content. **Forget Solution Consent** removes both the current validation key and live key without short-circuiting; it is safe to repeat and leaves other solution files' consent intact. It does not cancel an active run. Use **Cancel Validation** for cancellation.

## SARIF to Error List integration

`SarifErrorListPublisher.Load` retains one-based SARIF coordinates internally (invalid negative values are clamped to zero). The loader accepts non-UNC file URIs, rooted paths and relative paths with `uriBaseId: "%SRCROOT%"`; it rejects malformed paths and paths outside the solution after lexical normalization. This check does not resolve filesystem junctions or symbolic links. At most 2,000 diagnostics are retained per run. `ErrorListPresenter.CreateTask` converts each coordinate once with `Math.Max(1, value) - 1`.

| SARIF value | VS task / caret value | Displayed location |
|---|---|---|
| Line 1, column 1 | 0, 0 | 1:1 |
| Line 42, column 12 | 41, 11 | 42:12 |
| Missing, zero or negative coordinate | 0 | First line/column |

`ErrorTask` and `IVsTaskItem` expose zero-based coordinates. Navigation passes those unchanged to `IVsTextView.SetCaretPos` and `CenterLines`; human-readable logs remain one-based. Double-click a finding to open its source location. Findings refer to the saved file at validation time, so save and rerun if edits shifted lines. Missing files produce an Output message. Level maps to Error/Warning/Message; task category is BuildCompile and text is the diagnostic message.

## Results and exit codes

`ExitCodeExplainer` interprets the command, summary, SARIF and termination state together:

| Code / state | Meaning |
|---|---|
| 0 | Completed; inspect warnings, ground truth and unevaluated counts before treating it as clean. |
| 1 | Validation errors when a summary exists; without a summary, CLI failure before completion. For assess: findings. |
| 2 | Invalid arguments/configuration. |
| 3 (validate) | Incomplete validation, such as unavailable rules or missing contract source; preserves previous Error List results. |
| 4 | Validate: refused output write. Assess: tool errors. |
| 130 | User cancellation as recorded by the extension. |
| -1 / timeout | Extension timeout sentinel, not a normal CLI verdict. If the process was killed after results were written, its process exit code is not meaningful; inspect the timeout explanation. |

`137` is not the extension's cancellation contract. Live-command results have their own outcome handling; inspect their Output messages rather than applying the validation table blindly. Cancellation does not publish partial findings. Termination first uses `taskkill /T /F`; if that fails, the fallback kills the direct process only. A successful kill request is not proof that every descendant exited.

## Configuration and logging

General options include detailed logging (default on), custom log directory, custom CLI path, validation-on-build (default off), validation timeout (300 seconds) and assessment timeout (60 seconds), with timeouts clamped to 5–900 seconds. **Validation Rules** maps disabled rule groups to `validate --skip-rules`.

Logs default to `%APPDATA%\DataGuard\logs\dataguard-vs.log`, with a temporary-directory fallback. `DataGuardLogger` redacts supported credential patterns. The Output pane shows parsed progress, policy warnings, summaries and lifecycle messages. Use **View Diagnostic Logs** when diagnosing missing findings or CLI discovery failures.

## Bundled Roslyn analyzers and code fixes

The VSIX includes `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `DataGuard.Contracts.dll` and `DataGuard.SqlClassification.dll`. They are registered as analyzer assets and checked by `scripts/assert-vsix.ps1` and packaging tests. In-editor diagnostics and CLI batch results are separate paths; a successful batch run does not prove the analyzer loaded.

## Verification limits

This documentation review checked source only; it did not run a Windows editor or package installation. Regression tests cover Load → CreateTask → IVsTaskItem, coordinate bounds and consent isolation. Windows `net472` test execution, VSIX packaging and manual navigation at 1:1 and 42:12 remain distinct acceptance checks. Record the exact Visual Studio version, saved source and reproduction when reporting an IDE issue. See the [maintenance evidence](../../../plans/261015-vs-maintenance/plan.md) for actual execution status.

Display-only Beta transition: the menu/Output pane is **StackAccord (Beta)**; **Tools > Options > DataGuard**, command IDs, CLI/config, diagnostics and file names remain unchanged. Verify identity/settings retention on the exact Windows artifact.

## Constant SQL composition

Run Validation and validate-on-build use CLI project extraction. With a matching rebuilt CLI, supported constant `System.String.Concat` calls are extracted with source locations. The Roslyn analyzer also uses the same bounded evaluator for direct EF raw-SQL arguments: two to four string parameters, compiler constants/null, and supported nesting. No application code is executed. Diagnostics cover the composed expression; SELECT-star quickfix is unavailable for it to prevent editing only one fragment.

Typing support does not include Dapper Concat, local-variable flow, dynamic/object/array/enumerable overloads, or generator parity. Unresolved SQL is not evidence of successful validation. There is no new Unknown UI status. Windows editor-host responsiveness and installed VSIX behavior require Windows validation; compiler/analyzer tests do not substitute for it.

The shared evaluator checks cancellation between operands and mapping fragments and before returning SQL. Ordering uses a bounded scan of at most four arguments, with no persistent cache. Cancellation does not interrupt a single string copy or roll back callbacks already delivered; this is not an editor-host latency guarantee.
