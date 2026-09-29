# DataGuard for VS Code

DataGuard detects **database contract drift** between .NET code, stored procedures, raw SQL, and the validated schema before it reaches integration testing or production. It is built for backend and full-stack teams that need provider-aware contract evidence without giving the editor direct database credentials.

## What it does

- **Run Validation** from the status bar or Command Palette.
- Runs the local `dataguard` CLI once per trusted workspace with a bounded timeout, extracting C# AST models, inline SQL queries, and contracts directly via `--project`.
- Tracks live progress via line-delimited NDJSON events streamed to stderr (`--progress`).
- Writes SARIF to a private temporary file, maps violations into **Problems**, reads optional `summary.json` for high-level scan metrics, then deletes the temporary files.
- Drains CLI streams safely; the Output channel contains redacted lifecycle status, scan summary metrics, and findings counts.
- Supports cancellation and terminates the process tree owned by the extension.
- Never runs in untrusted or virtual workspaces. It does not send telemetry; the only credential it ever hands to the CLI is the one you stored in VS Code SecretStorage.
- Always launches `validate` and `assess` with `--ide-safe`: the CLI ignores any `.dataguard.yml` setting that could load an assembly (`GroundTruthMode: Manual`, `ManualAssemblyPath`), open a repository-configured database, secret-manager or network connection, or write to a repository-chosen path. The CLI must acknowledge the mode (`ide-safe: active`) before results are accepted.

## Requirements

Install the `dataguard` CLI from the [GitHub Releases](https://github.com/thanhnt-sm/eco_support_net_oracle/releases) page, verify its SHA-256 checksum against the release notes, and point `dataguard.cliPath` (User Settings) at the executable or make sure it is on `PATH`. CLI **0.3.0 or later** is required: an older CLI (0.2.2 and below) does not understand `--ide-safe`, so the extension rejects its output and shows an update hint instead of loading findings.

Commit a `.dataguard.yml` in the trusted workspace. Use snapshot/manual mode for offline or regulated environments; database access remains an explicit CLI configuration decision.

## Commands

| Command | Description |
| --- | --- |
| **DataGuard: Run Validation** | Validate the selected trusted workspace and populate Problems from SARIF. |
| **DataGuard: Cancel Validation** | Terminate the active validation process tree for the selected workspace. |
| **DataGuard: Configure Connection Credential** | Prompts for a connection string and stores it in encrypted VS Code SecretStorage for the selected workspace; an empty value removes it. |
| **DataGuard: Assess Workspace** | Local, IDE-safe assessment of the workspace; findings go to Problems. |
| **DataGuard: Refresh Snapshot** / **Create Baseline** / **Verify SQL Shapes Against Database** | Live-database commands; each asks for confirmation before connecting. |

## Settings

| Setting | Scope | Default | Description |
| --- | --- | --- | --- |
| `dataguard.enabled` | Workspace | `true` | Enables DataGuard commands. |
| `dataguard.configPath` | Workspace | `.dataguard.yml` | Relative path that must remain inside the trusted workspace. |
| `dataguard.cliPath` | User machine | `dataguard` | CLI command/path. Do not set it from workspace configuration. |
| `dataguard.timeoutSeconds` | Window | `60` | Validation limit, clamped to 5–900 seconds. |

## Security and enterprise use

- The extension invokes a fixed argument vector with `shell: false`.
- It never stores connection strings, passwords, tokens, or SARIF output in workspace settings; connection credentials are encrypted by VS Code SecretStorage and passed only to the spawned CLI process through the `DATAGUARD_CONNECTION_STRING` environment variable.
- IDE-safe mode applies to `validate` and `assess`. When a SecretStorage credential exists, `validate` also passes `--allow-env-connection`: the CLI keeps only that environment credential (never a `.dataguard.yml` connection string) and uses it **only to read the ground-truth catalog** (schema and stored-procedure definitions) while repository settings still cannot load code. `validate` never sends or describes repository SQL against your database; that happens only in **Verify SQL Shapes Against Database**, after you confirm. Without a stored credential the flag is never sent.
- `validate`/`assess` results are discarded unless the CLI's first stderr line is `ide-safe: active`; the extension never retries without `--ide-safe`. The acknowledgement itself is echoed to the DataGuard output channel as `[INFO]`; every other `ide-safe:` and `baseline:` policy line from the CLI is echoed as `[WARN]`.
- **Refresh Snapshot**, **Create Baseline** and **Verify SQL Shapes Against Database** are live-database commands: they use your credential (or the connection configured in `.dataguard.yml`/`DATAGUARD_CONNECTION_STRING`) and always ask for a modal confirmation that names the masked target host first.
- Raw CLI output is never displayed. Generated SARIF is deleted after diagnostics load.
- DataGuard source is [MIT licensed](LICENSE). These controls help operate in regulated environments but are not a compliance certification.

## Development

```bash
npm ci
npm test
npm run package
```

`npm run package` produces `dataguard-vscode-<version>.vsix`. Install it in an Extension Development Host or VS Code using **Extensions: Install from VSIX**.
