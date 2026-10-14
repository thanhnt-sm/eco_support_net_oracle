# CLI reproduction of the VS Code argv (2026-10-14, Release build @ 3975c06, samples/DataGuard.Sample)

| argv (as `src/DataGuard.VSCode/src/command-args.ts` builds it) | exit | first stderr line | artifact |
|---|---|---|---|
| `scan --project WS --format json --output OUT --progress` | 0 | `{"Kind":"ContractDiscovered",...}` | summary.json (camelCase, `queries[9]`, `connections[3]`) |
| `validate --provider sqlserver --format sarif --output OUT --project WS --progress --ide-safe` | 1 | `ide-safe: active`; later `Warning: no ground truth (...) only syntactic rules ran` | SARIF: DG004×2 (Order queries lacking CUSTOMER_ID), DG017×1 |
| `assess --workspace WS --provider sqlserver --format sarif --output OUT --ide-safe` | **1** | `Unrecognized command or argument '--provider'.` | none → host discards ("did not confirm IDE-safe mode") |
| `assess --workspace WS --format sarif --output OUT --ide-safe` (VS argv) | 1 | `ide-safe: active` | SARIF: DG1301 |
| `verify-shape --project WS --provider sqlserver --format json --output OUT` (no credential) | 2 | `verify-shape requires --connection-env NAME, DATAGUARD_CONNECTION_STRING, ...` | none |
| same with unreachable `DATAGUARD_CONNECTION_STRING` | 3 | `UNEVALUATED: contract acquisition failed: A network-related ...` | none |
| `validate ... --ide-safe --allow-env-connection` with unreachable credential | 3 | `ide-safe: active` / `ide-safe: suppressed live SQL shape rule disabled (use verify-shape)` / `ide-safe: kept environment connection` / `UNEVALUATED: contract acquisition failed: ...` | none |
| `snapshot refresh --provider sqlserver` (no credential) | 3 | `UNEVALUATED: snapshot refresh requires a database connection for fresh acquisition.` | none |
| `baseline --provider sqlserver` (no source) | **1** | `Baseline creation failed: Contract acquisition Unavailable: no configured source or connection` | none → host says "found findings" |
| `init --provider sqlserver` | 0 | — | `.dataguard.yml` only (README claims a snapshot is written too) |
| `validate --provider {oracle,postgresql,none}` on the sample | 1 | adds `Rule DG012 not evaluated: Requires analyzer DbContext provider-registration metadata.` (PG004 for postgresql) | same 3 results |

Suites at HEAD: VS Code `npm test` 97/97; `dotnet test DataGuard.CrossPlatform.slnf --filter Category!=LiveDb` 1997 passed / 5 skipped / 0 failed. None of the rows above is covered by a failing test.
