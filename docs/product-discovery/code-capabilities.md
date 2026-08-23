# DataGuard: Code Capability Evidence

Scope: implemented seams only; no recommendations. Every claim cites path + symbol.

## Delivery surface

| Surface | Evidence |
|---|---|
| CLI commands: `validate`, `baseline`, `snapshot refresh/show/diff`, `init`, `config show/validate`, `oracle-check`, `migrate`, `version` | `src/DataGuard.Cli/Program.cs`: `validateCommand`, `baselineCommand`, `snapshotCommand` (`snapshotRefreshCommand`, `snapshotShowCommand`, `snapshotDiffCommand`), `initCommand`, `configCommand` (`configShowCommand`, `configValidateCommand`), `oracleCheckCommand`, `migrateCommand`, `versionCommand`; registration via `rootCommand.AddCommand(...)`. |
| Programmatic .NET API | `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`: `DataGuardApi.CreatePipeline`; `ValidationPipeline.WithRules/WithPlugins/WithTelemetry/WithBaselineFile/ValidateAsync/CreateBaselineAsync/LoadBaselineAsync/CheckDriftAsync`. |
| Roslyn generator/diagnostics | `src/DataGuard.Analyzers/Analyzers.cs`: `UnvalidatedSqlCallGenerator.Initialize`, `DiagnosticIds`, `DiagnosticDescriptors`. |
| VS Code extension | `src/DataGuard.VSCode/src/extension.ts`: `activate`, `runValidation`, `cancelValidation`, `loadDiagnostics`; `src/DataGuard.VSCode/package.json`: `contributes.commands`, `contributes.configuration`. |
| Visual Studio package | `src/DataGuard.VisualStudio/DataGuardPackage.cs`: `InitializeAsync`, `RunValidationAsync`, `CancelValidationAsync`. |
| Git hook generators | `src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs`: `PreCommitHookInstaller`, `GenerateHuskyHook`, `GenerateLefthookConfig`, `GenerateNativeGitHook`. |

## Inputs and rules

- Contract sources: `src/DataGuard.Core/Abstractions/Contracts.cs`: `IContractSource`; implementations `src/DataGuard.Core/Sources/EfModelSource.cs`: `EfModelSource`, `src/DataGuard.Core/Sources/ManualContractSource.cs`: `ManualContractSource`, `src/DataGuard.Core/Sources/SqlServerParsers.cs`: `SqlServerStoredProcedureParser`, `RawSqlParser`.
- Rule model: `src/DataGuard.Core/Abstractions/Contracts.cs`: `IContractRule`, `ContractDescriptor`, `ContractType`; ordering `src/DataGuard.Core/Rules/RuleDependencyGraph.cs`: `RuleDependencyGraph`, `BuiltInRuleDependencies.CreateDefault`.
- Built-in rules: `src/DataGuard.Core/Rules/ContractRules.cs`: `ParameterCountRule`, `ParameterTypeMatchRule`, `ParameterDirectionRule`, `ColumnShapeMatchRule`, `NullableMismatchRule`, `NamingConventionRule`.
- Provider-specific seams: `src/DataGuard.Oracle.Adapter/LengthMismatch.cs`: `LengthExceedsColumnRule`, `ByteLengthOverflowRiskRule`, `InferredSizeFallbackRule`; `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs`: `OracleDialectChecker`; `src/DataGuard.MySql.Adapter/MySqlDialectChecker.cs`, `src/DataGuard.MySql.Adapter/MySqlLengthMismatchDetector.cs`; PostgreSQL equivalents in `src/DataGuard.PostgreSql.Adapter/`.

## Extension, config, state

- Plugin discovery (MEF): `src/DataGuard.Core/Plugins/RulePluginManager.cs`: `RulePluginManager`, `ExportRuleAttribute`, `GetAllRules`.
- Config/state: `src/DataGuard.Cli/Program.cs`: `LoadConfig`, `DeserializeConfig`, `SerializeConfig`, `initCommand`; `src/DataGuard.Core/Models/Configuration.cs`: `DataGuardConfiguration`, `GroundTruthMode`, `OracleConfiguration`, `SqlServerConfiguration`.
- Baseline persistence/migration: `src/DataGuard.Core/Baseline/BaselineManager.cs`: `CreateBaselineAsync`, `LoadAsync`, `FilterNewViolations`, `ComputeSchemaHash`, `MigrateBaselineAsync`.

## Reporting

- Text/SARIF sinks: `src/DataGuard.Core/Reporting/DiagnosticEmitter.cs`: `EmitAsync`, `FileSarifSink`, `ConsoleDiagnosticSink`; SARIF model `src/DataGuard.Core/Reporting/SarifTypes.cs`: `SarifLog`.
- Evidence/export writers: `src/DataGuard.Core/Reporting/ContractEvidence.cs`: `ContractEvidenceWriter.WriteAsync`; `src/DataGuard.Core/Reporting/ContractExport.cs`: `ContractExportWriter.WriteJsonAsync`, `TypeScriptContractWriter.WriteAsync`.
- Violation type: `src/DataGuard.Core/Abstractions/Contracts.cs`: `ContractViolation`.

## Observability and error handling

- Telemetry (opt-in): `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`: `WithTelemetry`, `ValidateAsync`; `src/DataGuard.Core/Telemetry/TelemetryCollector.cs`: `TelemetryCollector`, `RecordValidationSummary`, `FlushAsync`, `MaxConsecutiveExportFailures` (disabled returns immediately; 3-failure circuit breaker).
- Audit logging: `src/DataGuard.Core/Security/IAuditLogger.cs`: `FileAuditLogger`, `NullAuditLogger`.
- Credential resolution: `src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs`: `GetCredentialAsync`, `ResolveCredentialAsync`.
- CLI error paths: `src/DataGuard.Cli/Program.cs`: `validateCommand` validates format/output and prints verbose stack traces only with `--verbose`.
- VS Code safety behavior: `src/DataGuard.VSCode/src/extension.ts`: `runValidation`, `deactivate` handle trusted workspace, bounded timeout, concurrency, temp cleanup.

## Test conventions

- `tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj`: xUnit + FluentAssertions + Moq.
- Example style: `tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs`: `PublicApiAndPipelineTests`, `ValidationPipeline_FluentConfiguration_ChainsCorrectly` (`[Fact]`, async, temp-dir try/finally cleanup).
- Analyzer tests: `tests/DataGuard.Analyzers.Tests/` project; corpus tests: `tests/DataGuard.GoldenCorpus.Tests/GoldenCorpusTests.cs`: `GoldenCorpusTests`.
