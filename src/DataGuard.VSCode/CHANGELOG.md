# Change Log

All notable changes to the DataGuard VS Code extension are documented here.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Security
- Closed 17 OSV advisories flagged by OSSF Scorecard in `package-lock.json` (transitive dependencies):
  `undici` 7.29.0 → 7.30.0 (10 advisories, build-time only via `@vscode/vsce`), `brace-expansion`
  5.0.9 → 5.0.12 (build-time) and 2.1.4 → 2.1.7 (bundled in the VSIX via `vscode-languageclient`;
  GHSA-6j4f-fj2g-mc7p, GHSA-q2hr-2g5m-vwhr, GHSA-qhr7-859c-m2p7), `fast-uri` 3.1.6 → 3.1.8
  (`overrides` pin raised; GHSA-58mr-gqgx-xq4g, GHSA-hrr3-gc8f-f4qj, GHSA-qw65-cvwx-89v3) and
  `markdown-it` 14.3.0 → 14.3.2 (GHSA-253c-mchw-3w2r). No runtime behaviour change.

### Changed
- Version constants moved to 0.3.0 ahead of the v0.3.0 tag; the empty v0.2.3 GitHub release (workflow failed before publishing any asset) is superseded.
- **CLI 0.3.0 or later is required for `validate` and `assess`.** Both commands now require the CLI's
  `ide-safe: active` handshake (first stderr line). Output from CLI 0.2.2 and older, which does not
  understand `--ide-safe`, is rejected instead of loaded as findings: the extension shows
  `DataGuard CLI did not confirm IDE-safe mode; results were discarded` and
  `Update the dataguard CLI (0.3.0 or later) or set dataguard.cliPath`. It never retries without the flag.
- When a connection credential is stored in VS Code SecretStorage, `validate` passes
  `--allow-env-connection` so the CLI keeps that credential only; connection strings from
  `.dataguard.yml` are always ignored under `--ide-safe`. The kept credential is used only to
  read the ground-truth catalog (schema and stored-procedure definitions): `validate` never
  sends or describes repository SQL against the database — only **Verify SQL Shapes Against
  Database** does, after the modal confirmation.
- **Verify SQL Shapes Against Database** asks for the same modal confirmation as **Refresh Snapshot**
  and **Create Baseline**, naming the masked target host before connecting.
- `ide-safe:` and `baseline:` policy lines from the CLI (including
  `baseline: <n> violations suppressed by <path>`) are echoed to the DataGuard output channel as `[WARN]`;
  the `ide-safe: active` acknowledgement itself is echoed as `[INFO]`.
- The read-query count shown in the **Verify SQL Shapes Against Database** confirmation is now
  computed by a validated helper (`countReadQueries`) instead of a cast of the last scan's
  `summary.json`; a report without a `queries` array yields the "every read SQL query discovered
  in this workspace" wording (hardening; the extension already normalised `queries` to an array).
- CLI install guidance now points to GitHub Releases (`dataguard-<version>-<rid>.zip` + `.sha256`);
  `DataGuard.Cli` is not published on nuget.org.

## [0.2.3]

- `validate` and `assess` run the CLI with `--ide-safe` in addition to the workspace-trust gate.
