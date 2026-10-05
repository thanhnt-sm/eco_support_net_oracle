# Phase 4: Architecture (adapters, pipeline, analyzer, credentials)

Closes: A1/A2 layering, two pipelines, plugin metadata bug, graph bugs, D1/D2 credentials, Manual `LoadFrom`, analyzer semantic-in-IDE. Report recommendations 16, 17, 18, 21.

## 4.1 Core / SqlServer.Adapter split (rec 16)
- Move `Sources/SqlServerParsers.cs` (incl. `RawSqlParser`, `SqlServerStoredProcedureParser`, `SqlServerTableReader`), `SqlServerLiveQuerySchemaProvider`, `SqlServerTypeCompatibility` to `src/DataGuard.SqlServer.Adapter/` keeping namespaces `DataGuard.Core.Sources`/`DataGuard.Core.Rules` for binary compatibility of type names? **No**: use `DataGuard.SqlServer.Adapter` namespace and add `[TypeForwardedTo]`-free migration notes; `BinaryCompatibilityFixture` is updated accordingly.
- Core csproj drops `Microsoft.Data.SqlClient`, `Microsoft.SqlServer.TransactSql.ScriptDom`. `AWSSDK.SecretsManager`: `AwsSecretsManagerSecretStore` moves to `DataGuard.Cli/Security/` (only the CLI composes cloud stores); Core keeps the `ISecretStore` abstraction. If a Core test depends on the AWS store, it moves with it.
- Adapters' csproj: remove the copied Core package list; keep only their driver + `ProjectReference` Core.
- Regenerate every `packages.lock.json` (`dotnet restore --force-evaluate`), update `Dockerfile` COPY list, `DataGuard.sln`/`slnf` unchanged.
- `ColumnDescriptor.CharUsed`, `StoredProcedureDescriptor.PackageName/ReturnsRefCursor` stay (they are now used by the matcher) but are documented as dialect-optional; `Location`/`DiagnosticSeverity` remain Roslyn types (accepted; the analyzer package shares them).

## 4.2 Single rule composition (rec 17)
- `RuleDependencyGraph`: `RegisterRule` throws on a different rule with the same ID; `WithDependency` registers a null placeholder, never `DummyRule`; dependencies iterate in `Ordinal` order; `DummyRule` deleted.
- `ValidationPipeline.WithProviderRules(IEnumerable<IContractRule>)` and `ProviderRuleCatalog` becomes the single list builder; CLI `ValidateContractsAsync` runs through `ValidationPipeline` (same executor, same outcomes). `PublicApiAndPipelineTests` asserts CLI and API rule sets are identical for each provider.
- Plugins: `RulePluginManager` reads `ExportRuleAttribute` metadata; manifest `RuleId` must equal runtime `RuleId`; CLI gains `--plugins-dir` (admission enforced, never under `--ide-safe`); a test loads a real plugin assembly built in-test and asserts it runs.
- `IDialectAnalyzer` either gets a caller (dialect rules delegate to it) or is removed; decision: rules delegate to it so the interface is real.

## 4.3 Analyzer rework (rec 18)
- `ContractValidationAnalyzer` ⇒ `RegisterSyntaxNodeAction(InvocationExpression)`; no `IOperation`, no `SemanticModel`; keeps regex heuristics on literal arguments only.
- `UnvalidatedSqlCallGenerator`: model record `(string Path, int Start, int Length, string Method, SqlCallType Kind, string Sql)` (equatable, no `Location`); `FromSqlRaw`/`FromSqlInterpolated` detected syntactically (method name on any receiver); Dapper detection requires a string/interpolated first argument; `Execute*` without SQL text is not flagged.
- Analyzer IDs no longer collide with engine IDs: analyzer-only diagnostics renumber to `DG09x` where they differ in meaning (`DG002` "must start with EXEC" ⇒ `DG097`), with `DiagnosticDescriptors` as the single source and a test that engine `RuleTitles` and analyzer descriptors agree on shared IDs.
- Benchmark (`tools/benchmarks`) adds a keystroke-latency case for the generator.

## 4.4 Credentials and audit (rec 21)
- CLI: when `--connection` is absent, resolve via `ZeroTrustCredentialProvider` (env → secret store → encrypted file) honoring `AllowPlaintextConfigFallback`; new `--connection-env NAME`; `--connection` warns `credential on argv is visible to process listings; prefer --connection-env`.
- `EncryptConnectionStringAtRest` default `true` on supported OSes.
- `FileAuditLogger`: HMAC-SHA256 chain when `DATAGUARD_AUDIT_KEY` (or configured key file) is present; `CredentialManager.LogAuditAsync` writes through the same logger (single writer); `VerifyIntegrityAsync` test covers mixed writers.
- `MaskValue` keeps at most 4 characters; connection-string hash salted per file.
- Manual mode: `Assembly.LoadFrom` ⇒ `MetadataLoadContext` (reflection-only) for attribute reading; if a path comes from a repo YAML, require `--allow-assembly-from-config`.

## Acceptance
- `DataGuard.Core.csproj` has no SqlClient/ScriptDom/AWSSDK reference; `dotnet pack` of Core succeeds; all lock files regenerate without diff on a second restore.
- Plugin round-trip test passes; graph duplicate test passes.
- Analyzer tests (Phase 2) still pass with the syntax-only analyzer.
