# Red-team report (draft 1 of the plan, before the Visual Studio re-scope)

# Hostile review: Scope & Complexity (YAGNI) + Contract Verifier
Plan: `plans/261014-0750-ide-extension-db-phase/` (plan.md, phases 01–05, research/*)

I read the plan, all five phases and both research notes, then grepped `src/` and `tests/` (excluding bin, obj and node_modules). Six findings, most severe first.

---

### F1. Phase 2's status-bar result is reset to idle in the `finally` block
- **Severity:** High
- **Location:** Phase 2, Architecture / Related Code Files ("outcome branch at `:595-603`"), Success Criteria ("status bar `warning`").
- **Flaw & failure scenario:** After `setStatus("warning")` (or `reportRunError` → `setStatus("error")`), the `finally` block runs `runCoordinator.clear(run)`. That makes `current` undefined, so the guard passes and the status is set back to `idle`.
  - The plan only rewires `:595-603`. `classifyRunOutcome` would compute `warning`/`error` correctly, but the status bar would show idle a few ms later.
  - This also applies to the ide-safe failure `return` at `:526`.
  - `extension.ts` has no unit tests; only the manual `test/extension-host.cjs` exercises it. So every new `run-outcome.test.ts` case passes while the Phase 2 success criterion "status bar warning" fails in the real product. RC2 stays invisible in the status bar, which is exactly what the plan says it fixes.
- **Evidence:**
  - `src/DataGuard.VSCode/src/extension.ts:599` `setStatus("warning");`
  - `src/DataGuard.VSCode/src/extension.ts:606` `runCoordinator.clear(run);`
  - `src/DataGuard.VSCode/src/extension.ts:612-613` `if (runCoordinator.isReservationCurrent(reservationToken) && !runCoordinator.current && !run.timedOut && !run.cancelled) { setStatus("idle"); }`
  - `src/DataGuard.VSCode/src/run-coordinator.ts:37-41`: `clear` sets `activeRun = undefined`.
- **Suggested fix:** Add `extension.ts:604-615` (the `finally`) to Phase 2 "Modify". Then:
  - Extend the pure `classifyRunOutcome` to return the final status.
  - In `finally`, only reset to `idle` when no outcome status was applied (for example `let finalStatus: Status | undefined`; `if (finalStatus === undefined) setStatus("idle")`).
  - Add a 3.T pin documenting the current overwrite, a 3.V test on the extracted "final status" decision, and an `extension-host.cjs` assertion on `statusBarItem.text`.

### F2. Exit code 4 is mapped to "refused the output path", but for `assess` it means tool errors (SARIF is still written)
- **Severity:** High
- **Location:**
  - Phase 2, Requirements 4 ("exit 4 → *DataGuard refused the output path*") and Key Insights.
  - Phase 5, 3.V ("`Explain(4, …)` → output-path refusal text (closes D7)").
- **Flaw & failure scenario:** Exit 4 is the CLI's generic operational tool-error code. `assess` uses it when the report has tool errors (findings and SARIF are still produced) and when it throws.
  - **VS Code:** an assess run with one unreadable file loads its SARIF findings into Problems, then shows "refused the output path". That is false and sends the user to the wrong fix.
  - **Visual Studio:** Phase 5 would replace the correct existing text with the wrong one. This is a regression of shipped behaviour, not a parity fix.
- **Evidence:**
  - `src/DataGuard.Cli/Commands/AssessCommand.cs:167-168` `// Findings are a failed assessment; operational/tool errors use the frozen code 4.` / `Environment.ExitCode = report.Errors.Count > 0 ? 4 : report.Findings.Count > 0 ? 1 : 0;`
  - `src/DataGuard.Cli/Commands/AssessCommand.cs:190` `Environment.ExitCode = 4;` (exception path)
  - `src/DataGuard.Cli/Services/OutputSinks.cs:11` `// exit 4 is the operational tool-error code, the same one assess uses for its SARIF sink.`
  - `src/DataGuard.VisualStudio/ExitCodeExplainer.cs:76-77` `case 4: return "[WARN] Assessment completed with tool errors (check config or permissions).";`
- **Suggested fix:** Change Phase 2 Req 4 to "exit 4 → *DataGuard reported tool errors: <first `[CODE] path: message` / `Refusing to write` stderr line>*", with status `warning` when SARIF is present and `error` otherwise. In Phase 5, delete the `Explain(4…)` output-path bullet and pin the existing `ExplainAssess(4)` text in 3.T instead. Add a 3.V case: `assess` exit 4 with SARIF → findings loaded + tool-error warning.

### F3. Phase 3's DG017 de-dup middleware cannot match lines, and there are three DG017 producers, not two
- **Severity:** High
- **Location:** Phase 3, Requirements 4, Architecture (`middleware.handleDiagnostics`), new `ui/diagnostic-dedupe.ts`.
- **Flaw & failure scenario:**
  - **Key mismatch:** the de-dup key is `(file, startLine, ruleId)`. The CLI DG017 location is the call-site node (invocation, variable declarator, assignment, object creation), not the literal. The LSP DG017 range is the SQL literal.
    - Example: `conn.Query<Order>(\n  "SELECT * FROM Orders")`. CLI line = invocation line, LSP line = next line. Both rows stay in Problems.
    - Example: `var sql = "SELECT * ..."` followed by a later call. These differ too.
  - **Third producer:** the Roslyn analyzer (which `docs/USAGE.md` recommends as a PackageReference) also reports DG017, and C# Dev Kit shows it in the same Problems view.
  - Phase 3 also widens the LSP range to the whole literal. Several statements in one literal (`"SELECT ...; SELECT * ..."`) then produce stacked diagnostics with identical ranges. Nothing in the plan covers this.
- **Evidence:**
  - `src/DataGuard.Core/Rules/SelectStarUsageRule.cs:13` `public override string RuleId => "DG017";` with `contract.Location` (`:51`).
  - `src/DataGuard.Core/Sources/CSharp/ProjectCSharpSqlSource.Passes.cs:323` `Location = invocation.GetLocation(),` and `:180` `Location = variable.GetLocation(),`
  - `src/DataGuard.Analyzers/ContractValidationAnalyzer.cs:128` `context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.SelectStarUsage, location, properties, SelectStarMessage));`
  - `src/DataGuard.LanguageServer/Program.cs:73` `code = item.IsSelectStar ? "DG017" : "DGSQL001",`
  - `docs/USAGE.md:17-18` `# Analyzers cho IDE (khuyên dùng / recommended)` / `dotnet add package DataGuard.Analyzers`
- **Suggested fix (KISS):**
  - Remove `diagnostic-dedupe.ts`, the `handleDiagnostics` middleware and the `loadDiagnostics` key recording from Phase 3.
  - Have the LSP stop publishing DG017. Fold `IsSelectStar` into the DGSQL001 Hint message ("SQL SELECT * statement…").
  - DG017 is owned by the CLI and the analyzer, which already carry the authoritative message and the quick fix (`quick-fix-provider.ts:57` matches on code `DG017` from any source).
  - One server-side test replaces a host module plus its test file.

### F4. Phase 4 relaxes the `--ide-safe` boundary for `--ef-*` options no host sends, and adds a locator class that duplicates existing logic
- **Severity:** High
- **Location:** Phase 4, Requirements 2 and 3, Architecture (`EfModelSnapshotLocator`), Related Code Files, 3.T.
- **Flaw & failure scenario:**
  - **No consumer:** neither host builds `--ef-snapshot`, `--ef-project` or `--ef-context` (zero hits in `command-args.ts` and `CliArgumentBuilder.cs`).
    - Accepting them under `--ide-safe` widens a security boundary with no caller.
    - `--ef-snapshot <any path>` is not confined to `--project`, so under ide-safe the CLI would read any `.cs` path a wrapper or repo-driven tool passes.
    - Auto-detect rooted at `--project` already delivers RC5.
  - **Unlisted test breakage:** the plan says only the cases at `IdeSafePolicyTests.cs` lines 185–396 "stay green". The three `--ef-*` `InlineData` rows at `:88-90` would break and are not listed.
  - **Unlisted doc change:** `docs/USAGE.md:208` documents these options as rejected. The docs-sync gate will catch it, but the plan does not list it.
  - **False "unchanged" claim:** Req 3 says "EF entities are added … only when those give no `EntityDescriptor` (same as `:230-236` today)". Today the add is unconditional.
  - **Duplicate class:** `EfModelSnapshotLocator`, with its own depth and file-count caps and its own test file, duplicates `ResolveEfSnapshotSource`, which already does bin/obj/.git-filtered enumeration with one/zero/many handling.
- **Evidence:**
  - `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs:88-90` `[InlineData(null, false, null, "Snap.cs", null, null, "--ef-snapshot")]` … `"--ef-project"` … `"--ef-context"`
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:233` `contracts.AddRange(snapshotExtraction.Entities);` inside only `if (!string.IsNullOrWhiteSpace(efSnapshotPath))`
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:527-593` (`ResolveEfSnapshotSource`, single caller `:105`)
- **Suggested fix:**
  - Delete Req 2: keep the `--ef-*` rejections and leave `IdeSafePolicy.cs` unchanged.
  - Implement auto-detect as one branch in `ValidateCommand` for `ideSafe && AutoDetectEFContext && efSnapshotPath is null`. Call `ResolveEfSnapshotSource(null, projectPath, null)` and turn `Success=false` into an `ACQUISITION:` line instead of exit 2. That keeps the explicit-option exit-2 pins in `CliExitCodeTests.cs:355-369` intact.
  - Drop the `EfModelSnapshotLocator` class and its test file. Rewrite Req 3 to state today's unconditional merge, or make it a deliberate change with a pin.
  - The ADR in the Risk section becomes unnecessary because the policy no longer changes.

### F5. Phase 1 adds a breaking env change based on a false premise, and its "both sides" fixture leaves out the Visual Studio host
- **Severity:** Medium
- **Location:** Phase 1, Key Insights bullet 4, Step C (`buildChildEnvironment`), Architecture / Requirements ("Every host argv"), 3.T ("13 commands in the same order").
- **Flaw & failure scenario:**
  - **Env scrub (not in RC1–RC6):**
    - The premise is that the modal "is then wrong about the target". It is not: the modal explicitly names `.dataguard.yml or DATAGUARD_CONNECTION_STRING`.
    - README and USAGE document inheriting the env credential for snapshot, baseline and verify-shape.
    - Removing it breaks every developer who runs `snapshot refresh` or `verify-shape` from a shell-exported credential, and there is no migration note.
  - **Fixture coverage:** the shared fixture only pins VS Code's `buildCliArguments`. VS builds argv as strings in `CliArgumentBuilder.Build*Arguments` and also sends `--skip-rules`.
    - "Pinned on both sides" therefore holds for one host only.
    - The new 13-command order pin guards the extracted factory, not the host contract — this is YAGNI.
- **Evidence:**
  - `src/DataGuard.VSCode/src/live-database-confirmation.ts:100` `: "the database configured in .dataguard.yml or DATAGUARD_CONNECTION_STRING (no SecretStorage credential is stored)";`
  - `src/DataGuard.VSCode/README.md:47` `…they use your credential (or the connection configured in .dataguard.yml/DATAGUARD_CONNECTION_STRING)…`
  - `docs/USAGE.md:565` (same statement)
  - `src/DataGuard.VisualStudio/CliArgumentBuilder.cs:89-101` `return "validate --config " + Quote(configPath) … + " --progress " + IdeSafeOption + skipArg;`
- **Suggested fix:**
  - Cut `buildChildEnvironment`, the `extension.ts:457-465` change and the related USAGE edit (or move them to a separate, owner-approved plan).
  - In `IdeCliContractTests`, also parse the VS vectors: split `CliArgumentBuilder.BuildValidateArguments` / `BuildAssessArguments` output with a quote-aware splitter, or add VS vectors to the fixture and assert them in `CliArgumentBuilderTests`.
  - Drop the 13-command-order test; one test that parses the fixture through `CliRootCommandFactory` is enough.
  - The factory extraction is safe: the only external reference to the CLI entry is the Dockerfile `ENTRYPOINT ["dotnet", "DataGuard.Cli.dll"]` at `Dockerfile:75`. There is no `StartupObject`.

### F6. Phase 5 targets an `ExitCodeExplainer` signature that does not exist, and gates the analyzer packaging on something CI cannot check
- **Severity:** Medium
- **Location:** Phase 5, Architecture / Step B (`RunSummary`, `Explain(int exitCode, RunSummary? summary)` overload), Requirement 3 and Success Criteria (VSIX analyzer).
- **Flaw & failure scenario:**
  - **Wrong signature:** the real API is `Explain(string command, CliRunOutcome outcome, bool sarifExists)`, with 1 production caller and 13 test callers. Summary data already flows through `outcome.Progress`, which is a `ProgressReadResult` with `HasSummary`, `ErrorCount` and `WarningCount`. The proposed "old signature delegates with null" overload cannot exist as written, and a new `RunSummary` record would duplicate `ProgressReadResult` and `ParsedProgress`.
  - **Analyzer packaging (RC6):**
    - The analyzer is compiled against Roslyn 5.9.0, which the plan itself flags [UNVERIFIED] for VS 2022.
    - `VsixAnalyzerPackagingTests` and `assert-vsix.ps1` only check that the files are present, so Windows CI turns green whether or not the analyzer loads. Success Criterion 5 is unfalsifiable in CI.
    - If the analyzer does load, VS Error List gets DG017 from the VSIX analyzer, a PackageReference analyzer and CLI SARIF. That is the same duplication Phase 3 fixes for VS Code.
    - Bundling the analyzer in the VSIX was an earlier decision (`plans/260929-0835-vs-extension-hardening/phase-03-ci-vsix-packaging-gate.md:11`), so it is in scope. But the fix is incomplete without deciding the Roslyn version.
- **Evidence:**
  - `src/DataGuard.VisualStudio/ExitCodeExplainer.cs:18` `internal static string Explain(string command, CliRunOutcome outcome, bool sarifExists)`
  - `src/DataGuard.VisualStudio/ProgressReadModels.cs:40-46` `internal sealed class ProgressReadResult { public int ErrorCount … public int WarningCount … public bool HasSummary …`
  - `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:24` `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" PrivateAssets="all" />`
  - `src/DataGuard.VisualStudio/source.extension.vsixmanifest:30-35` (only Analyzers and CodeFixes are declared as `Microsoft.VisualStudio.Analyzer` assets)
- **Suggested fix:**
  - Replace `RunSummary` and the overload with two new properties on `ProgressReadResult`, `UnevaluatedContractCount` and `UnavailableRuleCount`, filled in `ProgressLineParser.cs:155-158`.
  - In `Explain`, add one branch before `[OK]`: `exitCode == 0 && outcome.Progress.UnevaluatedContractCount > 0 || groundTruthWarningSeen`. No signature change, so all 14 callers are untouched.
  - Split Req 3 into a separately gated item, blocked on Q5. Either lower the analyzer's Roslyn reference to the 4.x floor matching `[17.0,…)`, or remove the analyzer assets from the VSIX and point to the PackageReference path documented at `docs/USAGE.md:513-517`. Do not add DLLs whose loading CI cannot verify.
  - Keep the dead `InitCommand` / `InfoBarManager` removal; it is correctly scoped.

**Smaller trims, no separate finding:**
- **Phase 3, two test seams for the language server:** it adds both an in-process `Dispatch` seam and process-spawn tests. Keep one: the process tests, which match the `CliProcessTestRunner` pattern.
- **Phase 3, `SqlClassifierTests.cs` already exists:** the plan says "create if absent", but the file is there.
- **Phase 3, hover symptom in RC4 is overstated:** the client never sends `textDocument/hover`, because the server advertises only `textDocumentSync` (`LanguageServer/Program.cs:21`, `extension.ts:296`). Shutdown and exit are the real lifecycle defects.
- **Phase 2, new `run-outcome.ts`:** the separate module is acceptable. The pure split is justified, since `ide-safe-contract.ts` is the handshake contract.

---

## Callers table

| symbol | callers (file:line, …) | count | impact |
|---|---|---|---|
| `buildCliArguments` (`command-args.ts:56`) | prod: `extension.ts:449`; tests: `command-args.test.ts:7,12,17,21,41,45,49,53,60,71,75,78,83,90` | 1 prod + 14 test | The `assess` change (Phase 1) changes the literal at `:45` and the credential case at `:83`; all others are unchanged. VS argv is not covered (F5). |
| `isOldCliRejection` | internal `ide-safe-contract.ts:36`; tests `ide-safe-contract.test.ts:22-29` (8 asserts) | 0 external prod + 8 test | `:27` asserts `'--progress'` is **not** a rejection, so the generic-capture change in Phase 1 flips an existing pin. The plan's 3.T says "current cases" stay; this one must be listed as intentionally flipped. |
| `buildIdeSafeFailureMessage` | prod `extension.ts:524`; tests `ide-safe-contract.test.ts:38-41` | 1 + 4 | Message change is additive. |
| `formatProgressLine` / `formatProgressEvent` (private) | prod `extension.ts:680` (`processProgressText`); `ide-safe-contract.ts:62`; tests `ide-safe-contract.test.ts:48-77` | 1 + ~17 | `:76` asserts `"random CLI chatter"` → `undefined`; new prefixes must not match it. |
| `waitForExit` / `ChildExit` | def `extension.ts:687` / `:42`; only call `extension.ts:496`; fields read at `:497-526` | 1 | Adding `signals` is local to this function. |
| `setStatus` | `extension.ts:68, 493, 499, 597, 599, 613 (finally), 628, 901 (reportRunError)`; def `:905` | 8 | `:613` overwrites outcome status (F1). |
| `IdeSafePolicy.FirstRejectedValidateOption` | prod `ValidateCommand.cs:80`; tests `IdeSafePolicyTests.cs:85-90 (InlineData, ef rows 88-90), 94, 101, 102, 109, 376, 383` | 1 + 6 methods | Phase 4 breaks the three ef InlineData rows (F4). |
| `IdeSafePolicy.Apply` | prod `ValidateCommand.cs:139`; tests `IdeSafePolicyTests.cs:27,42,62,78,157,190,201,217,218,229,245,263,273,400` | 1 + 14 | Unaffected if F4's fix is taken. |
| other `IdeSafePolicy.*` | `AssessCommand.cs:62,63,66`; `CommonOptions.cs:48,56,58,60,62`; `ValidateCommand.cs:77,84,92,146` | 12 | None. |
| `--ef-*` in tests | `CliExitCodeTests.cs` (14 refs; ambiguous / conflict / invalid → exit 2 at `:355-369`), `CliBaselineSuppressionEndToEndTests.cs` (7), `IdeSafePolicyTests.cs` (3) | 24 | Auto-detect must not reuse explicit exit-2 semantics. |
| `ResolveEfSnapshotSource` | `ValidateCommand.cs:105` (def `:527`) | 1 | Reuse directly instead of a new locator class (F4). |
| `EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync` | prod `ValidateCommand.cs:232`; tests `SourceAndBaselineTests.cs:290,291` | 1 + 2 | Unchanged. |
| `SqlClassification` ctor (sealed **class**, not record) | `SqlClassifier.cs:199` only | 1 | Additive fields are safe. |
| `SqlClassification.Start/Length` | `LanguageServer/Program.cs:71` only | 1 | Analyzer: `SqlTextHeuristics.cs:72` reads only `IsSelectStar`, `SqlCallSyntax.cs:291` calls only `LooksLikeSql`, `UnvalidatedSqlCallGenerator.cs:74-75` uses its own Roslyn span. Phase 3's range change does **not** affect analyzer behaviour. |
| `SqlClassifier.Classify/ClassifyCSharp` tests | `SqlClassifierTests.cs:16,27,39,41,53,70,85,86,92,107,108,123,148,156`; `SqlClassifierPropertyTests.cs:23,35,45,57,58,67,81,88` | 22 | `:41,156` use `Match<>` on properties; additive fields are fine. |
| `ExitCodeExplainer.Explain` | prod `DataGuardPackage.Publishing.cs:117`; tests `CliArgumentBuilderTests.cs:76-81,87,89,99-102`, `ProcessTerminatorClassificationTests.cs:43`; constant use `CliRunSession.cs:106` | 1 + 13 | The plan's `Explain(int, RunSummary?)` does not match the real signature (F6); exit-4 text regression (F2). |
| VS `Summary` consumers | `ProgressLineParser.cs:155-158`; `ProgressStreamReader.cs:92-93`; `OutputPaneWriter.cs:97`; `ExitCodeExplainer.cs:32`; `DataGuardPackage.Publishing.cs:122`; tests `ProgressPumpTests.cs:19`, `ProgressStreamReaderTests.cs:16`, `CliArgumentBuilderTests.cs:47`, `DataGuardPackageTests.cs:56,369`, `RuleInventoryTests.cs:90` | 5 prod + 6 test | Extend `ProgressReadResult`; there is no need for a new record. |
| `InfoBarManager` | no prod instantiation; `InfoBarManagerTests.cs:68,90`; `InfoBarManager.cs:136` executes `0x0106` | 0 prod + 2 test | Safe to delete together with `Menus.vsct:66-72,87`. |
| CLI `Program.cs` (top-level) | `Dockerfile:75` `ENTRYPOINT ["dotnet", "DataGuard.Cli.dll"]`; `CliFollowUpEndToEndTests.cs:10` (comment only); no `StartupObject` | 0 code refs | `CliRootCommandFactory` extraction touches nothing else. |
