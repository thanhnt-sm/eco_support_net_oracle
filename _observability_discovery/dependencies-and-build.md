# Dependencies and Build Metadata

## Target frameworks and project shape

- **[CONFIRMED]** Default TFM is `net9.0`; compatibility libraries target `netstandard2.0`; Visual Studio integration targets `.NETFramework,Version=v4.7.2` (`Directory.Build.props:1-41`; project files; lock metadata).
- **[CONFIRMED]** 22 visible `.csproj` files were enumerated. 17 are represented as concrete solution project entries; samples, one benchmark path, Visual Studio, BinaryCompatibilityFixture and duplicate tools benchmark are outside the solution entry set (see inventory).
- **[CONFIRMED]** 21 visible `packages.lock.json` files are syntactically valid JSON. Every visible project has a lock file; lock targets align with project TFM families.

## Direct dependency families

- **[CONFIRMED]** Core/adapters use Microsoft.CodeAnalysis, EF Core 9, Microsoft.Data.SqlClient, provider-specific drivers, `System.Text.Json`, Microsoft.Extensions diagnostics/logging, YamlDotNet and AWS Secrets Manager as applicable.
- **[CONFIRMED]** CLI uses System.CommandLine and references Core/adapters/analyzers; Host references Core; LanguageServer references SqlClassification; Build uses Microsoft.Build.Utilities.Core; benchmark uses BenchmarkDotNet.
- **[CONFIRMED]** Directory-level references centralize Microsoft.CodeAnalysis.NetAnalyzers 10.0.400, StyleCop.Analyzers 1.1.118, SourceLink 10.0.400 and MinVer 7.0.0 with private assets (`Directory.Build.props:20-41`).

Representative versions read from project metadata (not an online support assessment):

- **[CONFIRMED]** Roslyn packages `5.9.0`; EF Core/Relational `9.0.19`; Microsoft.Data.SqlClient `7.0.2`; System.Text.Json and Microsoft.Extensions packages `10.0.11`.
- **[CONFIRMED]** Provider drivers: MySqlConnector `2.6.2`, Oracle.ManagedDataAccess.Core `23.26.300`, Npgsql `10.0.3`.
- **[CONFIRMED]** CLI System.CommandLine `2.0.11`; YamlDotNet `18.1.0`; AWS Secrets Manager `4.0.100.11`; BenchmarkDotNet `0.15.8`.
- **[CONFIRMED]** Visual Studio integration uses SDK `17.14.40265` and BuildTools `18.5.38461`; exact package applicability is constrained by `net472`.

## Lock-file inventory (direct/transitive counts)

| Project | Direct | Transitive | TFM |
|---|---:|---:|---|
| Core | 21 | 53 | net9.0 |
| CLI | 5 | 76 | net9.0 |
| Analyzers | 8 | 14 | netstandard2.0 |
| CodeFixes | 6 | 26 | netstandard2.0 |
| Contracts | 5 | 8 | netstandard2.0 |
| SQL Server adapter | 15 | 59 | net9.0 |
| Oracle adapter | 16 | 62 | net9.0 |
| PostgreSQL adapter | 16 | 59 | net9.0 |
| MySQL adapter | 16 | 59 | net9.0 |
| Host | 4 | 70 | net9.0 |
| Build | 5 | 9 | net9.0 |
| LanguageServer | 4 | 3 | net9.0 |
| SqlClassification | 5 | 8 | netstandard2.0 |
| VisualStudio | 8 | 125 | net472 |
| Core.Tests | 16 | 100 | net9.0 |
| GoldenCorpus.Tests | 13 | 95 | net9.0 |
| Analyzers.Tests | 10 | 24 | net9.0 |
| CodeFixes.Tests | 11 | 33 | net9.0 |
| BinaryCompatibilityFixture | 4 | 70 | net9.0 |
| Sample | 4 | 3 | net9.0 |
| Benchmark (root) | 5 | 87 | net9.0 |
| Benchmark (tools) | 5 | 87 | net9.0 |

Counts are package-lock metadata counts, not vulnerability conclusions.

## Verification and limitations

- **[CONFIRMED]** Read-only XML validation of all visible `.csproj` files exited 0; `jq` validation of all visible lock/package JSON exited 0; workflow actionlint exited 0.
- **[CONFIRMED]** No restore/build/test/format command was run due the discovery contract. Therefore compile status, test pass rate, coverage and runtime dependency resolution are not asserted.
- **[UNVERIFIED_EXTERNAL]** Package support lifecycle, CVEs, transitive advisories and license posture were not queried externally. CI contains a vulnerable-package gate but its result is not part of this snapshot.
