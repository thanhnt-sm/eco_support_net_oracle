# DataGuard — Contract Validation for Entity ↔ Stored Procedure / Raw SQL

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/thanhnt-sm/eco_support_net_oracle/actions)
[![OSS Scorecard](https://api.scorecard.dev/repos/github.com/thanhnt-sm/eco_support_net_oracle/badge)](https://scorecard.dev/viewer/?uri=github.com/thanhnt-sm/eco_support_net_oracle)

**DataGuard** detects drift between your .NET entities and the SQL they depend on — stored procedure parameters, result-set shapes, nullability, length semantics, and dialect mismatches — at design time and in CI.

> **Why it exists:** EF Core has tracked the gap for stored-procedure contract validation since [Microsoft EF issue #245 (2014)](https://github.com/dotnet/efcore/issues/245) and declined to build it. DataGuard ports the *model contracts* pattern that **dbt** proved out for data engineering (preflight column/parameter checks at compile time, since Core v1.5, 2023) to the .NET stored-procedure world.

## Quickstart

```bash
dotnet tool install -g DataGuard.Cli
cd YourProject
dataguard init            # writes .dataguard.yml + .dataguard-snapshot.json
dataguard validate        # runs contract rules against ground truth
dataguard snapshot diff   # detects schema drift vs the committed snapshot
```

## How it works — three ground-truth modes

| Mode | Source | Use case |
|------|--------|----------|
| **Full** | Live database connection | CI pipelines with DBA-approved credentials |
| **Snapshot** *(default)* | Committed `snapshot.json` | Zero CI credentials; offline validation |
| **Manual** | `[ExpectedColumn]` / `[ExpectedSpParameter]` attributes | Attribute-only validation, zero DB access |

The IDE layer (`DataGuard.Analyzers`) marks unvalidated SQL calls on every keystroke with a lightweight incremental generator; the CI layer (`dataguard validate`) runs the full diff engine with database ground truth.

## Rules

| ID | Rule | ID | Rule |
|----|------|----|------|
| DG001 | Parameter count match | DG009 | NVARCHAR2(2000) inference fallback |
| DG002 | Parameter type match | DG010-014 | Oracle dialect / provider checks |
| DG003 | Parameter direction match | DG015/016 | Phantom table / column (AI-hallucination detection) |
| DG004 | Result-set column shape | MY001-003 | MySQL syntax / length checks |
| DG005 | Nullability match | PG001-003 | PostgreSQL syntax / length checks |
| DG006 | Naming convention | DG007/008 | Oracle length semantics (CHAR/BYTE, ORA-12899) |

## Packages

| Package | Description |
|---------|-------------|
| `DataGuard.Core` | Contracts, rules engine, security, telemetry, baseline |
| `DataGuard.SqlServer.Adapter` | SQL Server catalog reader + ScriptDOM parsing |
| `DataGuard.Oracle.Adapter` | Oracle ALL_ARGUMENTS/ALL_TAB_COLUMNS/NLS readers |
| `DataGuard.MySql.Adapter` / `DataGuard.PostgreSql.Adapter` | MySQL / PostgreSQL support |
| `DataGuard.Analyzers` | Roslyn IDE analyzers + quick fixes |
| `DataGuard.Cli` | `dataguard` dotnet tool |

## CLI commands

```
dataguard validate        # text by default; SARIF requires --format sarif --output <path>
dataguard baseline        # freeze existing drift for legacy codebases
dataguard snapshot        # refresh / show / diff schema snapshots
dataguard oracle-check    # Oracle dialect + length checks (CHAR/BYTE semantics)
dataguard init            # generate configuration
dataguard config          # show / validate configuration
dataguard migrate         # migrate legacy v1 baseline files to v2
dataguard assess          # read-only legacy/dependency/config assessment; JSON/SARIF via --format
```

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success — validation passed, no drift, no assessment findings/tool errors, or informational output (`version`, `config show`, `snapshot show` without a snapshot) |
| `1` | Validation failures found (error-severity violations), drift detected with `--fail-on-drift`, or `assess` found findings/operational errors |
| `2` | Configuration / usage error — invalid `--format`, machine-readable format without `--output`, unsupported arguments |

CI note: `snapshot diff` reports drift with exit code `0` unless `--fail-on-drift` is passed; in CI environments (`CI`/`GITHUB_ACTIONS`) it prints a reminder to pass the flag.

## IDE support

- **Roslyn analyzers** (`DataGuard.Analyzers`): DG001 diagnostics with quick fixes (MaxLength, UseOracle, SkipContractCheck, naming) in any C# IDE.
- **VS Code extension** (`DataGuard.VSCode`): trusted-workspace CLI runner with private SARIF diagnostics, cancellation and bounded output.
- **Visual Studio 2022 extension** (`DataGuard.VisualStudio`): VSSDK Tools commands for the same local CLI workflow; packaged on Windows CI.

## Documentation

- [Solution overview](docs/SOLUTION.md) · [Product](docs/PRODUCT.md) · [Usage](docs/USAGE.md) · [Architecture](docs/architecture/system_architecture.md) · [Security](SECURITY.md) · [Marketplace publishing](docs/marketplace-publishing.md)

## License

[MIT](LICENSE)
