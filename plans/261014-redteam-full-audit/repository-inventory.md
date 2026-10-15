# Repository Inventory — `eco_support_net_oracle` (DataGuard)

- **Audit Date**: 2026-10-14
- **Target Repository**: `D:/100.Software/Github/eco_support_net_oracle`
- **Branch / Commit**: `main` @ `1e5a5922aeda4116a2198f56fe4ef3dd326330ee` (`Merge pull request #56 from thanhnt-sm/dependabot/github_actions/actions-26efc1942a`)
- **Target Framework / SDK**: `net9.0` (`netstandard2.0` for Contracts/Analyzers/CodeFixes/SqlClassification; `net472` for Visual Studio VSIX), pinned via `global.json` (`9.0.100`, `rollForward: latestFeature`, resolved locally to `9.0.317` at `C:\Users\thant\.dotnet\sdk\9.0.317`).
- **License Surface**: Dual-licensed `GPL-3.0-only` + Commercial (from v0.4.0), with `DataGuard.Contracts` under `MIT` and §7 additional permissions in `docs/legal/ADDITIONAL-PERMISSIONS.md`.

---

## 1. Uncommitted Working Tree State (Preserved Read-Only)

| Status | Path | Purpose / Context |
|---|---|---|
| `M` | `plans/261014-0750-ide-extension-db-phase/plan.md` | Deferred follow-up validation log for IDE extensions |
| `M` | `src/DataGuard.LanguageServer/Program.cs` | JSON-RPC `shutdown`, `exit`, and `-32601 MethodNotFound` handling |
| `M` | `src/DataGuard.VSCode/src/command-args.ts` | Removed unsupported `--provider` from `assess` argv |
| `M` | `src/DataGuard.VSCode/src/command-args.test.ts` | Unit tests for `assess` argv |
| `M` | `src/DataGuard.VSCode/src/extension.ts` | Child environment scrubbing & exit code / policy line parity |
| `M` | `src/DataGuard.VSCode/src/ide-safe-contract.ts` | `GroundTruthMissing`, `UNEVALUATED:`, `ACQUISITION:`, `buildChildEnvironment` |
| `M` | `src/DataGuard.VSCode/src/ide-safe-contract.test.ts` | Contract unit tests for IDE-safe mode |
| `M` | `src/DataGuard.VisualStudio/BindingRedirects.cs` | `System.Text.Json` binding redirect to `10.0.0.13` |
| `??` | `tests/DataGuard.Core.Tests/LanguageServerLifecycleTests.cs` | LSP lifecycle tests (`shutdown`/`exit`/`MethodNotFound`) |
| `??` | `docs/journals/261014-extension-ai-llm-provider-audit-brainstorm.md` | Technical journal entry |
| `??` | `docs/journals/261014-ide-extensions-deferred-items-and-vsix.md` | Technical journal entry |
| `??` | `plans/reports/brainstorm-261014-extension-ai-llm-provider-audit.md` | Brainstorm report |

---

## 2. Production Source Projects (`src/` — 18 Projects)

| # | Project / Path | TFM | Output / Packaging | Core Responsibility |
|---|---|---|---|---|
| 1 | `src/DataGuard.Contracts` | `netstandard2.0` | NuGet (`MIT`) | Public contract attributes (`[StoredProcedureContract]`, `[SqlParameterContract]`, `[TableContract]`, `[ColumnContract]`) and `NameConventions`. Zero external runtime dependencies. |
| 2 | `src/DataGuard.SqlClassification` | `netstandard2.0` | Internal / Lib | `SqlTokenizer`, `SqlClassifier`, `CSharpStringLiteralScanner` shared across Core and Roslyn Analyzers. |
| 3 | `src/DataGuard.Core` | `net9.0` | NuGet (`GPL-3.0-only`) | Validation pipeline, 24+ contract/dialect/security rules, `EfModelSource`, `ProjectCSharpSqlSource`, `BaselineManager`, `ZeroTrustCredentialProvider`, `RulePluginManager`, SARIF/Markdown/JSON reporting, `AutoDetectionEngine`. |
| 4 | `src/DataGuard.Oracle.Adapter` | `net9.0` | NuGet (`GPL-3.0-only`) | Oracle catalog readers (`ALL_ARGUMENTS`, `ALL_TAB_COLUMNS`, `ALL_SOURCE`), PL/SQL dialect checker, `VARCHAR2(N BYTE/CHAR)` length mismatch (`DG007`–`DG009`), `OracleLiveQuerySchemaProvider`, `OracleTypeCompatibility`. |
| 5 | `src/DataGuard.SqlServer.Adapter` | `net9.0` | NuGet (`GPL-3.0-only`) | `Microsoft.SqlServer.TransactSql.ScriptDom` AST parser, `TSqlPhantomAnalyzer` (`DG015`/`DG016`), `SqlServerParsers`, `SqlServerLiveQuerySchemaProvider`, `SqlServerTypeCompatibility`. |
| 6 | `src/DataGuard.PostgreSql.Adapter` | `net9.0` | NuGet (`GPL-3.0-only`) | PostgreSQL stored procedure/function parser, `PostgreSqlDialectChecker` (`PG001`–`PG005`), `PostgreSqlLengthMismatchDetector`, `PostgreSqlLiveQuerySchemaProvider`. |
| 7 | `src/DataGuard.MySql.Adapter` | `net9.0` | NuGet (`GPL-3.0-only`) | MySQL routine parser, `MySqlDialectChecker` (`MY001`–`MY005`), `MySqlLengthMismatchDetector`, `MySqlLiveQuerySchemaProvider`. |
| 8 | `src/DataGuard.Analyzers` | `netstandard2.0` | NuGet Analyzer | Roslyn `DiagnosticAnalyzer` (`ContractValidationAnalyzer`, `UnvalidatedSqlCallGenerator`, `SqlCallSyntax`, `TypeShapeIndex`), pinned to `Microsoft.CodeAnalysis.CSharp 4.12.0`. |
| 9 | `src/DataGuard.CodeFixes` | `netstandard2.0` | NuGet Analyzer | Roslyn `CodeFixProvider` implementations for `DG001`, `DG017`, `DG096`, `DG097`, `DG099`, `DG100`, pinned to Roslyn `4.12.0`. |
| 10 | `src/DataGuard.Cli` | `net9.0` | Dotnet Tool (`dataguard`) | CLI entrypoint (`validate`, `snapshot`, `baseline`, `assess`, `verify-shape`, `init`, `rules`, `forget-credentials`), `IdeSafePolicy`, `ProviderRuleCatalog`, `OfflineManifestWriter`. |
| 11 | `src/DataGuard.Build` | `net9.0` | NuGet MSBuild Task | `OfflineManifestValidationTask` (`DG_BUILD001`–`DG_BUILD004`) enforcing offline manifest digests during MSBuild. |
| 12 | `src/DataGuard.LanguageServer` | `net9.0` | Executable | Lightweight stdio JSON-RPC Language Server emitting `DG017`/`DG098` diagnostics from `SqlClassifier`. |
| 13 | `src/DataGuard.Host` | `net9.0` | ASP.NET Core App | Health check HTTP host (`/healthz/live`, `/healthz/ready`, `HealthRefreshService`, loopback/explicit binding guard). |
| 14 | `src/DataGuard.Observability` | `net9.0` | NuGet (`GPL-3.0-only`) | OpenTelemetry metrics/tracing (`CoreObservability`), PII/SQL tag sanitization, local JSONL file exporter. |
| 15 | `src/DataGuard.Observability.AspNetCore` | `net9.0` | NuGet (`GPL-3.0-only`) | ASP.NET Core request/exception observability middleware & enrichment. |
| 16 | `src/DataGuard.Observability.Messaging` | `net9.0` | NuGet (`GPL-3.0-only`) | W3C traceparent propagation (`W3CMessagePropagation`) and messaging operation metrics. |
| 17 | `src/DataGuard.VisualStudio` | `net472` | VSIX (`vsix`) | Visual Studio 2022 extension (`17.12`–`19.0`), `SolutionTrustGate`, `ProgressStreamReader`, `SarifErrorListPublisher`, bundled self-contained CLI + Roslyn 4.12 analyzers. |
| 18 | `src/DataGuard.VSCode` | Node 22 / TS | VSIX (`vsce`) | VS Code extension (`extension.ts`, `command-args.ts`, `ide-safe-contract.ts`, `live-database-confirmation.ts`, Webview dashboard, CodeLens, Hover, Decorations). |

---

## 3. Test, Benchmark & Sample Projects (11 Projects + 2 Script Suites)

| # | Path | TFM / Runtime | Included in `DataGuard.sln` | Included in `CrossPlatform.slnf` | Test Count (Baseline) |
|---|---|---|---|---|---|
| 1 | `tests/DataGuard.Core.Tests` | `net9.0` | Yes | Yes | 1,802 xUnit tests (non-LiveDb) + 5 LiveDb |
| 2 | `tests/DataGuard.Analyzers.Tests` | `net9.0` | Yes | Yes | 98 xUnit tests |
| 3 | `tests/DataGuard.GoldenCorpus.Tests` | `net9.0` | Yes | Yes | 61 xUnit tests |
| 4 | `tests/DataGuard.CodeFixes.Tests` | `net9.0` | Yes | Yes | 38 xUnit tests |
| 5 | `tests/DataGuard.Observability.Tests` | `net9.0` | Yes | Yes | 38 xUnit tests |
| 6 | `tests/DataGuard.VisualStudio.Tests` | `net472` | Yes | No (Windows-only execution) | 267 xUnit tests (266 pass, 1 skip) |
| 7 | `tests/DataGuard.BinaryCompatibilityFixture` | `net9.0` | No (Standalone CI check) | No | Compile-only API consumer gate (**FAILS locked restore: `NU1004`**) |
| 8 | `tools/benchmarks/DataGuard.Benchmarks` | `net9.0` | No | No | BenchmarkDotNet suite (**FAILS locked restore: `NU1004`**) |
| 9 | `tools/benchmarks/DataGuard.Observability.Benchmarks` | `net9.0` | No | No | BenchmarkDotNet suite (**FAILS locked restore: `NU1004`**) |
| 10 | `samples/DataGuard.Sample` | `net9.0` | No | No | Sample consumer project (**FAILS locked restore: `NU1004`**) |
| 11 | `samples/plugins/DataGuard.Samples.NamingPlugin` | `net9.0` | No | No | Sample plugin project (**FAILS locked restore: `NU1004`**) |
| 12 | `src/DataGuard.VSCode` (`npm test`) | Node `node:test` | N/A | N/A | 101 TypeScript unit tests (101 pass) |
| 13 | `scripts/tests` (`unittest`) | Python 3 | N/A | N/A | 117 Python unit tests (111 pass, 6 fail) |

---

## 4. CI/CD Workflows & Operational Scripts

- **GitHub Actions Workflows (`.github/workflows/`)**:
  1. `ci.yml` — Main CI pipeline (`build-and-test`, `live-db-integration`, `windows-vs-extension`, `vscode-extension`, `scripts-tests`, `docker-smoke`, `codeql`, `trufflehog`, `dependency-review`).
  2. `release.yml` — Tag-triggered release pipeline (NuGet pack, Sigstore cosign v3.1.3 signing, GitHub attestations, VSIX build/sign, GitHub Release creation).
  3. `marketplace.yml` — VS Code Marketplace & Open VSX publishing.
  4. `installers.yml` — Nightly/release standalone CLI bundle & installer generation with Sigstore provenance.
  5. `scorecard.yml` — OpenSSF Scorecard security scan & SARIF upload.
  6. `standards-audit.yml` — Repository governance & standards verification.
  7. `build_release.yml` / `release-provenance-backfill.yml` — Auxiliary release/provenance workflows.
- **Key Operational Scripts (`scripts/`)**:
  - `check-workflow-policy.py`, `check-nuget-licences.py`, `check-license-consistency.py`, `gen_rule_table.py`, `trx_to_junit.py`, `verify_docs_sync.sh`, `assert-roslyn-lock.sh`, `verify-analyzer-packaging.sh`, `assert-vsix.ps1`, `verify_local_gates.sh`.
