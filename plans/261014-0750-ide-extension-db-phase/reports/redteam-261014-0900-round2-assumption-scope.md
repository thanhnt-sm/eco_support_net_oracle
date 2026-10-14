# Red-team report (round 2, Visual Studio-focused plan, 4 phases)

# Red-team round 2: Assumption Destroyer and Scope Auditor (VS plan, 4 phases)

I read only the plan files, the research files and the cited code. Round-1 mitigations look correct, except where a finding below says otherwise.

## Findings

### 1. Phase 3 adds EF entities that Visual Studio cannot show, and it removes the honesty warning
**Severity:** Critical
**Location:** phase-03 Key Insights ("Oracles … DG006"), 3.V e2e, Success Criteria ("Error List có DG006 thật"); this also defeats Phase 2 Req 3.

**Flaw and failure scenario:**
- Every violation that comes only from an `EntityDescriptor` is created **without a Location**:
  - DG006 (`NamingConventionRule`).
  - The entity branch of DG004.
- The SARIF writer then emits `locations: []`.
- The VS publisher drops any result with an empty `locations` array. The user sees the misleading line "N SARIF results were skipped (malformed or outside the solution directory)".
- DG006 is also `Info`, so it reaches SARIF only as `note` and never moves ErrorCount or WarningCount.
- The entity branch of DG004 re-reports the same query that the RawSql branch already reports with a location. With the planned fixture ("repository selects a column set that differs from the entity"):
  - ErrorCount doubles.
  - The status bar says "2 errors" while the Error List shows 1.
- Net effect in VS after Phase 3: "Warning: no ground truth" disappears, so Phase 2 stops saying "syntactic only". The user believes the contract was compared, but nothing new is visible. RC2 comes back through RC3.

**Evidence:**
- `src/DataGuard.Core/Rules/NamingConventionRule.cs:18` `Severity => DiagnosticSeverity.Info`; `:52-55` `CreateViolation(RuleId, $"Property …", Severity)` (no location).
- `src/DataGuard.Core/Rules/ColumnShapeMatchRule.cs` entity branch (`if (contract is EntityDescriptor entityDesc)` … `CreateViolation(RuleId, $"Result set is missing required columns…", Severity, properties: …)`) has no location. The RawSql branch at `:110-115` passes `rawSql.Location`.
- `src/DataGuard.Core/Reporting/DiagnosticEmitter.cs` `Locations = v.Location != null ? … : new List<SarifLocation>()`.
- `src/DataGuard.VisualStudio/SarifErrorListPublisher.cs:131-136` `|| locations.GetArrayLength() == 0) { return null; }`.
- `DataGuardPackage.Publishing.cs:97-99` "SARIF results were skipped (malformed or outside the solution directory)".

**Suggested fix:**
- Add to Phase 3 Req: "entity-derived violations carry a location".
  - Either record the `b.Entity<T>` invocation line from `ModelSnapshotCSharpParser` on `EntityDescriptor`, and set it in DG006 and the DG004 entity branch.
  - Or Phase 3 must not claim Error-List value: change the Success Criterion to "SARIF contains DG006 (note)".
- Suppress the DG004 entity-branch result when a RawSql DG004 already exists for the same SQL text. They share `ShapeProperties(... sqlDesc.SqlText)`.
- Add a 3.V assertion that every SARIF result produced from the EF fixture has a non-empty `locations` array.
- Phase 2: keep a "ground truth: entities only (no schema)" line when entities exist but no `DatabaseSchemaDescriptor` does, so the honesty signal does not vanish.

### 2. Phase 4 reuses the IDE-safe consent and banner for commands that are not IDE-safe and honour repo write paths
**Severity:** High
**Location:** phase-04 Req 2 ("consent gate (same key as validate)"), Architecture ("reuse"), Req 4 (snapshot path "from stdout `Snapshot written …`").

**Flaw and failure scenario:**
- The consent the user granted literally promises IDE-safe mode, no database connections, and that "write paths from the config … are ignored". Phase 4 reuses that same key for `snapshot refresh` and `verify-shape`, which honour the repository's `.dataguard.yml`.
- `snapshot refresh` writes to the repo-chosen `SnapshotFilePath` (for example `..\..\..\Users\me\…`) through `File.Move(…, overwrite: true)`, with no `SafeWritablePath` check.
- `ExecuteRunAsync` prints the banner "Mode: ide-safe (no assembly loading, no database or network access)" for any command.
- The success path is also wrong:
  - There is no `Snapshot written` line.
  - Success text goes to stdout, which VS drains and discards.

**Evidence:**
- `src/DataGuard.VisualStudio/SolutionTrustGate.cs:85-86` "the CLI runs in IDE-safe mode (--ide-safe): … no database … connections are opened; • connection strings and write paths from the config or environment are ignored".
- `src/DataGuard.Cli/Commands/SnapshotCommands.cs` `var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;` then `Console.WriteLine($"Snapshot refreshed with …")` (stdout).
- `src/DataGuard.Core/Baseline/BaselineManager.cs:254` `File.Move(tempPath, _baselineFilePath, overwrite: true);`.
- `OutputPaneWriter.cs` `"Mode:   ide-safe (no assembly loading, no database or network access)\r\n"`, called from `DataGuardPackage.Commands.cs:120`.
- `CliRunSession.cs:68` `ProcessTerminator.DrainAsync(process.StandardOutput)`.

**Suggested fix:**
- Phase 4 Req 2: a separate consent key and prompt for live commands, e.g. `ComputeConsentKey(...) + ":live"`. The prompt text must state that the command is not IDE-safe, reads `.dataguard.yml`, and writes the snapshot file.
- Add a CLI step: `snapshot refresh` refuses a `SnapshotFilePath` that fails `SafeWritablePath.IsSafe(path, cwd)` or resolves outside the config directory, with exit 4. Add a test for it.
- Make `WriteCommandBannerAsync` take a per-command mode line.
- Req 4: the snapshot path is the default name, or a new stderr line `snapshot: written <relative path>`. Drop the stdout parsing.

### 3. The Roslyn pin is not actually enforced: CI SDK floats, Dependabot allows minor bumps, and the manifest still claims VS 17.0
**Severity:** High
**Location:** phase-01 Req 2 and Req 4, Key Insights, Architecture ("builds with the repo SDK").

**Flaw and failure scenario:**
- **CI does not use the pinned SDK.** The plan says the gate compiles with "the repo's pinned SDK", but:
  - CI installs `9.0.x` (the newest feature band).
  - `global.json` rolls forward with `latestFeature`.
  - So the gate runs on whatever Roslyn the newest 9.0 band ships, not 4.12.
- **Dependabot only blocks majors.** `version-update:semver-major` does nothing against a 4.12→4.13/4.14 bump. Such a bump:
  - passes CI on a newer-band SDK;
  - still raises CS9057 for VS 17.12 and SDK 9.0.1xx users.
- **The wildcard is too broad.** `Microsoft.CodeAnalysis.*` also freezes the majors of `Microsoft.CodeAnalysis.NetAnalyzers` and of Core's `Microsoft.CodeAnalysis.CSharp 5.9.0`.
- **The manifest overclaims.** It still advertises VS `[17.0,19.0)`, where a 4.12 analyzer is refused (17.0–17.11 ship older Roslyn).

**Evidence:**
- `.github/workflows/ci.yml:16` `DOTNET_VERSION: "9.0.x"`; `global.json` `"rollForward": "latestFeature"`.
- `.github/dependabot.yml:25-26` (`semver-major` only).
- `Directory.Build.props:43` `Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.401"`.
- `src/DataGuard.Core/DataGuard.Core.csproj:23-24` (5.9.0).
- `source.extension.vsixmanifest:12,15,18,26` `Version="[17.0,19.0)"` / `[17.0,)`.
- `research/analyzer-roslyn-version.md:301` ("VS 2022 17.12 ships the same line").

**Suggested fix:**
- Make the gate deterministic: the script asserts that `src/DataGuard.Analyzers/packages.lock.json` and `src/DataGuard.CodeFixes/packages.lock.json` resolve `Microsoft.CodeAnalysis.CSharp`/`.Workspaces` to exactly `4.12.0`. Keep the consumer build as a smoke test.
- Dependabot: an explicit ignore of `versions: [">= 4.13.0"]` on the three exact names. Document that this also freezes Core's copy, or move Core to a different policy.
- Bump the manifest `InstallationTarget` and `Prerequisite` to `[17.12,19.0)`, and state the floor in `docs/USAGE.md`.

### 4. Phase 2 tells users with a valid snapshot that they have none
**Severity:** Medium
**Location:** phase-02 Req 3 and 3.V ("`UnevaluatedContractCount 2` → same").

**Flaw and failure scenario:** `UnevaluatedContractCount` counts contracts that no rule could evaluate *after* ground truth loaded (for example a procedure missing from a committed snapshot). Mapping it to "no snapshot, EF model or database connection was available … Commit .dataguard-snapshot.json" is false for a user who already committed one. That is the mirror image of RC2.

**Evidence:**
- `ValidateCommand.cs:337` (the ground-truth gate is separate).
- `ValidateCommand.cs:443-447` `var unevaluated = validation.Unevaluated; … WriteUnevaluated(unevaluated, …)`.
- `:497` `["UnevaluatedContractCount"] = unevaluated.Count`.

**Suggested fix:**
- Show the syntactic-only text only when `GroundTruthMissing`.
- When `UnevaluatedContractCount > 0`, show `[WARN] N contract(s) could not be evaluated against the loaded ground truth (see UNEVALUATED lines).`
- Update 3.V to match.

### 5. Phase 3's new walker duplicates an existing public walker, and the escape helper is unnecessary
**Severity:** Medium
**Location:** phase-03 Key Insights ("internal … copy the pattern"), Req 1–2, Step B (`EfModelSnapshotLocator` walker, `PathText.EscapeControlCharacters`), Related Code Files (csproj `None` items).

**Flaw and failure scenario:**
- **A walker already exists.** `ProjectCSharpSqlSource.DiscoverSourceFiles` is **public**, is already called from the CLI, and already:
  - skips reparse-point directories;
  - prunes `bin`, `obj`, `.git` and `.vs`.
- **The caps protect nothing.** The same `validate --project` run already walks and reads every `.cs` file under that root with no depth or entry cap. A second walker capped at depth 8 and 5,000 entries cannot stop a hostile tree.
- **Control-character escaping is not needed for VS.** VS is Windows-only, NTFS forbids control characters in file names, and the VS reader splits only on `\n`.
- **The csproj edit is based on a false premise.** The existing fixtures have no `None` items. Tests find them by walking up to the source tree.

**Evidence:**
- `src/DataGuard.Core/Sources/CSharp/ProjectCSharpSqlSource.Discovery.cs:16` `public static List<string> DiscoverSourceFiles(string path)`.
- `:283-287` `if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;`.
- `src/DataGuard.Cli/Services/ContractAcquisition.cs:76-79` (same root scanned in the same run).
- `VerifyShapeCommand.cs` `ProjectCSharpSqlSource.DiscoverSourceFiles(project)` (CLI already calls it).
- `ProgressLineParser.cs:135` (split on `'\n'` only).
- `tests/DataGuard.Core.Tests/ProjectCSharpSqlSourceExtractorTests.cs:366` (walk-up fixture lookup).
- `DataGuard.Core.Tests.csproj` has no Fixtures items.

**Suggested fix:**
- `EfModelSnapshotLocator` becomes a filter over `DiscoverSourceFiles(projectPath)`: name ends with `ModelSnapshot.cs` and the path has no `node_modules` segment.
- Keep the per-file checks: `File.ResolveLinkTarget is null`, containment, `FileInfo.Length <= 1 MiB`.
- Drop `MaxRecursionDepth`, the 5,000-entry cap and `PathText`. If kept for VS Code later, place it next to `RelativizeToWorkspace` in `Services/OutputSinks.cs`.
- Remove the csproj fixture edit.

### 6. Phase 4 reads the wrong verify-shape fields, cites a count that has no source, and has a wrong test vector
**Severity:** Medium
**Location:** phase-04 Req 2 (modal "N from the last scan"), Req 4 (detail line `<file>:<line>`), 3.V mask vectors.

**Flaw and failure scenario:**
- **No file or line in `results[]`.** `results[]` has no file or line; the location is in `queries[i].location`, in the same order.
- **`untyped` is not handled.** It is a fourth status, and the planned counts ignore it.
- **Exit 4 is not mapped.** `RefuseUnsafeOutput` returns exit 4, which the outcome mapping does not cover.
- **VS has no scan count.** VS never runs `scan`, and the run temp directory (which holds `summary.json`) is deleted after every run, so "N from the last scan" has no source.
- **One mask test vector is wrong.** The test expects `pg***.local`; the reference implementation yields `p***.local`, because labels of 3 characters or fewer keep only one character.

**Evidence:**
- `src/DataGuard.Cli/Commands/VerifyShapeCommand.cs:172-181` (`results.Add(new { sql, targetType, status, … })`, no location).
- The `queries = results.Zip(readQueries, …) location = … new { file = …, line = … }` block.
- `:164-166` `"untyped"`.
- `Services/OutputSinks.cs:20` `Environment.ExitCode = 4;`.
- `DataGuardPackage.Commands.cs:138` `TempDirectoryCleaner.SafeDeleteDirectory(context.TemporaryDirectory)`.
- `src/DataGuard.VSCode/src/live-database-confirmation.ts:63-64` `first.length > 3 ? …slice(0,3)+"***" : …slice(0,1)+"***"`.
- `live-database-confirmation.test.ts:34` `"p***.org"`.

**Suggested fix:**
- Req 4: zip `results[i]` with `queries[i].location`; count `untyped` separately; map exit 4 to `[ERROR] refused output path`.
- Req 2: the modal says "describe every read SQL query in <solutionDir>" with no N.
- 3.V: copy the seven vectors verbatim from `live-database-confirmation.test.ts:31-47`.

## Assumption table

| # | Assumption | Evidence | Verdict |
|---|---|---|---|
| **P1-1** | 4.12 is the right floor for "VS 2022 17.x" | `vsixmanifest:12` `[17.0,19.0)`; research:301 | **BROKEN**: loads only on 17.12+. `Microsoft.VisualStudio.SDK 17.14.40265` (`csproj:21`) does not enforce a runtime floor. Bump the manifest. |
| P1-2 | Roslyn 5.9 is referenced only in the 4 analyzer-side projects | `Core.csproj:23-24`, `BinaryCompatibilityFixture.csproj:13` | **HOLDS** for the analyzer graph (test projects do not reference Core), but the Dependabot wildcard also hits Core. |
| P1-3 | `MS.CA.Analyzers 3.11.0` is OK with `EnforceExtendedAnalyzerRules`/`IsRoslynComponent` | `Analyzers.csproj:8-9,25`; research:297 | **HOLDS** (built empirically). |
| P1-4 | Dependabot `Microsoft.CodeAnalysis.*` matches like the `Microsoft.Build.*` entry | `dependabot.yml:25-26` | **HOLDS** as syntax; **BROKEN** in effect: also matches NetAnalyzers (`Directory.Build.props:43`) and does not block minor bumps. |
| P1-5 | The gate runs with "the repo's pinned SDK" | `ci.yml:16`; `global.json` latestFeature | **BROKEN**: it floats (Finding 3). |
| P1-6 | `AnalyzerReleases.Unshipped.md` needs an entry for a message change | `Unshipped.md:5-7` (ID/Category/Severity/Notes only) | **BROKEN**: no entry needed; a non-`;` free-text line risks an RS2007-family error under `TreatWarningsAsErrors` (`Directory.Build.props:6`). Drop it. |
| P1-7 | A new script fits the conventions | `verify-analyzer-packaging.sh:22,47-48` (`set -euo pipefail`, mktemp, trap) | **HOLDS** on style; DRY: that script already builds a consumer and greps `/analyzer:`, but no workflow calls it. |
| P1-8 | The script needs a CI or runbook reference | `rules/universal_ai_constitution.md:12` | **HOLDS** (the plan wires it into CI). |
| P1-9 | shellcheck covers the new script | `ci.yml` has no shellcheck | **N/A**: no lint gate exists. |
| P1-10 | `System.Composition.Hosting` may be needed at analyzer load (open Q2) | No `System.Composition` use in `src/DataGuard.Analyzers/*.cs`; only `CodeFixProviders.cs:9` | **BROKEN**: the Analyzers reference is dead. Q2 only concerns CodeFixes' MEF attributes, which VS provides. |
| P1-11 | Contracts and SqlClassification sit next to the analyzer | `bin/Release/netstandard2.0/` listing | **HOLDS**. |
| P1-12 | The DG001 text is pinned only by one test | `DiagnosticDescriptors.cs:89`; test `:41` | **HOLDS**; also update the doc comment at `UnvalidatedSqlCallGenerator.cs:16`. |
| P1-13 | The packaging test silently passes without a VSIX | `VsixAnalyzerPackagingTests.cs:47-51` | **HOLDS**. |
| P2-1 | `ParsedProgress` is a readonly struct with a fixed set of construction sites | `ProgressReadModels.cs:8-22`; `ProgressLineParser.cs:48,54,62,65`; no test constructs it | **HOLDS**. |
| P2-2 | Count renderers are only `Publishing.cs:121-123` and `OutputPaneWriter.cs:96-98` | plus `ProgressLineParser.cs:167` (Summary line); `Publishing.cs:28,43,53` and `Lifetime.cs:63` show no counts | **HOLDS**. |
| P2-3 | `Redact` is applied on the echo path | `ProgressLineParser.cs:59-62` | **HOLDS**; the new `[WARN]` branch must sit after `Redact`. |
| P2-4 | `Redact` masks `User Id` | `DataGuardLogger.cs:28` regex has no `user id` | **BROKEN** (as the plan suspected); `User Id` is not a secret, so test only `Password=`. |
| P2-5 | Explainer tests live in `CliArgumentBuilderTests` | `CliArgumentBuilderTests.cs:74-102` via `CliRunOutcomes.Create` (`CliRunOutcomes.cs:8-15`) | **HOLDS**; `CliRunOutcomes.Create` needs new parameters but is not listed in Related Files. |
| P2-6 | `Explain` has a single production caller | `Publishing.cs:117` | **HOLDS**. |
| P2-7 | `UNEVALUATED: no ground truth` can occur under ide-safe | `ValidateCommand.cs:129,339-346` (`allowSyntacticOnly = … \|\| ideSafe`) | **BROKEN**: dead branch, harmless. |
| P2-8 | Unevaluated > 0 means no ground truth | `ValidateCommand.cs:443-447` | **BROKEN** (Finding 4). |
| P2-9 | The Summary carries the three new counts | `ValidateCommand.cs:494-499` | **HOLDS**. |
| P2-10 | Only stderr carries signals | `CliRunSession.cs:68` | **HOLDS**. |
| P2-11 | `TryFormatProgress` can gain out parameters | 32 references in `DataGuardPackageTests`/`NavigationTests`/`RuleInventoryTests` | **RISK**: add an overload; do not change the existing ones. |
| P2-12 | Validate exit 4 means the output path was refused | `OutputSinks.cs:12-21` | **HOLDS**. |
| P3-1 | `AutoDetectEFContext` survives `IdeSafePolicy.Apply` | `IdeSafePolicy.cs:73-131` never touches it | **HOLDS**. |
| P3-2 | The yml opt-out binds | `ConfigLoader.cs:257`; `ConfigDocument.cs:63,135` | **HOLDS**. |
| P3-3 | `projectPath` is non-null for VS | `CliArgumentBuilder.BuildValidateArguments` always passes `--project` | **HOLDS**. |
| P3-4 | `EnumerationOptions.MaxRecursionDepth`/`AttributesToSkip` are available | `DataGuard.Cli.csproj:3` net9.0 | **HOLDS** (but YAGNI, Finding 5). |
| P3-5 | `File.ResolveLinkTarget` is available | net9.0 | **HOLDS**. |
| P3-6 | A `.cs.txt` fixture copied as `.cs` into a directory with no csproj is scanned | `Discovery.cs:41-44` | **HOLDS**. |
| P3-7 | The csproj needs `None` items like CSharpSql | `Core.Tests.csproj` has none; `ProjectCSharpSqlSourceExtractorTests.cs:366` | **BROKEN**: remove the edit. |
| P3-8 | DG006 fires from EF-only input | `NamingConventionRule.cs:34-56`; parser `:165` `HasColumnName` | **HOLDS** in the CLI; it is Info with no location, so it is invisible in VS (Finding 1). |
| P3-9 | A bad snapshot throws a catchable exception | `EfModelSource.cs:270-273`; DG1303/DG1306 in the parser | **HOLDS**: even valid C# with no entities throws (DG1306). |
| P3-10 | The size check must happen before the read | `EfModelSource.cs:252` | **HOLDS**. |
| P3-11 | The walker must be copied because it is internal | `Discovery.cs:16` public; used by the CLI | **BROKEN**: reuse it (Finding 5). |
| P3-12 | An unconditional merge is harmless | `ValidateCommand.cs:230-235`; `ColumnShapeMatchRule` entity branch vs `:110-115` | **BROKEN**: duplicate DG004 (Finding 1). |
| P3-13 | Printed paths are relative | partial-parse diagnostics use the full `snapshotFilePath`, printed at `ValidateCommand.cs:469` | **BROKEN** (minor): relativize. |
| P4-1 | `DialogPage` string properties persist per user | `DataGuardOptionsPage.cs:45` `CustomCliPath` | **HOLDS**. |
| P4-2 | Default No is available | `DataGuardPackage.Consent.cs` `OLEMSGDEFBUTTON_SECOND` | **HOLDS**. |
| P4-3 | `CliRunSession.RunAsync` works without SARIF | `CliRunSession.cs:57,76` (`sarifPath` used only for the timeout publish) | **HOLDS**; the status line is `:65`, not `:67`. |
| P4-4 | `ExecuteRunAsync` and its banner are reusable | `Commands.cs:120`; `OutputPaneWriter` "Mode: ide-safe…" | **BROKEN** (Finding 2). |
| P4-5 | The same consent key is acceptable | `SolutionTrustGate.cs:85-86` | **BROKEN** (Finding 2). |
| P4-6 | verify-shape scans `--project` in directory mode | `VerifyShapeCommand.cs:94` → `ContractAcquisition.cs:76-79` → `Discovery.cs:41-44` | **HOLDS**. |
| P4-7 | The temp `summary.json` passes the safe-output check | `VerifyShapeCommand.cs:71`, same policy as validate SARIF (`Commands.cs:91`) | **HOLDS**. |
| P4-8 | The snapshot lands in the solution directory | cwd = solution directory (`CreateProcess`); `SnapshotFilePath` comes from the repo | **HOLDS** only without a repo `SnapshotFilePath` (Finding 2). |
| P4-9 | A `Snapshot written` stdout line exists | `SnapshotCommands.cs` prints "Snapshot refreshed…" on stdout, which is discarded | **BROKEN**. |
| P4-10 | `results[]` has `file:line` | `VerifyShapeCommand.cs:172-181` | **BROKEN**: use `queries[i].location`. |
| P4-11 | An unset variable exits 2 before any secret-store lookup | `CliConfigurationResolver.cs:113-124` | **HOLDS**. |
| P4-12 | Deleting InfoBarManager breaks nothing | references only in `InfoBarManagerTests.cs:68,90` | **HOLDS**; also update `docs/adr/ADR-0001-visual-studio-extension.md:37`. |
| P4-13 | "N from the last scan" is available | no scan in VS; temp directory deleted at `Commands.cs:138` | **BROKEN**: drop it. |
| P4-14 | The mask vector `pg***.local` is correct | `live-database-confirmation.ts:63-64`; test `:34` | **BROKEN**: it should be `p***.local`. |

## Scope audit

**YAGNI (cut):**
- **Phase 3, walker and caps:** `MaxRecursionDepth`, the 5,000-entry cap and a separate walker (reuse `DiscoverSourceFiles`).
- **Phase 3, `PathText.EscapeControlCharacters`:** NTFS forbids control characters in names and VS is Windows-only. Defer it with the VS Code items.
- **Phase 3, fixture edit:** the csproj fixture edit.
- **Phase 1, release note:** the `AnalyzerReleases.Unshipped.md` note.
- **Phase 4, modal count:** the modal's "N queries".

**DRY:**
- **Phase 1:** extend `scripts/verify-analyzer-packaging.sh` instead of adding `check-analyzer-loads.sh`.
  - It already generates a consumer from packed nupkgs, uses mktemp and a trap, and greps the build log.
  - Add a `SELECT *` source plus "DG017 present / CS9057 absent" checks.
  - Wire it into `build-and-test` after a `dotnet pack`. That also gives the currently orphaned script its required CI reference and tests the NuGet path consumers actually use.
- **Phase 4:** reuse the VS Code test vectors verbatim.

**KISS (masking):**
- The VS Code port is about 40 lines, with URI, TNS and `//host` forms. Porting it is acceptable because it is pure and tested.
- A cheaper equivalent: `DbConnectionStringBuilder` (net472) with the keys `Server`/`Data Source`/`Host` plus `maskHost`, falling back to "host not recognised (variable NAME)".
- Showing only the variable name loses the prod-vs-dev check that the modal exists for. Keep the masking, but trim the forms it handles.

**Missing for RC1–RC4:**
- **RC1:** a manifest floor of 17.12, a deterministic lock-file assertion, and a narrower Dependabot rule (Finding 3).
- **RC2:** keep a signal when only entities load (Finding 1).
- **RC3:** a location for entity-derived violations (Finding 1).
- **RC4:**
  - a separate live-command consent;
  - a per-command banner;
  - CLI write-path containment for `SnapshotFilePath`;
  - the exit-4 and `untyped` outcomes (Findings 2 and 6).
