# Configure snapshot analysis deliberately

Use the tested [minimal Oracle snapshot example](oracle-snapshot.minimal.yml) or [complete commented example](oracle-snapshot.full.yml). The full example comes from `dataguard init --provider oracle`. Both are regression-tested with production `ConfigLoader`/`ConfigDocument`, the executable configuration schema. `config validate` checks binding and provider values; it does not prove snapshot existence, freshness, or correctness.

## Reproduced configuration problem

`Oracle: true` and `SqlServer: false` are invalid: those keys hold optional **mappings**, not provider switches. Previously, typed binding failed and a lossy fallback silently discarded them (and could lose lists/nested settings) while retaining `DefaultProvider: sqlserver`. `config validate` incorrectly exited 0. That selects T-SQL and reproduces DG019 on Oracle-style UPDATE/DELETE aliases. The corrected loader rejects malformed known shapes with exit 2 and an explanation, without guessing a provider.

Provider precedence is `--provider` → `DefaultProvider` → `sqlserver`. Neither `GroundTruthMode: Snapshot` nor `AutoDetectProvider: true` overrides it. `config validate` displays the effective configured provider and warns about unused provider-specific blocks. Valid `Oracle: {}` with `DefaultProvider: sqlserver` still selects SQL Server. The wizard has a separate project-evidence detection step. Current generators serialize objects/nulls; no evidence establishes that the current CLI or Visual Studio extension generated the reported boolean form.

For a confirmed Oracle snapshot:

```yaml
GroundTruthMode: Snapshot # Schema source, not SQL dialect selection.
DefaultProvider: oracle # --provider overrides this.
SnapshotFilePath: .dataguard-snapshot.json # Existing, reviewed Oracle snapshot.
EnableBaseline: false # Review findings before deliberately freezing a baseline.
AllowPlaintextConfigFallback: false # No credentials in YAML.
EnableTelemetry: false # Local observability is opt-in.
StrictConfig: true # Unknown top-level keys fail.
```

Run from the solution root:

```sh
dataguard config validate --config .dataguard.yml
dataguard validate --offline --config .dataguard.yml --project .
```

Snapshot mode reads persisted schema without a resolved connection. It is **not** an offline switch: use `--offline`; IDE hosts separately apply `--ide-safe`. Snapshot provider metadata must match the effective provider. `init` creates YAML only, not schema or baseline data. Obtain a reviewed snapshot through the established workflow; refreshing against a database is a separate explicitly authorized live action. Do not fabricate a snapshot to make checks pass.

Oracle raw SQL grammar is unavailable. Correct configuration avoids T-SQL parsing but Oracle syntax remains **unevaluated**, normally exit 3. `--allow-unevaluated` accepts incomplete results, not successful validation. Other supported Oracle rules still execute. See [DG019 guidance](dg019-dialect-and-locations.md). This change adds no regex alias exception, SQL rewriting, live execution, or blanket Oracle validation.

## Settings and best practices

| Settings | Meaning and limits |
|---|---|
| `Oracle`, `SqlServer` | Omit unused blocks or use null. Examples: `Oracle: { Owner: APP, DescribeRefCursors: false }`, `SqlServer: { Schema: dbo }`. Blocks do not select the provider. REF CURSOR execution is opt-in; keep false for snapshot work. |
| Baseline | `EnableBaseline` applies a reviewed `BaselineFilePath` only if present. Baseline suppressions differ from schema snapshots; no file is created automatically. Do not baseline a provider error. |
| Exclusions | `ExcludedEntities` uses exact CLR names in EF extraction, not globs/global SQL suppression. `ExcludedProcedures` is currently reserved and not consumed by CLI acquisition. Use YAML lists (`[]` or block lists). |
| Concurrency | `MaxDegreeOfParallelism: 0` means processor-count concurrency, not disabled/unlimited. `EnableConcurrentValidation: false` is sequential. `MaxViolationQueueSize` overflow marks validation incomplete. IDE-safe caps excessive resource settings. |
| Timeout | `ValidationTimeoutSeconds` is persisted but the current CLI does not enforce it as a deadline. Visual Studio has a separate timeout option. Do not treat this YAML field as a safety boundary. |
| Auto-detection | `AutoDetectProvider`/`EnableSmartDefaults` are library hints, not CLI dialect overrides. `AutoDetectDapper` is currently a placeholder; extraction detects supported call names independently. `AutoDetectEFContext` can parse design-time ModelSnapshot.cs under an IDE-safe project without running DbContext or loading assemblies. |
| Names | `DefaultSchema`/`DefaultPackage` resolve unqualified procedure names; `NamingConvention` controls matching. Do not copy `dbo` into Oracle unless it is the real intended schema. |
| Credentials | Snapshots need no credentials in YAML. Leave ConnectionString, KeyVaultUri, AwsRegion, VaultAddress and ManualAssemblyPath empty unless a separately chosen workflow needs them. For authorized live work prefer `--connection-env NAME`; never commit or log secrets. Secret-store backend support varies. |
| Encryption | `EncryptConnectionStringAtRest` protects the local credential store, **not YAML**. Defaults enable OS protection; `RequireEncryptedCredentialStore` controls fallback when protection is unavailable. Keep `AllowPlaintextConfigFallback: false`. Disabling encryption is unnecessary for offline snapshots. |
| Rotation/audit | Rotation settings apply to credential management. Audit is separate from telemetry; empty AuditLogPath uses the logger's application-data default. Optional integrity keys stay outside the repository. Offline/IDE-safe runs do not instantiate the live credential-provider chain. |
| Telemetry | Opt-in local observability with service/version labels and optional redacted bounded details. Keep disabled/details false unless intentionally needed. IDE-safe suppresses repository-directed telemetry output. |

For HashiCorp Vault, set the trusted `VAULT_ADDR` in the operator/CI environment as well as `VAULT_TOKEN`. The YAML `VaultAddress` must match that HTTPS origin (host and port); both addresses must be roots without user information, a path, query or fragment. A trailing slash and explicit port 443 are equivalent to their defaults; matching custom HTTPS ports are supported. Missing or mismatched trust is rejected before reading the token or sending a request. This intentionally tightens older configurations that supplied only `VaultAddress` and `VAULT_TOKEN`.

Azure `KeyVaultUri` must be an HTTPS root at `name.vault.azure.net`, using the default HTTPS port, with no user information, query or fragment. The built-in credential-provider HTTP client does not follow redirects. Hosts constructing a secret store with their own `HttpClient` must also disable automatic redirects. Secret-store failures remain warnings with the store name and exception type; arbitrary exception messages are omitted because they may contain credentials. These endpoint checks do not establish ownership of a hostname.

`init` and `init --wizard` share the commented writer and refuse an existing output file, preserving handwritten comments. Edit it in place or generate to a new `--output` and review the diff. `config validate`, `config show`, and validation do not rewrite YAML. Programmatic serialization regenerates canonical comments; arbitrary handwritten comments are not part of the configuration object and are not roundtripped by that API.

## Visual Studio settings are separate

**Live Provider = oracle** applies only to Refresh Snapshot and Verify SQL Shapes Against Database. Run Validation passes the solution's `.dataguard.yml` and `--ide-safe`, without `--provider`; it therefore uses YAML `DefaultProvider`. An empty live environment-variable name blocks live commands; it does not select a validation dialect. Live Schema is likewise separate. A configured **Custom CLI Path** takes precedence over the bundled CLI, so installing a new VSIX does not prove that validation used its new CLI. Check the executable reported in StackAccord output and its version; no credential value is needed.

Credential-boundary synthetic regression scope and compatibility evidence: [local reconciliation plan](../../plans/2026-10-16-credential-reconciliation.md).
