# FluentAssertions alternatives — compile/test feasibility check

Date: 2026-09-30 | Worktree: `.claude/worktrees/agent-a6ad3d6d5f7f32547` (throwaway, left dirty, no commits)
Question: can the six test projects move off FluentAssertions 8.11.0 (Xceed commercial licence) to an Apache-2.0 assertion library with zero or trivial code changes?

Baseline (current `main`): Core.Tests / CodeFixes.Tests / Analyzers.Tests / GoldenCorpus.Tests / Observability.Tests on FluentAssertions 8.11.0 (net9.0); VisualStudio.Tests on FluentAssertions 6.12.2 (net472). 80 test `.cs` files carry `using FluentAssertions;` — no sub-namespaces (`.Execution`, `.Extensions`), no fully-qualified references, no `AssertionScope`/`AssertionOptions` customisation. Assertion surface used: `BeTrue/BeFalse/BeEmpty/BeNull/ContainSingle/BeEquivalentTo/WithMessage/BeSameAs/BeOfType/BeGreaterThan[OrEqualTo]/BeLessThan/BeLessOrEqualTo/HaveCountGreaterOrEqualTo`.

Build env: `Directory.Build.props` has `TreatWarningsAsErrors=true`, NetAnalyzers 10.0.401, StyleCop 1.1.118, `RestorePackagesWithLockFile=true`. Worktree path length 88 chars — no MAX_PATH issue. All builds `-c Release -m:1`, one at a time.

## Trial 1 — FluentAssertions 7.2.2

| Item | Result |
|---|---|
| Version | **7.2.2** (latest 7.x on NuGet; published 2026-03-16; licence `Apache-2.0` per nuspec). 7.x is the last Apache line — 8.0.0+ is Xceed commercial. |
| TFMs | net47, netstandard2.0, netstandard2.1, net6.0 — covers net472 and net9.0 |
| csproj change | All six: `Version="8.11.0"`/`"6.12.2"` → `"7.2.2"` |
| Restore (`--force-evaluate`) | OK, 0 warnings |
| Build | **0 errors, 0 warnings** (2m03s). No CS/CA/SA/NU diagnostics. |
| Bin check | `FluentAssertions.dll` in all 6 test bin dirs; `project.assets.json` resolves `FluentAssertions/7.2.2` in all 6 |
| Tests (`--no-build`) | Core **898/898**, GoldenCorpus **28/28**, Analyzers **13/13**, CodeFixes **24/24**, Observability **38/38**, VisualStudio **155 pass + 1 skip** — identical to baseline |
| Behavioural diffs | None observed. 10 `BeEquivalentTo` sites (collection + anonymous-object equivalency) all pass unchanged. |
| NuGet audit (`-p:NuGetAuditMode=all`) | **0 warnings** (no NU1901–NU1904) |
| Lock-file impact | 12 files changed (6 csproj + 6 lock files), +187/−25. 7.2.2 pulls a new transitive `System.Configuration.ConfigurationManager 6.0.0` (+ its chain) into the net9.0 test projects; net472 project unchanged apart from the version bump. |
| Code changes needed | **None.** `BeLessOrEqualTo` / `HaveCountGreaterOrEqualTo` still exist and are not `[Obsolete]` in 7.2.2 (no CS0618, so no warnings-as-errors hit). |

Verdict: **drop-in, zero code changes.** Caveats: last 7.x release is 2026-03-16 (6 months ago); 8.x is the active feature line (8.11.0 on 2026-09-14). Xceed's ongoing maintenance policy for 7.x was not verified here. VisualStudio.Tests moves *up* from 6.12.2 to 7.2.2 — also clean.

## Trial 2 — AwesomeAssertions 9.6.0

| Item | Result |
|---|---|
| Version | **9.6.0** (latest stable; published 2026-08-20; licence `Apache-2.0`). Community fork of FluentAssertions (nuspec: "A fork of FluentAssertions"); versions on NuGet span 7.0.0 → 9.6.0. |
| TFMs | net47, netstandard2.0, netstandard2.1, net6.0, net8.0 — covers net472 and net9.0 |
| csproj change | All six: `<PackageReference Include="FluentAssertions" .../>` → `<PackageReference Include="AwesomeAssertions" Version="9.6.0" />` |
| Restore (`--force-evaluate`, test bin/obj wiped first) | OK, 0 warnings; `project.assets.json` resolves `AwesomeAssertions/9.6.0` in all 6, no `FluentAssertions` anywhere in the graph |
| Build, pass 1 (no code edits) | **FAIL — 80 × CS0246** `The type or namespace name 'FluentAssertions' could not be found` — one per file with `using FluentAssertions;`. First 10: `Analyzers.Tests/DescriptorArityTests.cs(7,7)`, `Analyzers.Tests/GeneratorExecutionTests.cs(7,7)`, `CodeFixes.Tests/CodeFixProviderTests.cs(14,7)`, `Core.Tests/AuditAndConfigTests.cs(7,7)`, `Core.Tests/AutoDetectionEngineTests.cs(9,7)`, `Core.Tests/BaselineCacheTests.cs(2,7)`, `Core.Tests/CliBaselineSuppressionEndToEndTests.cs(2,7)`, `Core.Tests/CliExitCodeTests.cs(1,7)`, `Core.Tests/CliProcessTestRunner.cs(2,7)`, `Core.Tests/ConcurrentValidationExecutionTests.cs(8,7)`. |
| Namespace | AwesomeAssertions **9.x does NOT keep the `FluentAssertions` root namespace.** XML doc for 9.6.0 lists 343 types under `AwesomeAssertions.*`, 0 under `FluentAssertions.*` (assembly is `AwesomeAssertions.dll`). The rename happened somewhere between 8.2.0 and 9.6.0 (9.0.0 itself not inspected); the **8.x line (last: 8.2.0, 2025-05-08) still ships `FluentAssertions.dll` with 236 types under `FluentAssertions.*`** — i.e. 8.2.0 would be the zero-`using`-change fork option, at the cost of being 16 months stale. |
| Build, pass 2 (after `using FluentAssertions;` → `using AwesomeAssertions;` in 80 files) | **FAIL — 2 × CS1061**, both in VisualStudio.Tests (the project that was on FA 6.12.2): `ProgressPumpTests.cs(77,34)` `'NumericAssertions<int>' does not contain a definition for 'BeLessOrEqualTo'`; `VsixAnalyzerPackagingTests.cs(98,33)` `'GenericCollectionAssertions<XElement>' does not contain a definition for 'HaveCountGreaterOrEqualTo'`. These are the FA-8 renames (`…ThanOrEqualTo`), inherited by AA 9.x. |
| Build, pass 3 (after the 2 renames) | **0 errors, 0 warnings** (58s). No CA/SA analyzer hits. |
| Bin check | `AwesomeAssertions.dll` in all 6 test bin dirs (incl. net472), no `FluentAssertions.dll` |
| Tests (`--no-build`) | Core **898/898**, GoldenCorpus **28/28**, Analyzers **13/13**, CodeFixes **24/24**, Observability **38/38**, VisualStudio **155 pass + 1 skip** — identical to baseline |
| Behavioural diffs | None observed. |
| NuGet audit (`-p:NuGetAuditMode=all`) | **0 warnings** |
| Lock-file impact | 6 lock files, minimal: package id/version/hash swap only. net9.0 projects gain **no** new transitive deps (cleaner than Trial 1). net472 project: `System.Threading.Tasks.Extensions 4.5.0 → 4.5.4`. |

Code changes needed (exact list, 82 edits, all mechanical):
1. `using FluentAssertions;` → `using AwesomeAssertions;` in **80 files** (all top-level `.cs` in the six test dirs; no subdirectories). One sed: `sed -i 's/^using FluentAssertions;/using AwesomeAssertions;/' tests/DataGuard.*.Tests/*.cs`
2. `tests/DataGuard.VisualStudio.Tests/ProgressPumpTests.cs:77` — `.BeLessOrEqualTo(` → `.BeLessThanOrEqualTo(`
3. `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs:98` — `.HaveCountGreaterOrEqualTo(` → `.HaveCountGreaterThanOrEqualTo(`

Verdict: **feasible, trivial-but-wide change** (1 sed over 80 files + 2 method renames). Not zero-touch because 9.x renamed the namespace. Actively maintained, API parity with FA 8, no new transitive deps on net9.0.

## Comparison / recommendation

| | FA 7.2.2 | AwesomeAssertions 9.6.0 |
|---|---|---|
| Licence | Apache-2.0 | Apache-2.0 |
| Code changes | 0 | 82 mechanical edits (80 `using` + 2 renames) |
| Build/test | clean, baseline-identical | clean, baseline-identical |
| Audit | 0 | 0 |
| Last release | 2026-03-16 (8.x is the active feature line) | 2026-08-20 |
| Transitive deps | adds `System.Configuration.ConfigurationManager` chain on net9.0 | none added on net9.0 |
| Future upgrade path | no Apache release above 7.x from Xceed | tracks FA 8 API |

- Goal = **licence compliance with the smallest diff**: FA 7.2.2, zero code edits, csproj-only. Accept that there is no Apache-licensed successor above 7.x from the original vendor.
- Goal = **a maintained Apache library**: AwesomeAssertions 9.6.0; the 82 edits are one sed plus two one-word renames, and VisualStudio.Tests gets aligned with the other five (it was 2 majors behind).
- Middle option not trialled end-to-end: AwesomeAssertions **8.2.0** keeps `FluentAssertions` namespace + assembly name, so likely zero `using` edits, but stale (2025-05); AA 8.x tracks FA 8 which already dropped the old `…OrEqualTo` names, so expect the same 2 CS1061 in VisualStudio.Tests. Not verified by compile.

Either way the swap is per-csproj only; no `Directory.Build.props`/`Directory.Packages.props` involvement (no CPM in this repo).

## Reproduction notes
- Logs in worktree `.trial-logs/` (t1-restore/build/test/audit, t2-restore/build/t2b-build/t2c-build/test/audit).
- Worktree left dirty at Trial 2 end state (AwesomeAssertions 9.6.0 + 82 edits + regenerated lock files). No commits, no push.
- Core.Tests: 898 passed, 0 failed, 0 skipped in both trials — matches baseline exactly.

## Unresolved questions
- Whether the team prefers the zero-diff option (FA 7.2.2) or the more recently released fork (AA 9.6.0) — product/licence-policy call.
- AA 8.2.0 as a zero-touch fork was only inspected (namespace confirmed via XML doc), not compiled.
- Xceed's stated maintenance policy for the FA 7.x line was not checked.

---

**Status:** DONE
**Summary:** Both Apache-2.0 candidates build clean under TreatWarningsAsErrors and reproduce the exact baseline test counts (898 / 28 / 13 / 24 / 38 / 155+1 skip). FluentAssertions 7.2.2 is a csproj-only, zero-code-change swap; AwesomeAssertions 9.6.0 needs 80 `using` renames plus 2 method renames in VisualStudio.Tests because 9.x moved to the `AwesomeAssertions` namespace. NuGet audit clean for both.
**Concerns/Blockers:** None blocking. Open items: FA 7.x vendor maintenance policy unverified; AA 8.2.0 (keeps `FluentAssertions` namespace) not compile-tested.
