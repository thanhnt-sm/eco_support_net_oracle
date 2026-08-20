# DataGuard for VS Code

DataGuard detects **database contract drift** between .NET code, stored procedures, raw SQL, and the validated schema before it reaches integration testing or production. It is built for backend and full-stack teams that need provider-aware contract evidence without giving the editor direct database credentials.

## What it does

- **Run Validation** from the status bar or Command Palette.
- Runs the local `dataguard` CLI once per trusted workspace with a bounded timeout.
- Writes SARIF to a private temporary file, maps violations into **Problems**, then deletes the file.
- Drains CLI streams without displaying them; the Output channel contains safe lifecycle status only.
- Supports cancellation and terminates the process tree owned by the extension.
- Never runs in untrusted or virtual workspaces. It does not send telemetry or connect to a database itself.

## Requirements

Install the DataGuard CLI and ensure it is on `PATH`:

```bash
dotnet tool install -g DataGuard.Cli
```

Commit a `.dataguard.yml` in the trusted workspace. Use snapshot/manual mode for offline or regulated environments; database access remains an explicit CLI configuration decision.

## Commands

| Command | Description |
| --- | --- |
| **DataGuard: Run Validation** | Validate the selected trusted workspace and populate Problems from SARIF. |
| **DataGuard: Cancel Validation** | Terminate the active validation process tree for the selected workspace. |

## Settings

| Setting | Scope | Default | Description |
| --- | --- | --- | --- |
| `dataguard.enabled` | Workspace | `true` | Enables DataGuard commands. |
| `dataguard.configPath` | Workspace | `.dataguard.yml` | Relative path that must remain inside the trusted workspace. |
| `dataguard.cliPath` | User machine | `dataguard` | CLI command/path. Do not set it from workspace configuration. |
| `dataguard.timeoutSeconds` | Window | `60` | Validation limit, clamped to 5–900 seconds. |

## Security and enterprise use

- The extension invokes a fixed argument vector with `shell: false`.
- It never stores connection strings, passwords, tokens, or SARIF output in workspace settings.
- Raw CLI output is never displayed. Generated SARIF is deleted after diagnostics load.
- DataGuard source is [MIT licensed](LICENSE). These controls help operate in regulated environments but are not a compliance certification.

## Development

```bash
npm ci
npm test
npm run package
```

`npm run package` produces `dataguard-vscode-<version>.vsix`. Install it in an Extension Development Host or VS Code using **Extensions: Install from VSIX**.
