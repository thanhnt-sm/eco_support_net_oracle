# DataGuard for Visual Studio

DataGuard validates database contracts between .NET code, stored procedures, and raw SQL before they fail in integration testing or production.

## Features

- **Tools > DataGuard: Run Validation** runs the local DataGuard CLI against `<solution>/.dataguard.yml`.
- **Tools > DataGuard: Cancel Validation** terminates the owned CLI process tree.
- Drains CLI streams without displaying raw output; the **DataGuard** Output Window pane shows lifecycle status only.
- Maps private SARIF results into the **Error List**, then deletes the temporary file after the run.
- Does not load database providers, retain database credentials, send telemetry, or invoke a shell inside Visual Studio.

## Requirements

Install `DataGuard.Cli` globally or set the machine environment variable `DATAGUARD_CLI_PATH` to an approved CLI executable path:

```powershell
dotnet tool install -g DataGuard.Cli
```

Place `.dataguard.yml` at the solution root. Snapshot/manual mode is appropriate for offline and regulated environments; live database access remains an explicit CLI configuration decision.

## Security

DataGuard is MIT licensed. It provides controls useful in regulated environments but is not a compliance certification.
