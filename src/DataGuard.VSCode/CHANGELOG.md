# Change Log

All notable changes to the DataGuard VS Code extension are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed
- **CLI 0.3.0 or later is required for `validate` and `assess`.** Both commands now require the CLI's
  `ide-safe: active` handshake (first stderr line). Output from CLI 0.2.2 and older, which does not
  understand `--ide-safe`, is rejected instead of loaded as findings: the extension shows
  `DataGuard CLI did not confirm IDE-safe mode; results were discarded` and
  `Update the dataguard CLI (0.3.0 or later) or set dataguard.cliPath`. It never retries without the flag.
- When a connection credential is stored in VS Code SecretStorage, `validate` passes
  `--allow-env-connection` so the CLI keeps that credential only; connection strings from
  `.dataguard.yml` are always ignored under `--ide-safe`.
- **Verify SQL Shapes Against Database** asks for the same modal confirmation as **Refresh Snapshot**
  and **Create Baseline**, naming the masked target host before connecting.
- `ide-safe:` and `baseline:` policy lines from the CLI (including
  `baseline: <n> violations suppressed by <path>`) are echoed to the DataGuard output channel as `[WARN]`.
- CLI install guidance now points to GitHub Releases (`dataguard-<version>-<rid>.zip` + `.sha256`);
  `DataGuard.Cli` is not published on nuget.org.

## [0.2.3]

- `validate` and `assess` run the CLI with `--ide-safe` in addition to the workspace-trust gate.
