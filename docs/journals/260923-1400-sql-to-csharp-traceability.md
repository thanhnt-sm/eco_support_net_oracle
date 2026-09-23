---
date: 2026-09-23
title: "SQL-to-C# traceability and visibility overhaul"
status: completed
---

# SQL-to-C# traceability and visibility overhaul

## Decision

- **Root cause resolution**: `src/DataGuard.VSCode/src/command-args.ts` now passes `--project <workspacePath>` and `--progress` for the `validate` command, allowing `ProjectCSharpSqlSource` to discover C# source contracts directly.
- **Credential safety**: Child process output in `extension.ts` parses line-delimited `ProgressEvent` JSON payloads and routes all messages through `redactSensitiveText()` / `redactAndBoundSensitiveText()`. Connection discovery in `ConnectionDiscovery.cs` masks all credentials (`Password=***`).
- **Expanded SQL detection**: `ProjectCSharpSqlSource.cs` now recognizes ADO.NET patterns (`ExecuteNonQuery`, `ExecuteReader`, `ExecuteScalar`), `cmd.CommandText = "..."` assignments, `new *Command("...", ...)` instantiations, and base repository constructor calls.
- **SQL operation & table classification**: Added `SqlOperationType` (`Read`, `Write`, `Join`, `Reference`, `Mixed`) and `ExtractReferencedTables` with alias and schema prefix stripping.
- **Multi-dialect parameter detection**: Added support for `@param`, `:param` (Oracle), and `$1` (PostgreSQL) with lexical masking of SQL string literals to prevent colon collisions.
- **Table qualifiers in shape matching**: Updated `ColumnShapeMatchRule.ExtractColumnNamesFromSql` to preserve table-qualified columns (`u.Id`, `c.CUSTOMER_ID AS Id`), stripping the table alias prefix rather than discarding the column.
- **Architectural boundaries & live query validation**: Added `OracleLiveQuerySchemaProvider` in `DataGuard.Oracle.Adapter` and `PostgreSqlLiveQuerySchemaProvider` in `DataGuard.PostgreSql.Adapter`. Wired them in `ProviderRuleCatalog.cs` via constructor injection, preventing circular project references into `DataGuard.Core`.
- **Offline CLI execution**: Modified `src/DataGuard.Cli/Program.cs` so `--offline` does not require `--assembly` when `--project` is provided. Added structured scan report in `--verbose` mode and automatic `summary.json` emission alongside SARIF.
- **Lightweight Language Server awareness**: Enriched `SqlClassifier` to extract statement metadata and detect `SELECT *` without heavy Roslyn compilation overhead.
- **Adversarial hardening & stream safety**:
  - Replaced raw buffer `chunk.toString()` with Node.js `StringDecoder("utf8")` across `extension.ts` stdout/stderr stream processing to prevent multi-byte UTF-8 boundary corruption.
  - Enhanced `UriCredentialMaskRegex` and `KeyValueCredentialMaskRegex` in `ConnectionDiscovery.cs` to safely mask passwords containing `@` symbols and escaped quotes (`""` / `\"`).
  - Added regex string literal masking to live query sanitizers in `OracleLiveQuerySchemaProvider.cs` and `PostgreSqlLiveQuerySchemaProvider.cs`, allowing queries with semicolons in string literals while rejecting dangerous stacked statements.
  - Added `.IsDefaultOrEmpty` guard checks on Roslyn `ImmutableArray` members (`typeSymbol.AllInterfaces`, `colAttr.ConstructorArguments`) to prevent `NullReferenceException` during AST inspection of malformed source code.
  - Expanded table extraction regex in `ProjectCSharpSqlSource.cs` to match bracketed (`[dbo].[Users]`) and backticked (```db```.```table```) identifiers, correctly stripping delimiters after schema resolution.
  - Pre-compiled all operational and table extraction regexes with `TimeSpan.FromSeconds(1)` timeouts and implemented a static space padding cache to eliminate garbage collection pressure in AST traversal.
  - Masked string literals before splitting the SELECT clause in `ColumnShapeMatchRule.ExtractColumnNamesFromSql` to prevent phantom column extraction from string literals containing commas, and handled qualified asterisks (`u.*`) to avoid false-positive missing property errors.

## Verification

- `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj`: 697 passed, 0 failed.
- `dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj`: 13 passed, 0 failed.
- `dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj`: 24 passed, 0 failed.
- `npm test` in `src/DataGuard.VSCode`: 27 passed, 0 failed.
- End-to-end CLI validation on `samples/DataGuard.Sample`:
  - Discovered 3 masked connection declarations (SQL Server, Oracle, PostgreSQL).
  - Detected 6 queries across Dapper, ADO.NET `CommandText`, and new command creations.
  - Successfully mapped `Customer` and `Order` properties, flagged 1 unmapped column (`PHONE`), 1 unmapped property (`PhoneNo`), and emitted `[WARNING] DG017: Avoid SELECT *`.
  - Outputted `summary.json` with structured mapping evidence.
