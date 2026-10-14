# Raw JSON-RPC probe of DataGuard.LanguageServer (2026-10-14, Release build @ 3975c06)

Script: `lsp-probe.cjs` (node, spawns `dotnet DataGuard.LanguageServer.dll`, frames `Content-Length`). Messages sent and replies seen:

| t (ms) | sent | reply |
|---|---|---|
| 0 | `initialize` (id 1) | `{"result":{"capabilities":{"textDocumentSync":1}}}` |
| 500 | `initialized`, `textDocument/didOpen` (Probe.cs with 4 SQL literals) | `publishDiagnostics`: DGSQL001 (sev 3, range 1:45–1:51 = `SELECT`), DG017 (sev 2, range 1:89–1:95), DGSQL001 (sev 3, range 1:215–1:221); the stored-procedure call and the interpolated SQL got nothing |
| 1300 | `textDocument/hover` (id 2) | **none** |
| 1800 | `didChange` (full text) | `publishDiagnostics` (same three) |
| 2400 | `shutdown` (id 3) | **none** within 1500 ms |
| 3900 | `exit` | **process still alive** after 1500 ms; killed by the probe (exit 143) |

Conclusion: lifecycle requests are not implemented (`src/DataGuard.LanguageServer/Program.cs:12-57` branches only on `initialize`, `didOpen/didChange`, `didClose`); ranges cover the statement keyword only (`src/DataGuard.SqlClassification/SqlClassifier.cs:200-207`).
