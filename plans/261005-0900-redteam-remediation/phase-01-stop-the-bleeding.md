# Phase 1: Stop the bleeding (CLI gates and rule correctness)

Closes: C2, C3 (key fix), C4, C5, H5, H10 (tokenizer part), H11. Report recommendations 1–5.

## Requirements

### 1.1 Unavailable rules must not block (C2)
- `Program.cs` validate: compute `unavailableOutcomes` **after** applying `skipRuleIds`; print each once to stderr as `Rule <id> not evaluated: <reason>`; do not set exit 3.
- New option `--fail-on-unavailable` and config key `FailOnUnavailableRules` (default false) restore the old behavior.
- SARIF/text output lists unavailable rules in the run summary (existing `RuleExecutionOutcome`).
- Tests: `CliExitCodeTests`: `validate --provider oracle` on the snapshot fixture exits 0; with `--fail-on-unavailable` exits 3; `--skip-rules DG012` with `--fail-on-unavailable` exits 0.

### 1.2 Phantom table key normalization (C3) and tokenizer hardening (H10)
- Introduce `SchemaObjectName.Canonical(provider, raw)` helper in Core (`Rules/Sql/SqlIdentifier.cs`): splits `db.schema.name`, strips brackets/quotes/backticks, returns `(Schema?, Name)` with dialect folding (Oracle/SQL Server upper for comparison, PG lower for unquoted, MySQL as-is).
- Phantom analyzer indexes catalog tables by **both** `(schema, name)` and bare `name`; a reference resolves by full key first, then bare; ambiguity across schemas with the same bare name resolves to "exists".
- Mask comments and string literals before scanning. Ignore `FROM` preceded by `EXTRACT(... ` or `TRIM(... ` or `DISTINCT` (IS DISTINCT FROM). Ignore identifiers followed by `(` (TVF), starting with `#` or `@`, three-part names whose first part is not the current db, `DUAL`, `sys.*`, `INFORMATION_SCHEMA.*`. Collect all CTE names from the `WITH [RECURSIVE] a AS (...), b AS (...)` list.
- SELECT-list split is parenthesis-aware; an alias after `AS` or trailing identifier after an expression is an **output** alias and never a column reference; unqualified columns are checked against the union of all referenced tables' columns when more than one table is referenced.
- Split into `PhantomTableRule` (DG015) and `PhantomColumnRule` (DG016) with one internal `PhantomSqlAnalyzer`. Register both in `ProviderRuleCatalog` and `BuiltInRuleDependencies`.
- Tests (new `PhantomIdentifierRuleNegativeTests.cs`): every bullet above has a negative case; positive cases for schema-qualified phantom, bare phantom, qualified phantom column.

### 1.3 DG005 correctness (C5)
- Use `PropertyDescriptor.IsNullable` (fallback to `Required` annotation only when present). Resolve the column by `(entity.TableName, prop.ColumnName)` using `SchemaObjectName.Canonical`; fall back to bare table name; never merge columns across tables.
- Both directions reported: NOT NULL column ↔ nullable property (Warning), nullable column ↔ non-nullable property (Warning, message names the runtime risk).
- Tests: positive both directions, negative both directions, two tables with same column name, entity table with schema prefix.

### 1.4 DG013 self-inflicted FP (H5)
- `SqlServerSyntaxLeakRule` (and DG010/DG011 Oracle wrappers) skip descriptors with `IsStoredProcedure == true`.
- Extractor still synthesizes `EXEC <proc>` for ADO/Dapper SP calls (DG101 relies on it) but sets `ProcedureName` and `IsStoredProcedure`.
- Test: `OracleAdapterTests`: Oracle SP descriptor `PKG.PROC` with `IsStoredProcedure` yields no DG013.

### 1.5 Empty-pass gate (C4)
- `--provider` whitelist: `sqlserver | oracle | mysql | postgresql | postgres`; otherwise exit 2 with the list.
- `--config` pointing to a missing file ⇒ exit 2 (currently silent default).
- YAML: unknown top-level keys produce a warning listing them (strict mode via `StrictConfig: true` turns them into exit 2).
- Ground-truth coverage: after acquisition, if no `DatabaseSchemaDescriptor` and no `StoredProcedureDescriptor` and no `EntityDescriptor` from EF came from a ground-truth source, and the run is not `--format contracts|yaml|typescript`, print `UNEVALUATED: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` and exit 3. `--allow-syntactic-only` (new flag) downgrades this to a warning for pure lint runs.
- `validate` default snapshot path: when `SnapshotFilePath` is null and `.dataguard-snapshot.json` exists next to the config (or cwd), use it, print `Using snapshot <path>`.
- Bare `--offline` (no `--assembly`) ⇒ Snapshot mode; `--offline --assembly` stays Manual. IdeSafe tests must remain green.
- Tests in `CliExitCodeTests`: unknown provider ⇒ 2; missing config ⇒ 2; project-only scan without ground truth ⇒ 3; same with `--allow-syntactic-only` ⇒ 0; default snapshot discovered ⇒ 0 and stdout contains `Using snapshot`.

### 1.6 DG016 collision (H11)
- `RawSqlParseStatusRule.RuleId` ⇒ `DG019`; update `ProviderRuleCatalog.RuleTitles`, `DiagnosticDescriptors` if present, README rule table, `docs/` mentions, VS `DataGuardRulesOptionsPage`.
- Test: `ProviderRuleCatalogTests` asserts rule IDs are unique across the catalog and that every emitted violation RuleId equals its rule's `RuleId`.

## Acceptance
- Full suite green; new tests ≥ 30.
- `dotnet run --project src/DataGuard.Cli -- validate --provider oracle --config samples/...` exits 0 on the sample snapshot.
