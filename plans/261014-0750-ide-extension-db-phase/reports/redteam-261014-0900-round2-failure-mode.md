# Red-team report (round 2, Visual Studio-focused plan, 4 phases)

# Round 2: Failure Mode Analyst + Flow Tracer findings (plan `261014-0750-ide-extension-db-phase`)

I found 6 problems in the current plan. The two most serious: a Phase 3 success criterion cannot be met, and Phase 4's Refresh Snapshot writes wherever the repo's yml points. The round-1 mitigations I traced still hold, except where a finding below says otherwise.

## Findings (most severe first)

### F1. DG006 from an EF ModelSnapshot never shows in the VS Error List, so the Phase 3 success criterion cannot be met
- **Severity:** High
- **Location:** phase-03, Success Criteria ("Error List có DG006 thật") and the 3.V oracle. Phase 2 also says "Error List giữ nguyên".
- **Flaw & failure scenario:**
  - `NamingConventionRule` only needs an `EntityDescriptor` whose property `ColumnName` is neither the snake_case nor the PascalCase form of the property name. No schema is needed.
  - But the violation is created without a location, and the snapshot parser builds entities without one either. So the SARIF result has `locations: []`.
  - The VS SARIF loader rejects results with no location and counts them as skipped. The Output pane then says "N SARIF results were skipped (malformed or outside the solution directory)".
  - Net effect: the CLI e2e test passes, but the VS developer sees no DG006 and a misleading "malformed" message. Any other entity-only finding without a location behaves the same way.
- **Evidence:**
  - `NamingConventionRule.cs:34-57`: `violations.Add(CreateViolation(RuleId, $"Property '{prop.Name}' … doesn't match database column '{columnName}'", Severity));` (no location argument)
  - `ContractRuleBase.cs:43`: `Location? location = null`
  - `ModelSnapshotCSharpParser.cs:129`: `return new EntityDescriptor($"snapshot:{entityName}", entityName, entityName, table, …)` (no location)
  - `DiagnosticEmitter.cs:101-123`: `Locations = v.Location != null ? … : new List<SarifLocation>()`
  - `SarifErrorListPublisher.cs:130-137`: `|| locations.GetArrayLength() == 0) { return null; }`, and `:106-109` then does `result.SkippedCount++`
- **Suggested fix:** Replace the Phase 3 success criterion with: "Output pane shows `ide-safe: ef model …`, no `Warning: no ground truth`, and the CLI SARIF contains DG006". Then pick one of these and say so:
  - (a) Add `Location.Create(snapshotPath, span, lineSpan)` to the entities `ModelSnapshotCSharpParser` produces, and add a 3.V test that DG006 has `locations[0].physicalLocation.artifactLocation.uri` set.
  - (b) Record "VS shows location-less results" as Deferred.
  
  Add a Phase 2 test that pins the skip of location-less results, so the behaviour is explicit.

### F2. Refresh Snapshot writes to a path chosen by the repo's yml, and VS cannot learn that path
- **Severity:** High
- **Location:** phase-04, Req 3, Req 4 ("path from stdout `Snapshot written …`") and the Risk section.
- **Flaw & failure scenario:**
  - `snapshot refresh` writes to `config.SnapshotFilePath` from the repo-controlled `.dataguard.yml`, relative to the working directory (the solution directory).
  - There is no containment and no safe-path check. The write does `Directory.CreateDirectory` and then `File.Move(..., overwrite: true)`.
  - A cloned repo with `SnapshotFilePath: ..\..\..\Users\dev\.gitconfig` (or any path) gets that file overwritten with snapshot JSON as soon as the developer clicks the new menu item.
  - This is the "repo-chosen write path" class that `--ide-safe` strips (`AuditLogPath`). The existing consent gate (keyed on the yml hash) was given for ide-safe runs only.
  - The plan's success text also cannot work:
    - VS throws stdout away.
    - The CLI never prints `Snapshot written`; it prints `Snapshot refreshed with N violations` on stdout.
    - So `[OK] Snapshot refreshed: <default name>` is wrong whenever the yml sets a path.
  - A kill at the timeout during the save leaves a `.<name>.<guid>.tmp` file in the solution directory (the `finally` block does not run), which can end up committed.
- **Evidence:**
  - `SnapshotCommands.cs:75`: `var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;`
  - `ConfigLoader.cs:239`: `"SnapshotFilePath" => config with { SnapshotFilePath = value }`
  - `BaselineManager.cs:238-255`: `Directory.CreateDirectory(directory)` … `File.Move(tempPath, _baselineFilePath, overwrite: true)`
  - `IdeSafePolicy.cs` (Apply): `suppressed.Add("AuditLogPath (repo-chosen write path)")`
  - `ProcessTerminator.cs:137-151` (the plan cites `:110-124`, which is drift): `// Drain without retaining potentially sensitive CLI output.`
  - `SnapshotCommands.cs:97`: `Console.WriteLine($"Snapshot refreshed with {baseline.Violations.Count} violations");`
- **Suggested fix:** Add a CLI requirement to Phase 4:
  - `snapshot refresh` refuses a `SnapshotFilePath` whose full path is outside the `--config` directory, or that fails `IsSafeWritablePath`. The refusal is exit 4 with `Refusing to write snapshot …`, matching `OutputSinks.cs:12-22`.
  - On success it prints `snapshot: wrote <path relative to config dir>` on **stderr**.
  
  VS takes the path only from that exact stderr prefix. The modal names the target file. Add CLI e2e tests: `SnapshotFilePath: ../outside.json` gives exit 4 and no file is written; the default path gives the stderr line. Add a Risk entry: "a timeout kill can leave a `.dataguard-snapshot.json.*.tmp` file; the docs tell users to .gitignore it."

### F3. The Phase 1 gate and Dependabot rule do not actually hold the "≤ Roslyn 4.12" contract
- **Severity:** High
- **Location:** phase-01, Req 2 and Req 4, the Architecture section, and Success Criteria row 4.
- **Flaw & failure scenario:**
  1. **The CI gate does not run on Roslyn 4.12.** CI installs `dotnet-version: 9.0.x` (the newest 9.0 SDK), and `global.json` sets `rollForward: latestFeature`. So the CI build uses the newest 9.0.x feature band, not 9.0.100. The script's consumer is generated in `mktemp`/`$RUNNER_TEMP`, outside the repo, so it does not even see `global.json` and uses the newest SDK installed on the runner. Which SDKs the runner has installed is unverified here (I had no CI logs).
  2. **Dependabot still allows minor bumps.** The plan only ignores `semver-major`, and the NuGet group is `patterns: ["*"]`. A 4.12 → 4.13/4.14 bump is a minor update, so Dependabot will propose it. CI's newer compiler accepts it, `check-analyzer-loads.sh` stays green, and VS 17.12 / SDK 9.0.1xx users lose all squiggles again (CS9057).
  3. **The ignore rule has side effects.** `Microsoft.CodeAnalysis.*` also matches `DataGuard.Core` (CSharp/Workspaces 5.9) and `Microsoft.CodeAnalysis.NetAnalyzers` (`Directory.Build.props:43`), so it freezes their majors too.
  4. **Older VS 2022 versions still fail.** The manifest installs on 17.0–17.11, which ship Roslyn 4.0–4.11. A 4.12-built analyzer still triggers CS9057 there.
- **Evidence:**
  - `ci.yml:17`: `DOTNET_VERSION: "9.0.x"`
  - `ci.yml:44-47`: `uses: actions/setup-dotnet… dotnet-version: ${{ env.DOTNET_VERSION }}`
  - `global.json`: `"version": "9.0.100", "rollForward": "latestFeature"`
  - `dependabot.yml:20-22`: `groups: nuget: patterns: ["*"]`
  - `DataGuard.Core.csproj:23-24`: `Microsoft.CodeAnalysis.CSharp" Version="5.9.0"`
  - `source.extension.vsixmanifest:12`: `Version="[17.0,19.0)"`
  - `scripts/verify-analyzer-packaging.sh:93` already builds a consumer, but only greps `/analyzer:`, so it passes even when CS9057 fires.
- **Suggested fix:**
  - Replace Req 2's "consumer gate" as the primary guard with a deterministic test in `DataGuard.Analyzers.Tests` (and `CodeFixes.Tests`): `typeof(<AnyAnalyzer>).Assembly.GetReferencedAssemblies()` has `Microsoft.CodeAnalysis` with `Version <= 4.12.0.0`. It does not depend on which SDK the runner has.
  - Keep the script as a secondary check, but have it copy `global.json` into the temp consumer with `"rollForward": "disable"` and print `dotnet --version`.
  - Change Req 4 to a Dependabot ignore of `versions: [">= 4.13"]` for `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Microsoft.CodeAnalysis.Analyzers`, with a comment noting that Core shares the id, or move the analyzer pins to their own Dependabot `directory:` entry.
  - Either raise the manifest `InstallationTarget` minimum to `17.12` or state in USAGE/overview that squiggles need VS ≥ 17.12.
  - Extend `verify-analyzer-packaging.sh` to fail on `CS9057` instead of keeping two consumer generators.

### F4. A locator exception (or its entry cap) turns every IDE run into "failed before summary", or picks an EF model at random
- **Severity:** Medium-High
- **Location:** phase-03, Architecture ("NEW … after :207") and Req 1–3.
- **Flaw & failure scenario:**
  - **The new code runs outside the try block.** The injection point after `:207` is outside the `try` that starts at `:222`. The plan adds a try/catch only around extraction (`:230`).
  - **Any locator exception aborts the run:**
    - `FileInfo.Length` or `File.ResolveLinkTarget` on a file deleted between enumeration and check. For example, `dotnet ef migrations add` rewrites the snapshot while Run-on-Build fires validate.
    - `DirectoryNotFoundException`, `PathTooLongException`, or an IOException on a network share.
  - That exception reaches System.CommandLine's default handler: exit 1, no SARIF, no `Summary`. VS prints "[ERROR] The CLI failed before producing a validation summary" on every build until the race clears. This is exactly the outcome the Risk section claims to prevent.
  - **The 5,000-entry cap has no defined verdict.** If the cap stops the walk after finding one candidate, that candidate is used silently even if a second `*ModelSnapshot.cs` exists past the cap. Enumeration order differs between NTFS (sorted) and ext4 (hash order), so the Linux test and the Windows product can disagree.
- **Evidence:**
  - `ValidateCommand.cs:222-225`: `try { ct.ThrowIfCancellationRequested(); var acquisition = await AcquireContractsAsync(…)`
  - `ValidateCommand.cs:507-516`: `catch (Exception ex) { Console.Error.WriteLine($"Validation failed: …"); Environment.ExitCode = 1; }`
  - `Program.cs:46`: `await parseResult.InvokeAsync(new InvocationConfiguration(), …)` (default exception handler)
  - `ValidateCommand.cs:589` shows the existing EF scan catching only `IOException or UnauthorizedAccessException`.
- **Suggested fix:**
  - Req 1: `EfModelSnapshotLocator.TryLocate` never throws. It catches `IOException`, `UnauthorizedAccessException`, `SecurityException`, `ArgumentException` and `NotSupportedException`, and turns each into `ACQUISITION: ef model auto-detect skipped: <escaped message>`. Alternatively, move the call to just after `:224`, inside the try.
  - Req 2: if the cap is hit, the result is "not chosen" plus `ACQUISITION: ef model auto-detect stopped after 5000 entries; commit .dataguard-snapshot.json`. Never return a partial single match.
  - 3.V: add a test that deletes the file between enumeration and size check through an injected `Func<string, FileInfo>` seam, expecting `ACQUISITION` and exit by violations. Add a cap test with 1 candidate before and 1 after the cap, expecting "not chosen".

### F5. Phase 2's "syntactic only" branch hides a CLI crash that happened after the Warning line
- **Severity:** Medium
- **Location:** phase-02, Req 3 ("before `[OK]`, when exit is 0 or 1 and (GroundTruthMissing or …)") and Req 4.
- **Flaw & failure scenario:**
  - The Warning line (`:346`) is printed before the rule run and the SARIF write.
  - If anything after it throws (a rule, `FileSarifSink`, the plugin manager), the CLI exits 1 with no `Summary` and no SARIF.
  - With the plan's condition (exit 0 or 1, no `HasSummary` check), Explain returns "[WARN] Ran syntactic rules only … See Error List for syntactic findings". It should return "[ERROR] The CLI failed before producing a validation summary". The Error List still shows the previous run.
  - The same happens for exit 1 with a Summary: "Validation found errors" gets replaced.
  - Ordering itself is safe: the Warning and the Summary both go to the same `Console.Error` writer, so the Warning always arrives before the Summary.
- **Evidence:**
  - `ValidateCommand.cs:97`: `new ProgressEmitter(Console.Error, …)`
  - `ValidateCommand.cs:346`: `Console.Error.WriteLine("Warning: no ground truth …")`, before `:408` `ValidateContractsDetailedAsync` and `:416-420` SARIF
  - `ExitCodeExplainer.cs:55-58`: `return hasSummary ? "[WARN] Validation found errors…" : "[ERROR] The CLI failed before producing a validation summary…"`
  - `Publishing.cs:115-117`: Explain runs whenever `exit != 0`.
- **Suggested fix:** Req 3 becomes: "only when `outcome.Progress.HasSummary && sarifExists`, evaluated after the `:27` check. For exit 0, replace `[OK]` and the warnings text. For exit 1, **append** ` Ground truth was not loaded (syntactic only).` to 'Validation found errors'." Apply the same `HasSummary` condition to the status bar text in Req 4. Add a 3.V case: `GroundTruthMissing=true, HasSummary=false, exit 1` gives the "failed before … summary" text.

### F6. Phase 4 exit-code mapping and summary parsing do not match the CLI
- **Severity:** Medium
- **Location:** phase-04, Key Insights and Req 4.
- **Flaw & failure scenario:**
  - **`snapshot refresh`:**
    - The plan's exit-3 text ("variable unset or empty, or the server is unreachable") never applies under VS. An unset or empty `--connection-env` variable gives exit 2.
    - An unreachable server gives acquisition status ≠ Complete, then `InvalidOperationException`, then exit 1.
    - Exit 3 needs an empty connection string, which `--connection-env` makes impossible.
  - **`verify-shape`:**
    - Exit 4 (refused output path) and exit 2 (unsupported provider) are not listed.
    - Exit 1 means two things: mismatches (summary written) or an exception (no summary).
    - `results[]` carries no file or line. The location is only in `queries[i].location` (paired by index with `results[]`), and it can be `null`. So the planned `<status> <file>:<line>` detail line cannot be built from `results[]`.
  - **Temp path:** the VS temp directory is `%TEMP%\DataGuard\<guid>\`, which passes the same safe-path check `validate` already uses for `validation.sarif` in that directory, so the summary path is fine.
- **Evidence:**
  - `ConfigLoader.cs:137-141`: `Console.Error.WriteLine(connection.Error); Environment.ExitCode = 2;`
  - `CliConfigurationResolver.cs:122`: `--connection-env {variableName}: the environment variable is not set or empty.`
  - `SnapshotCommands.cs:67-69`: `throw new InvalidOperationException($"Contract acquisition {acquisition.Status}…")`, then `:107-115`: `ExitCode = 1`
  - `VerifyShapeCommand.cs:71`: `RefuseUnsafeOutput(output, "verify-shape output")` (exit 4, `OutputSinks.cs:20`)
  - `VerifyShapeCommand.cs:87-91`: unsupported provider gives exit 2
  - `VerifyShapeCommand.cs:281`: `hasMismatch ? 1 : 0`
  - `VerifyShapeCommand.cs:288-295`: exception gives exit 1
  - `VerifyShapeCommand.cs:128-137` and `:173-182`: `results` objects have no location
  - `VerifyShapeCommand.cs:207-219`: `queries = results.Zip(readQueries…) location = … : null`
- **Suggested fix:** Rewrite Req 4:
  - **snapshot:** 0 → OK; 1 → "refresh failed (unreachable server, permissions or acquisition error, see [WARN] lines)"; 2 → "arguments/config, or variable NAME unset/empty"; 3 → "no connection resolved"; 4 → "refused snapshot path" (from F2).
  - **verify-shape:** 0/1 with `summary.json` → counts; 1 without `summary.json` → "verify-shape failed"; 2, 3, 4 each get their own text. Detail lines read `queries[i].location?.file/line` (path made relative to the solution, or `?`) together with `results[i].status`.
  - Add a 3.V test with a summary where `location: null`.

## Flow-trace table

### Phase 1
| Claim | Trace chain | Verdict |
|---|---|---|
| 5.9 pins in 4 projects | `Analyzers.csproj:24-25`; `CodeFixes.csproj:16`; `Analyzers.Tests:26,30`; `CodeFixes.Tests:27-28` | ✓ |
| Testing packages | `Analyzers.Tests:31-32` (Analyzer.Testing, SourceGenerators.Testing 1.1.4); `CodeFixes.Tests:31` (CodeFix.Testing 1.1.4) | ✓ |
| NU1605 risk with the 4.12 pin | lock files: Analyzer.Testing → Workspaces ≥1.0.1 and Workspaces.Common ≥1.0.1; SourceGenerators.Testing → ≥3.8.0; CodeFix.Testing → ≥1.0.1. 4.12 meets every floor | ✓ no downgrade |
| Other consumers of the analyzer project | Cli, GoldenCorpus, Benchmarks lock files list only `DataGuard.Contracts` + `System.Composition.Hosting` (PrivateAssets=all) | ✓ no lock churn; they run on Roslyn 5.9 from Core, and the 4.12-built dll binds upward |
| CodeFixes uses no post-4.12 API | `CodeFixProviders.cs:547-548` `Renamer.RenameSymbolAsync(…, default(SymbolRenameOptions), …)` (exists since 4.4), `DocumentEditor`, `WellKnownFixAllProviders` | ✓ |
| BinaryCompatibilityFixture | `.csproj:13` `Microsoft.CodeAnalysis.Common 5.9.0`, references Core only; built at `ci.yml:93-98` | ✓ unaffected; still matched by the Dependabot pattern (F3) |
| Core shares the package id | `Core.csproj:23-24` CSharp/Workspaces 5.9 | ✗ Dependabot ignore side effect (F3) |
| CI step placement | build-and-test steps: checkout, policy, setup-dotnet, cache, restore `--locked-mode`, Linux evaluation parity, licence ×2, **Build solution `:81-82`**, Run analyzers `:84`, format, BinaryCompatibilityFixture, tests, coverage, upload. The dll + Contracts + SqlClassification exist in `bin/Release/netstandard2.0` after `:82` (confirmed locally) | ✓ insert after `:82` |
| SDK used by the gate | `ci.yml:17,44-47` `9.0.x` + `global.json` latestFeature; temp consumer outside the repo | ✗ not 4.12 (F3) |
| Workflow policy constraints | `check-workflow-policy.py` rules (a)–(f) cover only VS MSBuild, VSIX upload, signing and publish steps; a plain `run:` step is unconstrained; `anti_garbage_guard.sh` allows `scripts/` | ✓ |
| Docs-sync constraints | `verify_docs_sync.sh` checks licence, rule table and CLI flags; no script registry | ✓ |
| Existing consumer script | `verify-analyzer-packaging.sh:93` checks only that `/analyzer:` is passed; not in CI | ⚠ overlaps; extend it (F3) |
| Env-gated VSIX test has a host job | VS test job builds with `CreateVsixContainer=false` (`ci.yml:270-271`), so the test returns early; the packaging job (`:300-315`) runs no `dotnet test` | ⚠ unresolved (validate Q2) |
| `assert-vsix.ps1` entries | `:22-29` lists six entries, no Contracts/SqlClassification | ✓ |
| DG001 message | `DiagnosticDescriptors.cs:89` `'dataguard check'`; pinned by `UnvalidatedSqlCallGeneratorTests.cs:41` | ✓ |

### Phase 2
| Claim | Trace chain | Verdict |
|---|---|---|
| Raw Warning line is echoed | `ProgressStreamReader.ProcessLine:65-100` → `FormatProgressLine:44-66` → not JSON → `"[DataGuard CLI] " + Redact(text)` (`:57-62`) | ✓ |
| Warning arrives before Summary | both written to `Console.Error` (`ValidateCommand.cs:97`, `:346`, `:488-500`) | ✓ |
| Warning without a later Summary | `:396-401` (FailOnUnavailable, exit 3); `:507-515` (exception, exit 1) | ✓ path exists (F5) |
| Explain reached at exit 0 without Summary | `Publishing.cs:115`: Explain is not called. With `--progress`, validate always emits Summary on the exit-0 path (`:485-500`), except contracts/yaml/typescript formats, which VS never uses | ✓ the `[WARN]` text can only print when there is a Summary |
| Explain order | `ExitCodeExplainer.cs:27` (summary but no SARIF) → `:32` (warnings) → `:37` (`[OK]`) → `:47` (per command) | ⚠ the plan's insertion point is ambiguous (F5) |
| Line truncation | `MaxProgressLineLength` = 16 KiB (`ProgressLineParser.cs:19`); the Warning line is ~120 chars and Summary JSON < 1 KiB | ✓ no risk |
| Drain grace on a slow machine | `CliRunSession.cs:28,137-147`: after 3 s, `CloseStreams`; the reader catches the IOException and returns a partial result; `:88` falls back to a fresh result. A lost Summary means `HasSummary=false`, so no `[OK]`; the Warning is parsed before the Summary so `GroundTruthMissing` survives | ✓ fails safe |
| Exit 4 meanings | `ExplainAssess(4)` at `:76-77`; validate has no case 4; `OutputSinks.cs:12-22` sets exit 4 | ✓ |
| Status bar / result block | `Publishing.cs:121-123`; `OutputPaneWriter.cs:94-105` (two counts only) | ✓ |
| Redaction of `User Id` | `DataGuardLogger.cs:25` comment already covers `User Id=x;Password=y` | ✓ the 3.V test is a pin, not a change |
| stdout discarded | `ProcessTerminator.DrainAsync` at `:137-151` | ⚠ the plan cites `:110-124` |
| Publish gate untouched | `ProgressReadModels.cs:81-94` | ✓ |

### Phase 3
| Claim | Trace chain | Verdict |
|---|---|---|
| Explicit EF options still rejected | `IdeSafePolicy.cs:188-200`; `:105` resolves the explicit path only | ✓ |
| Missing config allowed under ide-safe | `:119` `allowMissingConfig: ideSafe` | ✓ |
| Policy keeps `AutoDetectEFContext` | `IdeSafePolicy.Apply` changes GroundTruthMode, ManualAssembly, Connection, Vault, AuditLog, Telemetry only | ✓ |
| Default snapshot lookup | `:199-207` → `CliConfigurationResolver.cs:296-313` (next to `--config`, then cwd). VS cwd is the solution dir (`CliArgumentBuilder.cs:117-125`) | ✓ |
| `projectPath` value | `--project` is `Quote(solution.Directory)` (`CliArgumentBuilder.cs:94`), an absolute path | ✓ |
| Injection point inside the try | try starts at `:222`; injection "after :207" is outside it | ✗ (F4) |
| Escaped exception outcome | `Program.cs:46` default handler: exit 1, no Summary | ✗ (F4) |
| Extraction throws on zero entities | `EfModelSource.cs:269-273`; parser DG1306 at `:97`; DG1303 at `:64` | ✓ the auto-path catch is needed and sufficient |
| ContractDiscovered oracle | emitted by `ValidateCommand.cs:246-257` for every non-RawSql contract after the merge at `:233`; `EfModelSource` emits none | ✓ the oracle holds (source is ValidateCommand) |
| Ground-truth gate | `:337` counts `EntityDescriptor` | ✓ |
| DG006 inputs | `NamingConventionRule.cs:34-57`: needs only an `EntityDescriptor` with a differing `ColumnName`; Info severity; enabled in VS (`DataGuardRulesOptionsPage.cs:54`) | ✓ fires, but has no location (F1) |
| Error List visibility | `ContractRuleBase.cs:43` → `DiagnosticEmitter.cs:123` → `SarifErrorListPublisher.cs:130-137` | ✗ (F1) |
| Fixture csproj needed | `ProjectCSharpSqlSource.Discovery.cs:41-44`: a directory scans `*.cs` | ✓ `Shop.csproj.txt` is unnecessary (YAGNI) |
| Path in ACQUISITION lines | `:469` prints `diagnostic.Path` = the full snapshot path (`EfModelSource.cs:277`) | ⚠ absolute, not relative as Req 3 says; partial-parse lines need the same escaping |
| Malformed `FooModelSnapshot.cs` | `ModelSnapshotCSharpParser.cs:97` DG1306 → throw → auto-path catch | ✓ |

### Phase 4
| Claim | Trace chain | Verdict |
|---|---|---|
| Timeout choice | `Commands.cs:66` handles validate/assess only | ✓ needs a live-command branch |
| Temp dir / sarifPath | `:90-92` `%TEMP%\DataGuard\<guid>\validation.sarif` (`TempDirectoryCleaner.cs:20`) | ✓ (not `dataguard-*`) |
| Session status text | `CliRunSession.cs:65` (plan says `:67`) validate/assess | ✓ plan drift by 2 lines |
| Timeout publish rule | `CliRunSession.cs:76` → `CliRunTimeoutHandler.cs:70-78` needs `File.Exists(sarifPath)`. Snapshot refresh has no file, so `ProceedToPublish=false` and `Publishing.cs:32-36` returns silently | ✓ acceptable; tmp-file leftover (F2) |
| Branch point for live commands | `Publishing.cs:24` (Cancelled) → `:32` (ProceedToPublish) → **branch here** → `:38` `PublishGate.Decide` | ✓ |
| summary.json lifetime | `Commands.cs:138` deletes the temp dir in `finally`, so parsing must happen inside HandleRunOutcome | ✓ |
| Snapshot exit codes | `SnapshotCommands.cs:54-59` (3), `:67-70` → `:107-115` (1), `ConfigLoader.cs:137-141` (2) | ✗ plan text (F6) |
| Snapshot write path | `SnapshotCommands.cs:75` → `BaselineManager.cs:236-255`, cwd-relative, no containment | ✗ (F2) |
| `--config` omitted | `ConfigLoader.cs:16-19` uses defaults (provider sqlserver); snapshot goes to `<sln>\.dataguard-snapshot.json` | ✓ |
| `--config` missing file | `ConfigLoader.cs:29-31` exit 2 | ✓ |
| verify-shape exit codes | `:56-61` (2), `:63-68` (2), `:71` (4), `:87-91` (2), `:95-100` (3), `:281` (1/0), `:288-295` (1) | ✗ plan is missing 4 and the two meanings of 1 (F6) |
| summary.json path accepted | same `RefuseUnsafeOutput` as validate's `validation.sarif` in the same directory (`ValidateCommand.cs:194`) | ✓ |
| Summary shape | `results[]` has no location; `queries[i].location` can be null (`:207-219`) | ✗ (F6) |
| Dead Init command | `Menus.vsct:66-72,87`; no handler (`DataGuardPackage.cs:84-92`); `InfoBarManager.cs:136` runs `0x0106`; instantiated only in `InfoBarManagerTests.cs:68,90` | ✓ |
| Run-on-build | `Commands.cs:28-36` calls only `RunValidationAsync` | ✓ |
| Env-var precedence | `CliConfigurationResolver.cs:113-123` (`--connection-env` before `DATAGUARD_CONNECTION_STRING`); `ConfigLoader.cs:111-117` builds the secure source before resolution | ⚠ still needs the e2e check in the plan's Risk section (no HTTP when the variable is set) |
