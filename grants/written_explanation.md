# Written Explanation — DataGuard

## 1. The problem

.NET applications that call stored procedures or raw SQL have no tooling that validates the
**contract** between the C# entity/property layer and the database shape (parameter names/types,
result-set columns, nullability, length semantics). The EF Core team acknowledged the gap in
[issue #245 (2014)](https://github.com/dotnet/efcore/issues/245) and did not build it.

Consequences in practice:

- Renaming/retrofitting a column silently breaks `SELECT` projections or `EXEC` calls at runtime.
- `VARCHAR2(4000 BYTE)` vs `(100 CHAR)` length-semantics mismatches surface as `ORA-12899` in
  production, not in CI.
- AI-generated SQL introduces hallucinated table/column names that nothing catches before deploy.

## 2. The solution

**DataGuard** brings dbt's proven *model-contract* preflight pattern to .NET:

1. **Lightweight IDE layer** — a netstandard2.0 Roslyn incremental generator/analyzer marks
   unvalidated SQL calls (EF Core `FromSqlRaw`/`ExecuteSqlRaw`, Dapper) and provides quick fixes
   (`[SkipContractCheck]`, `[MaxLength]`, provider rename) that emit only compilable code.
2. **Heavy CI layer** — the `dataguard` dotnet tool extracts database ground truth and runs 22+
   rules (DG001-016, MY001-003, PG001-003) covering parameter count/type/direction, column shape,
   nullability, naming, length semantics (CHAR/BYTE), dialect confusion, and phantom identifiers.
3. **Three ground-truth modes** — Full (live DB), Snapshot (committed JSON, the default, zero CI
   credentials), Manual (`[ExpectedColumn]`/`[ExpectedSpParameter]` attributes).
4. **Legacy-friendly** — `dataguard baseline` freezes existing drift; `snapshot diff --fail-on-drift`
   gates new drift in CI; SARIF output integrates with GitHub/Azure DevOps.

## 3. Why the ecosystem needs it

- Stored-procedure-heavy .NET is a large, underserved surface; contract drift is a top production
  failure class there.
- AI-assisted coding increases hallucinated-SQL risk; phantom-identifier detection (DG015/DG016)
  targets exactly that.
- MIT license, 8 NuGet packages, four database providers, no vendor lock-in — a strong base for an
  ecosystem-impact grant: the funding enables real-DB integration tests (Testcontainers), NuGet
  Trusted Publishing, and the companion Claude skill.
