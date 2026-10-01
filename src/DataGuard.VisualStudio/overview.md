# DataGuard for Visual Studio

DataGuard validates database contracts between .NET code, stored procedures, and raw SQL before they fail in integration testing or production.

## Features

- **Tools > DataGuard: Run Validation** runs the local DataGuard CLI against `<solution>/.dataguard.yml`.
- **Tools > DataGuard: Cancel Validation** terminates the owned CLI process tree.
- **Tools > DataGuard: Assess Workspace** runs local-first `dataguard assess` with private SARIF output and never enables remote advisories.
- Drains CLI streams without displaying raw output; the **DataGuard** Output Window pane shows lifecycle status only.
- Maps private SARIF results into the **Error List**, then deletes the temporary file after the run.
- Does not load database providers, retain database credentials, send telemetry, or invoke a shell inside Visual Studio.

## Requirements

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
