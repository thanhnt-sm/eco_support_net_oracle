# Phase 5: Hygiene (dead code, god files, governance, docs)

Closes: Medium dead/orphan, god files, governance/docs drift, scripts foot-guns. Report recommendations 20, 23 and operational top-8 item 6 and 8.

## 5.1 Dead code and orphans (manifest `from → keep | extract | rewrite | remove`)
| From | Decision | Note |
|---|---|---|
| `src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs` | rewrite | Used by `init` wizard only via tests; keep but fix: single `EnumerateFiles` with `IgnoreInaccessible`, prune `bin/obj/node_modules/.git`, provider scoring incl. PG/MySQL, wizard option mapping bug |
| `src/DataGuard.Core/Assessment/UpgradePlanner.cs` | keep | Public API surface (`PublicApiSurface`); add CLI `assess --plan` caller or document as library-only |
| `src/DataGuard.Core/Security/SupplyChainVerifier.cs` | keep | Wire into `--plugins-dir` admission (4.2) |
| `Rules/RuleDependencyGraph.cs: DummyRule` | remove | Done in 4.2 |
| `Plugins/RulePluginManager.cs: CustomNamingConventionRule` | extract | Move to `samples/plugins/` as the plugin sample |
| `benchmarks/DataGuard.Benchmarks/` (root duplicate) | remove | `tools/benchmarks/` is the CI one; the observability benchmark moves there as a second project |
| `tests/DataGuard.BinaryCompatibilityFixture` | keep | Added to `slnf` build and CI (Phase 2) |
| `tests/git-tools/*.sh` | keep | CI job `scripts-tests` runs them |
| `.omo/run-continuation/*.json` | remove from index | `git rm --cached`, already gitignored |
| `_observability_discovery/*.zip` | keep | Owner-requested bundle per governance |

## 5.2 God files
- `src/DataGuard.Cli/Program.cs` ⇒ `Program.cs` (root command wiring ≤ 150 lines) + `Commands/ValidateCommand.cs`, `SnapshotCommands.cs`, `BaselineCommand.cs`, `ScanCommand.cs`, `InitCommand.cs`, `AssessCommand.cs`, `HookCommand.cs`, `Services/ContractAcquisition.cs` (`AcquireContractsAsync`, `BuildContractsAsync`), `Services/DatabaseVersionReader.cs`; delete the duplicate `ComputeSchemaHash`.
- `ContractRules.cs` ⇒ one file per rule under `Rules/`; `ColumnShapeMatchRule`'s SQL helpers ⇒ `Rules/Sql/SqlTextScanner.cs` with `Substring(i)` replaced by index scanning.
- `ProjectCSharpSqlSource.cs` ⇒ `Sources/CSharp/` partials: `InvocationPass`, `CommandTextPass`, `StoredProcedurePass`, `ObjectCreationPass`, `ConstantsPass`, `StringResolver`, `ExpectedPropertyExtractor`.
- `Analyzers.cs` ⇒ `UnvalidatedSqlCallGenerator.cs`, `ContractValidationAnalyzer.cs`, `DiagnosticDescriptors.cs`.
- No behavior change in this step; verified by the full suite and by byte-identical SARIF on the e2e fixture before/after.

## 5.3 Scripts, hooks, governance
- `tools/git-tools/dg-git`: bare invocation prints usage and exits 1; `-m` required for commit; `DG_YES_MODE` default false (matches `rules/git_workflow.md`).
- `scripts/git_sync.sh`, `scripts/github_automator.sh`: remove `git add -A` + auto push paths or gate behind `DG_ALLOW_AUTO_PUSH=1` with a loud banner.
- `.githooks/commit-msg`: regex also rejects `chore(sync)`, `automated workspace synchronization`.
- `scripts/verify_local_gates.sh`: remove the Windows swallow of test failures.
- `scripts/install-hooks.sh` sets `core.hooksPath=.githooks`; documented in CONTRIBUTING.
- `scripts/anti_garbage_guard.sh` / `preflight_agent_check.sh`: drop `claude` from allowlists; `preflight` counts docs dynamically.
- `.gitignore`: remove the trailing `.env.example`/`.release.env.example` ignores that negate the earlier allow.
- `rules/workspace_governance.md`, `CLAUDE.md`, `.agents/rules/ecosystem_rules.md`: list all 7 test projects; state that CI builds `DataGuard.CrossPlatform.slnf`; `plans/2026-08-20-workspace-rationalization.md` header updated to "executed".
- `.github/copilot-instructions.md`: GPL-3.0-only; `check-license-consistency.py` scans `.github/`.

## 5.4 Docs
- README rule table generated from `ProviderRuleCatalog.RuleTitles` by `scripts/gen_rule_table.py`; `tests` assert README matches; add DG017/018/019/020, MY004–007, PG004–005, DG098/099 and the assessment family; fix DG016 description.
- `docs/USAGE.md`, `docs/cli.md`: new flags (`--fail-on-unavailable`, `--allow-syntactic-only`, `--allow-unevaluated`, `--connection-env`, `--plugins-dir`), `--offline` semantics, snapshot v3, baseline v2 migration.
- `SECURITY.md`/`.vi.md`: credential resolution order, audit HMAC, `DescribeRefCursors` executes procedures.
- `CHANGELOG.md`: Unreleased section listing every behavior change with the report item IDs.
- `scripts/verify_docs_sync.sh`: adds rule-table and CLI-flag content checks (the plan-cleanup requirement).
