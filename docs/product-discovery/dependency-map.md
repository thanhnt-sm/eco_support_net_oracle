# Dependency map

## Language-server availability

LSP status request for `src/DataGuard.Cli/Program.cs` returned only `rust-analyzer`, `pyright`, and `ruff`; no C# language server is configured. Therefore `symbols`, `references`, and `definition` cannot supply C# evidence in this workspace. The build graph below is derived from declared MSBuild `ProjectReference` items and `DataGuard.sln`.

## Build graph

| Project | Project references | Major package dependencies |
|---|---|---|
| `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj` | `../DataGuard.Contracts/DataGuard.Contracts.csproj` | `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Analyzers`, `System.Composition.Hosting` |
| `src/DataGuard.Cli/DataGuard.Cli.csproj` | `../DataGuard.Core/DataGuard.Core.csproj`, `../DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj`, `../DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj`, `../DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj`, `../DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj`, `../DataGuard.Analyzers/DataGuard.Analyzers.csproj` | `System.CommandLine` |
| `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj` | `../DataGuard.Analyzers/DataGuard.Analyzers.csproj` | `Microsoft.CodeAnalysis.CSharp.Workspaces` |
| `src/DataGuard.Contracts/DataGuard.Contracts.csproj` | none | none |
| `src/DataGuard.Core/DataGuard.Core.csproj` | `../DataGuard.Contracts/DataGuard.Contracts.csproj` | `AWSSDK.SecretsManager`, `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Data.SqlClient`, `Microsoft.SqlServer.TransactSql.ScriptDom`, `System.Text.Json` |
| `src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj` | `../DataGuard.Core/DataGuard.Core.csproj` | `AWSSDK.SecretsManager`, `Microsoft.CodeAnalysis.Analyzers`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.Data.SqlClient`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Configuration` |
| `src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj` | `../DataGuard.Core/DataGuard.Core.csproj` | `AWSSDK.SecretsManager`, `Microsoft.CodeAnalysis.Analyzers`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.Data.SqlClient`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Configuration` |
| `src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj` | `../DataGuard.Core/DataGuard.Core.csproj` | `AWSSDK.SecretsManager`, `Microsoft.CodeAnalysis.Analyzers`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.Data.SqlClient`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Configuration` |
| `src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj` | `../DataGuard.Core/DataGuard.Core.csproj` | `AWSSDK.SecretsManager`, `Microsoft.CodeAnalysis.Analyzers`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder` |
| `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` | none | `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `System.Text.Json` |

## Verified seams

| Seam | Evidence |
|---|---|
| CLI command registration | `src/DataGuard.Cli/Program.cs`: `RootCommand`, command construction and `rootCommand.AddCommand(...)`. |
| Programmatic composition | `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`: `DataGuardApi`, `ValidationPipeline`, `DataGuardFactory`. |
| Rule extension | `src/DataGuard.Core/Abstractions/Contracts.cs`: `IContractRule`; `src/DataGuard.Core/Plugins/RulePluginManager.cs`: MEF rule discovery and `ExportRuleAttribute`. |
| Structured reporting | `src/DataGuard.Core/Reporting/SarifTypes.cs`: SARIF 2.1.0 model; `src/DataGuard.Core/Abstractions/Contracts.cs`: `ContractViolation`. |
| Configuration | `src/DataGuard.Cli/Program.cs`: `.dataguard.yml` option and `DeserializeConfig`; `src/DataGuard.Core/Models/Configuration.cs`: configuration model. |
| Test boundary | `tests/DataGuard.Core.Tests`, `tests/DataGuard.GoldenCorpus.Tests`, `tests/DataGuard.Analyzers.Tests` listed by `DataGuard.sln`. |

## Public-symbol analysis limitation

The source inventory contains declaration-anchored public symbols. Cross-reference evidence is blocked until a C# LSP server is configured; no text-based substitute is presented as LSP evidence.
