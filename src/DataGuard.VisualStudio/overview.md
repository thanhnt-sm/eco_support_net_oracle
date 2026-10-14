# DataGuard for Visual Studio

DataGuard validates database contracts between .NET code, stored procedures, and raw SQL before they fail in integration testing or production.

## Features

- **Tools > DataGuard: Run Validation** runs the local DataGuard CLI against `<solution>/.dataguard.yml`.
- **Tools > DataGuard: Cancel Validation** terminates the owned CLI process tree.
- **Tools > DataGuard: Assess Workspace** runs local-first `dataguard assess` with private SARIF output and never enables remote advisories.
- **Tools > DataGuard: Refresh Snapshot** runs `dataguard snapshot refresh --connection-env NAME --provider P` in the solution directory, so `.dataguard-snapshot.json` lands where the next validation finds it; **Verify SQL Shapes Against Database** runs `dataguard verify-shape` and lists every non-verified query with `file:line`. Both read the connection string from the environment variable named in **Tools > Options > DataGuard > General > Live Database** (Visual Studio stores only the name), never pass `--config`, `--ide-safe` or `--connection`, need a separate per-solution consent, and confirm the target host in a modal before every run.
- Drains CLI streams without displaying raw output; the **DataGuard** Output Window pane shows lifecycle status only.
- Maps private SARIF results into the **Error List**, then deletes the temporary file after the run.
- Reports what was actually compared: the Output pane shows the CLI's policy lines as `[WARN]`, the Summary names the ground truth kind (`none`, `entities`, `schema`, `schema+entities`), and a run that loaded no snapshot, EF model or connection ends with `[WARN] Ran syntactic rules only …` and the status `DataGuard: syntactic only (…)` instead of `[OK] No issues found.`.
- Does not load database providers, retain or log database credentials, send telemetry, or invoke a shell inside Visual Studio.
- In-editor squiggles and quick fixes (DG001, DG017, ...) come from the bundled Roslyn analyzers (`DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, with `DataGuard.Contracts.dll` and `DataGuard.SqlClassification.dll` that the analyzer loads).

## Requirements

Visual Studio 2022 17.12 or later: the bundled analyzer is built against Roslyn 4.12, and earlier VS 2022 releases refuse it (no squiggles). The VSIX installs only on 17.12 and later.

The extension bundles `dataguard.exe` and executes it directly without invoking a shell. It discovers the executable in the following precedence order:
1. An absolute `dataguard.exe` path configured in **Tools > Options > DataGuard > General > Custom CLI Executable Path** (for example a build from GitHub Releases; verify its SHA-256).
2. The CLI bundled with the extension (`cli\dataguard.exe`).
3. An approved `dataguard.exe` path set in the machine or user environment variable `DATAGUARD_CLI_PATH`.
4. Standard install directories (`%ProgramFiles%\DataGuard\dataguard.exe`, `%LocalAppData%\Programs\DataGuard\dataguard.exe`).
5. Directories listed in the `PATH` environment variable.

If the bundled CLI is missing, reinstall the extension or point the custom path at a verified `dataguard.exe`. Restart Visual Studio after changing environment variables.

Every run asks for consent once per solution file (and again when its `.dataguard.yml` changes); **Tools > DataGuard > Forget Solution Consent** revokes it. Results are published only after the CLI confirms IDE-safe mode.

Place `.dataguard.yml` at the solution root. Snapshot/manual mode is appropriate for offline and regulated environments; live database access remains an explicit CLI configuration decision.

## Security

DataGuard is licensed under GPL-3.0-only (releases up to v0.3.0 were MIT), with a commercial licence available. The extension ships `LICENSE`, `ADDITIONAL-PERMISSIONS.md` and `THIRD-PARTY-NOTICES.md` (Oracle and Microsoft SNI components keep their own terms). These texts are not lawyer-reviewed. It provides controls useful in regulated environments but is not a compliance certification.
