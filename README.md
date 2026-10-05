# DataGuard — Contract Validation for Entity ↔ Stored Procedure / Raw SQL

[![License: GPL-3.0-only + Commercial](https://img.shields.io/badge/license-GPL--3.0--only%20%2B%20Commercial-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/thanhnt-sm/eco_support_net_oracle/actions)
[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/thanhnt-sm/eco_support_net_oracle/badge)](https://scorecard.dev/viewer/?uri=github.com/thanhnt-sm/eco_support_net_oracle)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/15184/badge)](https://www.bestpractices.dev/projects/15184)

**DataGuard** detects drift between your .NET entities and the SQL they depend on — stored procedure parameters, result-set shapes, nullability, length semantics, and dialect mismatches — at design time and in CI.

> **Why it exists:** EF Core has tracked the gap for stored-procedure contract validation since [Microsoft EF issue #245 (2014)](https://github.com/dotnet/efcore/issues/245) and declined to build it. DataGuard ports the *model contracts* pattern that **dbt** proved out for data engineering (preflight column/parameter checks at compile time, since Core v1.5, 2023) to the .NET stored-procedure world.

## Quickstart

```bash
# Download dataguard-<version>-<rid>.zip for your OS from GitHub Releases and verify it with the
# .sha256 file next to it (needs the .NET 9 runtime; DataGuard.Cli is not yet published on nuget.org):
# https://github.com/thanhnt-sm/eco_support_net_oracle/releases
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
| DG001 | IDE: unvalidated SQL call (Roslyn analyzer) | DG009 | NVARCHAR2(2000) inference fallback |
| DG101 | Engine: parameter count match | DG010-014 | Oracle dialect / provider checks |
| DG002 | Parameter type match | DG015/016 | Phantom table / column (AI-hallucination detection) |
| DG003 | Parameter direction match | MY001-003 | MySQL syntax / length checks |
| DG004 | Result-set column shape | PG001-003 | PostgreSQL syntax / length checks |
| DG005 | Nullability match | DG007/008 | Oracle length semantics (CHAR/BYTE, ORA-12899) |
| DG006 | Naming convention | DG019 | Raw SQL parse error |

## Packages

| Package | Description |
|---------|-------------|
| `DataGuard.Core` | Contracts, rules engine, security, telemetry, baseline |
| `DataGuard.SqlServer.Adapter` | SQL Server catalog reader + ScriptDOM parsing |
| `DataGuard.Oracle.Adapter` | Oracle ALL_ARGUMENTS/ALL_TAB_COLUMNS/NLS readers |
| `DataGuard.MySql.Adapter` / `DataGuard.PostgreSql.Adapter` | MySQL / PostgreSQL support |
| `DataGuard.Analyzers` | Roslyn IDE analyzers + quick fixes (build-time only; see FAQ) |
| `DataGuard.Contracts` | Contract attributes (`[SkipContractCheck]`, `[ExpectedColumn]`, ...). Licensed MIT on purpose. Install it in projects that use the attributes |
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
- **Visual Studio 2022 extension** (`DataGuard.VisualStudio`): Tools → DataGuard commands over the bundled CLI, always run in `--ide-safe` mode with per-solution consent (keyed by the `.dataguard.yml` hash); SARIF results land in the Error List with line/column navigation. Packaged and content-checked on Windows CI.

## Documentation

- [Solution overview](docs/SOLUTION.md) · [Product](docs/PRODUCT.md) · [Usage](docs/USAGE.md) · [Architecture](docs/architecture/system_architecture.md) · [Security](SECURITY.md) · [Marketplace publishing](docs/marketplace-publishing.md)

## Dual licence & FAQ

From **v0.4.0** DataGuard is offered under two licences:

1. **GNU GPL version 3 only** (`GPL-3.0-only`, see [`LICENSE`](LICENSE)), plus an additional permission for
   the database drivers and IDE hosts it runs with ([`docs/legal/ADDITIONAL-PERMISSIONS.md`](docs/legal/ADDITIONAL-PERMISSIONS.md)).
2. **A commercial licence** for companies that want to embed DataGuard in a closed-source product they distribute.
   Contact `<contact email placeholder>`. Terms are agreed per licensee; there is no public price list or form.

> **Legal status:** the additional-permission text and the commercial-licence terms have **not been reviewed by a
> lawyer**. Nothing here is legal advice. If a licensing question matters to you, ask your own counsel.

**Does GPL-3.0 stop companies from using DataGuard commercially?** No. GPL-3.0 allows commercial use, copying and
modification. Running `dataguard validate` in your CI, or using the analyzers while you build your own product, needs
no commercial licence by itself.

**When does the copyleft apply?** When you *distribute* a work based on DataGuard, for example by shipping DataGuard
code or binaries inside your product. You then have to offer that work under GPL-3.0, or take the commercial licence
instead. Using a tool internally does not distribute it.

**Can the repository be made non-copyable?** No. It is public, and GitHub's terms let others fork it. Releases up to
and including v0.3.0 were published under the MIT licence and **stay MIT permanently**; the change is not retroactive.

**Which parts are not GPL?**

- `DataGuard.Contracts` is licensed under the MIT licence on purpose. It ends up inside your application and is read by
  DataGuard through reflection, so it must not pull your code into the GPL.
- `Oracle.ManagedDataAccess.Core` and `Microsoft.Data.SqlClient.SNI.runtime` ship with the CLI, RID archives, container
  image and IDE extensions under their own vendor terms. Their licence texts are in
  [`docs/legal/THIRD-PARTY-NOTICES.md`](docs/legal/THIRD-PARTY-NOTICES.md), which travels with every DataGuard download.

**What does the analyzer package leave in my output?** `DataGuard.Analyzers` is a build-time development dependency:
its analyzer assemblies load inside the compiler from `analyzers/dotnet/cs`, and the package has no `lib/` assets, so
`DataGuard.Analyzers.dll` and `DataGuard.SqlClassification.dll` do not appear in your build output (checked by
`scripts/verify-analyzer-packaging.sh`). This statement is about the Analyzers package only. `DataGuard.Contracts` (MIT)
is copied to your output when you use its attributes, and other packages such as `DataGuard.Build` or `DataGuard.Core`
place their own DLLs there and follow their own licence.

**Is the AI-generated part of the code protected?** Some commits were co-authored with an AI assistant. Copyright
protection for AI-assisted work is limited and unsettled; this is a risk to be aware of, not a claim either way.

**Full texts:** [`LICENSE`](LICENSE) (GPL-3.0),
[`docs/legal/ADDITIONAL-PERMISSIONS.md`](docs/legal/ADDITIONAL-PERMISSIONS.md),
[`docs/legal/THIRD-PARTY-NOTICES.md`](docs/legal/THIRD-PARTY-NOTICES.md) and the MIT text of v0.1.0-v0.3.0
([`docs/legal/MIT-v0.1.0-v0.3.0.txt`](docs/legal/MIT-v0.1.0-v0.3.0.txt)).
