# StackAccord – .NET Database Contracts (Beta)

StackAccord is a Beta display name for the existing DataGuard Visual Studio extension. Inspect supported contracts between .NET code, stored procedures and raw SQL. Provider, mode and grammar support differ; unknown or unevaluated results do not establish compatibility. This listing describes source capabilities, not installed-editor acceptance or an approved Marketplace release.

## Features

- **Tools > StackAccord (Beta): Run Validation** runs the local DataGuard CLI against `<solution>/.dataguard.yml`.
- **Tools > StackAccord (Beta): Cancel Validation** terminates the owned CLI process tree.
- **Tools > StackAccord (Beta): Assess Workspace** runs local-first `dataguard assess` with private SARIF output and never enables remote advisories.
- **Tools > StackAccord (Beta): Refresh Snapshot** runs `dataguard snapshot refresh --connection-env NAME --provider P` in the solution directory, so `.dataguard-snapshot.json` lands where the next validation finds it; **Verify SQL Shapes Against Database** runs `dataguard verify-shape` and lists every non-verified query with `file:line`. Both read the connection string from the environment variable named in **Tools > Options > DataGuard > General > Live Database** (Visual Studio stores only the name), never pass `--config`, `--ide-safe` or `--connection`, need a separate per-solution consent, and confirm the target host in a modal before every run.
- The **StackAccord (Beta)** Output Window pane shows parsed progress, policy warnings, summaries and lifecycle status.
- Maps private SARIF results into the **Error List**, then deletes the temporary file after the run.
- Reports what was actually compared: the Output pane shows the CLI's policy lines as `[WARN]`, the Summary names the ground truth kind (`none`, `entities`, `schema`, `schema+entities`), and a run that loaded no snapshot, EF model or connection ends with `[WARN] Ran syntactic rules only …` and the status `DataGuard: syntactic only (…)` instead of `[OK] No issues found.`.
- Runs the CLI outside Visual Studio. Live database commands require separate consent; IDE-safe is not an OS sandbox. Local logs use conservative redaction and size limits, not an anonymity guarantee. This display change does not enable telemetry.
- In-editor squiggles and quick fixes (DG001, DG017, ...) come from the bundled Roslyn analyzers (`DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, with `DataGuard.Contracts.dll` and `DataGuard.SqlClassification.dll` that the analyzer loads).

## Requirements

Windows amd64, Visual Studio versions in `[17.12,19.0)`: the bundled analyzer is built against Roslyn 4.12, and earlier VS 2022 releases refuse it (no squiggles). The manifest excludes versions outside that range. Installation and upgrade on the intended VS 18.10.2 host remain unverified.

The extension bundles `dataguard.exe` and executes it directly without invoking a shell. It discovers the executable in the following precedence order:
1. An absolute `dataguard.exe` path configured in **Tools > Options > DataGuard > General > Custom CLI Executable Path** (for example a build from GitHub Releases; verify its SHA-256).
2. The CLI bundled with the extension (`cli\dataguard.exe`).
3. An approved `dataguard.exe` path set in the machine or user environment variable `DATAGUARD_CLI_PATH`.
4. Standard install directories (`%USERPROFILE%\.dotnet\tools\dataguard.exe`, then `%ProgramFiles%\DataGuard\dataguard.exe`, `%LocalAppData%\Programs\DataGuard\dataguard.exe`).
5. Directories listed in the `PATH` environment variable.

If the bundled CLI is missing, reinstall the extension or point the custom path at a verified `dataguard.exe`. Restart Visual Studio after changing environment variables.

Validation/assessment consent is remembered per solution file and current `.dataguard.yml` hash. Live commands have a separate solution-file key and confirm the host every run. **Tools > StackAccord (Beta) > Forget Solution Consent** removes both the current validation key and live key; it does not cancel a running command. Validation/assessment SARIF publication requires the IDE-safe handshake; live commands use separate outcome handling.

Place `.dataguard.yml` at the solution root. Save files before **Run Validation**, and save/rerun after edits move source lines. SARIF stays one-based internally; tasks and caret use zero-based coordinates converted once, so SARIF 42:12 appears at 42:12 in the UI. URI containment remains enforced. A second command during an active run is rejected, not queued.

Use a schema snapshot or supported EF ModelSnapshot source for offline comparison; the IDE-safe path does not load compiled manual assemblies. IDE-safe is a CLI policy, not an OS sandbox or proof of universal network isolation; the separate audit still tracks conditional Oracle execution. See the [English guide](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/03-components/tooling/visual-studio-extension.md) / [hướng dẫn tiếng Việt](https://github.com/thanhnt-sm/eco_support_net_oracle/blob/main/docs/03-components/tooling/visual-studio-extension.vi.md) for mode boundaries, exit codes (user cancellation: 130), and actual verification limits.

## Security

DataGuard is licensed under GPL-3.0-only (releases up to v0.3.0 were MIT), with a commercial licence available. The extension ships `LICENSE`, `ADDITIONAL-PERMISSIONS.md` and `THIRD-PARTY-NOTICES.md` (Oracle and Microsoft SNI components keep their own terms). These texts are not lawyer-reviewed. It provides controls useful in regulated environments but is not a compliance certification.

## Beta feedback and compatibility

Use **Tools > StackAccord (Beta) > Export Support Summary** to review an immutable preview, then **Save locally…** to save a text file. Cancel either dialog to write nothing. Up to 200 allowlisted diagnostic entries and 64 KiB; no raw logs, diagnostic messages, SQL, paths, configuration or credentials. No automatic upload. Share only after reviewing the file and separately choosing to send it. **View Diagnostic Logs** opens a different raw local file, not an export bundle.

Technical identity stays `DataGuard.VisualStudio`, publisher `thanhnt-sm`, source manifest version `0.4.0` (release packaging stamps the artifact version). **Tools > Options > DataGuard**, CLI `dataguard`, `.dataguard.yml`, `DATAGUARD_*`, log paths, diagnostic prefixes and support-file names remain unchanged. Same-ID upgrade/settings retention must still be tested with the exact artifact. The word Beta is an evaluation label, not a separate private/prerelease Marketplace channel.

React-aware contracts, agent integration and enterprise operating profiles are **Planned**, not released features. CLI descriptor-to-TypeScript export is not an editor action or proof of end-to-end React/API compatibility. No guaranteed prevention, automatic safe repair, performance or compliance promise is made.

Report artifact version/hash, actual host/provider/mode, expected versus observed behavior and a minimal synthetic reproduction through the [existing issue tracker](https://github.com/thanhnt-sm/eco_support_net_oracle/issues). Do not attach raw logs or private project files by default. License/version terms are unchanged; inspect the exact package notices before distribution.
