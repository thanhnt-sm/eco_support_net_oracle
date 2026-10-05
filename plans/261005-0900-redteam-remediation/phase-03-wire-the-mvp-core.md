# Phase 3: Wire the MVP core

Closes: C1, C6, H1, H2, H3, H4, H6, H7, H8, H9, H10 (AST for T-SQL), Medium catalog items, `[SkipContractCheck]` in CLI. Report recommendations 8–15.

## 3.1 Call-site ↔ catalog matching (C1, H6, rec 8)
- `ProjectCSharpSqlSource`: for SP calls, populate `RawSqlDescriptor.ProcedureName`, `Schema`, `Package` (parsed from `EXEC a.b.c`, `CALL`, `BEGIN pkg.p(...)`, or `CommandText`), `IsStoredProcedure`, and per-argument `ParameterDescriptor` with `ClrType` (from `ITypeSymbol.ToDisplayString()`, unwrapping `Nullable<T>` and enums to the underlying type) and `CallSiteDirection` (Dapper `DynamicParameters.Add(..., direction: ParameterDirection.Output)`, ADO `.Direction = ParameterDirection.Output`, `out`/`ref` arguments). Sources: Dapper anonymous object (`new { Id = id }`), `DynamicParameters`, `cmd.Parameters.Add/AddWithValue`.
- New `Core/Rules/StoredProcedureCallMatchRule.cs` (RuleId `DG101`): resolves the call against `StoredProcedureDescriptor`s using `SchemaObjectName.Canonical(provider)`, default schema/package from config, and overload resolution (named args must exist; positional then named binding; missing required = no default ⇒ reject; best score). Emits DG101 for unresolvable (with nearest candidate), missing required, extra unknown.
- `ParameterTypeMatchRule` (DG002) and `ParameterDirectionRule` (DG003) consume the resolution result (shared `StoredProcedureCallResolver` static helper, cached per contract) instead of guessing from `RawSqlDescriptor.Parameters` alone.
- `Core/Rules/TypeCompatibility/ITypeCompatibility.cs` + `SqlServerTypeCompatibility` (Core, later moves with 4.1), `OracleTypeCompatibility`, `PostgreSqlTypeCompatibility`, `MySqlTypeCompatibility` in adapters; keyed by CLR special type names (`System.Int32`, `int`, `Int32` all normalized), handling precision/scale/length, `Guid`↔`RAW(16)`/`uniqueidentifier`/`uuid`/`char(36)`, `bool`↔`NUMBER(1)`/`bit`/`boolean`/`tinyint(1)`, `DateOnly`, `TimeOnly`, `TimeSpan`, enums. Unknown on either side ⇒ `Unknown` (no finding). `ProviderRuleCatalog` injects the provider's table.
- Severity for DG002/DG003/DG101 findings produced by the new path: Warning in this release (note in CHANGELOG), Error toggle via config `StrictProcedureContracts`.
- Tests: `StoredProcedureCallMatchRuleTests` (≥ 15: exact, overload by arity, named args, defaults, missing, extra, package-qualified, case folding per provider, unknown type no finding); `ProjectCSharpSqlSourceTests` (ClrType and direction extraction for Dapper/ADO/EF patterns); e2e `CliExitCodeTests` on a fixture project + snapshot v2 with SPs.

## 3.2 Oracle catalog (C6, rec 9)
- `AllArgumentsReader`: one query on `ALL_ARGUMENTS` with `owner, package_name, object_name, subprogram_id, overload, position, sequence, argument_name, in_out, data_type, data_length, data_precision, data_scale, char_used, char_length, defaulted, data_level`, `WHERE owner = UPPER(:owner) AND data_level = 0 AND (:pkg IS NULL AND package_name IS NULL OR package_name = UPPER(:pkg))`, `UPPER()` on both sides; group by `(package_name, object_name, subprogram_id)`; keep 0-argument overloads; `Defaulted` ⇒ `HasDefault`.
- `GetProcedureNamesAsync`: use `ALL_PROCEDURES` with `procedure_name` when `object_type = 'PACKAGE'`, else `object_name`; never reference `package_name` on that view.
- `StoredProcedureDescriptor.Id` = `oracle:{owner}.{pkg?}.{proc}#{subprogram_id}`; `PackageName` populated.
- `NlsSessionReader`: charset from `nls_database_parameters`; wire into `BuildContractsAsync` for Oracle and persist in snapshot v2.
- `RefCursorDescriber`: wired behind config `DescribeRefCursors: true` (documented as executing the procedure); populates `ResultColumns` and `ReturnsRefCursor`.
- Live test (`LiveDbFact`): package with two overloads (one 0-arg), standalone proc, ref cursor describe behind the flag.

## 3.3 Other catalogs (Medium)
- PostgreSQL: `proargmodes 't'` ⇒ `ResultColumns`; `pronargdefaults` ⇒ `HasDefault` for the trailing N inputs; signature key = in-arg type oids; empty `proargnames` ⇒ `p{i}`; identifier dictionary `Ordinal` with canonical folding; materialized views via `pg_matviews`.
- MySQL: include `ROUTINE_TYPE IN ('PROCEDURE','FUNCTION')`; default schema = `DATABASE()`; table key honors `lower_case_table_names` (read `@@lower_case_table_names`); `CharUsed` no longer carries charset (new `Charset` field on `ColumnDescriptor`, nullable).
- SQL Server: normalize `max_length` to characters (`nchar/nvarchar` ⇒ `/2`, `-1` ⇒ null = MAX) for both `sys.parameters` and `sp_describe_first_result_set`; per-proc `try/catch` marks the proc `Unknown` instead of aborting; table key split into `Schema` + `Name`.
- `ParameterDescriptor` gains `HasDefault` (bool) and `ColumnDescriptor` gains `Charset` (string?) and `Schema` on `DatabaseTableDescriptor`; all constructors and snapshot serialization updated.

## 3.4 Snapshot v2 (H4, rec 10)
- `BaselineFile` v3: `Provider`, `DatabaseVersion`, `LengthSemantics`, `Charset`, `Schema` (tables with schema), `StoredProcedures` (parameters, overloads, result columns), `SchemaHash` over everything.
- `snapshot refresh` writes v3; `validate` reads v2 (tables only, warns "snapshot has no procedures; run snapshot refresh") and v3.
- `validate` checks: provider mismatch ⇒ exit 3; `SchemaHash` recomputed and compared ⇒ exit 3 on mismatch; DB major.minor drift ⇒ warning (uses the dead code at `Program.cs:1238`); snapshot age > `SnapshotMaxAgeDays` (config, default 90) ⇒ warning.
- Default path discovery (1.5) and `--offline` ⇒ Snapshot already landed in Phase 1.

## 3.5 Baseline fingerprint v2 (H3, rec 11)
- `ContractViolation.Properties` carries structured keys (`table`, `column`, `procedure`, `entity`, `property`, `sqlHash`); every rule sets them.
- `BaselineManager.Fingerprint(v)` = SHA-256 of `ruleId | canonical subject (sorted properties) | repo-relative path | hash(normalized SQL or member id)`; baseline is a multiset `{fp: count}`; new = count(current) > count(baseline).
- Legacy entries without `Fingerprint` match by `RuleId:Message` for compatibility; `baseline` rewrites them; `validate` prints a one-line hint when legacy entries are present.
- Messages never truncate lists used in fingerprints (`Take(5)` only in display text, full list in `Properties`).
- SARIF emits `partialFingerprints.dataguard/v2`.

## 3.6 One `Unevaluated` semantics (H1, H2, rec 15)
- `ILiveQuerySchemaProvider.DescribeAsync` returns `LiveSchemaResult { Columns, Status: Described | Failed, Error }`; Oracle/PG providers stop falling back to syntactic columns; SQL Server DG020 becomes an `Unevaluated` outcome; new `MySqlLiveQuerySchemaProvider` (`CommandBehavior.SchemaOnly`).
- Validation result carries `UnevaluatedContracts`; CLI prints them and exits 3 unless `--allow-unevaluated`.
- Acquisition diagnostics: unreadable `.cs` file, >256 KB literal, partial `ModelSnapshot` parse, unknown YAML key are collected into `AcquisitionDiagnostics` and reported; unreadable file ⇒ `Incomplete`.

## 3.7 Length semantics (H7, H8, rec 14)
- Oracle: `bytesPerUtf16Unit(charset, isUnicode)` (AL32UTF8/UTF8 = 3, AL16UTF16 = 2, single-byte = 1); `IsUnicode` flows from `EfModelSource` into `PropertyDescriptor.Annotations["IsUnicode"]`; CHAR-semantics columns are also checked against `min(char_length × maxCharBytes, 4000|32767)` using `MAX_STRING_SIZE` when available; `ToOracleColumnName` tries `CUSTOMERID`, `CUSTOMER_ID`, and the EF column name; table lookup by `(schema, name)` canonical.
- MySQL: TEXT family limits in bytes; `utf8mb3 = 3`; MY005 replaced by row-size (65 535) and index-prefix (3072/767) checks; MY007 premise corrected (Pomelo unbounded `string` ⇒ `longtext`, so only flag when the column is `varchar(n)` with n < implied length).
- PostgreSQL: single PG003 per property; drop the byte check; try snake_case column name.
- `[MaxLength]`/`[StringLength]` read constant expressions, not only literals.

## 3.8 Phantom detection for T-SQL via ScriptDOM (H10, rec 12)
- `SqlServer` provider: `TSqlPhantomAnalyzer` builds per-SELECT scopes (base tables, CTEs, derived tables, TVFs, temp tables, table variables), resolves column refs through the scope chain; parse failure ⇒ no DG015/DG016 (DG019 reports the parse error). Other providers keep the Phase 1 tokenizer.

## 3.9 Extractor fixes (H9, rec 13)
- `TargetMethodNames` adds `QueryFirst*`, `QuerySingle*`, `QueryUnbufferedAsync`, `SqlQuery`, `SqlQueryRaw`, `ExecuteSql`, `ExecuteSqlAsync`, `ExecuteReader*`, `ExecuteScalar*`.
- Handle `ConditionalAccessExpression` and `ImplicitObjectCreationExpression`.
- `IsSqlString` requires a statement-shaped prefix (`^\s*(SELECT|INSERT|UPDATE|DELETE|MERGE|WITH|EXEC|EXECUTE|CALL|BEGIN|DECLARE)\b`) and at least one of `FROM|INTO|SET|VALUES|(`; `base("…")` synthesis only for identifiers that exist in the catalog.
- `ExpectedProperties`: public instance properties with a setter or init, skip `string`/primitives/`DateTime`/`Guid` target types; skip `[NotMapped]`.
- Compilation references: prefer `project.assets.json` of the scanned project when present; fall back to trusted platform assemblies; deterministic ordering.
- `Id` = `project-sql:{repo-relative path}:{span.Start}:{sha256(normalizedSql)[..8]}`.
- `[SkipContractCheck]` on the containing method or type (semantic) ⇒ descriptor skipped; CLI reports the count.
- `ExtractParameters` masks comments; handles `{0}` and `?` placeholders; ignores `::cast` and `@@var`.

## Acceptance
- Golden `SP_Contract/` cases pass through the CLI path.
- Live Oracle test proves package + overload + 0-arg catalog.
- Mutation checks from the report (factor 4→3 sensitivity, DG101 `EXECUTE`, schema-qualified phantom) are covered by failing-before/passing-after tests.
