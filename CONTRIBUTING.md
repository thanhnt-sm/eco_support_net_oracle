[English](CONTRIBUTING.md) | [Tiếng Việt](CONTRIBUTING.vi.md)

# Contributing to DataGuard

Thank you for your interest in contributing to **DataGuard**! DataGuard is a contract validation engine for .NET — it detects drift between your entities and the SQL they depend on (stored procedure parameters, result-set shapes, nullability, length semantics, dialect mismatches) at design time and in CI.

---

## 🧭 Code of Conduct & First Principles

1. **Evidence-first**: every claim (docs, PR description, commit message) should be verifiable — command + output. Bug fixes should come with a test that failed before the fix.
2. **Enterprise posture**: no telemetry by default, no secret material in logs/argv/SARIF, fail-closed credential handling. If your change touches these areas, state explicitly how the guarantees are preserved.
3. **One concern per PR**: keep pull requests small and focused; follow conventional commits (`fix:`, `feat:`, `test:`, `docs:`, `ci:`).

---

## 🛠️ Development Workflow

1. **Fork and Clone** the repository:
   ```bash
   git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
   cd eco_support_net_oracle
   ```
2. **Enable the git hooks** (once per clone): `./scripts/install-hooks.sh`. It sets `core.hooksPath=.githooks`,
   which turns on `pre-commit` (whitespace format, staged-path topology, docs inventory), `commit-msg`
   (Conventional Commits; rejects `auto-sync` / `chore(sync)` messages) and `pre-push` (restore, build, test).
   Toolchain pins: `global.json` (.NET SDK 9.0, latest feature band), `NuGet.config` (nuget.org only, with
   package source mapping) and `.nvmrc` (Node 22 for `src/DataGuard.VSCode`).
3. **Build, Test, Format** (.NET 9):
   ```bash
   dotnet build DataGuard.CrossPlatform.slnf                 # must be 0 errors, 0 warnings
   dotnet test DataGuard.CrossPlatform.slnf                  # all non-Visual Studio tests must pass
   dotnet format DataGuard.CrossPlatform.slnf --verify-no-changes
   ```
   Live-database integration tests (Testcontainers) carry the trait `Category=LiveDb` and are reported as
   **Skipped** unless you opt in: `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1` (Oracle, MySQL, PostgreSQL) and/or
   `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1` (SQL Server). Once opted in, a container that cannot start fails the test
   instead of skipping it. Run only that suite with
   `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 DATAGUARD_REQUIRE_LIVE_SQLSERVER=1 dotnet test DataGuard.CrossPlatform.slnf --filter Category=LiveDb`
   (needs a Docker daemon); CI's `build-and-test` job excludes it with `--filter "Category!=LiveDb"` and the
   `live-db-integration` job runs it.
4. **Submitting a Pull Request**:
   - Ensure new features/rules have accompanying unit tests under `tests/`.
   - Keep the public API surface in mind: `DataGuard.Contracts` (netstandard2.0) is referenced by consumer projects — breaking changes need an ADR in `plans/adr/`.
   - Run `dotnet restore DataGuard.sln --locked-mode -p:NuGetAuditMode=all '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905'`; it audits direct and transitive packages across the full solution.

---

## 📐 Project Layout

| Path | Contents |
|---|---|
| `src/DataGuard.Contracts` | Contract attributes shared with IDE analyzers (netstandard2.0, zero deps) |
| `src/DataGuard.Core` | Rules engine, baseline, security, sources, reporting |
| `src/DataGuard.*.Adapter` | SQL Server / Oracle / MySQL / PostgreSQL ground-truth readers |
| `src/DataGuard.Analyzers` / `CodeFixes` | Roslyn analyzer (IDE-light) and code fixes |
| `src/DataGuard.Cli` | `dotnet tool` — validate/snapshot/baseline/oracle-check |
| `src/DataGuard.VSCode` / `DataGuard.VisualStudio` | Editor extensions (CLI is the authority; hosts stay thin) |
| `tests/` | Core.Tests, GoldenCorpus.Tests, Analyzers.Tests |
| `plans/` | Plans and ADRs (see `plans/ACTIVE_SESSION_REGISTER.md`) |

---

## 📖 Where to Look Next

- Architecture decisions: `plans/adr/`
- Current roadmap and open work: `plans/ACTIVE_SESSION_REGISTER.md`
- Security policy: [SECURITY.md](SECURITY.md)
