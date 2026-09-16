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

The extension executes `dataguard.exe` directly without invoking a shell. It discovers the executable in the following precedence order:
1. An absolute `dataguard.exe` path configured in **Tools > Options > DataGuard > General > Custom CLI Executable Path**.
2. An approved `dataguard.exe` path set in the machine or user environment variable `DATAGUARD_CLI_PATH`.
3. `%USERPROFILE%\.dotnet\tools\dataguard.exe`.
4. Standard install directories (`%ProgramFiles%\DataGuard\dataguard.exe`, `%LocalAppData%\Programs\DataGuard\dataguard.exe`).
5. Directories listed in the `PATH` environment variable.

Install `DataGuard.Cli` globally:

```powershell
dotnet tool install -g DataGuard.Cli
```

> **Note:** After installing `DataGuard.Cli` globally, restart Visual Studio so its inherited `PATH` and user profile environment variables are refreshed.

Place `.dataguard.yml` at the solution root. Snapshot/manual mode is appropriate for offline and regulated environments; live database access remains an explicit CLI configuration decision.

## Security

DataGuard is MIT licensed. It provides controls useful in regulated environments but is not a compliance certification.
