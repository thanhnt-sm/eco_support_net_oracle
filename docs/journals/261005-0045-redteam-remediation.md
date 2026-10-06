# Journal: Red-team remediation (plan `261005-0900-redteam-remediation`)

- Date: 2026-10-05 · Branch `docs/redteam-261004-source-vs-goals` · Base `046f91d`
- Source report: `plans/reports/redteam-261004-1500-source-vs-original-goals.md`
- Plan: `plans/261005-0900-redteam-remediation/plan.md`

## 1. Environment and baseline

| Item | Value |
|---|---|
| .NET SDK | 9.0.318 installed in the sandbox via `dotnet-install.sh` (no SDK was present) |
| Docker | daemon started in the sandbox; images pulled: `gvenzl/oracle-free:23-slim-faststart`, `mcr.microsoft.com/mssql/server:2022-latest`, `mysql:8.4`, `postgres:16-alpine`, `testcontainers/ryuk:0.11.0` |
| Baseline build | `dotnet build DataGuard.CrossPlatform.slnf -c Release -p:RunAnalyzers=true`: 0 warnings |
| Baseline tests | 1007 passed / 5 skipped / 0 failed |
| Live Oracle probe | confirmed before any change: `ORA-00904 "PACKAGE_NAME"` when filtering by package; no package subprogram catalogued; 0-argument overload lost; `NLS_CHARACTERSET` absent from `nls_session_parameters` |

## 2. Execution model

Six agents per wave in isolated git worktrees, each with a disjoint file scope, merged by the lead with a full rebuild, format check and test run after every merge. Worktrees were created from `main`, so every agent fast-forwarded to the lead's HEAD before editing (checked with `git merge-base --is-ancestor`). Five agents of wave 4 were interrupted by a model session limit and resumed from their uncommitted worktree state once the limit reset; no work was lost.

## 3. Per-phase evidence

| Phase | Commits | Tests after merge | Notes |
|---|---|---|---|
| 1 Stop the bleeding | `6b8a991` (CLI gates), `e4b7eff` (rules), `713781f` (contracts) | 1212 / 5 skipped | Unavailable rules no longer exit 3; provider whitelist; `--config` missing ⇒ 2; ground-truth gate ⇒ 3; default snapshot discovery; `--offline` ⇒ Snapshot; DG015/DG016 split with schema-aware keys; DG005 uses `IsNullable`; DG013 skips SP descriptors; parse status moved to DG019 |
| 2 Test and CI integrity | `fa00c74` (analyzer verifier tests), `d74ef86` + `3d7a720` (workflows, scripts, governance, TruffleHog digest), `1fbcfc9` (LiveDb, corpus) | 1359 / 12 skipped | 7 LiveDb tests are real skips, run for real with the env vars (10/10 pass against containers); corpus 8 → 27 cases incl. 7 negatives and 4 providers; publish jobs need `dotnet test`; `release.yml` verify-ci + draft-until-published; `marketplace.yml` least privilege and pinned vsce |
| 3 Wire the MVP core | `bd6bd23` (T-SQL AST phantom), `b68c5cf` (unevaluated), `91a7822` (snapshot v4 + fingerprints), `b1a78c0` (SP matching + type tables), `e498afa` (extractor), `64e4c74` (catalogs + length) | 1671 / 15 skipped | DG101/DG002/DG003 resolve calls against the catalog; Oracle package catalog proven on a live container (2 `GET_ONE` + 2 `COUNT_ALL` incl. 0-arg, `NOARGS`, `WITH_DEF` default, REF CURSOR describe); snapshot v4 carries procedures, semantics, charset and is integrity-checked; baseline fingerprints are a multiset with legacy compatibility |
| 4 Architecture | `9d81238` (Core/SqlServer split), `02103a5` (one pipeline, plugins), `ae4aabd` (syntax-only analyzers), `f3b03fb` (credentials, audit, MetadataLoadContext) | 1908 / 15 skipped | Core csproj has no SqlClient/ScriptDom/AWSSDK; adapters keep only their driver; CLI and API build the same rule plan; plugins load from `--plugins-dir` under admission; analyzers have no `SemanticModel`/`IOperation` and real locations (15 KnownGap tests flipped); CLI credentials resolve through the provider, audit chain is HMAC-keyed with one writer |
| 5 Hygiene | `4a814f6`, `b79faba` (5A) · 5B/5C: see §4 | see §4 | VS Code passes `--config` only when the file exists; `ModelSnapshotCSharpParser` emits CLR types/IsUnicode; dialect-aware identifier folding; `AutoDetectionEngine` rewrite + wizard fixes; root `benchmarks/` removed per manifest; README rule tables generated and checked; `verify_docs_sync.sh` validates content |
| 6 Verification and handoff | `a6a2a66` (10 ADRs) · this journal | see §5 | |

## 4. Phase 5B/5C (filled on merge)

Landed in `af950b2` and `68f7efc` (merged at `fabe2d3`, see `plan.md`): `Program.cs` split into `Commands/` and `Services/` (52 lines left, 56 CLI invocations byte-identical before/after), one file per rule under `Rules/`, typed YAML binding, `verify-shape` for MySQL, snapshot `Provider` required; Core follow-ups: type registry, v4 baselines in the API, keyed audit in the API, PG002 dedupe, own-dialect false positives, SQL Server `HasDefault`, `ArgumentsKnown`, MY003 byte semantics. All covered by the final run in §5.

## 5. Final verification (HEAD `95a5120`)

| Check | Result |
|---|---|
| `dotnet build DataGuard.CrossPlatform.slnf -c Release -p:RunAnalyzers=true` | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test DataGuard.CrossPlatform.slnf` | 1997 passed / 15 skipped / 0 failed (Core 1764, Analyzers 97, GoldenCorpus 61, Observability 38, CodeFixes 37); the 15 skips are 5 Windows-only junction tests and 10 LiveDb tests |
| LiveDb (`DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 DATAGUARD_REQUIRE_LIVE_SQLSERVER=1 --filter Category=LiveDb`) | 10 passed / 0 failed against real Oracle 23 Free, SQL Server 2022, MySQL 8.4 and PostgreSQL 16 containers (package overloads, 0-argument subprograms, REF CURSOR describe, SQL Server defaulted parameter, MySQL functions and live describe) |
| Line coverage (CI gate method, ≥ 60 %) | 70.99 % (14 635 / 20 615 lines, deduplicated across the five reports) |
| `tests/DataGuard.BinaryCompatibilityFixture` against current Core | builds, 0 warnings |
| E2E `validate` in snapshot mode (v4 snapshot with one table and one procedure per provider; clean repo) | sqlserver / oracle / postgresql / mysql: exit 0, `Using snapshot .dataguard-snapshot.json`, 0 findings |
| E2E seeded phantom table + call missing a required parameter | all four providers: `DG015` Error (exit 1) and `DG101` Warning naming the missing parameter |
| Mutation spot checks | AL32UTF8 factor 3→4: 4 tests fail; DG101 `EXECUTE` prefix removed: 2 tests fail; schema-keyed table index disabled: 1 test fails (the last two guards were added in `95a5120` after the first run survived) |
| `scripts/check-workflow-policy.py`, `scripts/tests` (92), `verify_docs_sync.sh` (rule table + CLI flag content checks), `check-license-consistency.py` | all pass |
| Not run here | `docker build` of the Dockerfile (proxy CA not trusted inside the build container); `DataGuard.sln` Windows projects (VS extension) — covered by the Windows CI jobs |

Totals: 59 commits on top of `046f91d`, 402 files changed, golden corpus 8 → 34 cases, `DataGuard.Core.csproj` has no SqlClient, ScriptDom or AWSSDK reference.

## 6. Deferred (owner decisions)

- GPL §7 additional-permission text and commercial terms need legal review; the §7 text is silent on third-party rule plugins (ADR-0007).
- VSIX Authenticode signing key; NuGet author-signing certificate (cosign keyless is in place, NuGet clients do not verify Sigstore).
- Reserve `DataGuard.Cli` on nuget.org (pre-existing).
- GitHub settings: required checks `live-db-integration`, `vscode-extension`, `scripts-tests`; `release` environment reviewers; NuGet trusted publishing environment.
- Keep / freeze / extract decisions for the ten components in `docs/adr/` (recommendations recorded, decisions pending).

## 7. Known limitations recorded in CHANGELOG

- Baseline fingerprints include the repo-relative path: a file rename invalidates its entries; `baseline` and `validate` must run from the same directory.
- Oracle stored-procedure Ids changed (`oracle:{OWNER}.{PKG|_}.{NAME}#{subprogram_id}`); old baselines keyed on the previous Ids do not match.
- `DescribeRefCursors: true` executes the procedure with NULL inputs to describe the cursor.
- Analyzer heuristic DG002 is now DG097; analyzer titles DG001–DG017 changed to match the engine.
- Docker smoke test could not run in the sandbox (proxy CA not trusted inside the build container); CI covers it.

## 8. After the PR (2026-10-05)

PR #40 (`docs/redteam-261004-source-vs-goals` → `main`) exposed four CI failures that the sandbox runs had not covered; each was reproduced locally failing, fixed, shown passing, and pushed:

| Commit | Check | Cause → fix |
|---|---|---|
| `03917e5` | Security Scan (NuGet audit) | `Microsoft.CodeAnalysis.*.Testing 1.1.4` pulls `System.Private.Uri 4.3.0` (three advisories) → pin 4.3.2 in `DataGuard.Analyzers.Tests` and `DataGuard.CodeFixes.Tests`, lock files regenerated |
| `e6af446` | Build and Test (licence allow-list) | 26 legacy-nuspec packages in the same closure (`Microsoft.VisualStudio.Composition 16.1.8`) → each listed under `[exceptions]` with its licence-URL marker and MIT source |
| `ab4d7d8` | CodeQL (25 error alerts) | both custom queries used anchored `regexpMatch` (fired on literals exactly `Server=env` / `SELECT`, never on real credential strings or `"SELECT * FROM " + x`) → rewritten; the corrected SQL query found one real defect, the SQL Server `sp_describe_first_result_set` batch inside an `N'...'` literal with only `]` escaped, now passed as an NVARCHAR parameter; six by-design sites carry a `// codeql[...]` justification. Verified with the CodeQL CLI bundle on a database built from the tree: 0 results (default suite + custom pack) |
| `6011c8a` | Required check "Standards audit" never reported | the `main` ruleset names the check `Standards audit` but the job was displayed as `Golden standard checklist` (also on `main`) → job renamed |

Merge: squash `d0ec928` on `main` (all 18 checks green on head `6011c8a`; the ruleset's review rules cannot be satisfied by a single maintainer who is also the PR author, so the merge used the Repository-admin bypass the ruleset grants; recorded as an owner decision in `plans/ACTIVE_SESSION_REGISTER.md`). CI on `main` for `d0ec928`: CI, Standards audit, Marketplace Extensions, Build Installers (incl. nightly pre-release), Scorecard, Dependabot all green. PR #45 (`release-provenance-backfill.yml`, Scorecard guide) merged as `170e40d` the same day.
