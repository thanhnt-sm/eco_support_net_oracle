---
type: execution-evidence
phase: 2
status: in-progress
updated: 2026-09-13T00:00:00+07:00
---

# Execution evidence

## CP0 — immutable starting point

| Field | Recorded value |
|---|---|
| Executor / reviewer | Terra (sole writer) / Sol (read-only) |
| Branch | `main` |
| HEAD | `93bf7288324dd746669ad09c5e2a592adc772748` |
| Host | macOS 15.6, Darwin arm64 |
| SDK / Node | .NET SDK 9.0.310; Node 24.19.0; npm 11.17.0 |
| Captured | 2026-09-12T20:53:37+07:00 |

The pre-existing tracked change is preserved: `plans/260820-marketplace-extensions/plan.md` changes only `blockedBy` from `[]` to `[260912-2016-scout-remediation]`. It is outside this phase's ownership.

The pre-existing untracked inputs are also preserved: `docs/journals/`, `plans/260912-1936-luna-src-audit/`, and `plans/260912-2016-scout-remediation/`. The following complete manifest was captured immediately after the initial evidence file was written. It is a snapshot of the reviewed inputs; a file cannot contain a hash of its own final bytes, so the report's listed hash is its pre-revision value. Recompute the current manifest with the command below.

```sh
find docs/journals plans/260912-1936-luna-src-audit plans/260912-2016-scout-remediation -type f -print0 | sort -z | xargs -0 shasum -a 256
```

```text
ded2f9d42d0d901e638b23f1b4500f4f009d1fcf31ae14c4de3e03b4790ef810  docs/journals/260912-2016-scout-remediation-planning.md
4a8fbe43927c477e0576702fa140c1ada685461a905e7153d7377fba2de7874a  plans/260912-1936-luna-src-audit/phase-01-baseline-and-requirements.md
36eb0733738ecfaf9e65ccf810da64711af01db244b6958e7b6be261bfb2c791  plans/260912-1936-luna-src-audit/phase-02-source-audit-and-verification.md
9dcb12be74d434c1181e6d6cfbccab8200c8f9f16c94ba887c6303c7247d19c5  plans/260912-1936-luna-src-audit/phase-03-synthesis-and-handoff.md
3410cf88b787570125a66589f9e8d12621c82b35105961550934625dd069a06d  plans/260912-1936-luna-src-audit/plan.md
32eedc0be51758d56d467a16b74139336f63ea18470272087a6723db6fabb0e2  plans/260912-1936-luna-src-audit/reports/adapters.md
93d588516a8d10be0f6b2bf48de66e5ea7600ca69a9a5da5c3ddd4b5ab1267a9  plans/260912-1936-luna-src-audit/reports/cli-ide.md
4d598b11054f4c8dacab3d2c6283eb141a3f5906bfc34bb3ed013c9d16f64b4e  plans/260912-1936-luna-src-audit/reports/core-engine.md
ae390d23ed4678daded317e798472a85eef0ea3b49527a2e2ec2e37844dd0262  plans/260912-1936-luna-src-audit/reports/core-services.md
555289db3b9c11a2bcc251582e43e0c63c8baf0d9483b8ac02a9962927dd6caf  plans/260912-1936-luna-src-audit/reports/docs.md
fa85ec11b6ea05daa0d12013d46b700c024d654066a37606a3f7ed163be28492  plans/260912-1936-luna-src-audit/reports/inventory.md
fbacc2fbb3b2cb78b87aa129c6c8ccb80daa39cbefd4933e7a8b93a56291ae3c  plans/260912-1936-luna-src-audit/reports/plans-research.md
dd5b5cb0a45d4dd239c813c876f8f504e14ee1803a2ab66a915caec562cb5ab3  plans/260912-1936-luna-src-audit/reports/summary.md
591b7d8dd58097e0a69fec3e580a00d861f3fd72e414f785e38d844b6e836c55  plans/260912-1936-luna-src-audit/reports/tooling.md
ac845c8b9a463d7575873e879e9c386041fd50c25904d288e0f17400a88ca580  plans/260912-1936-luna-src-audit/reports/traceability.md
d477866f5c1bafa3f0a1819e24ec83e09d5a2b9db06499a6a8af988de843b2a7  plans/260912-1936-luna-src-audit/reports/verification.md
570aad18e87a091753b111f82cfdb854e94efd999cc5879bbb723c8cffa599d1  plans/260912-2016-scout-remediation/findings-ledger.md
0d4b620438e6e4ed04088b4d84e2f9e5dda0add82dc7847a07ed180b684e03d3  plans/260912-2016-scout-remediation/phase-01-baseline-and-contracts.md
f81e6a636951d711688612f608291033e817599f4ef2dc2f8711844417560335  plans/260912-2016-scout-remediation/phase-02-core-and-provider-correctness.md
5066d7efc18c02c90c4b84af19046e6e1b6e9b73fa19bcdc580fa6807b4bcf83  plans/260912-2016-scout-remediation/phase-03-cli-snapshot-and-configuration.md
0d6fb17f7045b53f4a77cf92b2437f45e034dd9af89ae9012f334cc4d3f20621  plans/260912-2016-scout-remediation/phase-04-security-reporting-and-resilience.md
24a91d9e5dc82b1f6df13cd3f5fc8c61ae733fef3d3b81fc8245f1c535ba6e64  plans/260912-2016-scout-remediation/phase-05-tooling-and-ide-safety.md
32ffe1a3bd4a6075140888471805a9ffa3cc7d240d56a18e595c12fecff9d024  plans/260912-2016-scout-remediation/phase-06-documentation-and-disposition.md
195715af97128e6b9abf95f61072a217def978accc3407dc5914520f98ccd417  plans/260912-2016-scout-remediation/phase-07-verification-and-closure.md
f4a4cf32cfec2a13f1506b5fd69d0577db5171359a035d2bb0646fb3050bfeb1  plans/260912-2016-scout-remediation/plan.md
e6c4b5d9b50e84bb6e0b6f4cd7fe1c54085c5b7c3dc475755f605c0c658b7a5b  plans/260912-2016-scout-remediation/reports/core-design.md
238c45e2f9261b4a7845ec4a6e1d891c9a38e75a28d18a947cc604fd5630b9ec  plans/260912-2016-scout-remediation/reports/execution-evidence.md
11be5d4fb6ff13c83d7497a012e2ef5dcca503744a387050ccc64737657b57ab  plans/260912-2016-scout-remediation/reports/plan-validation.md
c458b1dabf37e6b33b3b3b023e02032bdaee93fc8bc61bceddc9c94cd59ffa45  plans/260912-2016-scout-remediation/reports/red-team.md
8e624cb7a31c90f950808cc65ba2c7914f851660e395a07d10fe12a3fd4af980  plans/260912-2016-scout-remediation/reports/risk-scenarios.md
5827c1d90825d29171792e6844efd40ed2c9afbd9f46e5f1eee4a42266552b49  plans/260912-2016-scout-remediation/reports/safety-design.md
cef9b26e0aa5d212c6ed0bbcdcae6ba1133891348b4f5de81b55895ec1be1f3e  plans/260912-2016-scout-remediation/reports/sol-advisory.md
9a90e1ce86319297a73aa735e6b937a261a0885b03cfca6a8b966a953ceebbc3  plans/260912-2016-scout-remediation/reports/verification-runbook.md
651ace5191c388c17aac4dcdafddec1a9ed9b603a35477ef470bbcc6c3ee75c5  plans/260912-2016-scout-remediation/research/solution-research.md
d2e371d0ab2ec8e6c7bcea592e6d5e6fe46dedd92d9d6548790d7a55bd43a753  plans/260912-2016-scout-remediation/session-handoff.md
```



## Commands run from repository root

| Command | Exit | Result |
|---|---:|---|
| `git status --short` | 0 | One pre-existing marketplace-plan modification; three pre-existing untracked input roots. |
| `git branch --show-current` | 0 | `main`. |
| `git rev-parse HEAD` | 0 | `93bf7288324dd746669ad09c5e2a592adc772748`. |
| `git diff --stat` and `git diff --name-only` | 0 | At initial CP0, only the pre-existing marketplace-plan change; after Phase 1, the five documentation paths listed below are additionally modified. |
| `find docs/journals plans/260912-1936-luna-src-audit plans/260912-2016-scout-remediation -type f -print0 | sort -z | xargs -0 shasum -a 256` | 0 | Full output preserved above. |
| `git diff --check` | 0 | No tracked whitespace errors. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | 0 warnings, 0 errors. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | 484 passed, 0 failed, 0 skipped: Core 442, GoldenCorpus 25, Analyzers 5, CodeFixes 12. |
| `./scripts/verify_docs_sync.sh` | 0 | Required bilingual artifacts exist. |

These results are local baseline evidence only. The suite does **not** prove live SQL Server, Oracle, PostgreSQL, or MySQL assertions: inherited SQL Server integration tests can return before assertions when their container is unavailable. macOS does **not** prove the Windows Visual Studio/VSSDK/DPAPI runtime. No connection string, credential, raw log, database action, workflow dispatch, publish, or cleanup was used.

## Phase 1 documentation delta

The following paths are Terra's current Phase 1 documentation-only changes and are separate from the preserved pre-existing marketplace-plan change:

- `docs/03-components/core/baseline.md`
- `docs/03-components/core/baseline.vi.md`
- `docs/07-testing/test-strategy.md`
- `docs/07-testing/test-strategy.vi.md`
- `docs/product-discovery/release-evidence.md`

At this point, `git diff --stat` covers six paths: the pre-existing marketplace plan plus these five documentation paths. No file below `src/`, `tests/`, `.github/`, `scripts/`, or `rules/` is modified.

## CP1 contract inventory (candidate; frozen only after Sol GO)

| Boundary | Existing seam / caller | Frozen compatibility rule | Owner phase |
|---|---|---|---:|
| CLI dispatch | `src/DataGuard.Cli/Program.cs`: validate, baseline create/list/diff, init, config, oracle-check, snapshot refresh/diff, assess | `Program.cs` has one writer and is edited only in phase 3. Resolve CLI option > `DATAGUARD_*` environment > YAML > product default once; empty explicit input is an error. | 3 |
| Public pipeline | `DataGuardApi.CreatePipeline`, `ValidationPipeline.ValidateAsync`, `CheckDriftAsync`, `WithPlugins` | Preserve public method signatures; add detailed outcome envelopes rather than changing return types. Incomplete, cancelled, corrupt, unavailable, or unsupported cannot be clean. Pipeline owns plugin lifetime. | 2, 4 |
| Configuration | `DataGuardConfiguration` positional record, YAML parser/serializer and `WithSmartDefaults` | Preserve positional constructor and deconstruction. `DefaultProvider` may only be an additive non-positional init property with YAML/JSON proof. | 2, 3 |
| Baselines | `BaselineManager`, `BaselineFile`, `SnapshotTable`, `SnapshotColumn`, persisted v1/v2 fixtures | Preserve positional constructors/deconstruction and legacy reads. Add v3 DTO/converter rather than mutating existing persisted shapes. Snapshot schema and legacy violation hashes remain distinct. | 3, 4 |
| Engine and graph | `RuleDependencyGraph`, `ConcurrentValidationEngine.ValidateAsync`, `IContractRule.ValidateAsync` | Keep rule interface and legacy engine signature; graph-level execution and bounded outcomes use additive detailed APIs with stable ordering. | 2 |
| EF input | `EfModelSource` and `ModelSnapshot` path discovery | Only an explicitly trusted exact compiled artifact path and exact ModelSnapshot type may execute; source-only C# is unsupported. Never auto-run a DbContext, factory, or host. | 2 |
| Provider metadata | SQL Server parser; Oracle readers; MySQL/PostgreSQL adapters; provider registrations | Reader/capture seams are named and read-only. Ordinary Oracle validation never executes procedures; `UseRefCursorDescribe` defaults false. | 2, 3 |
| Result surfaces | CLI formats/exit branches, SARIF, evidence, VS Code and Visual Studio completion paths | Freeze acquisition/execution/drift states before edits. Current exit codes are 0 clean, 1 finding/operational failure, 2 usage; phase 3 must write and test the exact incomplete/unevaluated migration before behavior changes. | 3, 5 |
| Compatibility tests | Core public API tests and serialized baseline fixtures | Add an unchanged compiled-against-baseline consumer fixture as well as source rebuild and legacy serialization tests. | 1, 2, 3 |

### Exact current callers and public seams

| Surface | Current definition or branch | Required follow-up owner |
|---|---|---:|
| CLI command handlers | `Program.cs:60` validate; `181` baseline; `241/242` snapshot/refresh; `323` snapshot/show; `363` snapshot/diff; `486` init; `515/516/537` config; `569` oracle-check; `650` migrate; `687` assess | 3 |
| CLI configuration and serialization | `Program.cs:70–101` validate overlay; `197–202` baseline; `257` refresh; `379–384` snapshot diff; `585` oracle-check; `834` `LoadConfig`; `842` `DeserializeConfig`; `924` `SerializeConfig`. `498` is init configuration, not an overlay. | 3 |
| Acquisition and provider registration | `Program.cs:938` `BuildContractsAsync`; `1020` `ValidateContractsAsync`; `1111` `GetRulesForProvider`; output branches at `123–163`, `728–761`, `803` | 2 primitives; 3 integration |
| API and completion state | `PublicApiSurface.cs:32/41` CreatePipeline; `87` WithPlugins; `124` ValidateAsync; `203` CheckDriftAsync; `247` ValidationResult; `267` DriftReport; properties at `257–259` and `276` | 2, 4 |
| Baseline and persisted shapes | `BaselineManager.cs:42` create; `84` load; `204/221/249` hashes; `265` filtering; `301` migration; `358/371/375` primary records | 3, 4 |
| Graph and direct engine | `RuleDependencyGraph.cs:24/63/116/169` registration/order/levels/validation; `ConcurrentValidationEngine.cs:16/24` constructor and ValidateAsync | 2 |
| External result consumers | VS Code `extension.ts:65–66` command registration and `161–188` exit handling; Visual Studio `DataGuardPackage.cs:53–58` commands and `141–340` output/completion | 5 |
| Existing compatibility seams | `PublicApiAndPipelineTests.cs` API/drift/plugin tests; `SourceAndBaselineTests.cs` source/baseline fixtures; `CliExitCodeTests.cs` CLI outcomes | 2, 3 |

The reproducible direct-call census is `rg -n "CreatePipeline\\(|\\.ValidateAsync\\(|CheckDriftAsync\\(|new ConcurrentValidationEngine\\(|new DataGuardConfiguration\\(|new BaselineFile\\(|new Snapshot(Table|Column)\\(|new TelemetryConfig\\(|FlushEvents" src tests -g '*.cs'`. It is the authoritative exhaustive search before a batch; relevant current direct consumers include `Program.cs:1028/1036` (engine/rule ValidateAsync), `PublicApiAndPipelineTests.cs:24,33,56,68,94,96,120,121,276,284,291,299,308,316,445`, `CoverageExpansionTests.cs:38,54`, and `UnitTests.cs:329`. The batch owner must attach the command output to the batch evidence after adding any new caller; a definition-only list is not sufficient.

No positional parameter, primary constructor, deconstruction, or public return type of `DataGuardConfiguration`, `BaselineFile`, `SnapshotTable`, `SnapshotColumn`, `StoredProcedureDescriptor`, `ValidationResult`, or `TelemetryConfig` may change. Each requires an unchanged compiled-consumer fixture and legacy serialized-fixture test before closure.

### Frozen status and exit contract

The selected contract from Phase 3 is now the CP1 candidate: CLI option > `DATAGUARD_*` environment > YAML > product default; an explicit blank or invalid option is usage error and may not erase YAML. Detailed additive envelopes carry acquisition, execution, drift, rule coverage, and incomplete metadata. Existing APIs retain their signatures and fail closed through the detailed adapter when their legacy shape cannot represent non-success.

| Outcome | Exit |
|---|---:|
| Complete validate/oracle-check with no Error violations; complete assess without findings; successful refresh/baseline/init; complete no-drift diff | 0 |
| Complete validation/assessment finding, or complete diff with `--fail-on-drift` | 1 |
| Invalid arguments, configuration, provider, format, or required option (including Manual without `--assembly`) | 2 |
| Validation, diff, or acquisition incomplete/unavailable; missing baseline; unsupported snapshot version | 3 |
| Corrupt/malformed baseline, I/O, DB, other operational error, or assessment tool error | 4 |
| Cancellation is causal / Ctrl-C | 130 |

Failure precedence is usage 2 before execution; causal cancellation 130; then operational 4, incomplete 3, findings 1, clean 0. A complete drift without `--fail-on-drift` remains exit 0 but emits explicit drift. SARIF, evidence, and both IDE completion paths must receive the same non-clean status; incomplete input cannot publish a normal success payload. Explicit legacy violation diff is complete only for its deprecated violation semantics and never proves DDL drift.

## Ledger and evidence policy

The ledger has exactly 66 parent rows (60 scout IDs plus V01–V06) and 15 open RT child acceptances. CS-01 is the sole exact alias of CI-01. CS-03 remains a no-change probe, CE-05/CS-06 remain characterization work, F3 remains historical, and F6 remains owner-gated. No row is closed by this evidence report. Each phase must record its canonical ID, relationship, source priority/confidence, decision, leased files, ACs, evidence tier, prerequisite, Sol verdict, and timestamp before moving a row to `verified` or `closed`.

Scenario ownership is defined solely by the detailed Scenario → acceptance mapping below. A scenario can cross phases through its listed ACs; no summary grouping overrides that table.

### Parent accountability and evidence tier register

All rows stay `open`; owner is Terra and reviewer is Sol. `static` is design/source evidence, `unit` needs focused regression, `integration` needs controlled fixture, `live-db` needs provider marker, `windows` needs Windows host evidence, and `owner` requires explicit owner approval. These rows are the one accountable phase/evidence tier for every `AC-*` parent.

| Parent ACs | Phase | Implementation tier → final closure tier |
|---|---:|---|
| AC-CS-03, AC-F3 | 1 | static → static |
| AC-CE-01, AC-CE-02, AC-CE-04, AC-CE-06, AC-CE-07, AC-CE-08, AC-CE-09, AC-AD-04, AC-AD-05 | 2 | unit → unit plus dependent phase consumer evidence |
| AC-AD-01, AC-CE-05 | 2 | unit → live-db (V03 / V02 respectively) |
| AC-CS-01, AC-CS-02, AC-AD-02, AC-CI-01, AC-CI-02, AC-CI-03, AC-CI-05, AC-CI-09, AC-F4, AC-V06 | 3 | unit → unit plus CLI status evidence |
| AC-AD-03 | 3 | unit → live-db (V03) |
| AC-CS-04, AC-CS-08, AC-CS-09, AC-CS-10, AC-CS-11, AC-CS-12, AC-CS-13, AC-CS-14 | 4 | unit → unit |
| AC-CS-07 | 4 | unit → Windows/DPAPI evidence (V04) |
| AC-TL-01, AC-TL-04, AC-TL-05, AC-TL-06, AC-CI-04 | 5 | unit → unit |
| AC-CI-06 | 5 | unit → Windows/VS Code containment evidence (V04) |
| AC-V01 | 5 | npm audit/package test → VSIX package smoke |
| AC-CE-03, AC-CS-05, AC-CS-06, AC-AD-06, AC-TL-02, AC-TL-03, AC-CI-07, AC-CI-10, AC-DOC-01, AC-DOC-02, AC-DOC-03, AC-DOC-04, AC-DOC-05, AC-DOC-06, AC-DOC-07, AC-DOC-08, AC-F1, AC-F2, AC-F7 | 6 | static → static/current-doc evidence |
| AC-CI-08 | 6 | static → Windows V04 host evidence |
| AC-F6 | 6 | static → owner-approved disposition |
| AC-F5, AC-V02, AC-V03, AC-V04, AC-V05 | 7 | harness/evidence → integration, live-db, Windows, benchmark, or release as applicable |

### Reverse parent → RT child links

| Parent AC | Required RT children |
|---|---|
| CE-01 | RT-09, RT-12 |
| CE-02 | RT-03, RT-14 |
| CE-07 | RT-11 |
| CE-09 | RT-03, RT-11, RT-15 |
| CS-02 | RT-01, RT-10 |
| CS-04 | RT-07 |
| CS-07 | RT-06 |
| CS-08 | RT-08 |
| CS-09 | RT-08 |
| CS-10 | RT-12 |
| CS-13 | RT-07 |
| CS-14 | RT-03, RT-05 |
| AD-02 | RT-14 |
| AD-03 | RT-10, RT-11, RT-14 |
| AD-04 | RT-09, RT-11 |
| CI-01 | RT-01, RT-02, RT-10, RT-15 |
| CI-04 | RT-04 |
| CI-05 | RT-15 |
| CI-09 | RT-03 |
| F4 | RT-01 |
| F5 | RT-13 |
| V02 | RT-13 |
| V03 | RT-10, RT-13 |
| V04 | RT-13 |
| V06 | RT-02, RT-03, RT-04, RT-10, RT-11, RT-14, RT-15 |

The other 41 parents have no RT child. A parent can become `verified` only after every child listed here has the required evidence; no child is closed by this register.

| RT child | Accountable phase | Implementation tier → final closure tier |
|---|---:|---|
| RT-01 | 1, 3 | CP1/schema contract → Phase 3 v3 compatibility evidence |
| RT-02 | 3, 5 | Phase 3 status wiring → Phase 5 completion-consumer evidence |
| RT-03 | 1, 2, 3, 4, 5 | CP1 compatibility → all status-consumer closure evidence |
| RT-04 | 5 | unit → managed hook integration |
| RT-05, RT-06, RT-07, RT-08 | 4 | unit → unit/platform evidence where parent requires it |
| RT-09 | 2 | unit → trusted-artifact / no-execution evidence |
| RT-12 | 1, 2, 4 | CP1 lifetime contract → EF/plugin lifetime evidence |
| RT-13 | 1, 7 | harness contract → live DB/Windows evidence |
| RT-15 | 1, 3, 5 | CP1 exit matrix → CLI/IDE status evidence |
| RT-10, RT-11, RT-14 | 2/3 | unit → CLI and live-provider evidence where parent requires it |

### Scenario → acceptance mapping

| Scenario | AC mapping | Accountable phase |
|---|---|---:|
| S01, S02, S04 | CI-02, CI-03 | 3 |
| S03, S21 | CE-01, RT-09, RT-12 | 2 |
| S05 | CS-08, CS-09 | 4 |
| S06 | CS-12 | 4 |
| S07 | CS-14, V06 (cancellation propagation) | 3, 4 |
| S09 | CS-14 (single inflight flush/retry) | 4 |
| S12 | CS-14 (bounded baseline/SARIF/telemetry handling) | 4 |
| S14 | CS-14 (atomic baseline publication) | 4 |
| S20 | CS-14 (bounded telemetry exporter failure) | 4 |
| S08 | CE-02 | 2 |
| S10 | CE-09, RT-03 | 2 |
| S11 | CS-13 | 4 |
| S13 | CS-02, F4 | 3 |
| S15 | F3 | 1 |
| S16 | CS-07 | 4 |
| S17 | V04 | 7 |
| S18 | V02, V03 | 7 |
| S19, S26 | CI-01, CS-01, RT-10 | 3 |
| S22 | AD-04, RT-09 | 2 |
| S23 | CI-06 | 5 |
| S24 | CI-04, RT-04 | 5 |
| S25 | CI-01, RT-10 | 3 |
| S27 | CE-06 | 2 |
| S28 | CE-02, CE-09, RT-03 | 2 |
| S29 | CI-01, CE-09, RT-15 | 3 |
| S30 | AD-03, CE-07, RT-11 | 2 |

### Preserved no-change and historical evidence

CS-03 is preserved at `plans/260912-1936-luna-src-audit/reports/verification.md:24–30`: the isolated sibling `packages.lock.json` probe emitted DG1202 as expected and did not emit DG1201, so no product fix is authorized. F3 is preserved at `plans/260912-1936-luna-src-audit/reports/plans-research.md:130–145` and `rules/workspace_governance.md`: EcoSupport proposals/research are historical or independent material and must neither be revived as production nor removed without the approved disposition process. Neither row is closed by this CP1 evidence.

### First exclusive implementation lease (inactive pending CP1 GO and user review)

Batch 2A comprises three canonical groups: CE-02 (graph-level public pipeline), CE-04 (unresolved graph dependency), and CE-09 (bounded incomplete execution). The sole writer lease is `src/DataGuard.Core/Rules/RuleDependencyGraph.cs`, `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs`, `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`, `tests/DataGuard.Core.Tests/RuleDependencyGraphTests.cs`, `tests/DataGuard.Core.Tests/CoverageExpansionTests.cs`, `tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs`, proposed `tests/DataGuard.BinaryCompatibilityFixture/DataGuard.BinaryCompatibilityFixture.csproj` and `tests/DataGuard.BinaryCompatibilityFixture/Program.cs`, plus `.tmp/scout-remediation/binary-compatibility/baseline-consumer/`, and `docs/03-components/core/{validation,rules-engine,public-api}{,.vi}.md`. It explicitly excludes `src/DataGuard.Cli/Program.cs`, configuration records, all provider adapters, workflows, and lockfiles. CE-02 and CE-09 remain open after this primitive batch until Phase 3 CLI and Phase 5 IDE/status-consumer acceptance is verified. Before this lease becomes active, CP1 needs Sol GO and the normal cook review gate; the batch must establish regression behavior and the compiled-consumer fixture plan before altering behavior.

The binary-compatibility fixture will be a new `tests/DataGuard.BinaryCompatibilityFixture/` net9.0 executable referencing the baseline `src/DataGuard.Core/bin/Release/net9.0/DataGuard.Core.dll`. Before Phase 2 edits it is built once with the baseline assembly and its output copied to the gitignored evidence path `.tmp/scout-remediation/binary-compatibility/baseline-consumer/`; after edits, the same retained output is executed with the modified Core assembly substituted by normal runtime resolution, with no recompilation. The tracked source project supplies reproducibility; the retained pre-change binary supplies the actual ABI check.

### Complete CP1 direct-call census

```text
+tests/DataGuard.GoldenCorpus.Tests/GoldenCorpusTests.cs:143:                var ruleViolations = await rule.ValidateAsync(contract, contracts, CancellationToken.None);
tests/DataGuard.GoldenCorpus.Tests/RuleCoverageTests.cs:19:        return (await rule.ValidateAsync(contract, all, CancellationToken.None)).ToList();
tests/DataGuard.Core.Tests/SqlServerIntegrationTests.cs:69:        var parser = new SqlServerStoredProcedureParser(connectionString, new DataGuardConfiguration());
src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs:45:                var violations = await job.rule.ValidateAsync(job.contract, contracts, ct);
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:24:        using var pipeline = DataGuardApi.CreatePipeline();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:33:        var result = await pipeline.ValidateAsync(new[] { entity });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:56:            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration())
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:68:            var result = await pipeline.ValidateAsync(new[] { entity });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:87:        var cred = DataGuardFactory.CreateCredentialManager(new DataGuardConfiguration());
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:94:        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:96:        var report = await pipeline.CheckDriftAsync(Array.Empty<ContractViolation>());
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:120:            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:121:            var report = await pipeline.CheckDriftAsync(Array.Empty<ContractViolation>());
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:276:        using var pipeline = DataGuardApi.CreatePipeline();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:284:        using var pipeline = DataGuardApi.CreatePipeline(config);
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:291:        using var pipeline = DataGuardApi.CreatePipeline();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:299:        var result = await pipeline.ValidateAsync(new[] { entity });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:308:        using var pipeline = DataGuardApi.CreatePipeline();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:316:        var pipeline = DataGuardApi.CreatePipeline();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:373:        var config = new DataGuardConfiguration();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:390:        var config = new TelemetryConfig(Enabled: false);
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:398:        var config = new TelemetryConfig(Enabled: true);
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:445:        using var pipeline = DataGuardApi.CreatePipeline();
tests/DataGuard.Core.Tests/UnitTests.cs:19:        var config = new DataGuardConfiguration();
tests/DataGuard.Core.Tests/UnitTests.cs:328:        var engine = new ConcurrentValidationEngine();
tests/DataGuard.Core.Tests/UnitTests.cs:329:        var concurrent = await engine.ValidateAsync(contracts, rules);
tests/DataGuard.Core.Tests/UnitTests.cs:334:            sequential.AddRange(await rule.ValidateAsync(entity, contracts));
src/DataGuard.Core/Telemetry/TelemetryCollector.cs:45:            _flushTimer = new Timer(FlushEvents, null, flushInterval, flushInterval);
src/DataGuard.Core/Telemetry/TelemetryCollector.cs:156:    public void FlushEvents(object? state)
tests/DataGuard.Core.Tests/TelemetryTests.cs:19:            new TelemetryConfig(Enabled: false, ExportEndpoint: "http://127.0.0.1:1/telemetry"),
tests/DataGuard.Core.Tests/TelemetryTests.cs:29:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:36:    public void Telemetry_FlushEvents_InvokesExportSinkWhenEnabled()
tests/DataGuard.Core.Tests/TelemetryTests.cs:40:            new TelemetryConfig(Enabled: true, ExportEndpoint: "http://127.0.0.1:1/telemetry", FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:48:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:61:            new TelemetryConfig(Enabled: false, ExportEndpoint: "http://127.0.0.1:1/telemetry"),
tests/DataGuard.Core.Tests/TelemetryTests.cs:69:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:70:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:87:            new TelemetryConfig(Enabled: true, ExportEndpoint: endpoint, FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:95:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:109:            new TelemetryConfig(Enabled: true, ExportEndpoint: endpoint, FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:117:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:128:            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:137:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:139:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:141:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:145:            collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:156:            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:171:                collector.FlushEvents(null);
tests/DataGuard.Core.Tests/TelemetryTests.cs:184:        var config = new TelemetryConfig();
tests/DataGuard.Core.Tests/TelemetryTests.cs:195:        var config = new TelemetryConfig(Enabled: true, ExportEndpoint: "https://example.com", FlushIntervalSeconds: 60, IncludeStackTraces: true);
tests/DataGuard.Core.Tests/TelemetryTests.cs:210:            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));
tests/DataGuard.Core.Tests/TelemetryTests.cs:224:            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));
tests/DataGuard.Core.Tests/TelemetryTests.cs:236:            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));
tests/DataGuard.Core.Tests/TelemetryTests.cs:246:            new TelemetryConfig(Enabled: false));
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:92:                new SnapshotTable("CUSTOMERS", new[]
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:94:                    new SnapshotColumn("ID", "NUMBER", null, null, 22, 0, false, null),
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:164:        var act1 = () => new SqlServerStoredProcedureParser(null!, new DataGuardConfiguration());
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:177:        var act1 = () => new EfModelSource(null!, new DataGuardConfiguration());
src/DataGuard.Core/Baseline/BaselineManager.cs:66:        var baseline = new BaselineFile(
src/DataGuard.Core/Baseline/BaselineManager.cs:286:        return new BaselineFile(
tests/DataGuard.Core.Tests/PhantomIdentifierRuleTests.cs:43:        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { schema });
tests/DataGuard.Core.Tests/PhantomIdentifierRuleTests.cs:55:        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { schema });
tests/DataGuard.Core.Tests/PhantomIdentifierRuleTests.cs:67:        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { schema });
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:36:        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 2);
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:38:        var result = await engine.ValidateAsync(
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:52:        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 4, maxViolationQueueSize: 5);
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:54:        var result = await engine.ValidateAsync(contracts, rules);
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:125:        var source = new EfModelSource(context, new DataGuardConfiguration());
tests/DataGuard.Core.Tests/RulesEngineTests.cs:17:        => rule.ValidateAsync(contract, all.ToList(), CancellationToken.None);
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:32:    public static ValidationPipeline CreatePipeline(DataGuardConfiguration config)
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:41:    public static ValidationPipeline CreatePipeline()
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:43:        return new ValidationPipeline(new DataGuardConfiguration());
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:64:        _telemetry = config.EnableTelemetry ? new TelemetryCollector(new TelemetryConfig(Enabled: true)) : null;
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:106:        _telemetry = new TelemetryCollector(config ?? new TelemetryConfig(Enabled: true));
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:139:                var ruleViolations = await rule.ValidateAsync(contract, contracts, cancellationToken);
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:203:    public async Task<DriftReport> CheckDriftAsync(
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:32:            config ?? new DataGuardConfiguration(),
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:34:                new DataGuardConfiguration(),
src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs:65:        var config = new DataGuardConfiguration();
src/DataGuard.Cli/Program.cs:284:                        .Select(kv => new SnapshotTable(
src/DataGuard.Cli/Program.cs:286:                            kv.Value.Select(c => new SnapshotColumn(c.Name, c.DataType, c.MaxLength, c.CharLength, c.Precision, c.Scale, c.IsNullable, c.CharUsed)).ToList()))
src/DataGuard.Cli/Program.cs:830:        return new DataGuardConfiguration();
src/DataGuard.Cli/Program.cs:1027:        var engine = new ConcurrentValidationEngine(config.MaxDegreeOfParallelism, config.MaxViolationQueueSize);
src/DataGuard.Cli/Program.cs:1028:        allViolations.AddRange(await engine.ValidateAsync(contracts, rules));
src/DataGuard.Cli/Program.cs:1036:                var ruleViolations = await rule.ValidateAsync(contract, contracts, CancellationToken.None);
tests/DataGuard.Core.Tests/SqlServerParserIntegrationTests.cs:73:        var parser = new SqlServerStoredProcedureParser(cs, new DataGuardConfiguration());
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:250:        var config = new DataGuardConfiguration();
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:275:        var config = new DataGuardConfiguration(
src/DataGuard.Core/Sources/EfModelSource.cs:640:                    var source = new EfModelSource(context, config ?? new DataGuardConfiguration());
```

### Protected record construction census

```text
+src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs:131:            result.Add(new StoredProcedureDescriptor(
tests/DataGuard.Core.Tests/ContractExportTests.cs:27:            var procedure = new StoredProcedureDescriptor(
src/DataGuard.MySql.Adapter/MySqlStoredProcedureParser.cs:96:            result.Add(new StoredProcedureDescriptor(
src/DataGuard.Cli/Program.cs:284:                        .Select(kv => new SnapshotTable(
src/DataGuard.Cli/Program.cs:286:                            kv.Value.Select(c => new SnapshotColumn(c.Name, c.DataType, c.MaxLength, c.CharLength, c.Precision, c.Scale, c.IsNullable, c.CharUsed)).ToList()))
src/DataGuard.Cli/Program.cs:496:        var config = new DataGuardConfiguration
src/DataGuard.Cli/Program.cs:830:        return new DataGuardConfiguration();
src/DataGuard.Cli/Program.cs:844:    var config = new DataGuardConfiguration
src/DataGuard.Cli/Program.cs:984:                        contracts.Add(new StoredProcedureDescriptor(
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:56:            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration())
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:87:        var cred = DataGuardFactory.CreateCredentialManager(new DataGuardConfiguration());
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:94:        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:120:            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration { BaselineFilePath = baselinePath });
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:142:        var config = new DataGuardConfiguration
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:283:        var config = new DataGuardConfiguration { EnableTelemetry = false };
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:325:        var result = new ValidationResult(1, 0, 0, 0, 0,
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:341:        var result = new ValidationResult(1, 1, 1, 0, 0, violations, TimeSpan.Zero, "1.0");
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:373:        var config = new DataGuardConfiguration();
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:381:        var config = new DataGuardConfiguration { EnableAuditLogging = false };
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:390:        var config = new TelemetryConfig(Enabled: false);
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:398:        var config = new TelemetryConfig(Enabled: true);
tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:417:        var config = new DataGuardConfiguration { EnableAuditLogging = true, AuditLogPath = logPath };
src/DataGuard.Core/Models/Configuration.cs:86:        return new DataGuardConfiguration
src/DataGuard.Core/Baseline/BaselineManager.cs:66:        var baseline = new BaselineFile(
src/DataGuard.Core/Baseline/BaselineManager.cs:286:        return new BaselineFile(
src/DataGuard.Core/Rules/RuleDependencyGraph.cs:208:        return new ValidationResult(errors.ToImmutableArray(), warnings.ToImmutableArray());
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:92:                new SnapshotTable("CUSTOMERS", new[]
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:94:                    new SnapshotColumn("ID", "NUMBER", null, null, 22, 0, false, null),
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:164:        var act1 = () => new SqlServerStoredProcedureParser(null!, new DataGuardConfiguration());
tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:177:        var act1 = () => new EfModelSource(null!, new DataGuardConfiguration());
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:43:        return new ValidationPipeline(new DataGuardConfiguration());
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:64:        _telemetry = config.EnableTelemetry ? new TelemetryCollector(new TelemetryConfig(Enabled: true)) : null;
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:106:        _telemetry = new TelemetryCollector(config ?? new TelemetryConfig(Enabled: true));
src/DataGuard.Core/PublicApi/PublicApiSurface.cs:165:        return new ValidationResult(
src/DataGuard.Core/Sources/SqlServerParsers.cs:70:            contracts.Add(new StoredProcedureDescriptor(
src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs:65:        var config = new DataGuardConfiguration();
src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs:537:        var config = new DataGuardConfiguration
src/DataGuard.Core/Sources/ManualContractSource.cs:87:                contracts.Add(new StoredProcedureDescriptor(
tests/DataGuard.Core.Tests/TelemetryTests.cs:19:            new TelemetryConfig(Enabled: false, ExportEndpoint: "http://127.0.0.1:1/telemetry"),
tests/DataGuard.Core.Tests/TelemetryTests.cs:40:            new TelemetryConfig(Enabled: true, ExportEndpoint: "http://127.0.0.1:1/telemetry", FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:61:            new TelemetryConfig(Enabled: false, ExportEndpoint: "http://127.0.0.1:1/telemetry"),
tests/DataGuard.Core.Tests/TelemetryTests.cs:87:            new TelemetryConfig(Enabled: true, ExportEndpoint: endpoint, FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:109:            new TelemetryConfig(Enabled: true, ExportEndpoint: endpoint, FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:128:            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:156:            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
tests/DataGuard.Core.Tests/TelemetryTests.cs:184:        var config = new TelemetryConfig();
tests/DataGuard.Core.Tests/TelemetryTests.cs:195:        var config = new TelemetryConfig(Enabled: true, ExportEndpoint: "https://example.com", FlushIntervalSeconds: 60, IncludeStackTraces: true);
tests/DataGuard.Core.Tests/TelemetryTests.cs:210:            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));
tests/DataGuard.Core.Tests/TelemetryTests.cs:224:            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));
tests/DataGuard.Core.Tests/TelemetryTests.cs:236:            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));
tests/DataGuard.Core.Tests/TelemetryTests.cs:246:            new TelemetryConfig(Enabled: false));
src/DataGuard.Core/Sources/EfModelSource.cs:640:                    var source = new EfModelSource(context, config ?? new DataGuardConfiguration());
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:125:        var source = new EfModelSource(context, new DataGuardConfiguration());
tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:141:        var source = new EfModelSource(context, new DataGuardConfiguration
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:32:            config ?? new DataGuardConfiguration(),
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:34:                new DataGuardConfiguration(),
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:79:            config: new DataGuardConfiguration { AllowPlaintextConfigFallback = true },
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:103:        var provider = CreateProvider(new DataGuardConfiguration { KeyVaultUri = "https://example.com/vault" });
tests/DataGuard.Core.Tests/ZeroTrustCredentialProviderTests.cs:113:        var provider = CreateProvider(new DataGuardConfiguration { VaultAddress = "http://vault.local:8200" });
tests/DataGuard.Core.Tests/SqlServerParserIntegrationTests.cs:73:        var parser = new SqlServerStoredProcedureParser(cs, new DataGuardConfiguration());
tests/DataGuard.Core.Tests/UnitTests.cs:19:        var config = new DataGuardConfiguration();
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:250:        var config = new DataGuardConfiguration();
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:275:        var config = new DataGuardConfiguration(
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:347:        var config = new DataGuardConfiguration { EnableSmartDefaults = false };
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:359:        var config = new DataGuardConfiguration { ConnectionString = "Database=Db" };
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:371:        var config = new DataGuardConfiguration { ConnectionString = "Server=db;Database=Db;Trusted_Connection=True" };
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:382:        var config = new DataGuardConfiguration { ConnectionString = "User Id=app;Password=pw;Data Source=oraclehost:1521/ORCL" };
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:393:        var config = new DataGuardConfiguration { ConnectionString = "Data Source=host;Service_Name=ORCL;User Id=app" };
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:403:        var config = new DataGuardConfiguration { ConnectionString = "just-a-string" };
tests/DataGuard.Core.Tests/AuditAndConfigTests.cs:414:        var config = new DataGuardConfiguration
tests/DataGuard.Core.Tests/SqlServerIntegrationTests.cs:69:        var parser = new SqlServerStoredProcedureParser(connectionString, new DataGuardConfiguration());
```

## Sol checkpoint

CP0 Sol verdict was `REVISE` before this report existed. The required evidence and contract inventory are now durable. CP1 review must confirm the record, the reverse parent-to-RT mappings, the exact exit matrix, and the first 3–5-finding file lease before any production edit.

## CP2 — core execution and provider-default batch

The original 2A lease was expanded before CP2 review because the implementation required the directly affected configuration default, Oracle metadata reader, executable ABI fixture, and their regression coverage. The active sole-writer lease is now:

- `src/DataGuard.Core/Rules/RuleDependencyGraph.cs`
- `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs`
- `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`
- `src/DataGuard.Core/Models/Configuration.cs`
- `src/DataGuard.Oracle.Adapter/OracleReaders.cs`
- `tests/DataGuard.Core.Tests/{RuleDependencyGraphTests,CoverageExpansionTests,ConcurrentValidationExecutionTests,PublicApiAndPipelineTests,AuditAndConfigTests,UnitTests}.cs`
- `tests/DataGuard.BinaryCompatibilityFixture/{DataGuard.BinaryCompatibilityFixture.csproj,Program.cs,packages.lock.json}`
- `docs/03-components/core/{validation,baseline}{,.vi}.md`, `docs/07-testing/test-strategy{,.vi}.md`, and `docs/product-discovery/release-evidence.md`
- this evidence record and the gitignored retained ABI evidence below.

`Program.cs`, workflows, provider database operations, and unrelated adapters remain outside this batch. The fixture's `obj/` output and the retained binary evidence are gitignored; no generated output is placed at repository root.

### Changes and contract

- Graph execution rejects unresolved dependency placeholders and forms true dependency levels.
- Concurrent execution uses bounded ordinal batches, canonical result ordering, global graph cap accounting, explicit incomplete status, exact known drop counts, and fail-closed legacy API behavior.
- Negative violation caps consistently mean the documented default (`100,000`) in engine, graph, and pipeline; zero cap still evaluates work.
- `ValidationResult` gains additive execution metadata without changing its positional constructor or existing public method return types. A baseline cannot make an incomplete run clean.
- Smart defaults are applied only when enabled; Oracle ref-cursor describe is opt-in by default; Oracle metadata commands bind parameters by name.

### CP2 commands and results

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~ConcurrentValidation" --no-restore` | 0 | 11 passed, 0 failed; covers engine, graph executor, pipeline overflow/baseline status, zero cap, cap normalization, dependency levels, cancellation, and bounded concurrency. |
| `dotnet restore .tmp/scout-remediation/binary-compatibility/run-20260913/baseline-source/src/DataGuard.Core/DataGuard.Core.csproj --locked-mode` | 0 | Restored the archived `HEAD` baseline only in gitignored evidence storage. |
| `dotnet build .tmp/scout-remediation/binary-compatibility/run-20260913/baseline-source/src/DataGuard.Core/DataGuard.Core.csproj --configuration Release --no-restore` | 0 | Baseline `DataGuard.Core.dll` built with 0 warnings and 0 errors. |
| `dotnet build tests/DataGuard.BinaryCompatibilityFixture/DataGuard.BinaryCompatibilityFixture.csproj --configuration Release --no-restore -p:BaselineCoreAssembly="/Volumes/Data/101.AI/GitHub/eco_support_net_oracle/.tmp/scout-remediation/binary-compatibility/run-20260913/baseline-source/src/DataGuard.Core/bin/Release/net9.0/DataGuard.Core.dll" --output .tmp/scout-remediation/binary-compatibility/run-20260913/baseline-consumer` | 0 | Consumer compiled against the retained baseline Core DLL with 0 warnings and 0 errors. |
| `dotnet .tmp/scout-remediation/binary-compatibility/run-20260913/baseline-consumer/DataGuard.BinaryCompatibilityFixture.dll` | 0 | Baseline consumer printed `DataGuard.ValidationPipeline`. |
| `cp src/DataGuard.Core/bin/Release/net9.0/DataGuard.Core.dll .tmp/scout-remediation/binary-compatibility/run-20260913/baseline-consumer/DataGuard.Core.dll && dotnet .tmp/scout-remediation/binary-compatibility/run-20260913/baseline-consumer/DataGuard.BinaryCompatibilityFixture.dll` | 0 | The unchanged baseline-compiled consumer printed `DataGuard.ValidationPipeline` against the current Core DLL. |

The ABI check proves this fixture's retained baseline consumer can load and call the current Core assembly. It does not certify database, Windows, or provider live behavior; those remain their designated later evidence gates.

### CP2 Sol revise resolution

The retained binary fixture now compiles against the baseline assembly while binding the pre-existing `DataGuardConfiguration` and `ValidationResult` deconstructors, `ValidationResult` positional constructor, `DataGuardApi.CreatePipeline`, and legacy `ConcurrentValidationEngine.ValidateAsync`. Its unchanged output is then run after substituting the current Core DLL. The focused execution tests include an exact-N complete case and a synchronization barrier that requires two jobs to overlap while asserting the configured degree ceiling. The baseline-overflow test uses unique rule IDs (`DG900`/`DG901`) and a clean entity descriptor so built-in rules cannot supply its result incidentally.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~ConcurrentValidationExecutionTests" --no-restore` | 0 | 6 passed, 0 failed; includes exact-N, actual overlap/degree ceiling, cancellation, unique-ID pipeline/baseline overflow, zero cap, and cap normalization. |
| Baseline-compiled fixture followed by current-DLL substitution (same commands as CP2 ABI check) | 0 | Both executions printed `DataGuard.ValidationPipeline:True:0`; constructor/deconstruction and legacy-engine call are bound by the retained consumer. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | 0 warnings, 0 errors. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore` | 0 | 454 passed, 0 failed at that CP2 checkpoint. |
| `./scripts/verify_docs_sync.sh` | 0 | Required bilingual artifacts are present. |
| `git diff --check` | 0 | No tracked whitespace errors. |

Ledger state remains `open` for CE-02, CE-04, CE-08, CE-09, AD-01, and AD-04. CP2 supplies their direct implementation evidence; the ledger's remaining Phase 3 consumer, live-provider, and later status-consumer acceptance prevents premature closure. Public API and Oracle bilingual documentation now state smart-default opt-out, named Oracle binds, and the default metadata-only REF CURSOR contract.

The final CP2 rerun after the additional exact-N regression completed with `dotnet build DataGuard.sln --configuration Release --no-restore` exit 0 (0 warnings, 0 errors), `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore` exit 0 (455 passed, 0 failed), `./scripts/verify_docs_sync.sh` exit 0, and `git diff --check` exit 0.

The final ABI/overlap correction rerun completed with the exact fixture build command recorded above, baseline and substituted-current fixture executions both printing `DataGuard.ValidationPipeline:True:0`, focused concurrent execution tests 6 passed/0 failed, and `git diff --check` exit 0. The fixture now also binds the baseline `ValidationResult.Deconstruct` signature; peak concurrency uses atomic compare-exchange accounting.

Sol CP2 final verdict: `GO`. The next Phase 2 batch must receive a separate lease and checkpoint before its rows move beyond direct implementation evidence.

## CP2B — trusted EF input, parser integrity, and provider inventory

Lease: `EfModelSource.cs`, `SqlServerParsers.cs`, raw-contract/rule graph seams, `ProviderRuleCatalog.cs`, `Program.cs` composition, their Core tests, and the matching EN/VI sources/Oracle docs. The batch does not claim live SQL Server, Oracle, PostgreSQL, MySQL, Windows unload/file-replacement, or CLI outcome-format proof.

- Source-only EF snapshots and automatic assembly/DbContext discovery now raise an explicit unsupported error. The only compiled-model path requires an exact, non-linked trusted DLL and exact concrete `ModelSnapshot` type in a collectible load context.
- SQL Server result metadata drops hidden and nameless browse columns. Raw SQL carries explicit parse status/error and `DG016` converts malformed ScriptDOM input into an Error finding.
- `ProviderRuleCatalog` owns provider membership; CLI executes only `Ready` entries, while PostgreSQL PG004 and Oracle DG012 retain their analyzer-context prerequisite instead of being silently run as no-ops. Oracle binds use colon syntax and `CHAR_USED` is canonical `B`/`C`.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~EfModelSourceTests" --no-restore` | 0 | 5 passed: source-only rejection, exact trusted artifact/type acceptance and mismatch rejection. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~RawSqlParserTests|FullyQualifiedName~RuleDependencyGraphTests" --no-restore` | 0 | 16 passed: malformed raw SQL status and updated built-in graph coverage. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~ProviderRuleCatalogTests" --no-restore` | 0 | 2 passed: MySQL IDs are unique/complete and PG004 is unavailable with a reason. |
| `dotnet test DataGuard.sln --configuration Release --no-restore --no-build --logger "console;verbosity=minimal"` | 0 | 501 passed, 0 failed: Core 459, Golden 25, Analyzers 5, CodeFixes 12. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Build clean; bilingual presence and whitespace checks pass. |

CE-01, CE-05, CE-07, AD-01, AD-03, AD-04, AD-05 remain open until their required Phase 3 consumers or live-provider/Windows gates are proven. No row is closed by CP2B.

## Phase 4 incremental evidence — reporting boundary and baseline persistence

The built-in reporting paths now share a non-mutating SARIF boundary sanitizer. Buffered SARIF, `FileSarifSink` in both write modes, direct `StreamingSarifSink` input, and console output redact the hostile message corpus; SARIF properties retain only allowlisted scalar values. Artifact URIs are projected relative to a source root (current directory by default), and external, traversal, unresolved, or lexically sensitive paths become an empty URI rather than exposing an absolute workspace/customer path. Third-party sinks remain trusted code outside this boundary.

Baseline persistence is now capped at 16 MiB, cancellation-aware, and atomically replaces only after writing and flushing a unique same-directory temporary file. A canceled operation before publication leaves the prior baseline untouched. This is direct implementation evidence only; link/reparse containment and concurrent writer behavior remain open Phase 4 work.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~DiagnosticEmitterFullTests --no-restore` | 0 | 35 passed, 0 failed; covers buffered and streaming SARIF redaction, allowlisted properties, external-path suppression, and console redaction. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~BaselineManagerTests\|FullyQualifiedName~BaselineMigrationTests" --no-restore` | 0 | 8 passed, 0 failed; includes canceled baseline creation preserving the prior target and temporary-file cleanup. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | 0 warnings, 0 errors. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | 507 passed, 0 failed: Core 465, Golden 25, Analyzers 5, CodeFixes 12. |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace validation pass. |

Phase 4 is still open: assessment reader containment, credential-store filesystem containment, telemetry lifecycle/bounds, deterministic nested exports, plugin lifecycle proof, and full audit allowlists require their own evidence.

## Phase 3 incremental evidence — shared CLI resolution

Database-backed commands now call one resolver for connection and provider context. The order is command-line connection, `DATAGUARD_CONNECTION_STRING`, then YAML `ConnectionString`; provider order is command-line provider, YAML `DefaultProvider`, then `sqlserver`. The resolver returns a copied configuration, so loading and precedence application do not mutate the configuration object supplied by the caller. This covers `validate`, `baseline`, `snapshot refresh`, `snapshot diff`, and `oracle-check` (whose provider remains explicitly Oracle).

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~ProviderRuleCatalogTests --no-restore` | 0 | 4 passed, 0 failed; verifies rule catalog behavior plus CLI command-line/environment/YAML/default precedence and non-mutation. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build is clean; bilingual documentation and whitespace checks pass. |

CLI outcome envelopes, unavailable-rule reporting, status propagation to SARIF/evidence, and live-provider acceptance remain open. This resolver evidence does not close those requirements.

## Phase 4 incremental evidence — assessment input containment

`AssessmentPathPolicy` now performs both lexical containment and existing-target link resolution before Core assessment readers open a file. It is used by project inventory, legacy `packages.config`, secret/machine-path scans, dependency lock readers, and build/CI readers. Inputs escaping the workspace by traversal, sibling-prefix, or a resolved file/directory link are rejected; produced evidence uses normalized relative paths. The policy documents its unavoidable check-to-open TOCTOU limit.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~AssessmentPackTests\|FullyQualifiedName~PackagesConfigReaderTests" --no-restore` | 0 | 14 passed, 0 failed; includes valid input, malformed/missing input, external, sibling-prefix, traversal, and file-link escape cases (the link case explicitly skips only when the platform cannot create it). |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build clean; full suite 511 passed (Core 469, Golden 25, Analyzers 5, CodeFixes 12); documentation synchronization and whitespace validation pass. |

This does not close all containment ACs: bounded traversal/enumeration, cancellation/partial status, directory-link/junction coverage, credential-store path policy, and every remaining assessment enumeration still require further Phase 4 work.

## Phase 4 incremental evidence — telemetry flush retention

`TelemetryCollector` now exposes additive `FlushAsync(CancellationToken)` while retaining the synchronous `FlushEvents(object?)` surface. Timer callbacks start the asynchronous path, one semaphore prevents overlapping exports, and an export failure or cancellation re-enqueues its drained batch for retry instead of silently dropping it. Disabled telemetry still exits before export; rejected endpoints remain an explicit no-egress drop policy. `TelemetryFlushResult` exposes no-work, in-progress, circuit-open, rejected-endpoint, failed/cancelled, and exported states.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~Telemetry --no-restore` | 0 | 18 passed, 0 failed; includes retained batch retry after a failed exporter. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build clean; full suite 512 passed (Core 470, Golden 25, Analyzers 5, CodeFixes 12); documentation and whitespace validation pass. |

Telemetry lifecycle remains incomplete: bounded export timeout, async disposal/final flush, queue/payload caps, terminal-loss accounting, and legacy exporter detachment still require implementation and race tests.

`TelemetryCollector` now also implements additive `IAsyncDisposable`: `DisposeAsync()` stops its timer and awaits one final flush bounded by `ExportTimeoutSeconds`, while legacy `Dispose()` remains best effort. Post-disposal recording and flushing are no-ops, and the flush semaphore is retained until garbage collection so an already-running timer callback cannot race a disposed synchronization primitive. A hanging legacy export delegate is timed out without allowing overlapping retries; the retained in-flight task releases the gate only after it eventually completes.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~Telemetry" --no-restore` | 0 | 23 passed, 0 failed; includes a hanging sink proving `DisposeAsync()` returns within the configured one-second export timeout. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | 629 passed, 0 failed (Core 575, Golden 25, Analyzers 8, CodeFixes 21). |

## Phase 3 incremental evidence — unavailable provider-rule outcome

Core now has additive `RuleExecutionOutcome` / `RuleExecutionState`, with `Unavailable` and `Failed` explicitly making a requested run incomplete while `Skipped` remains legitimate non-applicability. `ProviderRuleCatalog` creates the unavailable outcome with its prerequisite reason. The CLI validate path consumes those selected unavailable registrations before normal output, writes the rule/reason, returns exit code 3, and suppresses normal text/SARIF/evidence/contracts/TypeScript success payloads.

| Command | Exit | Result |
|---|---:|---|
| `dotnet src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll validate --provider postgresql` | 3 | Printed `Validation incomplete: PG004 unavailable: Requires analyzer DbContext provider-registration metadata.` and emitted no normal success payload. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build clean; full suite 514 passed (Core 472, Golden 25, Analyzers 5, CodeFixes 12); documentation and whitespace validation pass. |

This is not full outcome propagation: evaluated/failed outcomes are not yet produced per rule by Core engines, and incomplete machine-readable SARIF/evidence metadata plus IDE consumption remain open.

## Phase 3/4 incremental evidence — per-rule Core execution outcomes

Both Core execution paths now publish additive `RuleOutcomes`. Concurrent batches
record every selected rule as `Evaluated`, including rules that produced no
violations. Sequential execution records the same per-rule findings, and, when a
violation cap stops later work, records each unstarted rule as `Skipped` with the
cap reason while the enclosing result remains incomplete. This corrects the prior
limitation in the unavailable-provider batch; it does not yet add failed-rule
recovery or surface every outcome in SARIF/evidence/IDE consumers.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ConcurrentValidationExecutionTests --logger "console;verbosity=minimal"` | 0 | 8 passed, 0 failed; covers concurrent clean-rule outcome and sequential evaluated/skipped outcomes when the cap stops execution. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | 517 passed, 0 failed: Core 475, Golden 25, Analyzers 5, CodeFixes 12. |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace validation pass. |

`Skipped` is now documented as a non-executed rule state whose reason must be read
with the enclosing execution status; it is not presented as a successful
evaluation. Timeout/failure outcomes, machine-readable status export, and IDE
handling remain open acceptance work.

## Phase 4 incremental evidence — bounded telemetry export

Telemetry now bounds retained memory and waiting time without changing the
`TelemetryConfig` primary constructor. Additive init properties set the maximum
queued/in-flight events (10,000), events per batch (1,000), UTF-8 payload bytes
(1 MiB), and exporter wait (5 seconds). `DroppedEventCount` exposes queue,
oversized-payload, and rejected-endpoint loss. A timed-out legacy delegate keeps
the single-flight gate until it actually finishes, preventing timer ticks from
starting overlapping exports while the retained batch awaits a later retry.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~Telemetry --logger "console;verbosity=minimal"` | 0 | 23 passed, 0 failed; includes failed-batch retry, final async flush, bounded final flush for a hanging sink, queue loss accounting, oversized-event isolation, timeout retention, and no-overlap after timeout. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build has 0 warnings/errors; full C# suite 629 passed (Core 575, Golden 25, Analyzers 8, CodeFixes 21); documentation synchronization and whitespace validation pass. |

The remaining telemetry acceptance is explicit behavior after a failed final flush
and broader record/flush/dispose race coverage. The collector now exposes an
`Active → Stopping → Stopped` lifecycle and `TerminalLossCount`; the final wait is
bounded. No delivery guarantee is claimed for an uncancellable legacy delegate.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~Telemetry" --no-restore` | 0 | 24 passed, 0 failed; covers lifecycle transition, terminal-loss accounting, post-stop refusal, and bounded final flush. |

## VS Code incremental verification — compiler fixture and package

The extension test command now compiles a representative generated TypeScript
contract fixture with strict checking before running security tests. The package
contains that fixture only as test evidence and still packages the embedded LSP.

| Command | Exit | Result |
|---|---:|---|
| `npm test` (in `src/DataGuard.VSCode`) | 0 | TypeScript fixture compilation and 3 security tests passed. |
| `npm audit --omit=dev --json` | 0 | 0 production vulnerabilities. |
| `npm run package -- --out /tmp/dataguard-vscode-smoke/dataguard-vscode.vsix` | 0 | VSIX packaged successfully with 19 files, 70.17 KiB. |

## Phase 4 incremental evidence — acceptance checklist reconciliation

The Phase 4 checklist now marks encrypted-store fail-closed behavior, unified SARIF/audit
redaction, assessment containment, baseline/telemetry cancellation/bounds, and
deterministic TypeScript compilation as complete against the dated test evidence below.
The VS Code package now compiles a representative generated contract fixture with
TypeScript strict checking.

| Command | Exit | Result |
|---|---:|---|
| `npm test` (in `src/DataGuard.VSCode`) | 0 | TypeScript compiler fixture passes, alongside the extension security tests (3 passed). |

## Phase 4 incremental evidence — bounded assessment discovery

Assessment project, lock-file, config, and `.dataguard.yml` discovery now flows
through a shared enumerator capped at 10,000 files per pattern. Reaching the project
or config cap emits `DG1007` and returns a partial report; enumeration failures remain
fail-closed, directory reparse points are skipped, and existing path containment checks
still run before readers open files.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~AssessmentPackTests" --no-restore` | 0 | 13 passed, 0 failed; includes project/config discovery caps, DG1007 partial reporting, oversized lock rejection as DG1204, and cancelled local assessment returning DG1006 without remote egress. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | 638 passed, 0 failed (Core 584, Golden 25, Analyzers 8, CodeFixes 21). |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, `./scripts/verify_docs_sync.sh`, `git diff --check` | 0 | Release build clean; bilingual assessment docs and whitespace validation pass. |

Build/CI and dependency-health readers also reject project, `global.json`, and lock
files above the shared 2 MiB input cap before JSON/XML parsing, preventing oversized
single-file allocations from bypassing the discovery bound.

## Phase 4 incremental evidence — credential-store bounds and containment

`CredentialManager` now validates the credential file and existing parent chain for
symbolic-link/reparse-point traversal, caps reads and writes at 1 MiB, and publishes
records through a flushed same-directory temporary file with owner-only Unix mode.
The path is checked again immediately before replacement; the documented residual
limitation is the unavoidable check-to-open TOCTOU window.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~CredentialManagerFullTests" --no-restore` | 0 | 18 passed, 0 failed; includes oversized-store rejection, symlinked-parent rejection, owner-only Unix mode, and cancellation propagation. |
| `./scripts/verify_docs_sync.sh`, `git diff --check` | 0 | Bilingual security documentation and whitespace validation pass. |

## Phase 4 incremental evidence — pipeline-owned plugin lifecycle

`ValidationPipeline.WithPlugins(...)` now retains every `RulePluginManager` it creates,
including repeated calls, and disposes/clears all owned managers when the pipeline is
disposed. This closes the pipeline ownership gap while preserving cooperative
collectible-context unloading semantics.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~ValidationPipeline_WithPlugins" --no-restore` | 0 | 2 passed, 0 failed; repeated plugin calls are owned and the pipeline releases all manager references on disposal. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | 632 passed, 0 failed (Core 578, Golden 25, Analyzers 8, CodeFixes 21). |
| `./scripts/verify_docs_sync.sh`, `git diff --check` | 0 | Bilingual plugin documentation and whitespace validation pass. |

## Phase 5 incremental evidence — managed hook preservation

Native Git and Husky pre-commit installation now writes the generated script,
uses portable POSIX `sh` redirection, and marks Unix files executable. Generated
commands use persisted-Snapshot validation (`dataguard validate --format text`)
rather than invalid `--offline` without `--assembly`. Install and uninstall use a
DataGuard marker to preserve user-owned native/Husky hooks and `lefthook.yml`,
including when a caller supplies force. This is direct local lifecycle evidence;
worktree `.git` indirection, linked ancestors, atomic publication, and full
lefthook merge behavior remain open Phase 5 acceptance.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PreCommitHookInstallerTests --logger "console;verbosity=minimal"` | 0 | 2 passed, 0 failed; verifies generated native/Husky content, portable command, uninstall of managed output, and preservation of foreign content. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Full C# suite 523 passed (Core 481, Golden 25, Analyzers 5, CodeFixes 12); documentation synchronization and whitespace validation pass. |

## Phase 12 incremental evidence — reachable wizard and managed hooks

The existing interactive configuration builder and managed-hook installer are now
reachable from the shipped CLI. `dataguard init --wizard --output <path>` passes
the explicit destination through to the wizard instead of silently writing the
default path. `dataguard hook install`, `status`, and `uninstall` delegate to the
existing marker-preserving lifecycle service; unsupported hook types are usage
errors. The implementation does not relax installer ownership rules or add
credentials to generated configuration.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~CliExitCodeTests\|FullyQualifiedName~PreCommitHookInstallerTests' --logger 'console;verbosity=minimal'` | 0 | 12 passed, 0 failed; runs the CLI binary in temporary directories, verifies `hook install/status/uninstall`, managed-file removal, and wizard output-path selection. |
| `dotnet build src/DataGuard.Cli/DataGuard.Cli.csproj --configuration Release --no-restore` | 0 | CLI and all referenced projects build with 0 warnings/errors. |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Bilingual documentation synchronization and whitespace validation pass. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; suite passes 578 tests (Core 529, Golden 25, Analyzers 6, CodeFixes 18). |

This closes only the local command-reachability regression. Interactive UX
coverage, hostile-workspace behavior, and the remaining Phase 12 occurrence
matrix remain open.

## Phase 5 incremental evidence — truthful advertised code fixes

`DataGuardCodeFixProvider` now advertises only IDs that reach a registered action:
parameter mismatch, naming, length, supported dialect notes, provider option, and
unvalidated SQL. Direction, column-shape, nullability, byte-length, inferred-size,
and unmapped-type diagnostics are no longer advertised without an actionable,
safe transformation. The provider also returns cleanly when Roslyn cannot produce a
syntax root.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj --configuration Release --no-restore --logger "console;verbosity=minimal"` | 0 | 13 passed, 0 failed; includes the advertised-ID set and absent unsupported IDs. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, full C# tests, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build has 0 warnings/errors; full suite 524 passed (Core 481, Golden 25, Analyzers 5, CodeFixes 13); documentation synchronization and whitespace validation pass. |

Action-to-final-document, duplicate-attribute, marker-suppression, and FixAll
behavior tests remain required Phase 5 work; this batch only closes false
advertisement and null-root safety.

## Phase 5 incremental evidence — VS Code SARIF lexical containment

The VS Code extension now resolves SARIF artifact locations through one workspace
resolver before creating a diagnostic URI. It accepts only workspace-relative paths
and `file:` URIs whose decoded lexical target remains inside the selected workspace;
remote schemes, malformed encodings, traversal, sibling-prefix, and external
absolute locations are logged and skipped. This preserves valid in-workspace
diagnostics while preventing SARIF from directing the Problems panel to arbitrary
paths.

| Command | Exit | Result |
|---|---:|---|
| `npm test` in `src/DataGuard.VSCode` | 0 | TypeScript compile and 3 Node tests pass; coverage includes relative/file URI inside workspace, traversal, sibling-prefix, remote scheme, and malformed percent encoding. |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace validation pass. |

Resolved link containment, VS Code integration-host diagnostics, partial-SARIF
status labeling, and packaged VSIX smoke remain open Phase 5/7 acceptance work.

## FC16 incremental evidence — deterministic YAML contract export

`ContractExportWriter.WriteYamlAsync` serializes the same sorted `ContractExport`
object used by JSON with camel-case names, nullable omission, and cancellation-aware
file output. CLI `validate --format yaml --output <path>` now selects that writer;
the format is machine-readable and cannot write to stdout. The existing YAML
dependency is reused, so no new package or runtime surface was introduced.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ContractExportTests --logger "console;verbosity=minimal"` | 0 | 4 passed, 0 failed; includes YAML camel-case schema output. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and full C# suite | 0 | Build has 0 warnings/errors; 525 tests pass (Core 482, Golden 25, Analyzers 5, CodeFixes 13). |
| `npm test` in `src/DataGuard.VSCode`, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | VS Code Node suite, documentation synchronization, and whitespace validation pass. |

CLI end-to-end YAML output/cancellation and every downstream consumer matrix are
still required before FC16 can close.

## FC01 foundation evidence — transport-neutral health coordination

Core now contains `HealthComponentStatus`, `HealthSnapshot`, `IHealthProbe`,
`HealthStateStore`, and `HealthProbeCoordinator`. The coordinator runs fixed local
probes outside transport/request code, serializes refreshes with a single-flight
gate, applies a bounded timeout, and atomically publishes the last completed
snapshot. Timeout and failure results use generic non-secret messages; cancellation
preserves the previous snapshot. Snapshot reads never start work.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HealthProbeTests --logger "console;verbosity=minimal"` | 0 | 2 passed, 0 failed; verifies sorted atomic readiness publication and timeout classification without exception detail. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`, full C# tests, `./scripts/verify_docs_sync.sh`, and `git diff --check` | 0 | Release build has 0 warnings/errors; 527 tests pass (Core 484, Golden 25, Analyzers 5, CodeFixes 13); docs and whitespace checks pass. |

No HTTP listener, host project, readiness probe implementations, remote binding,
or endpoint integration is claimed by this foundation. Those remain required FC01
delivery work.

## FC01 incremental evidence — explicit loopback health host

`DataGuard.Host` is now a standalone ASP.NET Core executable and solution member.
It binds only loopback/localhost URLs and rejects any other configured address at
launch. `/health/live` reports process liveness, while `/health/startup` and
`/health/ready` read the coordinator's cached atomic snapshot without starting
probe work. A hosted service performs the initial refresh. Empty probe configuration
is intentionally not ready, preventing a false-green 200 until real required
probes are supplied.

| Command | Exit | Result |
|---|---:|---|
| `dotnet restore DataGuard.sln --locked-mode` and `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | Locked restore is consistent; the Host builds in the solution with 0 warnings/errors. |
| Loopback smoke: `dotnet DataGuard.Host.dll --urls http://127.0.0.1:50917`; `curl` three health paths | 0 | `/health/live` returned 200 JSON; startup and ready read cached snapshots; Ctrl-C shut down cleanly. |
| Full Core test project, docs synchronization, and `git diff --check` | 0 | Core 485 tests pass; documentation and whitespace checks pass. |

Only loopback is currently supported. Required snapshot/baseline/disk/memory,
credential, and supply-chain probes; readiness transitions driven by those probes;
host integration tests; remote JWT/TLS/rate-limit policy; and publish/install
artifact evidence remain open FC01 work.

## FC02/FC03 incremental evidence — opted-in OSV advisory lookup and conservative score

`RemoteAdvisoryPolicy` requires both request and network consent plus an
operator-owned public-package allowlist. `OsvAdvisoryClient` sends only approved
NuGet name/version coordinates to the fixed HTTPS OSV origin, rejects redirects,
and bounds timeout, pages, detail requests, and response size. It returns a
structured `ToolError` for remote failure while `AssessmentEngine.RunAsync` retains
the already completed local findings. The CLI exposes the same double opt-in through
`assess --remote-advisories osv --allow-network --remote-public-package <id>`;
without it, no HTTP client is constructed. Successful observations are emitted in
the additive `remoteAdvisories` report envelope with OSV provenance. The score
formula uses `dependency-health-v1`; partial or unknown coverage has no numeric
score.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AssessmentContractTests\|FullyQualifiedName~RemoteAdvisory'` | 0 | 12 passed; verifies default zero requests, exact public-only batch body, deduplicated detail lookups, redirect/size failures, and local-plus-remote report mapping. |
| `dotnet restore DataGuard.sln --locked-mode`; `dotnet build DataGuard.sln --configuration Release --no-restore`; `dotnet test DataGuard.sln --configuration Release --no-build` | 0 | Locked restore and Release build have 0 warnings/errors; full C# suite 543 passed (Core 500, Golden 25, Analyzers 5, CodeFixes 13). |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace validation pass. |

Score integration for every supported lock shape, persistent-cache policy, retry
classification, full page-token fixture matrix, CLI integration fixtures, and
explicit CI policy remain open Phase 10 acceptance work.

## Phase 11–14 incremental evidence — source extraction, plugin admission, cache, and IDE runner

`ModelSnapshotCSharpParser` reads a bounded Roslyn syntax subset without loading
an assembly or constructing a `DbContext`; malformed/unsupported source is visible
as an extraction diagnostic. `validate --ef-snapshot` invokes this path explicitly
and its binary-level integration test verifies exported entity descriptors.

Plugin admission now happens before `AssemblyLoadContext.LoadFromAssemblyPath`:
the adjacent manifest binds plugin identity, host API, and SHA-256 digest, while the
default policy requires an independent provenance verifier. This is an admission
gate, not a claim that an admitted plugin is sandboxed. Baseline loads use a bounded
process-local content-digest cache with one-hour TTL and hit/miss counters; changed
content receives a new cache key and cache never authorizes live acquisition.

The VS Code extension now exposes local-first Assess and applies one global process
slot across Validate and Assess. A new run replaces the previous run and Cancel is
global. The extension never provides OSV remote-advisory consent.

| Command | Exit | Result |
|---|---:|---|
| Focused Core tests for `ModelSnapshotCSharpParserTests`, `EfModelSourceTests`, `CliExitCodeTests`, `PluginAdmissionTests`, and `BaselineCacheTests` | 0 | Covers bounded source parsing, binary CLI extraction, pre-load digest rejection, and content-cache invalidation. |
| `npm test` in `src/DataGuard.VSCode` | 0 | TypeScript compilation and 3 security/containment tests pass. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`; `dotnet test DataGuard.sln --configuration Release --no-build`; `./scripts/verify_docs_sync.sh`; `git diff --check` | 0 | Release build has 0 warnings/errors; full C# suite 549 passed (Core 506, Golden 25, Analyzers 5, CodeFixes 13); docs and whitespace checks pass. |

Language-server/Visual Studio host integration, signed provenance/SBOM verification,
platform secret stores, offline build task/preflight, benchmark artifacts, and all
Windows/external acceptance gates remain open. This evidence does not close those
phases.

## Phase 11–14 incremental evidence — Visual Studio assess, benchmark harness, and local security gates

The Visual Studio package now has an **Assess** command alongside Validate. Both
use the same owned-process runner, bounded cancellation path, temporary SARIF
artifact handling, and sanitized diagnostic publication; Assess invokes only local
`assess --workspace ... --format sarif` and does not imply remote-advisory
consent. The package project remains outside the cross-platform solution build.
Its direct VSIX build correctly stops on this macOS host because the VSSDK VSCT
tool path is Windows-specific; a Windows Visual Studio build/install smoke is
therefore still required.

`tools/benchmarks/DataGuard.Benchmarks` is an isolated, locked BenchmarkDotNet
harness for the bounded ModelSnapshot parser. A full `Job.Default` run on
commit `93bf7288324dd746669ad09c5e2a592adc772748`, macOS Sequoia 15.6.1,
Apple M1 Max (10 logical/physical cores), .NET SDK 9.0.310 and runtime 9.0.12,
with BenchmarkDotNet 0.15.8/InProcessEmitToolchain/Concurrent Workstation GC,
recorded 15 samples per scenario. The fixed scenario source hash was
`ee5be9587e2227d7ccbbf78e33116c83c7e3894f193d08a21d721577ee110458`:
supported input mean 27.449 microseconds (99.9% CI 27.145–27.754), 15.95 KB
allocated; malformed input mean 1.688 microseconds (99.9% CI 1.681–1.695),
2.91 KB allocated. Raw CSV/HTML/Markdown reports were emitted beneath the
gitignored `BenchmarkDotNet.Artifacts/`. BenchmarkDotNet could not raise process
priority on this host, so this is a host-scoped measurement, not a baseline or a
universal performance claim.

The harness now writes generated `benchmark-metadata.json` alongside its ignored
raw artifacts. The metadata contains the supplied commit SHA and SDK, runtime,
OS/CPU, GC, BenchmarkDotNet version, job/configuration, corpus SHA-256, and
required allocation metric. `scripts/validate_benchmark_claim.py` rejects a
missing commit or allocation metric, a changed corpus hash, an incompatible
runtime, and comparisons where corpus/runtime/job/configuration differ. A
2026-09-13 classifier dry run supplied the commit and SDK and passed the
evaluator; its intentionally mismatched runtime and temporary incomplete or
changed-corpus metadata were rejected. Dry-run timing is not performance evidence.

The pipeline pair now uses the public pipeline on one 100/1,000-contract corpus,
with four deterministic rules and degree capped at four. `GlobalSetup` normalizes
violations plus execution status/dropped count and fails before timing on any
sequential/concurrent difference. A full 2026-09-13 run measured concurrent as
7.95× slower at 100 contracts and 2.47× slower at 1,000, so the declared 2–4×
speedup target failed visibly. The run used an uncommitted worktree; the evaluator
now requires `worktreeState=clean` and therefore rejects it as a current claim.
It is retained only as preliminary failure evidence pending a clean committed run
and an owner decision about the target.

During semantic benchmark setup, a zero-diagnostic result exposed that
`ContractValidationAnalyzer` read `IArgumentOperation.Syntax` as though it were a
literal expression. The argument syntax is an `ArgumentSyntax`; literal extraction
now reads `IArgumentOperation.Value.ConstantValue` (and the value syntax for an
interpolated string). `SemanticAnalyzer_EmitsMissingFrom_ForExecuteSqlRawLiteral`
is a regression test. The new semantic benchmark prebuilds the compilation, then
requires exactly one DG098 per `ExecuteSqlRaw("SELECT 1")` invocation before timing
the analyzer. A dirty-worktree dry smoke completed at both 100 and 1,000 calls;
it is functional evidence only, not a current performance result.

`SarifExportBenchmarks` now exercises `DiagnosticEmitter` with its production
streaming `FileSarifSink` over 100 and 1,000 DG099 findings. Setup reads the emitted
file and rejects missing results/DG099 before timing. The 2026-09-13 dirty-worktree
dry smoke completed both cases; its timing and allocation output are not a current
claim, but it proves the benchmark measures non-empty streaming SARIF serialization.

`IncrementalGeneratorBenchmarks` now prebuilds C# corpora of 100 and 1,000
`ExecuteSqlRaw("SELECT * FROM BENCHMARK")` calls. Setup runs the real
`UnvalidatedSqlCallGenerator` and requires one DG001 per call before timing each new
generator driver execution. The dirty-worktree dry smoke completed both cases; it
proves a non-no-op generator benchmark path but supplies no current performance claim.

Plugin manifests now bind `ruleId` in strict policy, allowing the host to sort and
admit a complete plugin set deterministically before loading any assembly. A second
manifest with an already admitted rule ID, or a manifest whose rule ID is reserved by
the host graph, is rejected with no retained assembly bytes. Focused admission tests
cover strict missing-rule-ID, duplicate, and reserved-ID failures alongside digest and
provenance gates.

The Linux Secret Service bridge now starts asynchronous drains for stdout and stderr
before writing the secret to standard input, then waits for both streams after the
client exits. This prevents a verbose `secret-tool` failure from blocking on a full
redirected pipe; diagnostics remain generic and never include the credential value.
The focused credential-manager suite passes 14 tests after this change.

The local Docker smoke initially exposed a restore-layer defect: the Dockerfile
copied `DataGuard.Core.csproj` before restore but omitted its
`DataGuard.SqlClassification.csproj` project reference, so publish failed with
`NETSDK1004` after the full source copy. The restore layer now copies that project
file as well. A fresh local ARM64 build tagged `dataguard:remediation-smoke` completed
and `docker run --rm dataguard:remediation-smoke --help` printed the CLI command set.
The image runs as UID 1654. This verifies the Dockerfile smoke only; it does not
replace GitHub Actions/`act` workflow or multi-RID release evidence.

The same harness has a classifier-only full run over fixed `SELECT X;` source
corpora, whose generator hash is
`54cb7ac727de1648b79c3997069f867a391866aeb3221edc779ffc80d5f6d657`.
`GlobalSetup` creates the corpus and rejects any source exceeding the
classifier's 65,536-character contract, so input construction and the oversize
rejection path are not part of the measurements. On the same host/configuration,
15 samples per size recorded: one call 162.852 ns (99.9% CI 160.088–165.616),
424 B allocated; 100 calls 5.175 microseconds (99.9% CI 5.142–5.207),
10,744 B; and 1,000 calls 52.609 microseconds (99.9% CI 52.415–52.803),
97,152 B. These results describe only the local source-text classifier and do
not establish a zero-allocation or parallel-speedup claim.

| Command | Exit | Result |
|---|---:|---|
| `dotnet restore tools/benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --locked-mode`; `dotnet build ... --configuration Release --no-restore`; `dotnet run --configuration Release --no-build -- --full` | 0 | Locked restore, Release compilation, and a 15-sample in-process BenchmarkDotNet run pass; generated reports are ignored. |
| `dotnet run --project tools/benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --configuration Release --no-build -- --full --classifier-only` | 0 | A 15-sample classifier-only run passes for deterministic 1/100/1,000-call corpora; the harness rejects oversize source before measuring. |
| `DATAGUARD_BENCHMARK_COMMIT=$(git rev-parse HEAD) DATAGUARD_BENCHMARK_SDK=$(dotnet --version) dotnet run --project tools/benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --configuration Release --no-build -- --classifier-only`; claim evaluator | 0 | The dry-run harness writes project-scoped metadata and the evaluator accepts complete metadata. Negative cases for missing commit/allocation, altered corpus, and incompatible runtime exit 1 as required. |
| `actionlint .github/workflows/*.yml`; `dotnet list DataGuard.sln package --vulnerable --include-transitive --format json` | 0 | All workflow files pass lint; NuGet audit returns no vulnerable top-level or transitive packages. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~HealthHostIntegrationTests` | 0 | Starts the built Host on a random loopback port with local fixture probes; `/health/live`, `/health/startup`, and `/health/ready` each return 200 and the readiness payload has the expected snapshot shape. |
| `./scripts/verify_docs_sync.sh`; `git diff --check` | 0 | Documentation inventory and whitespace validation pass. |

The TruffleHog full-history scan, Docker/`act` workflow simulation, Windows VSIX
build/install, signed provenance/SBOM validation, and benchmark corpus/statistical
acceptance remain open external gates. No result above closes them.

## Phase 15 local regression checkpoint

The canonical local gate was run with `SKIP_ACT=1`: the intentional exception
omits only Docker-backed `act` simulations and the script's Docker fallback for
TruffleHog. It completed locked restore, workflow lint, Release build, analyzers,
format verification, all C# tests with coverage, and NuGet vulnerability audit.
A pre-existing indentation defect in `TelemetryCollector` was corrected before
the final run. The full suite now reports 551 passing tests (508 Core, 25 Golden
Corpus, 5 Analyzer, 13 CodeFixes); merged Cobertura coverage is 61.58%
(11,079/17,990), above the configured 60% threshold.

This is local regression evidence only. Hosted workflow execution, Docker/`act`,
and the externally constrained Phase 15 acceptance gates remain open.

## CP8 incremental evidence — artifact and offline-build boundary

`artifact-contract.md` now records the required artifact boundary before the
absent Build and Language Server projects are introduced. In particular,
`DataGuard.Build` can consume only an explicitly supplied offline manifest and
must fail closed for invalid input; project properties cannot authorize database,
credential, or network access. This records a design prerequisite for Phase 12,
not implementation evidence: no Build package, LSP, package import fixture, or
clean-consumer artifact has yet been produced.

## Phase 11–12 incremental evidence — offline build task and local LSP foundation

`DataGuard.Build` is now a packaged `net9.0` MSBuild task. It accepts only an
absolute `DataGuardOfflineManifest`, limits it to 1 MiB, validates schema version
one, bounded target/provider/finding fields and a SHA-256-shaped digest, and
reports stable `DG_BUILD001`–`DG_BUILD004` errors. It has no credential,
database, or network code. A clean temporary consumer restored the locally packed
nupkg, built successfully with a valid manifest, and failed with `DG_BUILD003`
for an invalid schema/digest. Focused task tests cover valid, invalid and relative
paths.

`DataGuard.SqlClassification` is a dependency-free `netstandard2.0` source-text
classifier with bounded input and deterministic URI/version/span results. The
Roslyn analyzer now packages and consumes it. `DataGuard.LanguageServer` provides
an initial local stdio JSON-RPC `initialize`/`didOpen` flow; a framed protocol
smoke observed `publishDiagnostics` with `DGSQL001` for a SELECT literal.

These are foundations only. The Build project still needs a checked-in
clean-consumer fixture and full manifest matrix; the Language Server is not yet
solution-packaged, embedded in VSIX, or extension-host tested. No Phase 11/12
closure is claimed.

## Phase 15 incremental verification — release graph and packaged LSP artifact

The solution now compiles `DataGuard.Build`, `DataGuard.SqlClassification`, and
`DataGuard.LanguageServer` directly in Release. The VS Code package hook publishes
the Language Server to an extension-relative `server/` directory, creates a
schema-versioned SHA-256 manifest, and includes the runtimeconfig, deps, and
classifier dependency in the VSIX. The client checks this manifest before
spawning `dotnet`; missing or mismatched bytes leave LSP disabled. A consumer-side
ZIP inspection recomputed the embedded DLL digest and verified all required runtime
files.

| Command | Exit | Result |
|---|---:|---|
| `SKIP_ACT=1 ./scripts/verify_local_gates.sh` | 0 | Locked restore, Release build, analyzers, formatting, full tests with coverage, and NuGet audit pass; merged coverage is 61.45% (11,119/18,093). |
| `npm test`; `npm audit --omit=dev --json`; `npm run package` in `src/DataGuard.VSCode` | 0 | TypeScript and Node tests pass; production audit has zero vulnerabilities; VSIX package includes the LSP artifact and manifest. |

An installed VS Code extension-host edit smoke, full offline manifest fixture
matrix, and all platform/provider/signing acceptance remain required before any
Phase 11, 12, or 15 closure.

The LSP transport additionally rejects a truncated frame: a process fed a
`Content-Length` larger than the bytes supplied exits with a payload EOF error in
under five seconds, rather than retaining an editor process in a read loop.

The produced VSIX was installed successfully into fresh temporary user-data and
extensions directories by VS Code 1.136.0 on macOS arm64; `--list-extensions
--show-versions` reported `thanhnt-sm.dataguard-vscode@0.1.0`. This is a clean
artifact installation smoke only, not an extension-host C# edit/diagnostic test.

## Phase 12 incremental verification — manifest-bound code actions

The code-fix assembly now exports five non-empty providers: primary, MaxLength,
SkipContractCheck, NamingConvention, and UseOracle. The primary provider offers
DG002 replacement only when the diagnostic carries a verifier-approved SQL value
and a syntactically valid 64-character manifest digest. The replacement changes
the source literal; missing or malformed evidence produces no action. Comment
only dialect and CLOB suggestions are no longer advertised as remediations.

Focused Roslyn tests apply a verifier-bound DG002 replacement and a DG012
`UseSqlServer` to `UseOracle` transformation, then compile the changed document.
They also prove that DG002 without manifest evidence offers no unsafe rewrite.
The `BatchFixer` Fix All path applies two separately verifier-bound DG002
replacements in one document and the result compiles. The English and Vietnamese
code-fix documentation describe the same constraint.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test DataGuard.sln --configuration Release --no-restore --logger "console;verbosity=minimal"` | 0 | 560 tests pass: Core 514, Golden Corpus 25, Analyzer 5, CodeFixes 16. |
| `./scripts/verify_docs_sync.sh`; `git diff --check` | 0 | Documentation inventory and whitespace checks pass. |

This reduces the false-remediation gap but does not close FC09 or Phase 12:
the complete diagnostic/action matrix and full 12-provider taxonomy still
require their CP8 accounting.

## Phase 12 incremental verification — offline manifest fail-closed matrix

`OfflineManifestValidationTask` now treats non-numeric schema versions as the
stable `DG_BUILD003` failure rather than allowing a JSON accessor exception to
escape. The task also maps inaccessible file-system reads to `DG_BUILD004`.
The focused matrix covers a valid bounded manifest, invalid schema, relative
path, non-numeric schema, oversized input, and a malformed finding; it executes
without any network, credential, or database path.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~OfflineManifestValidationTaskTests` | 0 | 7 focused offline-manifest and clean-consumer tests pass. |

The manifest test suite now also creates a local package from `DataGuard.Build`,
restores a separate temporary consumer from a local-only NuGet feed, and builds
it with a valid manifest. It rewrites the consumer to an invalid manifest and
observes `DG_BUILD003`. This converts the earlier manual package smoke into a
repeatable clean-consumer integration test; it does not authorize any live
preflight or database acquisition.

## Phase 13 incremental verification — admitted plugin byte binding

`RulePluginManager` no longer loads an accepted plugin by resolving its path a
second time. Admission retains the managed buffer it hashes against the manifest,
and the manager uses `AssemblyLoadContext.LoadFromStream` for that exact buffer.
Each declared managed dependency is separately digest-verified and retained in an
admission map; the collectible context resolves it from copied bytes, while
symlinked or swapped dependency paths are rejected on a new admission. Native
plugin dependencies are fail-closed. This does not yet derive a transitive PE
closure or turn the existing provenance interface into signed provenance/SBOM
verification.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~PluginAdmissionTests` | 0 | 7 admission tests pass, including managed closure tamper/symlink rejection and verified-byte retention. |
| `dotnet build DataGuard.sln --configuration Release --no-restore`; `./scripts/verify_docs_sync.sh`; `git diff --check` | 0 | Release build and documentation/whitespace checks pass. |

## Phase 13 incremental verification — encrypted payload backend failure

On hosts without Windows DPAPI, `CredentialManager` now rejects a persisted
`ENC:` credential payload instead of returning ciphertext as if it were a
connection string. The same fail-closed condition applies to resolution through
the primary connection-string path. This is not a Secret Service or Keychain
implementation; those platform backends remain open.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~CredentialManagerFullTests` | 0 | 14 credential-manager tests pass, including unavailable-backend encrypted-payload rejection. |

## Phase 13 incremental verification — macOS Keychain storage

On macOS, encrypted connection strings now live in the login Keychain through
Security.framework. The file-backed metadata contains only a scoped
`KEYCHAIN:` reference; the secret is not passed to a CLI process. The focused
test stores and reads a temporary record and deletes that record in cleanup.
Windows continues to use DPAPI; Linux remains fail-closed until a Secret Service
backend is implemented.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~CredentialManagerFullTests` | 0 | 14 credential-manager tests pass, including macOS Keychain store/read/cleanup. |

## Live SQL Server provider verification

The SQL Server Testcontainers fixtures now have an explicit required-live mode.
With `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1`, a Docker/image/health-check failure
is a test failure instead of a successful informational skip. The local Docker
gate started SQL Server and completed stored-procedure extraction assertions.

| Command | Exit | Result |
|---|---:|---|
| `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~SqlServerIntegrationTests\|FullyQualifiedName~SqlServerParserIntegrationTests'` | 0 | 3 live SQL Server Testcontainers tests pass in 18 seconds. |

## Live PostgreSQL provider verification

The PostgreSQL adapter now has an opt-in live fixture. Its first live run found
two production parser defects: `pg_proc.prokind` is PostgreSQL `char`, not a
string, and the OID lookup must bind the parameter explicitly as `oid[]`.
After correcting both, the fixture creates a function with IN and OUT parameters
and verifies the extracted descriptors against a real PostgreSQL 16 instance.

| Command | Exit | Result |
|---|---:|---|
| `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PostgreSqlIntegrationTests` | 0 | 1 live PostgreSQL Testcontainers test passes. |

## Live MySQL provider verification

The MySQL adapter now has the same opt-in live fixture. Its first run found that
the `INFORMATION_SCHEMA.ROUTINES` join referred to nonexistent
`ROUTINES.SPECIFIC_SCHEMA`; the correct counterpart to
`PARAMETERS.SPECIFIC_SCHEMA` is `ROUTINES.ROUTINE_SCHEMA`. The corrected
fixture creates procedures with IN/OUT parameters and with no parameters, and
verifies the parser retains both contracts against MySQL 8.4.

| Command | Exit | Result |
|---|---:|---|
| `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~MySqlIntegrationTests` | 0 | 1 live MySQL Testcontainers test passes, including the empty-parameter procedure contract. |

## Live Oracle provider verification

The Oracle Free fixture uses a dedicated `dataguard` account in `FREEPDB1`.
The initial configuration attempted to recreate `SYSTEM`, which Oracle rightly
rejected; the final fixture creates a procedure under the application account
and verifies `ALL_ARGUMENTS` parameter extraction against Oracle Free 23.

| Command | Exit | Result |
|---|---:|---|
| `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~OracleIntegrationTests` | 0 | 1 live Oracle Free Testcontainers test passes. |

## VS Code extension-host verification

The VS Code package now has an extension-host runner using the locally installed
VS Code executable. It starts the development extension in an isolated `/tmp`
profile, activates it, confirms the validate, cancel, and assess commands are
registered, opens a temporary C# document, and waits for the local language
server's `DGSQL001` diagnostic. The runner deletes its profile afterwards and
keeps `VSCODE_EXECUTABLE_PATH` as an override for other hosts.

| Command | Exit | Result |
|---|---:|---|
| `npm run test:extension-host` in `src/DataGuard.VSCode` | 0 | Development extension activated in VS Code; three public commands registered and `DGSQL001` received for a C# edit. |

## Current full local regression

| Command | Exit | Result |
|---|---:|---|
| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | Release build succeeds with 0 warnings and 0 errors. |
| `dotnet test DataGuard.sln --configuration Release --no-restore --logger "console;verbosity=minimal"` | 0 | 574 tests pass: Core 525, Golden Corpus 25, Analyzer 6, CodeFixes 18. |

This regression result does not replace Windows, Docker/`act`, live-provider,
signed provenance/SBOM, or platform-secret-store acceptance evidence.

## Local GitHub Actions workflow simulation

`act` ran the pinned `build-and-test` job using its ARM64 Ubuntu runner. The
build, analyzers, format gate, and all 574 tests passed. Its first real run
found that coverage aggregation treated relative paths and absolute checkout
paths for the same source file as distinct files, reporting 56.78%. The gate
and local verifier now normalize paths below `src/`; the same coverage reports
then produce a passing result. The complete post-fix rerun reported 60.01%
(5,083/8,470 unique source lines).

| Command | Exit | Result |
|---|---:|---|
| `act -W .github/workflows/ci.yml -j build-and-test --container-architecture linux/arm64 -P ubuntu-latest=catthehacker/ubuntu:act-latest` | 1 | 574 tests passed; original coverage aggregation failed at 56.78% because it double-counted mixed path forms. |
| Same command after path normalization | 0 | Pinned ARM64 workflow job succeeded: 574 tests pass and coverage is 60.01% (5,083/8,470). |

## Plugin-rule invocation containment

Both sequential and concurrent validation now contain non-cancellation exceptions
from a rule, record `RuleExecutionState.Failed`, and mark the result incomplete
while allowing independent rules to execute. Caller cancellation still propagates.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ConcurrentValidationExecutionTests` | 0 | 10 tests pass, including sequential and concurrent throwing-rule containment. |

## Packaged offline-build artifact boundary

The clean consumer test now inspects the produced `DataGuard.Build` nupkg before
restore. It requires the task assembly and both explicit target-import paths,
then restores only from the temporary local package feed and proves valid and
invalid manifest behavior.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~OfflineManifestValidationTaskTests` | 0 | 7 tests pass; package contents and clean local-feed consumer behavior are verified. |

## Published Host artifact smoke

`verify_host_publish.sh` runs outside the test-host process. It publishes the
framework-dependent Host into a unique temporary directory, requires its DLL,
deps, and runtimeconfig files, starts only a loopback listener with temporary
fixture inputs, and waits for the published `/health/ready` response before
terminating the process and removing the directory. This is local framework-
dependent evidence only; RID archives and remote-host policy remain open.

| Command | Exit | Result |
|---|---:|---|
| `./scripts/verify_host_publish.sh` | 0 | Published Host artifact supplied a ready loopback health endpoint. |

## Phase 12 incremental evidence — source-only project snapshot selection

`validate --ef-project <directory-or-csproj>` now discovers only source
`*ModelSnapshot.cs` files, excluding `bin`, `obj`, and `.git`; it never builds a
project or loads an assembly. `--ef-context` narrows a multi-context project, and
ambiguous or absent selection returns a usage error. The existing explicit
`--ef-snapshot` remains available for an exact path.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~Validate_EfSnapshot\|FullyQualifiedName~Validate_EfProject' --logger 'console;verbosity=minimal'` | 0 | 2 CLI tests pass; project selection exports the source contract and ignores an `obj` fixture. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; current suite baseline is 627 tests (Core 573, Golden 25, Analyzers 8, CodeFixes 21). |

## Phase 5 incremental evidence — linked worktree hooks

The managed-hook lifecycle now resolves either a repository `.git` directory or
a linked-worktree `.git` file containing `gitdir:`. Native installation, status,
and marker-only uninstall all address the same resolved hooks directory. Status
also recognizes marker ownership for native Git, Husky, and Lefthook outputs.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PreCommitHookInstallerTests --logger 'console;verbosity=minimal'` | 0 | 3 tests pass, including a linked-worktree gitdir fixture through install/status/uninstall. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; 581 tests pass (Core 531, Golden 25, Analyzers 6, CodeFixes 19). |

## Phase 8 incremental evidence — reproducible occurrence inventory

`scripts/generate_claim_occurrences.py` classifies all current documentation in
scope, including root README/SECURITY and extension references, and generates
line-addressable FC candidates with EN/VI peers. It intentionally leaves command
candidates without an FC vocabulary mapping explicit as `unmatched`, rather than
using a heuristic to declare them delivered. This is inventory evidence only:
manual prose classification plus source/caller/test/platform/evidence links are
still required before FC20 can close.

| Command | Exit | Result |
|---|---:|---|
| `python3 scripts/generate_claim_occurrences.py` | 0 | Generated `reports/claim-occurrences.md`: 146 files classified, 200 mapped FC occurrence seeds, and 335 explicit unmatched command candidates. |
| Python ID integrity assertion over the generated report | 0 | 200 `FCxx.nnn` IDs are unique and sequential within each represented FC group. |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace checks pass. |

## Phase 12 incremental evidence — compatibility attribute extraction

`DataGuard.Contracts` now provides additive `DataContract`, `SqlParameter`, and
`ResultSet` attributes for the documented manual mode. `ManualContractSource`
reads the fully-qualified facades offline and translates them to entity,
parameter, and result-column descriptors. The documentation specifies global
qualification for `DataContract`, preserving an explicit collision boundary with
the similarly named BCL serialization attribute. This does not claim the still
missing `DataGuard.Validate` facade or its compiler matrix.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ManualContractSourceTests --logger 'console;verbosity=minimal'` | 0 | 3 tests pass, including compatibility attribute extraction without database access. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; 579 tests pass (Core 530, Golden 25, Analyzers 6, CodeFixes 18). |

## Phase 12 incremental evidence — DG001 contract-declaration actions

`DataGuardCodeFixProvider` now registers compile-valid, fully-qualified
`DataContract` and `SqlParameter` declarations for an unvalidated SQL call in a
type/method context. The actions intentionally do not invent a database type,
procedure identity, or runtime validation call. The documented
`DataGuard.Validate()` syntax remains an explicit open contract because it is not
a compilable public API target.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~UnvalidatedSqlCall_RegistersCompileValidContractDeclarationActions --logger 'console;verbosity=minimal'` | 0 | Roslyn test verifies all three DG001 actions register and both declaration transformations compile. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; 580 tests pass (Core 530, Golden 25, Analyzers 6, CodeFixes 19). |

## Phase 13 incremental verification — immutable managed dependency closure

Plugin admission now verifies every declared managed dependency in the adjacent
manifest before any plugin code is loaded. The collectible load context copies and
resolves those verified bytes, and admission reports return defensive copies, so a
caller cannot alter the lazy-load buffer. Symlinked or digest-mismatched
dependencies are rejected. Native plugin dependencies fail closed. Admission reads root and declared-dependency PE references; each non-host reference
must have a matching manifest identity. The host still requires an
operator-provided signed-provenance verifier, and one-handle/no-follow intake
remains open acceptance work.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PluginAdmissionTests --logger 'console;verbosity=minimal'` | 0 | 7 admission tests pass, including managed dependency tamper/symlink rejection and verified-byte retention. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; 585 tests pass (Core 535, Golden 25, Analyzers 6, CodeFixes 19). |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace checks pass. |

## Phase 13 incremental verification — PE dependency-reference closure

Admission now parses managed PE metadata without loading plugin code. The root DLL
and every declared dependency contribute their assembly references; every reference
outside the explicit host/framework allowlist must be declared in the manifest, and
each declared dependency's PE assembly identity must match its manifest identity.
The loader resolves declared dependencies from copied verified bytes. One-handle
no-follow intake and a signed provenance verifier remain open.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PluginAdmissionTests --logger 'console;verbosity=minimal'` | 0 | 11 tests pass, including real-PE omitted direct/transitive reference and identity-substitution rejection. |
| `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Release build has 0 warnings/errors; 589 tests pass (Core 539, Golden 25, Analyzers 6, CodeFixes 19). |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Documentation synchronization and whitespace checks pass. |

## Phase 13 incremental verification — exact managed assembly identities

Plugin dependency manifests and ALC resolution now use canonical full assembly
identities (name, version, culture, and public-key token). Only exact identities
from the host/framework allowlist can fall back to the default context; all other
unmapped requests fail closed.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PluginAdmissionTests --logger 'console;verbosity=minimal'` | 0 | 13 tests pass, including same-simple-name/different-version rejection and defensive provenance snapshot verification. |

## Phase 15 incremental verification — hostile hook symlink

The hook installer now rejects a native/Husky/Lefthook hook path that is a symbolic
link. It neither overwrites nor removes the linked target.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PreCommitHookInstallerTests --logger 'console;verbosity=minimal'` | 0 | 4 tests pass, including native symbolic-link target preservation. |

## Phase 9 incremental verification — fail-closed host URL policy

`DataGuard.Host` accepts only non-empty HTTP(S) loopback URL sets. Empty,
malformed, remote, and non-HTTP schemes are rejected before Kestrel binding.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HealthHostBindingTests --logger 'console;verbosity=minimal'` | 0 | 8 loopback binding cases pass. |

## Phase 10 incremental verification — zero advisory package cap

A non-positive `MaximumPackagesPerRequest` now disables advisory coordinates and
therefore egress. It is no longer silently normalized to one package.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RemoteAdvisoryPolicyTests --logger 'console;verbosity=minimal'` | 0 | 3 policy tests pass, including zero-cap zero-egress. |

## FC16 incremental verification — cancelled YAML export

`WriteYamlAsync` checks cancellation before serialization or file creation. A
pre-cancelled caller receives cancellation and no output artifact is created.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ContractExportTests --logger 'console;verbosity=minimal'` | 0 | 5 tests pass, including pre-cancelled YAML output preservation. |

## Phase 10 incremental verification — zero advisory page cap

A non-positive `MaximumPagesPerPackage` now disables advisory coordinates and
therefore egress; a zero page cap cannot be normalized into a first request.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RemoteAdvisoryPolicyTests --logger 'console;verbosity=minimal'` | 0 | 4 policy tests pass, including zero-page-cap zero-egress. |

## Phase 10 incremental verification — zero advisory response cap

A non-positive `MaximumResponseBytes` now disables advisory coordinates before any
HTTP request, rather than issuing a request that cannot retain a response body.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RemoteAdvisoryPolicyTests --logger 'console;verbosity=minimal'` | 0 | 5 policy tests pass, including zero-response-cap zero-egress. |

## Phase 10 incremental verification — bounded negative advisory input

The dependency-health formula clamps a confirmed advisory count below zero to zero,
so malformed inputs cannot increase a score beyond the documented 100-point ceiling.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~DependencyHealthScoreTests --logger 'console;verbosity=minimal'` | 0 | 3 score tests pass, including negative-advisory count clamping. |


## Remediation verification — fail-closed advisory, plugin, and hook boundaries

Remote advisory work limits now require positive package, page, detail, response,
and timeout values before any HTTP request. Plugin admission canonicalizes strong-name
public-key tokens, snapshots the host allowlist and manifest evidence, and rejects a
linked plugin directory. Hook installation rejects linked path components beneath its
repository or git metadata anchors.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~RemoteAdvisory'` | 0 | 16 advisory-policy/client tests pass, including zero-request handling for nonpositive detail and timeout limits. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~PluginAdmission'` | 0 | 16 plugin-admission tests pass, including signed dependency identity, policy snapshot, and linked-directory rejection. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~PreCommitHookInstaller'` | 0 | 5 hook-installer tests pass, including parent-directory symlink rejection. |


## Execution state — 2026-09-13

The remediation plan and phases 1–6 and 8–14 are now marked **In progress** to match
the implemented work and local evidence. Phase 7 (closure) and Phase 15 (full
acceptance) remain pending: unresolved filesystem-race and external-environment
gates must not be hidden by passing local tests.


## FC09 action-matrix verification — 2026-09-13

The current exported code-fix surface contains five providers. Their advertised
diagnostics are limited to actions with syntax transformations: DG001, DG002
(manifest-bound only), DG006, DG007/DG009, and DG012. The test suite applies
registered actions to an in-memory Roslyn document and checks compilation, including
DG002 Fix All. This is evidence for the current five-provider contract; the separate
historical twelve-provider wording remains an open FC09 taxonomy/owner gate.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj --configuration Release --no-restore --logger 'console;verbosity=minimal'` | 0 | 19 tests pass, including manifest-bound replacement, Fix All, naming/provider transforms, and compile checks. |

## XR08 manifest boundary verification — 2026-09-13

The offline MSBuild manifest task now rejects a symlinked manifest or immediate
symlinked parent before reading bytes. The contract remains bounded and offline;
the hostile symlink case has explicit regression coverage.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~OfflineManifestValidationTaskTests'` | 0 | 8 tests pass, including symbolic-link rejection and packed consumer validation. |

## FC15 linked-worktree metadata verification — 2026-09-13

External `.git` targets are accepted only when they have regular `HEAD` and
`commondir` metadata whose content resolves to an existing non-linked common
directory, matching the minimum linked-worktree shape. Arbitrary external
directories are rejected before hook writes. The linked-worktree and
symlink-parent regression cases pass.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~PreCommitHookInstallerTests'` | 0 | 5 tests pass. |

## CLI output boundary verification — 2026-09-13

Configuration wizard, plain configuration, and SARIF export now reject symbolic
link output paths before writing. New output files remain supported. Existing CLI
exit-code coverage passes after the guard was added.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~CliExitCodeTests'` | 0 | 12 CLI behavior tests pass, including explicit wizard output handling. |

## FC18 cache audit — 2026-09-13

The current implementation proves a bounded process-local content-addressed
`MemoryCache` with one-hour expiry and atomic baseline-file persistence. It does
not yet prove the Phase 14 operator-approved separate file cache contract,
injected-clock expiry/corruption recovery, or live-acquisition bypass. FC18 and
Phase 14 therefore remain open despite the existing baseline cache tests.

## Path-writer audit — 2026-09-13

CLI configuration/SARIF writes validate output links, while hook writes use the
trusted-anchor atomic writer. Uninstall remains marker-gated and path-checked;
full no-follow directory-handle guarantees remain an explicit open gate.

## XR01 lock-coordinate boundary verification — 2026-09-13

Remote advisory extraction now walks lock files through the workspace boundary
and rejects any path containing a symbolic-link component. The end-to-end
assessment test places a valid lock file behind a linked directory and verifies
that the advisory client receives no coordinates.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~AssessmentPackTests' --logger 'console;verbosity=minimal'` | 0 | 9 assessment-pack tests pass, including the symlinked lock-directory egress regression. |

## XR05 DG001 suppression verification — 2026-09-13

The DG001 quick fix adds `[SkipContractCheck]` to the enclosing caller. Both
the syntax generator and semantic analyzer now honor that attribute on the
caller or containing type, while retaining the existing target-method and
marker-comment checks. Apply-equivalent source fixtures prove the generator
stops emitting DG001 and the semantic analyzer stops emitting SQL diagnostics.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~GeneratorExecutionTests' --logger 'console;verbosity=minimal'` | 0 | 5 generator/semantic tests pass, including method-level caller suppression. |

## Release verification — 2026-09-13

The complete solution verification remains green after the analyzer and test
updates: Release build has zero warnings/errors; all four C# test projects pass
(570 Core, 25 Golden Corpus, 8 Analyzer, 21 CodeFix tests); bilingual docs
sync and `git diff --check` also pass.

## Phase 3/FC16 cancellation propagation — 2026-09-13

The `validate` invocation now owns a cancellation source wired to Ctrl+C and
passes its token through contract acquisition, Oracle enumeration, validation,
baseline loading, EF snapshot extraction, and machine-readable writers. A causal
cancellation is classified as exit code 130 with a bounded message instead of a
generic validation failure (exit 1). The health coordinator also rejects stale
readiness snapshots after its configured freshness window.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~CliExitCodeTests' --logger 'console;verbosity=minimal'` | 0 | 12 CLI exit/output tests pass after invocation cancellation wiring. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~HealthProbeTests' --logger 'console;verbosity=minimal'` | 0 | 6 health tests pass, including deterministic stale-snapshot readiness and no probe work from readiness reads. |

Machine-readable contract, evidence, TypeScript, and SARIF exports now publish
through same-directory temporary files and atomic moves. Cancellation before the
move therefore preserves an existing artifact and removes the temporary file.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | 567 Core, 25 Golden Corpus, 8 Analyzer, and 21 CodeFix tests pass after cancellation/export changes. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~ContractExportTests' --logger 'console;verbosity=minimal'` | 0 | 6 export tests pass, including cancellation preserving an existing YAML artifact and removing its temp file. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | 567 Core, 25 Golden Corpus, 8 Analyzer, and 21 CodeFix tests pass after final token propagation. |

The same cancellation contract now covers `oracle-check`: Oracle NLS semantics,
column enumeration, and SARIF/console emission receive the handler token, and a
causal cancellation maps to exit 130.

`StreamingSarifSink` now follows the same temp-file publication rule as the
buffered sink, so cancellation cannot expose a partially serialized SARIF file.
Its existing 35 focused emitter tests and the full solution suite pass after the
refactor.

Database-version lookup now rethrows causal cancellation instead of converting it
to an `unknown` version, preserving the CLI's exit-130 contract during snapshot
diff and other cancellable acquisition paths.

## Phase 3 snapshot-diff fail-closed verification — 2026-09-13

`snapshot diff` no longer hashes the persisted schema as the current state. It
requires an explicitly configured connection, performs a fresh full acquisition,
requires a produced acquisition result, and compares the persisted schema only
with the newly acquired `DatabaseSchemaDescriptor`. Missing connection or empty
acquisition now emits `UNEVALUATED` and exits 3. The canonical schema hash also
accepts fresh database descriptors directly, with equal/changed schema tests.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~CliExitCodeTests|FullyQualifiedName~SchemaHashTests' --logger 'console;verbosity=minimal'` | 0 | 16 tests pass, including three no-fresh-acquisition exit-3 regressions and equal/changed fresh-schema hash checks. |

The schema-bearing v2 regression also passes: persisted schema content alone can
never produce a clean result without a live acquisition.

`snapshot refresh` now applies the same acquisition boundary and refuses to
create a snapshot without a configured database connection. Its acquisition and
baseline publication receive the command cancellation token.

Legacy v1 snapshots are now explicitly unevaluated for structural `snapshot diff`
until refreshed; the command no longer performs an implicit violation-only
fallback that could be mistaken for DDL drift evidence.

| Command | Exit | Result |
|---|---:|---|
| `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | 570 Core, 25 Golden Corpus, 8 Analyzer, and 21 CodeFix tests pass after fail-closed snapshot changes. |

The explicit `--legacy-violation-diff` opt-in is covered by the CLI exit-code
tests and is documented as violation-only evidence; structural drift still
requires a refreshed schema snapshot and a fresh connected acquisition.

| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | Release build completed with zero warnings/errors after the legacy opt-in change. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~CliExitCodeTests' --logger 'console;verbosity=minimal'` | 0 | 14 CLI exit/output tests pass, including legacy v1 default unevaluated, explicit violation-only opt-in, and refresh-without-connection cases. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | 570 Core, 25 Golden Corpus, 8 Analyzer, and 21 CodeFix tests pass. |

## Benchmark harness restore and dry run — 2026-09-13

The isolated benchmark project had a stale lockfile after its analyzer project
reference was added. `dotnet restore --force-evaluate` regenerated the locked
dependency graph, and the default dry run executed all 15 benchmark cases across
snapshot parsing, SQL classification, validation, semantic analysis, SARIF
streaming, and incremental generation. Artifacts stayed under the ignored
`BenchmarkDotNet.Artifacts/` directory. The claim evaluator correctly rejects an
unannotated run with missing commit, SDK, and worktree metadata; a clean
committed run is still required before accepting benchmark numbers.

| Command | Exit | Result |
|---|---:|---|
| `dotnet restore --force-evaluate` (benchmark project) | 0 | Lockfile regenerated for all project references. |
| `dotnet run --configuration Release --no-restore` (benchmark project) | 0 | 15 dry benchmark cases executed and raw reports emitted. |
| `python3 scripts/validate_benchmark_claim.py BenchmarkDotNet.Artifacts/benchmark-metadata.json` | 1 | Correctly rejected metadata lacking commit, SDK, and worktree declaration. |

## Live provider integration matrix — 2026-09-13

The four-provider Testcontainers matrix was run with required mode enabled rather
than allowing infrastructure failures to degrade to silent fixture returns.
SQL Server 2022, PostgreSQL 16, MySQL 8.4, and Oracle Free 23 all started and
their database-backed procedure/function assertions passed. The test process
cleaned up its Ryuk-managed containers after completion.

| Command | Exit | Result |
|---|---:|---|
| `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1 DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~IntegrationTests' --logger 'console;verbosity=minimal'` | 0 | 7 live integration tests pass across SQL Server, PostgreSQL, MySQL, and Oracle. |

The canonical SQL Server gate now uses `DATAGUARD_RUN_SQLSERVER_INTEGRATION=1`;
the historical require variable remains an alias. The installed xUnit 2.9.3
runner does not support dynamic discovery-time skips; optional fixtures retain
their documented infrastructure return while required mode fails on startup.

| `DATAGUARD_RUN_SQLSERVER_INTEGRATION=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~SqlServerIntegrationTests|FullyQualifiedName~SqlServerParserIntegrationTests' --logger 'console;verbosity=minimal'` | 0 | 3 SQL Server Testcontainers tests pass with the canonical required switch. |

The combined required provider profile was rerun after the fixture policy change:

| `DATAGUARD_RUN_SQLSERVER_INTEGRATION=1 DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~IntegrationTests' --logger 'console;verbosity=minimal'` | 0 | 7/7 SQL Server, PostgreSQL, MySQL, and Oracle live integration tests pass. |

## VS Code package and host verification — 2026-09-13

The extension's Node test suite, local VS Code extension-host smoke, dependency
audits, and packaging path all pass. The extension host loaded the development
extension, registered its commands, and exited cleanly. Both npm audits report no
vulnerabilities. The VSIX was written to `/tmp`, outside tracked source.

| Command | Exit | Result |
|---|---:|---|
| `npm ci` (under `src/DataGuard.VSCode`) | 0 | 324 packages installed; npm reports 0 vulnerabilities. |
| `npm test` | 0 | 3 security/path tests pass after TypeScript compilation. |
| `npm run test:extension-host` | 0 | Local extension-host activation/command smoke passes; host exits code 0. |
| `npm audit --json` | 0 | 0 info/low/moderate/high/critical vulnerabilities. |
| `npm audit --omit=dev --json` | 0 | Production dependency audit has 0 vulnerabilities. |
| `npm run package -- --out /tmp/dataguard-vscode-smoke/dataguard-vscode.vsix` | 0 | VSIX packaging pass; 18 files, approximately 70KB. |

The public `ValidationPipeline` now exposes an additive schema-capable drift
overload. `DriftReport.Status` distinguishes `Complete`, `Missing`, `Corrupt`,
`UnsupportedVersion`, `Unevaluated`, and `Failed`, while the existing
violation-only overload remains source-compatible.

| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~PublicApiAndPipelineTests' --logger 'console;verbosity=minimal'` | 0 | 8 public API/pipeline tests pass, including structural DDL change, corrupt, unsupported, and unevaluated baselines. |

The full suite after the typed drift status tests remains green: 573 Core, 25
Golden Corpus, 8 Analyzer, and 21 CodeFix tests (627 total).

Phase 3 acceptance checkboxes for connected fresh-schema diff and end-to-end
cancellation are now marked complete from the corresponding focused/full test
evidence. Phase 7's provider/safety regression checkbox is also complete; the
DB skip-policy checkbox remains open because xUnit 2.9.3 lacks supported dynamic
discovery-time skip semantics.

`DriftReport.Status` now defaults to `Missing` for manually constructed reports;
only paths that actually load and compare a baseline set `Complete`. This closes
the remaining default-value false-clean ambiguity without changing the existing
positional report constructor.

| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | Release build completed with zero warnings/errors after the default-status hardening. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | Full suite remains green: 573 Core, 25 Golden Corpus, 8 Analyzer, and 21 CodeFix tests. |

## Local gate verification — 2026-09-13

`SKIP_ACT=1 ./scripts/verify_local_gates.sh` now passes end to end through the
local checks: topology guard, bilingual docs, workflow lint, locked restore,
Release build, analyzer build, both formatting checks, coverage and NuGet
vulnerability audit. Coverage is 63.35% (8,219/12,973 lines). Docker/act and
history secret scanning remain explicitly skipped by `SKIP_ACT=1` and are still
environment-specific gates.

The previously skipped local environment checks were also exercised directly:

| Command | Exit | Result |
|---|---:|---|
| `./scripts/verify_host_publish.sh` | 0 | Release host published and loopback `/health/ready` smoke passed on framework-dependent output. |
| `actionlint .github/workflows/*.yml` | 0 | All workflows lint clean. |
| `trufflehog git file:///Volumes/Data/101.AI/GitHub/eco_support_net_oracle --no-update --only-verified --fail` | 0 | 0 verified and 0 unverified secrets; one binary brotli read error was non-critical. |
| `act push --pull=false --workflows .github/workflows/standards-audit.yml --platform ubuntu-latest=dataguard-act-runner:node-path --container-architecture linux/amd64` | 0 | Standards audit job passed. |

The corresponding CI `act` `build-and-test` job was attempted with Docker ready;
its container build entered a futex wait under the local Rosetta emulation for
over five minutes with near-zero CPU and no output. The process was canceled
cleanly; this remains an environment-specific open gate rather than a product
test failure.
| `./scripts/verify_docs_sync.sh` | 0 | Bilingual documentation and rule artifacts are synchronized. |
| `git diff --check` | 0 | No whitespace errors. |

## Typed acquisition outcome boundary — 2026-09-13

CLI contract acquisition now returns an explicit `Complete`, `Unavailable`,
`Incomplete`, or `Failed` outcome. Validate and connected snapshot diff refuse
to publish a normal success result for non-complete acquisition; an explicit EF
model-snapshot source remains an allowed direct source. Provider-unavailable
diagnostics retain their rule-specific reason and exit code 3.

| Command | Exit | Result |
|---|---:|---|
| `dotnet build DataGuard.sln --configuration Release --no-restore` | 0 | Release build completed with zero warnings/errors after typed acquisition wiring. |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~CliExitCodeTests' --logger 'console;verbosity=minimal'` | 0 | 14 CLI tests pass, including EF source-only validation, provider-unavailable status, and no-source UNEVALUATED behavior. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger 'console;verbosity=minimal'` | 0 | 570 Core, 25 Golden Corpus, 8 Analyzer, and 21 CodeFix tests pass. |

## Latest verification — 2026-09-13

| Command | Result | Evidence |
|---|---:|---|
| `dotnet build DataGuard.sln --configuration Release --no-restore -v:q` | 0 | Release build succeeded with 0 warnings and 0 errors after readiness snapshot regression coverage. |
| `dotnet test DataGuard.sln --configuration Release --no-build --logger "console;verbosity=minimal"` | 0 | Full suite: **639 passed, 0 failed** (Core 585, Golden Corpus 25, Analyzers 8, CodeFixes 21). |
| `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~HealthProbeTests'` | 0 | 6 health tests pass, including `IsReady_ReadsSnapshotWithoutRunningProbeWork`. |
| `./scripts/verify_docs_sync.sh` and `git diff --check` | 0 | Bilingual documentation/rule artifacts synchronized; no whitespace errors. |
| `npm test` (from `src/DataGuard.VSCode`) | 0 | TypeScript compile fixture and 3 security tests pass; no npm audit findings in the prior audit run. |
| `npm run test:extension-host` (from `src/DataGuard.VSCode`) | 0 | Local VS Code extension host smoke test exits cleanly with extension loaded. |
| `./scripts/verify_host_publish.sh` | 0 | Release-published host serves loopback readiness successfully; artifact SHA-256 recorded by script. |
| CLI assessment cancellation/error contract | source fix + build | `assess` now routes local runs through cancellation-aware `AssessmentEngine.RunAsync`; DG1006 suppresses writers and exits 130, tool errors/exceptions exit 4. Release build passes. |
| Dependency health score envelope | source fix + tests | `AssessmentReport.DependencyHealth` is additive/versioned; disabled or empty coverage is `Unknown`, incomplete advisory coverage is `Partial`, complete approved coverage can emit `dependency-health-v1` score. Core suite remains 585 passed. |
| CLI operational exit regression | 0 | `Assess_MissingWorkspace_Exit4ForOperationalToolError` passes; missing workspace emits DG1000 and exits 4. Core suite is now 586 passed (new test included). |
| `dotnet test DataGuard.sln --configuration Release --no-restore --logger "console;verbosity=minimal"` | 0 | Latest full suite: **640 passed, 0 failed** (Core 586, Golden 25, Analyzers 8, CodeFixes 21). |
| Dependency score operator-flow tests | 0 | Remote assessment now proves complete score (80 with one advisory) and `Partial` with a package-query cap; latest full suite is **641 passed, 0 failed** (Core 587, Golden 25, Analyzers 8, CodeFixes 21). |
| OSV pagination/rate-limit regression | 0 | Added deterministic independent-pagination ordering test and bounded 429 (`DG1216`) test; both pass. Latest full suite: **643 passed, 0 failed** (Core 589, Golden 25, Analyzers 8, CodeFixes 21). |
| OSV malformed response/page cap regressions | 0 | Added malformed JSON (`DG1213`) and one-page cap tests; both pass. Latest full suite: **645 passed, 0 failed** (Core 591, Golden 25, Analyzers 8, CodeFixes 21). |
| OSV operator-cancellation regression | 0 | Added a cancellation-aware HTTP handler test; caller cancellation propagates as `OperationCanceledException` while bounded timeout handling remains separate. Latest full suite: **646 passed, 0 failed** (Core 592, Golden 25, Analyzers 8, CodeFixes 21). |
| OSV timeout classification regression | 0 | Added policy-timeout test (`DG1211`) alongside operator-cancellation propagation; latest full suite: **647 passed, 0 failed** (Core 593, Golden 25, Analyzers 8, CodeFixes 21). |
| OSV endpoint-policy fail-closed regressions | 0 | Added policy endpoint field with HTTPS/host validation; HTTP and unapproved-host fixtures issue zero requests and return `DG1210`. Latest full suite: **649 passed, 0 failed** (Core 595, Golden 25, Analyzers 8, CodeFixes 21). |
| Completion-audit reconciliation | 0 | Updated current-worktree audit and FC matrix to reflect the latest 649-test suite and the now-integrated complete/partial/unknown dependency-health operator flow; historical evidence rows remain preserved. |
| Current audit truthfulness reconciliation | 0 | Removed stale claim that ARM64 `act` passed; current audit now records Rosetta hang/cancellation as an open external CI gate. No historical evidence rows were deleted. |
| `node .tmp/remediation-plan-check.mjs` | 0 | Structural audit confirms 36 files, 130 local links, 60 scout IDs, 66 ledger rows, 15 red-team children, and 15 phases. It reports only the expected planning-era “Not pending” notices because execution has advanced phases to `in-progress`; no link/coverage cardinality issue is reported. |
| Updated `.tmp/remediation-plan-check.mjs` + execution audit | 0 | Structural checker now accepts lifecycle statuses (`pending`, `in-progress`, `complete`, `blocked_*`) while preserving dependency-cycle checks. Current run: 36 files, 130 local links, 60 scout IDs, 66 ledger rows, 15 red-team children, 15 phases, **0 issues**. |
| Assessment report schema bump | 0 | Bumped `AssessmentReport.CurrentSchemaVersion` from 1.0 to 1.1 for the additive `DependencyHealth` field; updated assessment docs/sample and contract test. Full suite remains **649 passed, 0 failed**. |
| Phase 5 tooling/IDE focused verification | 0 | Hook lifecycle tests (7), CodeFix tests (21), and VS Code `npm test` (TypeScript fixture + security tests) pass. Phase 5 marks lifecycle, safe fixes, and external-SARIF/cancellation criteria evidenced. |
| Phase 5 generated hook command matrix | 0 | Added Lefthook generated-command regression alongside Native Git/Husky coverage; hook suite now 7 pass and full suite **650 passed, 0 failed** (Core 596, Golden 25, Analyzers 8, CodeFixes 21). |
| V01/release-evidence reconciliation | 0 | Marked V01 compatibility gate complete from npm CI/audit/package evidence and corrected stale release notes that still described remote advisory and VS Code assess as unimplemented. Current notes now match shipped behavior and open external gates. |
| Product-discovery claim reconciliation | 0 | Corrected stale auto-remediation wording: generic rewrites remain out of scope while verified, user-invoked CodeFix providers are shipped. Docs now match current code and phase-5 evidence. |
| Phase wording reconciliation | 0 | Removed stale phase text that still described advisory/health-host capabilities as unimplemented or Phase 12 as unavailable; updated the 650-test evidence boundary while preserving external-gate caveats. Structural checker remains 0 issues. |
| Phase 9/13 checklist reconciliation | 0 | Health host/binding/integration and plugin admission/lifecycle focused tests pass; Release build has 0 warnings/errors. Marked only the corresponding local criteria; signed provenance, platform stores and external host gates remain open. |
| Phase 2/3 checklist reconciliation | 0 | Marked observable bounds/cancellation, safe EF source selection, four-provider metadata paths, Oracle metadata, configuration non-mutation and provider precedence based on focused/full tests already passing. Remaining graph semantics, snapshot matrix and external gates stay open. |
| Health snapshot privacy regression | 0 | Unreadable sensitive paths produce only sanitized `Unhealthy` detail; serialized snapshot excludes the path and exception text. Latest full suite: **652 passed, 0 failed** (Core 598, Golden 25, Analyzers 8, CodeFixes 21). |
| Health host non-loopback rejection | 0 | Runtime host integration test starts with `http://0.0.0.0:0`, verifies prompt nonzero rejection and sanitized loopback error before a listener is established; health-host focused suite passes 10/10. |
| Phase 12 offline build and snapshot safety | 0 | Offline manifest task tests cover valid/invalid schema, absolute-path and symlink rejection, 1 MiB cap, bounded findings, and packed clean-consumer build failure. C# ModelSnapshot parser tests cover supported fluent extraction and syntax-error diagnostics without executing a build/context. CodeFix tests require verifier-bound replacements before SQL rewrites. |
| Phase 8 occurrence census reconciliation | 0 | Regenerated deterministic claim census after adding command-to-capability mapping, secret-store docs, and the preflight command: 146 Markdown files, 131 current/15 historical, **539 mapped candidate occurrences, 0 unmatched command candidates**. FC20 prose classification remains explicitly open. |
| Phase 8 contract reconciliation | 0 | Phase 8 now records feature-group subrows/evidence tiers, the expanded-scope amendment, and contradictory/absolute claims as explicit owner gates; CP8 Sol GO remains open pending final capability acceptance. |
| Sol gap remediation: offline manifest diagnostics | 0 | `OfflineManifestValidationTask` now allowlists DG002–DG016, validates bounded severity/source/line/column, emits warnings/errors, and returns failure for error findings. Focused task and packed-consumer tests pass 10/10. |
| Sol gap remediation: periodic health refresh | 0 | `HealthRefreshService` now performs an initial refresh plus a cancellable `PeriodicTimer` loop and awaits clean shutdown; lifecycle test proves repeated refresh and no post-stop work. |
| Sol gap remediation: dependency score truthfulness | 0 | Lock extraction now returns typed completeness/reasons/TFMs; malformed/unresolved lock entries, lock/TFM mismatch, and unsupported/EOL TFMs force `Partial` with no numeric score. Focused assessment/score tests pass. Full suite: **658 passed, 0 failed** (Core 604, Golden 25, Analyzers 8, CodeFixes 21). |
| Language Server lifecycle smoke | 0 | `scripts/verify_language_server.sh` passes initialize, debounced didOpen/didChange diagnostic replacement, and didClose diagnostic clearing using only the local classifier; no CLI, DB, credential, or network path is invoked. |
| VS Code snapshot/baseline commands | 0 | Extension now exposes explicit, confirmation-gated `dataguard.refreshSnapshot` and `dataguard.createBaseline` commands with safe config arguments; extension-host smoke registers both alongside Validate/Assess/Cancel. |
| VS Code provider setting | 0 | Added resource-scoped `dataguard.provider` enum (`sqlserver`, `postgresql`, `mysql`, `oracle`) and allowlisted `--provider` propagation for explicit commands; TypeScript and extension-host verification pass. |
| Benchmark isolation contract | 0 | `tools/benchmarks/DataGuard.Benchmarks` remains an explicit isolated project; generated `BenchmarkDotNet.Artifacts/` is gitignored and the Release solution build/test path does not reference or execute benchmark code. |
| Health refresh interval contract | 0 | `RefreshIntervalSeconds` is configurable with a 1-hour upper bound; invalid non-positive/out-of-range values fail startup, and the focused lifecycle/validation suite passes 4/4. Full suite at that checkpoint: **659 passed, 0 failed** (Core 605, Golden 25, Analyzers 8, CodeFixes 21). |
| Supply-chain hash-anchor hardening | 0 | Expected hash anchors now require an absolute, non-symlink file within a 1 KiB bound; oversized and symbolic-link fixtures fail closed. SupplyChainVerifier focused suite passes 6/6. Latest full suite: **661 passed, 0 failed** (Core 607, Golden 25, Analyzers 8, CodeFixes 21). |
| Supply-chain hash-anchor parent-link hardening | 0 | Hash anchors are rejected when any parent directory is a symbolic link/reparse point, closing directory redirection before digest verification. SupplyChainVerifier focused suite passes 7/7. |
| Full-suite regression after parent-link hardening | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"` passes **662/662** (Core 608, Golden 25, Analyzers 8, CodeFixes 21). |
| Health startup/readiness matrix | 0 | Added no-wait startup false-readiness coverage with a fake clock and explicit Healthy/Degraded/Unhealthy/Unknown matrix; focused HealthProbeTests pass 12/12. |
| Full-suite regression after health readiness coverage | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"` passes **667/667** (Core 613, Golden 25, Analyzers 8, CodeFixes 21). |
| Credential plaintext opt-out metadata | 0 | Added regression proving explicit plaintext opt-out persists `IsEncrypted: false`; credential-focused suite passes 19/19 and never labels the plaintext record protected. |
| Credential metadata mismatch and health stop-race regression | 0 | Protected payloads with `IsEncrypted=false` now fail closed; health lifecycle assertion samples after awaited stop to avoid a tick race. Focused credential suite passes 20/20 and health host suite 4/4; full suite passes **669/669** (Core 615, Golden 25, Analyzers 8, CodeFixes 21). |
| Credential path preflight ordering | 0 | CredentialManager now validates existing ancestors before directory creation and revalidates after creation, preventing known symlink traversal before any filesystem mutation; focused credential suite 20/20 and full suite 669/669 pass. |
| Audit detail/error sanitization | 0 | `FileAuditLogger.LogDatabaseOperationAsync` now sanitizes secret-shaped details and errors and bounds persisted text to 4096 characters; focused audit suite 13/13 and full suite **670/670** (Core 616, Golden 25, Analyzers 8, CodeFixes 21) pass. |
| Audit JSON-shaped secret sanitization | 0 | Sanitizer now handles quoted JSON keys/values such as `"password":"..."` in addition to connection-string syntax; audit suite 13/13 and full suite 670/670 pass. |
| Audit quoted-secret sanitization | 0 | Sanitizer now redacts quoted values containing spaces (for example `Password="LONG SECRET"`) before unquoted processing; audit suite passes 13/13. |
| Release build after audit sanitizer hardening | 0 | `dotnet build DataGuard.sln --configuration Release --no-restore` completes with 0 warnings and 0 errors after the quoted-value sanitizer change. |
| Audit log path hardening | 0 | `FileAuditLogger` validates log parent/file reparse points before and after directory creation; symlink-parent regression passes. Audit suite 14/14 and full suite **671/671** (Core 617, Golden 25, Analyzers 8, CodeFixes 21) pass. |
| Plugin admission ancestor-link hardening | 0 | Plugin file and manifest reads now reject reparse-point ancestors below the OS temp root while preserving `/tmp`/macOS `/var/folders` aliases; PluginAdmission suite 17/17 and full suite 671/671 pass. |
| Benchmark dry-run artifact pipeline | 0 | Isolated BenchmarkDotNet dry run completed 15 offline benchmarks and emitted machine-readable artifacts/metadata on macOS ARM64; claim evaluator correctly rejected the dirty worktree (`worktreeState is not clean`), so no current performance claim was asserted. |
| Credential secret-store contract | 0 | Added additive `ICredentialSecretStore` contract and injected fake-store regression for platform-backed encrypted paths; CredentialManager suite passes 21/21 and full suite **672/672** (Core 618, Golden 25, Analyzers 8, CodeFixes 21). Native Linux daemon/Windows DPAPI host gates remain external. |
| Credential unavailable-store fail-closed contract | 0 | Injected unavailable secret store now proves encrypted writes fail before persistence; credential suite passes 22/22 and full suite **673/673** (Core 619, Golden 25, Analyzers 8, CodeFixes 21). |
| Credential reference-prefix validation | 0 | Encrypted persistence now allowlists `KEYCHAIN:`/`SECRET-SERVICE:` references and rejects unsupported injected prefixes before writing metadata; credential suite 23/23 and full suite **674/674** (Core 620, Golden 25, Analyzers 8, CodeFixes 21) pass. |
| Credential backend validation ordering | 0 | Protected reference prefixes are validated before invoking an injected backend, preventing an untrusted implementation from receiving the secret when its reference format is unsupported; full suite remains **674/674**. |
| Secret-store contract Release build | 0 | `dotnet build DataGuard.sln --configuration Release --no-restore` succeeds with 0 warnings and 0 errors after adding the injectable platform secret-store contract and pre-backend validation. |
| Local gate pipeline re-run | 130 | `scripts/verify_local_gates.sh` completed restore, Release build (0 warnings/errors), analyzer build, coverage (64.10%), TruffleHog (0 verified/unverified secrets), and standards audit. The blocking `act ci` job reached solution build/analyzer success but hung at the dotnet-format step under local emulation; canceled cleanly after 2m11s. |
| CI formatting timeout guard | 0 | `.github/workflows/ci.yml` now bounds the dotnet-format gate with a 5-minute timeout and 30-second kill-after window, so local/hosted runners fail clearly instead of hanging indefinitely. `actionlint .github/workflows/*.yml`, docs sync, plan checker, and `git diff --check` pass. |
| Support-table target framework scoring | 0 | Dependency health now evaluates target frameworks through the effective `LegacySupportTable`, including custom tables and curated `net10.0`/`net480`/`net481` rows, instead of a hard-coded prefix list. Regression coverage passes. |
| Health refresh/readiness interval safety | 0 | `HealthHostOptions.Validate()` now rejects non-positive intervals and any refresh interval at or above maximum snapshot age, preventing periodic stale readiness; validation regressions pass. |
| Full-suite regression after Sol fixes | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **677/677** (Core 623, Golden 25, Analyzers 8, CodeFixes 21). |
| Operator preflight manifest producer | 0 | Added `dataguard preflight --connection ... --provider ... --target ... --output ...`, requiring an explicit operator connection and allowlisted provider, acquiring live contracts outside MSBuild, and atomically writing a bounded redacted hash-bound manifest. Help/argument guards and writer regression pass. |
| Full-suite regression after preflight producer | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **678/678** (Core 624, Golden 25, Analyzers 8, CodeFixes 21); Release build remains 0 warnings/errors. |
| Preflight CLI documentation parity | 0 | Added the `preflight` operator workflow and its safety boundaries to the English and Vietnamese CLI documentation; docs sync and structural plan checks remain green. |
| Preflight manifest path hardening | 0 | Manifest output now rejects existing symlink/reparse-point parents and targets before atomic replacement; Release build and the full **678-test** suite remain green. |
| YAML downstream consumer round-trip | 0 | Contract YAML export is parsed back through the shared camelCase `ContractExport` schema, confirming provider/schema/entity/property fidelity; ContractExport focused suite passes 6/6. |
| Bounded preflight contract metadata | 0 | Operator manifests now include up to 1,000 bounded contract summaries (`id`, `name`, `type`); the MSBuild task validates the optional array while remaining compatible with legacy manifests. Focused manifest suite passes 12/12. |
| Full-suite regression after bounded manifest metadata | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **679/679** (Core 625, Golden 25, Analyzers 8, CodeFixes 21); Release build remains 0 warnings/errors. |
| FC09 provider/action accounting | 0 | Recorded the five shipped code-fix providers, their diagnostic scopes, verified transformation boundaries, and FixAll coverage; the contradictory twelve-provider claim remains an explicit owner gate. |
| Local gate pipeline rerun after manifest metadata | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` passes topology/docs/workflow validation, locked restore, Release build, analyzers, formatting, full tests with coverage, and coverage threshold. Full test total is **679**; merged coverage is **63.93% (8748/13683)**. Docker/act and history scanning remain intentionally environment-specific. |
| Manifest count/summary consistency hardening | 0 | Preflight refuses more than 1,000 contracts; MSBuild validation rejects mismatched `contractCount` and summary array length. Focused manifest suite passes 13/13; full suite passes **680/680** (Core 626, Golden 25, Analyzers 8, CodeFixes 21). |
| Preflight bound regression coverage | 0 | Added direct writer tests for the 128-character target bound and 1,000-contract cap; focused writer suite passes 3/3. Full suite now passes **682/682** (Core 628, Golden 25, Analyzers 8, CodeFixes 21), with Release build at 0 warnings/errors. |
| FC20 bounded prose audit | 0 | Scanned current product/overview/architecture/component docs for absolute secrecy, performance, dependency, provider-count, and network/DB claims; contextual claims are qualified and memory-dump/zero-allocation wording is bounded. Remaining FC20 work is explicit manual line-level review. |
| Phase 7 evidence/DB policy closure | 0 | Local gate output and dated execution evidence are reproducible and bounded; SQL Server, PostgreSQL, MySQL, and Oracle integration fixtures record explicit enabled/skip outcomes, and the latest live provider profile exercised 10/10 assertions. Phase 7 marks these two local criteria complete; Windows and ledger-owner closure remain open. |
| Acceptance DAG state reconciliation | 0 | Phase 7 and Phase 15 now correctly show `in-progress` after local verification; Phase 15 marks only safety/ABI/false-clean and occurrence/graph invariants proven. Full capability, external gates, and Sol final GO remain open. |
| Manifest reparse-ancestor hardening | 0 | Offline preflight writer and MSBuild validation task now reject reparse-point ancestors while allowing the OS temp alias used by test/runtime paths; focused OfflineManifest suite passes 15/15, Release build passes with 0 warnings/errors, and the full suite remains 682/682. |
| Final local gate rerun after path hardening | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` passes topology, docs, workflow, restore, Release build, analyzers, formatting, all 682 tests, and coverage threshold at **64.06% (8806/13747)**. Heavy TruffleHog/`act` simulation remains intentionally skipped and separately environment-specific. |
| Phase 7 lifecycle status reconciliation | 0 | Phase 7 is now marked `in-progress`, matching its completed local evidence and still-open Windows/owner closure criteria; no external gate is inferred as passed. |
| Phase 12 semantic-input pathway coverage | 0 | Existing regression suites cover YAML contract export round-trip, interactive CLI wizard output, and native/Husky/Lefthook managed hook transformations. Repository inspection confirms no body-parser pathway feeds semantic contract inputs; the criterion records that bounded scope explicitly. |
| VS Code CLI output containment | 0 | Extension CLI execution now drains stdout/stderr with a 16 KiB cap and applies the shared sensitive-text redactor before output-channel display. `npm test` passes 4/4 security tests and `npm run test:extension-host` passes; external SARIF locations remain rejected by workspace containment. |
| VS Code VSIX packaging after output hardening | 0 | `npm run package` succeeds, runs the LSP packaging/prepublish compile, and emits `dataguard-vscode-0.1.0.vsix` with the extension, packaged language server, manifest, and test fixtures. The artifact remains local evidence; Windows VSIX runtime smoke is still external. |
| Non-blocking benchmark CI lane | 0 | Added a pinned Ubuntu CI benchmark job with a 10-minute bound, deterministic `--classifier-only --job Dry` smoke, and always-uploaded artifact output. The same command executes locally on macOS ARM64 (3 classifier benchmarks); performance claims remain gated on clean-tree comparative runs. |
| Benchmark metadata validation contract | 0 | CI now injects the checked-out commit SHA, SDK version, and clean-worktree declaration before the benchmark smoke, then runs `validate_benchmark_claim.py`. The metadata evaluator accepts a locally simulated run when those explicit fields are supplied; comparative speedup/zero-allocation claims remain open. |
| Linux Secret Service output bounds | 0 | Secret Service bridge now bounds stdout to 1 MiB and stderr to 16 KiB while preserving concurrent pipe draining, failing closed when the helper exceeds limits. Release build succeeds with 0 warnings/errors and credential-focused tests pass 25/25; daemon-backed Linux integration remains platform-gated. |
| Full-suite regression after Secret Service bounds | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **682/682** (Core 628, Golden 25, Analyzers 8, CodeFixes 21). |
| Linux Secret Service helper timeout | 0 | `secret-tool` operations now have a 10-second hard timeout with process-tree termination and a fail-closed `TimeoutException`; Release build and credential-focused tests pass 25/25. |
| Full-suite regression after helper timeout | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **682/682** (Core 628, Golden 25, Analyzers 8, CodeFixes 21). |
| macOS Keychain integration check | 0 | `StoreConnectionString_EncryptAtRest_UsesAvailablePlatformStoreOrFailsClosed` passes on macOS and successfully exercises the available platform-store branch; Linux Secret Service daemon and Windows DPAPI remain separately gated. |
| Live four-provider integration rerun | 0 | With all four integration flags enabled and Docker available, `dotnet test ... --filter 'FullyQualifiedName~IntegrationTests'` passes **10/10** across SQL Server, PostgreSQL, MySQL, and Oracle Testcontainers. |
| Docker image and CLI smoke rerun | 0 | Docker daemon is available; `docker build --tag dataguard-remediation-smoke:local .` succeeds, the container exposes the expected command set including `preflight`, and missing explicit preflight connection fails closed with exit code 2. Docker build emits only expected SourceLink/MinVer warnings because the build context has no `.git` directory. |
| Benchmark documentation parity | 0 | `docs/PERFORMANCE.md` now documents the non-blocking CI benchmark job, metadata validation, uploaded raw artifacts, and the rule that smoke output cannot establish speedup or zero-allocation claims. Docs sync and structural checks remain green. |
| NuGet package artifact inspection | 0 | `dotnet pack DataGuard.sln --configuration Release --no-build --no-restore` produces 12 `.nupkg` artifacts in a temporary ignored directory; package hashes were recorded locally and `DataGuard.Build` contains both `build/` and `buildTransitive/` targets. No package was published. |
| TruffleHog verified-secret scan | 0 | Pinned TruffleHog 3.97.0 filesystem scan over the current workspace reports **0 verified and 0 unverified secrets** across 1,060,442,402 bytes. Generated build artifacts were excluded via the repository exclusion list. |
| Workspace preflight guard correction | 0 | `scripts/preflight_agent_check.sh` now allowlists the tracked `.release.env.example` and policy-approved runtime/cache paths (`TestResults`, `.commandcode`, `.tmp`). The guard rerun reports root topology compliant and bilingual docs synchronized. |
| Preflight shell validation | 0 | `bash -n scripts/preflight_agent_check.sh` and `shellcheck scripts/preflight_agent_check.sh` both pass after removing the unused color variable; `actionlint` remains green for all workflows. |
| Local gate pipeline with preflight guard | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` now invokes the workspace preflight guard and passes topology/docs/workflow validation, locked restore, Release build, analyzers, formatting, **682 tests**, and coverage threshold at **63.89% (8806/13783)**. TruffleHog/`act` remain separately verified or environment-specific. |
| Final phase checkbox audit | 0 | Current phase status is `in-progress` for all 15 phases. There are 32 explicitly unchecked acceptance criteria, concentrated in Windows/IDE runtime, signed provenance, compatibility-owner decisions, comparative performance, and final Sol/ledger acceptance; no incomplete criterion is represented as complete. Structural checker reports 0 issues. |
| Phase 15 blocker record reconciliation | 0 | Added owner, prerequisite, and resume-evidence records for Windows VSIX/DPAPI, signed provenance/SPDX, Linux Secret Service, comparative performance, and CP8 provider/API/Sol decisions. These remain explicit blockers and do not change Phase 15 to complete. |
| NuGet vulnerability scan on resumed audit | 0 | `dotnet list DataGuard.sln package --vulnerable --include-transitive --format json` exits 0 with zero reported problems and zero vulnerable packages. |
| VS Code npm vulnerability scan on resumed audit | 0 | `npm audit --audit-level=high --json` reports zero info/low/moderate/high/critical vulnerabilities in the extension dependency tree. |
| Published host readiness rerun | 0 | `scripts/verify_host_publish.sh` publishes the Release host, records RID `framework-dependent` and SHA-256, starts it on a loopback port, and verifies readiness successfully. |
| Language Server lifecycle rerun | 0 | `scripts/verify_language_server.sh` passes initialize, didOpen, didChange replacement, and didClose diagnostic clearing using the local classifier path. |
| Explicit external-gate disposition | 0 | Phase 7 now marks the Windows criterion complete only as an explicit `blocked_external` disposition, and Phase 15 marks external/absolute/performance disposition complete while retaining the blocking semantics. No blocked gate is counted as passed. |
| Claim census cardinality correction | 0 | Re-running `scripts/generate_claim_occurrences.py` is authoritative: 146 Markdown files, 131 current/15 historical, **539** mapped candidate occurrences, and 0 unmatched command candidates. Stale 541 references were corrected in Phase 8 and FC20 evidence. |
| Preflight rerun after census reconciliation | 0 | `scripts/preflight_agent_check.sh` passes again: root topology is compliant and all required bilingual documentation artifacts are synchronized. |
| Phase 6 historical/ledger disposition | 0 | Claim census preserves 15 historical/proposal files, and `findings-ledger.md` retains F6 as `open` with the tracked `.pyc` retain disposition; no blocked or unevidenced finding was marked terminal-success. |
| Benchmark truncation contract probe | 0 | A bounded pipeline probe exposed a real sequential/concurrent `DroppedViolationCount` mismatch under truncation (unknown sequential total versus known concurrent total). The probe was removed before CI integration; the existing uncapped equivalence smoke remains green, and the Phase 14 capped-output criterion stays open pending a contract-level fix. |
| Final format gate after benchmark/preflight edits | 0 | `dotnet format DataGuard.sln --verify-no-changes --no-restore` and `dotnet format whitespace DataGuard.sln --verify-no-changes` both pass; only expected workspace-loading warnings are emitted. |
| Preflight pipefail hardening | 0 | `scripts/preflight_agent_check.sh` now runs with `set -euo pipefail`, preventing failures inside command pipelines from being silently ignored. `bash -n`, ShellCheck, the live preflight run, plan structural checker, and `git diff --check` all pass. |
| Full local gate rerun after pipefail hardening | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` passes preflight/topology/docs/workflow validation, locked restore, Release build (0 warnings/errors), analyzers, formatting, all four test projects (**682/682**), and coverage **63.89% (8806/13783)**. Environment-specific TruffleHog/act steps remain separately verified or hosted-CI gates. |
| Snapshot schema acquisition across adapters | 0 | Snapshot refresh now reuses one acquisition result and persists any `DatabaseSchemaDescriptor` from the selected provider. SQL Server parser now reads `INFORMATION_SCHEMA.COLUMNS` into a schema descriptor; live SQL Server parser integration (1 test) and the existing two SQL Server integration tests pass, Release build is clean, formatting/docs checks pass. Full four-provider snapshot matrix remains an open acceptance criterion. |
| Core regression after multi-provider snapshot changes | 0 | `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build --no-restore` passes **628/628**. |
| Snapshot v3 column metadata preservation | 0 | `SnapshotColumn` now carries additive `DataDefault` and `ColumnId` fields; schema hashing includes length, character semantics, default and ordinal metadata. Regression tests prove default/ordinal changes alter the hash; baseline metadata tests pass 4/4, SQL Server integration passes, and Release build is clean. |
| PostgreSQL schema descriptor integration | 0 | PostgreSQL adapter now appends a schema descriptor from `information_schema.columns` on every live extraction, including routine-empty schemas. Live MySQL/PostgreSQL integration tests pass **2/2** with the schema assertion; Release build remains clean. |
| Oracle snapshot schema acquisition restoration | 0 | Oracle CLI acquisition now appends `DatabaseSchemaDescriptor` from `AllTabColumnsReader` and records resolved NLS length semantics, so the unified snapshot refresh path retains Oracle structural ground truth. Release build and CLI exit contract tests (15/15) pass; live Oracle execution remains environment-gated. |
| Provider schema matrix regression | 0 | Oracle acquisition now emits schema plus resolved NLS semantics; PostgreSQL always emits schema; MySQL and SQL Server emit `INFORMATION_SCHEMA` schema descriptors. Release build passes; Core suite passes **629/629** after the new metadata/provider regression coverage. |
| Drift metadata round-trip | 0 | Offline snapshot loading and public `CheckDriftAsync` now preserve `DataDefault` and `ColumnId`; live schema conversion and snapshot conversion feed the same v3 hash contract. Build is clean and focused Public API/CLI/baseline tests pass **28/28**. |
| Full Core regression after drift metadata wiring | 0 | Core suite passes **629/629** after adding v3 default/ordinal preservation through offline/public drift paths. |
| Snapshot v3 envelope metadata | 0 | Schema-bearing baselines now serialize as version 3 with `SchemaHashKind`, `Provider`, `SchemaScope`, and `SchemaCanonicalizationVersion`; violation-only baselines remain version 2 and legacy v1 migration remains intact. Public drift accepts v2/v3 and rejects unknown v3 hash kinds. Focused baseline/API/CLI tests pass **28/28**. |
| Snapshot v3 provider/scope validation | 0 | CLI snapshot refresh now records provider, normalized schema scope and canonicalizer version; CLI diff rejects unsupported hash kinds and provider mismatches before acquisition, returning UNEVALUATED (3). Focused CLI/API/baseline tests pass **28/28**, Release build is clean. |
| Snapshot v3 scope mismatch fail-closed | 0 | CLI snapshot diff now rejects a selected schema scope that differs from the recorded snapshot scope before live acquisition. Build is clean and CLI/API drift tests pass **23/23**. |
| V3 provider mismatch regression | 0 | Added a real CLI fixture proving a version-3 snapshot with `Provider: sqlserver` and a PostgreSQL selection exits **3** with `UNEVALUATED` before attempting connection. CLI exit-code suite passes **16/16**. |
| Provider default-expression capture | 0 | MySQL and PostgreSQL schema readers now select `COLUMN_DEFAULT`/`column_default` into `ColumnDescriptor.DataDefault` while retaining ordinal metadata. Live MySQL/PostgreSQL integration tests pass **2/2** and Release build is clean. |
| SQL Server default-expression capture | 0 | SQL Server schema reader now selects `INFORMATION_SCHEMA.COLUMNS.COLUMN_DEFAULT` and preserves it with ordinal position. Live SQL Server parser integration passes **1/1** and Release build is clean. |
| Empty evaluated schema semantics | 0 | Snapshot acquisition/persistence/diff now treats `Schema: []` as a valid evaluated empty schema, while absent `Schema` remains UNEVALUATED. Regression tests cover both states and pass **2/2**. |
| Full Core regression after v3 hash and empty-schema semantics | 0 | Core suite passes **631/631** after provider/scope metadata hashing and explicit empty-schema handling. |
| V3 canonical hash metadata binding | 0 | Canonical schema hashes now bind provider, selected scope, canonicalizer version and all column metadata; v2 legacy hashes retain their prior representation. Empty-schema and v3 baseline fallback tests pass; build and focused drift tests pass **14/14**. |
| Snapshot v3 operator visibility | 0 | `snapshot show` now displays hash kind, provider, schema scope and canonicalization version; EN/VI CLI docs document the added output. Build, CLI suite (16/16), docs sync and structural checks pass. |
| Automatic canonical hash for schema baselines | 0 | `BaselineManager.CreateBaselineAsync` now computes the canonical schema hash whenever `schema` is supplied and `schemaHash` is omitted, including empty evaluated schemas; violation hashing remains for schema-less v2 baselines. Regression coverage passes **6/6**. |
| Full Core regression after automatic schema hashing | 0 | Core suite passes **632/632** after schema-bearing baseline hash defaulting and v3 metadata changes. |
| Canonicalization version fail-closed | 0 | CLI and public drift validation now reject v3 snapshots whose canonicalization version is not `v1`; the public regression fixture passes with `UnsupportedVersion` and no clean result. Focused Public API tests pass **10/10**. |
| Full solution regression after snapshot v3 hardening | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **687/687** (Core 633, Golden 25, Analyzers 8, CodeFixes 21). |
| Full local gates after snapshot v3 envelope | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` passes topology/preflight/docs/workflows, restore, Release build (0 warnings/errors), analyzers, formatting, all tests (**687/687**), and coverage **63.86% (8986/14071)**. Hosted/act-heavy gates remain environment-specific. |
| Snapshot scope normalization | 0 | Provider scope is now resolved once and trimmed before persistence/hash/comparison, while preserving provider identifier casing semantics. Build and CLI/public drift tests pass **26/26**. |
| Public API v3 provider/scope mismatch | 0 | `ValidationPipeline.CheckDriftAsync` now returns UNEVALUATED for configured provider or scope mismatches before comparing hashes, matching CLI fail-closed behavior. Public API regression suite passes **11/11**. |
| Public v3 missing-provider fail-closed | 0 | Public drift now returns UNEVALUATED when a v3 snapshot records provider/scope but the configured drift context omits them, avoiding an `unknown` hash comparison. Regression suite passes **12/12**. |
| Baseline v3 documentation envelope | 0 | EN/VI baseline docs now show version-3 JSON metadata (`schemaHashKind`, provider, scope, canonicalizer) and distinguish v2 violation-only files from v3 schema-bearing files. Docs sync and plan structural checks pass. |
| SQL Server default metadata assertion | 0 | Live SQL Server integration now asserts the captured `Status` column preserves its `COLUMN_DEFAULT` expression and schema ordinal path; test passes **1/1**. |
| PostgreSQL default metadata assertion | 0 | Live PostgreSQL fixture now creates a defaulted table and asserts `column_default` is preserved in the emitted schema descriptor; integration test passes **1/1**. |
| MySQL default metadata assertion | 0 | Live MySQL fixture now creates a defaulted table and asserts `COLUMN_DEFAULT` is preserved in the emitted schema descriptor; MySQL/PostgreSQL integration pair passes **2/2**. |
| Full solution regression after relational default assertions | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **689/689** (Core 635, Golden 25, Analyzers 8, CodeFixes 21) after MySQL/PostgreSQL/SQL Server default metadata coverage. |
| Sequential/concurrent truncation contract alignment | 0 | Sequential public validation now evaluates all scheduled rules under a violation cap, retains capped output, and reports the same exact `DroppedViolationCount` semantics as concurrent execution; regression suite passes **10/10** and full solution passes **689/689**. |
| Local gates after truncation contract alignment | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` passes topology/preflight/docs/workflows, locked restore, Release build (0 warnings/errors), formatting, all tests (**689/689**), and coverage **63.80% (9020/14138)**. Heavy TruffleHog/act and hosted matrix remain separately gated. |
| VS Code global run coordination | 0 | Added an identity-safe `RunCoordinator` enforcing one global run, deterministic replacement cancellation, and stale cleanup protection; extension TypeScript and security tests pass **6/6** via `npm test`. Existing docs' global cancellation contract now has a directly tested implementation seam. |
| VS Code extension-host smoke | 0 | `npm run test:extension-host` activates the packaged development extension, verifies all five commands, and receives a live `DGSQL001` language-server diagnostic; exit code 0. |
| VS Code command argument safety | 0 | Added tested provider normalization and positional CLI argv construction for Validate/Assess/Snapshot/Baseline; injection-like provider values are rejected. VS Code suite passes **8/8** via `npm test`. |
| VS Code extension host after command safety refactor | 0 | Extension-host smoke still activates, registers all commands, and exercises live LSP diagnostics with exit code 0 after the tested argv/coordinator refactor. |
| Current full-claims gate refresh | 0 | Updated full-claims evidence to the current 689-test/63.80%-coverage gate while preserving explicit Windows, Linux, signing, owner, and exhaustive-prose blockers; docs sync, structural checker, and diff check pass. |
| VS Code sanitized SARIF summary | 0 | Extension now reports only a bounded finding count after contained SARIF parsing (`SARIF summary: N finding(s)`), while raw CLI/SARIF content remains redacted and never rendered as an editor document. VS Code unit suite **8/8** and extension-host smoke pass. |
| VS Code SARIF loaded-count correction | 0 | SARIF summary now counts only diagnostics accepted after workspace containment filtering, excluding ignored external locations; correct-path `npm test` passes **8/8**. |
| CLI snapshot schema metadata round-trip | 0 | Snapshot refresh conversion now persists `DataDefault` and `ColumnId` from every provider schema descriptor before canonical v3 hashing; focused CLI/baseline/Public API regression suite passes **28/28**, Release build clean. |
| Full regression after CLI snapshot metadata round-trip fix | 0 | `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` passes **689/689** (Core 635, Golden 25, Analyzers 8, CodeFixes 21). |
| Final current-tree structural audit | 0 | Rechecked docs synchronization, 36-file/130-link/60-scout-ID/66-ledger-row/15-phase structural invariants and `git diff --check`; all pass. Remaining open rows are explicit external/owner or scope acceptance gates. |
| Local gates after latest snapshot/VS Code changes | 0 | `SKIP_ACT=1 ./scripts/verify_local_gates.sh` passes topology/preflight/docs/workflows, restore, Release build (0 warnings/errors), formatting, all tests (**689/689**), and coverage **63.71% (9020/14158)**. Heavy TruffleHog/act and hosted matrix remain environment-gated. |
| Required live four-provider regression after snapshot metadata fix | 0 | With SQL Server and relational live gates required, `dotnet test ... --filter 'FullyQualifiedName~IntegrationTests'` passes **10/10** across SQL Server, PostgreSQL, MySQL, and Oracle Testcontainers. |
| Visual Studio net472 build probe | 0 | `dotnet build src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj --configuration Release --no-restore` is blocked on macOS because the VSSDK package resolves a Windows-only `vsct.exe` path (`MSB6004`); this confirms the documented Windows external gate rather than a source compile result. |
| Full-history verified-secret scan | 0 | Local `trufflehog git file://... --no-update --only-verified --fail` completed after scanning 1.9 GB/154,608 chunks with **0 verified and 0 unverified secrets**; binary parsing warnings were non-critical and did not produce findings. |
| Workflow and ARM64 container smoke after latest changes | 0 | `actionlint .github/workflows/*.yml` passes; Docker builds the `linux/arm64` image and runs the non-root CLI `version` command successfully. |
| Dependency vulnerability audit refresh | 0 | VS Code `npm audit --omit=dev --audit-level=high` reports **0 vulnerabilities**; root `dotnet list DataGuard.sln package --vulnerable --include-transitive --format json` reports no problems and no vulnerable packages. |
| Working-tree Gitleaks scan | 0 | `gitleaks dir . --no-banner --redact` reports no findings in the current tree. Full-history Gitleaks separately surfaced one historical `curl-auth-header` placeholder in `docs/mcp.md` (`REDACTED`), a documented sample rather than a verified secret; TruffleHog full-history verified-secret scan remains 0/0. |
| Claim occurrence census regeneration | 0 | `python3 scripts/generate_claim_occurrences.py` reproduces the committed deterministic inventory with 0 unmatched candidates; plan checker remains clean at 36 files, 130 links, 60 scout IDs, 66 ledger rows and 15 phases. |
| Benchmark pipeline dry profile | 0 | Isolated pipeline benchmark dry run executes sequential/concurrent 100/1,000-contract scenarios and proves capped-output equivalence before timing; observed ratios are 2.99x (100) and 1.54x (1,000) concurrent/ sequential. Claim evaluator correctly rejects this dirty-tree run because metadata lacks the required `worktreeState`, so no performance claim is promoted. |

## CP0 — 2026-09-18 continuation fingerprint

The 2026-09-12 CP0 snapshot remains historical evidence. A new continuation baseline was captured
before resuming remediation; it does not retroactively alter that snapshot.

| Field | Recorded value |
|---|---|
| Captured | 2026-09-18 |
| Commit | `2ff02e4320c109e42546852699522f5d1bc685d5` |
| Tracked-diff command and SHA-256 | `git diff --binary -- . ':(exclude)plans/260912-2016-scout-remediation/reports/execution-evidence.md' \| sha256sum` → `9a3e4f610e277caab1c60b20c9e342f9e8348a9e86a8562859080128d1f0828b` |
| Worktree command | `git status --porcelain` |
| Test gate | `dotnet test DataGuard.sln -c Release` with `DOTNET_ROLL_FORWARD=LatestMajor`: 783 passed, 0 failed |

The tracked paths are `DataGuard.sln`; both Visual Studio extension documentation pages;
`plans/2026-08-18-agentize/plan.md`; Phase 1, 8 and the remediation index; both observability
Phase 6 files; four completed-plan metadata files; `src/DataGuard.Cli/Program.cs`;
`ConcurrentValidationEngine.cs`; three Visual Studio extension source/project files; two Core test
files; and `tools/verify-vs-cli-launch.ps1`. The untracked paths are both root VS plan files, the
VS-red-team journal, `ProgressEmitter.cs`, `ProgressEvent.cs`, `BindingRedirects.cs`, the Visual
Studio `NuGet/` directory, `ProgressEmitterTests.cs`, and the Visual Studio test directory.

The continuation limits are explicit: the current suite does not prove live SQL Server, Oracle,
PostgreSQL or MySQL behavior; it does not prove Windows Visual Studio/VSSDK/DPAPI behavior; it
does not execute release publication, external network services or owner-gated environments.

The original sequence breached the intended pre-Phase-2 CP1 freeze. This continuation baseline is
prospective only: it preserves that breach and supplies a fresh caller/ABI map for future edits;
it does not retroactively authorize already-existing implementation.

### CP1 continuation caller and ABI inventory

The continuation census is bound to the commit and scoped tracked-diff hash above. Current command roots
are `Program.cs:130` (`validate`), `345` (`preflight`), `405` (`baseline`), `465/466/580/626`
(`snapshot`), `822` (`init`), `880/881/905/908` (`hook`), `927/928/949` (`config`), `981`
(`oracle-check`), `1045` (`version`), `1065` (`migrate`) and `1109` (`assess`). Configuration and
YAML seams are `Program.cs:1328/1358/1453/1539` and `CliConfigurationResolver.cs:8`;
`ProviderRuleCatalog.cs:11` is the provider composition seam.

The protected API seams are `PublicApiSurface.cs:34/44` (`CreatePipeline`), `:99/106`
(`WithPlugins`), `:293/335` (`CheckDriftAsync`) and `:460-469` (plugin disposal); the direct
engine seam is `ConcurrentValidationEngine.cs:35/274`. ABI constructors/deconstruction are
`Configuration.cs:6`, `Contracts.cs:171` (`StoredProcedureDescriptor`),
`BaselineManager.cs:458/475/479` (baseline/snapshot records), and
`PublicApiSurface.cs:478` (`ValidationResult`). The retained compiled-consumer fixture at
`DataGuard.BinaryCompatibilityFixture/Program.cs:7-22` exercises configuration, pipeline,
validation-result, legacy-engine, `SnapshotColumn`, `SnapshotTable`, `BaselineFile`, and
`StoredProcedureDescriptor` constructor/deconstruction compatibility.

Procedure construction/routing is at `Program.cs:1644`, `ManualContractSource.cs:124`,
`SqlServerParsers.cs:71`, the MySQL and PostgreSQL parsers, and `ContractExport.cs:170`, with
compatibility coverage in `ContractExportTests.cs:28`. EF source/trusted-artifact seams are
`EfModelSource.cs:216/234/613/638-648/723`; persisted snapshot seams are
`BaselineManager.cs:80/325/382` and `Program.cs:518/769`; IDE completion handlers are
`DataGuardPackage.cs:511/724-728` and `extension.ts:310-335`.

### Untracked input identities

The following hashes are from `git ls-files --others --exclude-standard -z | sort -z | xargs -0 sha256sum`:

```text
37285207f325fbc232550311590d316efe324346f7704c7e0f50338178dcf1b9  DATAGUARD_VS_EXTENSION_PLAN.md
a16e292e19f2a34f74efd8db22bf98511c7f805e9bf4dd315368371866753c09  docs/journals/260918-0749-vs-redteam-lifecycle-remediation.md
0b5dc09f3f07f138f829c6bc7cfc5b9ff6106d88fb2a27e8a0a432eaceaadb69  FIX_DATAGUARD_VISUAL_STUDIO_PLAN.md
e3a7fd8019095728856f1808857f6ce7c1f9f9404fa5fca47844714c5726540e  src/DataGuard.Core/Reporting/ProgressEmitter.cs
280a34d7b9f07e370ba61c05d1024d73728085e9b33bbd0264ddd8430a732d7e  src/DataGuard.Core/Reporting/ProgressEvent.cs
e6800880d340dc5d433f5740b3188ffd067746adf6886c5e705ecbf457962657  src/DataGuard.VisualStudio/BindingRedirects.cs
e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  src/DataGuard.VisualStudio/NuGet/Migrations/1
cea4d74624be767e0569c891597c0e6f5329f19f6c9b7fcf8b8c9e9f4933a4bb  src/DataGuard.VisualStudio/NuGet/v3-cache/670c1461c29885f9aa22c281d8b7da90845b38e4$ps_api.nuget.org_v3_index.json/service_index.dat
e50e838b5b651f067a8756c33e2aca03a145e36fe6161b717bdf06ea03f083dc  src/DataGuard.VisualStudio/NuGet/v3-cache/670c1461c29885f9aa22c281d8b7da90845b38e4$ps_api.nuget.org_v3_index.json/vuln_data_base.dat
b99fb8f6e723531cd0de4137ab19b75b94684333b5ecf12b229af011252081e6  src/DataGuard.VisualStudio/NuGet/v3-cache/670c1461c29885f9aa22c281d8b7da90845b38e4$ps_api.nuget.org_v3_index.json/vuln_data_update.dat
c830fa2b221742be5e122ab7ccd3e74deaf8cab768d3d2922f784a9d1a216f23  src/DataGuard.VisualStudio/NuGet/v3-cache/670c1461c29885f9aa22c281d8b7da90845b38e4$ps_api.nuget.org_v3_index.json/vuln_index.dat
279a6f648f9ff3b063362655970bf8b31152f782ca0ab05492958d8f318d44a9  tests/DataGuard.Core.Tests/ProgressEmitterTests.cs
d0eb3007c95d9d3149f9a34deef6658ea9689f22efd8b6c500b19ac899824483  tests/DataGuard.VisualStudio.Tests/DataGuardPackageTests.cs
57d69f21953a10e4a084c7490625c4ff9bda91e2062d34ae708d16cc5cacc138  tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj
c32435d7053bff52a050cdc4e33441939a4fa2c183e06a34be2df84a0c94167c  tests/DataGuard.VisualStudio.Tests/packages.lock.json
```

The cache files are recorded only as untrusted build inputs and are not accepted as product provenance.

### Retained binary-consumer compatibility — 2026-09-18

An isolated worktree at baseline commit `93bf7288324dd746669ad09c5e2a592adc772748` built
`DataGuard.Core` Release with 0 warnings/errors. The binary fixture was then compiled against that
baseline assembly with its complete declared dependency closure. It was executed against current
Core through the current CLI Release runtime closure and printed:

```text
DataGuard.ValidationPipeline:True:0
```

The baseline-shaped consumer constructs and deconstructs `DataGuardConfiguration`,
`ValidationResult`, `SnapshotColumn`, `SnapshotTable`, `BaselineFile`, and
`StoredProcedureDescriptor`. Current Core exposes explicit legacy 8-argument constructors and
8-argument `Deconstruct` overloads for `SnapshotColumn` and `BaselineFile`, while preserving their
current v3 members. The original pre-CP1 sequencing breach remains historical and is not
retroactively authorized.

The retained-consumer artifacts are bound as follows:

```text
78e2757624e7a79d0129770ab4e4934f51fb902a569e64a92207d4fc94b1b0e9  baseline Core DLL built in detached 93bf728 worktree
277a26fb0c0697841acd7460ed891e00826aaac5de901090d0d662c28591164d  retained fixture DLL compiled against that baseline Core DLL
01c6618680ab8a70d135f1ad8aa61ff5619bab0e4af02017d1d52f43f492a896  current Core DLL loaded through the CLI Release runtime closure
```

CP1 remains a candidate until an independent Sol review accepts this continuation inventory and its
compatibility constraints.

The post-compatibility Release suite also passed **783/783**: Core 667, Observability 38,
Analyzers 13, Golden Corpus 28, Visual Studio 16 and Code Fixes 21. The retained baseline
consumer was rerun against the current CLI runtime closure with the same successful output above.
