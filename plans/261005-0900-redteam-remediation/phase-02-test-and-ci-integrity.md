# Phase 2: Test and CI integrity

Closes: H12, H13, H14, H15, Medium test items, report recommendations 6, 7, 19 and the test top-8.

## Requirements

### 2.1 No false-green tests (H12)
- Add `LiveDbFactAttribute` / `LiveDbTheoryAttribute` in `tests/DataGuard.Core.Tests/LiveDbFactAttribute.cs`: `Skip` is set unless `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1` (Oracle/MySQL/PG) or `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1`; trait `Category=LiveDb`.
- Replace every `return; // xUnit 2.9 has no supported dynamic skip API.` in the five integration test files.
- Fixtures fail loudly (throw) when the env var is set and the container cannot start.
- CI: new job `live-db-integration` in `ci.yml` on `ubuntu-latest` with the env vars set, `--filter Category=LiveDb`, `timeout-minutes: 40`; `build-and-test` excludes `Category=LiveDb`. The job is required (no `continue-on-error`).

### 2.2 Golden corpus becomes a real regression suite (rec 19)
- `GoldenCorpusTests`: build the rule set from `ProviderRuleCatalog.Get(provider)` (add `ProjectReference` to `DataGuard.Cli`); malformed JSON fails the theory; assert `cases.Count >= 24` and every category directory has ≥ 1 case; compare the **full** expected set for Error and Warning (Info stays lenient).
- Add cases: `Negative/` (≥ 6, `expectedDiagnostics: []`: CTE, alias, schema-qualified, DUAL, EXTRACT FROM, JOIN unqualified); `SqlServer/` (≥ 4, incl. `dbo.Orders`), `MySql/` (≥ 3), `PostgreSql/` (≥ 3), `H3_Dialect_Confusion/case_03_pivot_bracket.json`, `SP_Contract/` (≥ 3: missing param, extra param, direction) using the fake catalog in the case file.
- Add `provenance` object to the case schema (`source: "manual" | "llm"`, `model`, `date`); existing cases get `manual`.
- Add `tools/corpus/collect_hallucinations.py` skeleton + README describing the 4.md loop (prompt → execute on Testcontainers → label). It is documented tooling, not a CI step.

### 2.3 Analyzer tests on `Microsoft.CodeAnalysis.Testing` (rec 19)
- Add `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` + `CodeFix.Testing` (xunit flavor) to `Analyzers.Tests`/`CodeFixes.Tests`; regenerate lock files.
- DG001 trigger matrix: `ExecuteSqlRaw`, `FromSqlRaw`, `FromSqlInterpolated`, Dapper `Query<T>`, `QueryFirst`, `ExecuteReader`; non-trigger: non-SQL `Execute`, `[SkipContractCheck]` on class, marker comment; assert location via markup.
- DG098 negative, DG099 positive/negative, DG012 descriptor emission.
- One round-trip analyzer → code fix test per existing fix provider.

### 2.4 Coverage gaps named in the report
- DG012 Oracle: tests for `OracleDialectChecker.CheckProviderOptionMismatch` (copy the PG trio).
- DG008 boundary: `entityMaxBytes == column.MaxLength`, factor sensitivity (a test that fails if the factor is 4 or 2).
- DG101: `EXECUTE`, lowercase, tab/newline after EXEC, Oracle `BEGIN pkg.p(:a); END;`.
- Phantom: schema-qualified, alias that is a keyword.
- Fix misleading tests: `My003_LengthExceeds_Flags` asserts MY003; `TimedOperation_Dispose_RecordsHistogram` asserts the histogram; `Console.SetOut` tests get `[Collection("Console")]`; `HealthHostIntegrationTests` fixed delays replaced by polling with timeout.

### 2.5 Workflows (H13, H14, H15)
- `installers.yml`: `publish` and `cli` jobs `needs: [test]` where `test` runs `dotnet test DataGuard.CrossPlatform.slnf` (excluding LiveDb); nightly is created only when green.
- `build_release.yml`: add `dotnet test`; use `assert-vsix.ps1`; add `prepare-lsp`; remove `TreatWarningsAsErrors=false`.
- `release.yml`: job `verify-ci` (tag must be an ancestor of `origin/main` and the latest `CI` run on that SHA must be `success`, via `gh api`); release stays `--draft` until NuGet/Docker/attest jobs finish, then a final job flips draft off; `publish-attestations` takes the tag from `needs.validate-version.outputs.tag` through `env:`; `environment: release` on publish jobs.
- `marketplace.yml`: move `id-token`/`attestations: write` to the attest steps' jobs; `publish-vscode` does `actions/checkout` + `npm ci` and runs `npx --no-install @vscode/vsce publish`; verify `sha256sum -c` and `gh attestation verify` before publish.
- `scripts/check-workflow-policy.py`: include `build_release.yml`; new rule: any job that calls `cosign`, `gh release`, `vsce publish`, `dotnet nuget push`, or `docker push` must transitively `needs` a job that runs `dotnet test`; add unit tests.
- `dependabot.yml`: add `npm` (`/src/DataGuard.VSCode`) and `docker` (`/`).
- Add `global.json` (SDK `9.0.x`, `rollForward: latestFeature`) and `NuGet.config` with `packageSourceMapping` to nuget.org; add `.nvmrc` (22).
- `ci.yml`: `npm ci && npm test` for `src/DataGuard.VSCode`; `npm audit --omit=dev --audit-level=high`; build `tests/DataGuard.BinaryCompatibilityFixture`.

## Acceptance
- `python3 scripts/check-workflow-policy.py` passes and its new rule fails on a crafted fixture in `scripts/tests`.
- Local: `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 DATAGUARD_REQUIRE_LIVE_SQLSERVER=1 dotnet test --filter Category=LiveDb` runs the five fixtures for real (record pass/fail and image pull status in the journal).
- Golden corpus ≥ 24 cases across 4 providers with ≥ 6 negatives.
