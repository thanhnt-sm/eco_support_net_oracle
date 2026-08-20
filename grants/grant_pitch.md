# DataGuard — Grant Pitch

**Track**: Anthropic Claude for Open Source — Ecosystem Impact (developer tooling)

## The gap

Since **2014**, EF Core has tracked the missing ability to validate that a .NET entity matches the
stored procedure / raw SQL it depends on — parameters, result-set shape, nullability, length
semantics — and **Microsoft declined to build it** ([EF issue #245](https://github.com/dotnet/efcore/issues/245)).

Legacy .NET codebases drift silently: a DBA renames a column or changes `VARCHAR2(4000 BYTE)` to
`VARCHAR2(100 CHAR)`, and the first sign of trouble is an `ORA-12899` or `SqlException` in production.

## The proven pattern

**dbt** proved that *model contracts* — preflight column/parameter checks at compile time — prevent
exactly this class of data-engineering failure (dbt Core v1.5, 2023). DataGuard ports that pattern to
the .NET stored-procedure world, where it did not exist:

- **IDE layer** (netstandard2.0 Roslyn analyzer): flags unvalidated SQL calls on every keystroke,
  with safe quick fixes.
- **CLI layer** (`dataguard` dotnet tool): full diff engine against database ground truth in three modes —
  **Full** (live DB), **Snapshot** (offline JSON, zero CI credentials), **Manual** (attributes).
- **CI integration**: SARIF output, baseline freeze for legacy drift, `snapshot diff --fail-on-drift`.

## Why it matters for the ecosystem

- Stored-procedure-heavy .NET codebases are the most fragile, least-tooled part of the ecosystem;
  AI-generated SQL (a growing failure mode) makes phantom-table/column detection
  (DG015/DG016) more valuable every day.
- MIT-licensed, NuGet-distributed, SQL Server + Oracle + MySQL + PostgreSQL, no vendor lock-in.
- The grant would fund real-DB integration testing (Testcontainers), NuGet publishing, and docs —
  the exact parts a solo maintainer cannot do alone.

## What we ask

Claude Max usage to complete: container-based integration tests, NuGet Trusted Publishing rollout,
and the companion Claude skill for DataGuard.
