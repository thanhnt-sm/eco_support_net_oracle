# Red-team report (draft 1 of the plan, before the Visual Studio re-scope)

## Hostile review: Assumption Destroyer and Scope Auditor (Tier Full)
Plan: `plans/261014-0750-ide-extension-db-phase/` (plan.md, phases 01–05, research/*). I only read files and modified nothing.

### Finding 1: Phase 2's status bar result is always overwritten with `idle`, and a replaced run can still post its outcome
- **Severity:** Critical
- **Location:** Phase 2, Architecture ("extension.ts: setStatus(outcome.status)") and Success Criteria ("status bar `warning`").
- **Flaw & failure scenario:**
  - **Status reset.** The plan only replaces the outcome branch at `:595-603`. The `finally` block runs right after it. There it calls `clear(run)`, so `current` becomes undefined. The run is neither cancelled nor timed out, so `finally` then calls `setStatus("idle")`. Every `warning` or `error` is wiped within the same tick. The criterion "validate on sample → status bar warning" cannot pass, and no test would catch it: there is no `setStatus` ordering test, and the only grep hits in tests are `run-coordinator.test.ts:42-46`.
  - **Late outcome from a replaced run.** `run.cancelled` is checked once, at `:517`. After that come awaits: `loadDiagnostics` at `:530` and the summary.json read at `:540-542`. If a new run reserves during those awaits, `nextReservation()→cancel()` sets `cancelled=true` on the old run. The old run still carries on to `:596-603`. With Phase 2 that means it overwrites the new run's `running` status and pops the new "syntactic rules only" notification with **Configure / Refresh Snapshot** buttons for a run that no longer exists. `RunCoordinator` has no per-outcome guard; it only has the token (`run-coordinator.ts:11-18`).
- **Evidence:**
  - `src/DataGuard.VSCode/src/extension.ts:612-614` `if (runCoordinator.isReservationCurrent(reservationToken) && !runCoordinator.current && !run.timedOut && !run.cancelled) { setStatus("idle"); }`
  - `extension.ts:598-599` `} else if (exitCode === 1) { setStatus("warning");`
  - `extension.ts:517` `if (run.cancelled) {` is the only check before the awaits at `:530` and `:540`.
- **Suggested fix:**
  - Add to Phase 2 "Modify extension.ts": "(a) `finally` resets to idle only when no outcome was applied (track `let outcomeApplied = false`), (b) immediately before applying `classifyRunOutcome`, re-check `run.cancelled || !runCoordinator.isReservationCurrent(reservationToken)` and return silently."
  - Put that predicate in `run-outcome.ts` as `shouldApplyOutcome({cancelled, reservationCurrent})`.
  - Add 3.V tests: "replaced run → outcome suppressed" and "applied warning is not reset to idle".

### Finding 2: Phase 4 auto-detect turns an unparseable or large ModelSnapshot into a hard "Validation failed" (exit 1), and its precedence claim is false
- **Severity:** Critical
- **Location:** Phase 4, Requirements 1 and 3, Risk ("giới hạn kích thước như ModelSnapshotCSharpParser").
- **Flaw & failure scenario:**
  - **Hard failure on parse errors.** `ExtractFromModelSnapshotWithDiagnosticsAsync` throws when zero entities parse. That covers a snapshot over 1 MiB (DG1301), over 50k syntax nodes, or any file that merely ends in `ModelSnapshot.cs`. Inside `validate` the exception is caught by the generic handler and the run exits 1 with `Validation failed: …`. Today an EF project in the IDE gets a syntactic-only run. After Phase 4, every **Run Validation** on a large real-world EF schema fails, with no SARIF and no ground-truth warning. Phase 2 would then show it as an error.
  - **No size check before reading.** The size limit is applied *after* `File.ReadAllTextAsync` has loaded the whole file, so the "limit" does not bound memory.
  - **Precedence is not "same as today".** Requirement 3 says EF is "added … only when those give no `EntityDescriptor` (same as `:230-236` today)". In fact `:230-236` adds EF entities whenever a path is set. And the plan puts the locator call at `:105-113`, *before* acquisition, so it cannot know whether the snapshot or connection produced anything.
- **Evidence:**
  - `src/DataGuard.Core/Sources/EfModelSource.cs:270-273` `if (result.Diagnostics.Count > 0 && result.Entities.Count == 0) { throw new EfModelExtractionException(...)`
  - `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs:49-51` `if (source.Length > options.MaximumSourceBytes) { return Failure("DG1301", ...`
  - `EfModelSource.cs:252` `var source = await File.ReadAllTextAsync(snapshotFilePath, cancellationToken)`
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:507-515` `catch (Exception ex) { Console.Error.WriteLine($"Validation failed: {ex.Message}"); ... Environment.ExitCode = 1;`
  - `ValidateCommand.cs:230-233` `if (!string.IsNullOrWhiteSpace(efSnapshotPath)) { ... contracts.AddRange(snapshotExtraction.Entities);` (unconditional)
  - No `EfModelExtractionException` handling anywhere in `src/DataGuard.Cli` (grep returned nothing).
- **Suggested fix:**
  - Requirement 1: an auto-detected snapshot is wrapped in `try/catch (EfModelExtractionException)`. It emits `ACQUISITION: <path>: <message>` and continues syntactic-only, so it never changes the exit code. Check `new FileInfo(path).Length > ModelSnapshotParseOptions.MaximumSourceBytes` *before* reading.
  - Requirement 3: restate it truthfully. Either "auto-detected EF entities are added after acquisition only when `contracts` holds no `DatabaseSchemaDescriptor`/`EntityDescriptor`", with the call moved to just after `:228`, or accept that both sources are added (today's behaviour) and drop the precedence test.
  - Add 3.V e2e tests: "oversized snapshot → exit by violations + ACQUISITION line" and "non-EF `FooModelSnapshot.cs` → no exit 1".

### Finding 3: Phase 3's publish-time dedupe cannot remove an LSP DG017 that was published before the CLI run, so the "one DG017 row" criterion fails
- **Severity:** High
- **Location:** Phase 3, Requirement 4, Architecture (`middleware.handleDiagnostics`), Success Criteria ("Problems chỉ có một dòng DG017").
- **Flaw & failure scenario:**
  - **Ordering.** The server publishes only on `didOpen`/`didChange`, and `handleDiagnostics` only filters at publish time. The real sequence is: open file → LSP publishes DG017 → user runs validate → SARIF loads. Nothing re-publishes, so the LSP DG017 stays and Problems still shows two rows until the user edits the file.
  - **Line bases.** The `(file,line)` key mixes bases: LSP positions are 0-based, while SARIF `startLine` is 1-based and converted in `toRange`.
  - **Server shutdown leaks.** The server's `shutdown` CTS is never cancelled. The planned `shutdown` handler must cancel it and drain `pendingDiagnostics`, or a debounced publish can be written after the `shutdown` response. `pendingDiagnostics` is also a plain `Dictionary` mutated from thread-pool continuations, a race the new `Dispatch` seam would inherit.
- **Evidence:**
  - `src/DataGuard.LanguageServer/Program.cs:23` `else if (method.GetString() is "textDocument/didOpen" or "textDocument/didChange")` is the only publish trigger besides didClose.
  - `src/DataGuard.VSCode/node_modules/vscode-languageclient/lib/common/client.d.ts:222` `handleDiagnostics?: (this: void, uri: Uri, diagnostics: VDiagnostic[], next: HandleDiagnosticsSignature) => void;` (publish-time only). `client.d.ts:361` `get diagnostics(): DiagnosticCollection | undefined;` (the re-filter seam exists).
  - `extension.ts:812` `const startLine = Math.max(0, (region?.startLine ?? 1) - 1);`
  - `Program.cs:8` `using var shutdown = new CancellationTokenSource();` is never cancelled. `Program.cs:85-87` mutates `pendingDiagnostics` inside the async `finally`.
- **Suggested fix:**
  - Requirement 4: after `loadDiagnostics` (and on `clearFindingsAndDiagnostics` and at run start, `extension.ts:434`), re-apply `dedupeAgainstCli` to `languageClient.diagnostics.get(uri)` for every URI in the CLI set. Keep the middleware for later publishes. Key both sides as 0-based `(fsPath,line)`.
  - Architecture: `shutdown` → `shutdown.Cancel()`, then respond. Guard `pendingDiagnostics` with the existing `writeGate` or a lock.
  - Related Files: add `package.json` `test` script `out/ui/diagnostic-dedupe.test.js`. The script is an explicit file list (`package.json:247`), so the new test would otherwise never run.

### Finding 4: Phases 2 and 5 redefine exit 4 as "output path refused", but `assess` (which Phase 1 makes reachable) uses 4 for tool errors and exceptions; Phase 5's explainer signature does not exist
- **Severity:** High
- **Location:** Phase 2, Requirement 4 ("exit 4 → DataGuard refused the output path"). Phase 5, Step B (`Explain(int exitCode, RunSummary? summary)`) and 3.V (`Explain(4, …) → output-path refusal`).
- **Flaw & failure scenario:**
  - **Exit 4 meaning.** For `assess`, exit 4 is the frozen code for tool errors. It is also set on any exception. A SARIF with findings can exist at the same time. Once Phase 1 fixes RC1, VS Code users will hit assess exit 4 routinely, for example on an unreadable project. They would be told the output path was refused, and the SARIF would be discarded or misreported.
  - **Phase 5 clash.** VS already maps assess exit 4 to "Assessment completed with tool errors", so Phase 5 would regress it.
  - **Signature.** `ExitCodeExplainer.Explain` already takes `(string command, CliRunOutcome outcome, bool sarifExists)` and already reads `outcome.Progress.HasSummary/WarningCount`. The proposed `RunSummary` record and `(int, RunSummary?)` overload duplicate an existing model and would break 12 existing test calls (`CliArgumentBuilderTests.cs:76-102`).
- **Evidence:**
  - `src/DataGuard.Cli/Commands/AssessCommand.cs:167-168` `// Findings are a failed assessment; operational/tool errors use the frozen code 4. Environment.ExitCode = report.Errors.Count > 0 ? 4 : ...`
  - `AssessCommand.cs:189-190` `Console.Error.WriteLine($"Assessment failed: ..."); Environment.ExitCode = 4;`
  - `src/DataGuard.VisualStudio/ExitCodeExplainer.cs:76-77` `case 4: return "[WARN] Assessment completed with tool errors (check config or permissions).";`
  - `ExitCodeExplainer.cs:18` `internal static string Explain(string command, CliRunOutcome outcome, bool sarifExists)`
  - `src/DataGuard.Cli/Services/OutputSinks.cs:20` (exit 4 for an unsafe path, shared by all commands)
- **Suggested fix:**
  - Phase 2 Requirement 4: "exit 4 → for `assess`: if SARIF is present, load it and show *'assessment completed with tool errors'* (warning). Otherwise, or for other commands: show the first stderr line (covers `Refusing to write …`)." Add `classifyRunOutcome` cases per command.
  - Phase 5: drop `RunSummary` and the overload. Add the three nullable counts to the existing progress read model (`ProgressReadModels.cs` `HasSummary/WarningCount` class) and branch inside the existing `Explain`. 3.V: `Explain("assess", Create(4), true)` stays "tool errors"; `Explain("validate", Create(4), false)` gives the output-refusal text.

### Finding 5: Phase 1's env stripping is out of RC1–RC6 scope, rests on a false premise, and breaks a documented workflow; Phase 4 widens `--ide-safe` for options no host sends
- **Severity:** Medium
- **Location:** Phase 1, Key Insights bullet 4, Related Files (`extension.ts:457-465`), 3.V `buildChildEnvironment`. Phase 4, Requirement 2.
- **Flaw & failure scenario:**
  - **False premise.** The plan says the modal "says no SecretStorage credential is stored … the modal text is then wrong about the target". It is not wrong: the modal already names `DATAGUARD_CONNECTION_STRING` as the target.
  - **Documented workflow.** USAGE and the VS Code README both document the inherited env var for Refresh Snapshot / Create Baseline / Verify Shapes. Deleting it silently turns those commands into exit 3 (`snapshot refresh requires a database connection`) for every developer who uses a shell variable. This is a behaviour change with no RC behind it. `--ide-safe` already scrubs the environment for validate/assess.
  - **Phase 4 Requirement 2 (YAGNI).** No host sends `--ef-*`: the `validate` argv has no EF arguments. Requirement 2 is also non-actionable. The plan's own multi-match guidance "pass --ef-context" fails with exit 2 on its own because `--ef-context requires --ef-project`. It widens the security surface for nothing the IDE uses.
- **Evidence:**
  - `src/DataGuard.VSCode/src/live-database-confirmation.ts:98-99` `: "the database configured in .dataguard.yml or DATAGUARD_CONNECTION_STRING (no SecretStorage credential is stored)";`
  - `docs/USAGE.md:565` "...dùng credential của bạn (hoặc kết nối trong `.dataguard.yml`/`DATAGUARD_CONNECTION_STRING`)..."
  - `src/DataGuard.VSCode/README.md:47` "...(or the connection configured in `.dataguard.yml`/`DATAGUARD_CONNECTION_STRING`)..."
  - `src/DataGuard.VSCode/src/command-args.ts:66-81` (validate argv: no `--ef-*`)
  - `ValidateCommand.cs:537-540` `"--ef-context requires --ef-project."`
- **Suggested fix:**
  - Remove the env-isolation change, `buildChildEnvironment`, and the related USAGE/CHANGELOG lines from Phase 1. If wanted, track it as a separate decision.
  - In Phase 4, drop Requirement 2 and its tests (and so do not touch `IdeSafePolicyTests.cs:88-90`). Make the multi-match diagnostic name the files and suggest committing a snapshot instead of `--ef-context`.

### Finding 6: Phase 4 and 5 tests and fixtures as written will break the build or not fix RC6
- **Severity:** Medium
- **Location:** Phase 4, Related Files (`Fixtures/EfProject/…*.cs`) and 3.T ("existing cases at lines 185–396 stay green"). Phase 5, Requirement 3 and 3.T (explicit skip).
- **Flaw & failure scenario:**
  1. `DataGuard.Core.Tests` has no `Compile Remove`. The repo convention is `.cs.txt` fixtures. `Migrations/ShopContextModelSnapshot.cs` plus a repository `.cs` would be compiled into the test assembly, which has no EF `ModelSnapshot` or `DbContext` types, and the build fails.
  2. Relaxing the policy turns the existing rows at `IdeSafePolicyTests.cs:88-90` red. These are outside the "185–396" range the plan protects.
  3. `ResolveEfSnapshotSource` is unbounded `AllDirectories`, follows reparse points, and has no `node_modules` skip. "Extract … behaviour identical" contradicts the new depth ≤ 6 / ≤ 2000 caps. A bounded, reparse-skipping enumerator already exists, but it is `internal` to Core, which has no `InternalsVisibleTo`.
  4. RC6: the VSIX installs on 17.0+, but the analyzer and code fixes compile against Roslyn 5.9.0. VS 2022's 4.x compiler rejects such analyzers (CS9057). Adding the two dependency DLLs alone does not make them load, yet the plan marks RC6 fixed and leaves this as Q5.
  5. "Skip explicitly" is impossible on xunit 2.9.3 without a new package. xunit v2 has no dynamic skip (`Assert.Skip` is v3; `SkippableFact` is a separate package).
- **Evidence:**
  - `tests/DataGuard.Core.Tests/Fixtures/CSharpSql/AdoProcedures.cs.txt` (convention). `tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj` has no `Compile Remove`.
  - `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs:88` `[InlineData(null, false, null, "Snap.cs", null, null, "--ef-snapshot")]`
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:568` `Directory.EnumerateFiles(root, "*ModelSnapshot.cs", SearchOption.AllDirectories)`
  - `src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs:523` `AttributesToSkip = FileAttributes.ReparsePoint,` and `:493` `"bin", "obj", "node_modules", ".git", ".vs",`
  - `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:24` `Microsoft.CodeAnalysis.CSharp" Version="5.9.0"`. `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj:16` `...Workspaces" Version="5.9.0"`. `source.extension.vsixmanifest:12` `Version="[17.0,19.0)"`.
  - `tests/DataGuard.VisualStudio.Tests/*.csproj:20` `<PackageReference Include="xunit" Version="2.9.3" />`. `VsixAnalyzerPackagingTests.cs:50` `return; // VSIX not built in this test run — skip gracefully.`
- **Suggested fix:**
  - Phase 4: generate the EF fixture into `Directory.CreateTempSubdirectory` from `.cs.txt` templates, the same pattern as `CliExitCodeTests.cs:316`. Add "delete InlineData rows 88-90" to 3.I. Write the locator as a new bounded `FileSystemEnumerable` (reparse-skip, pruned dirs, caps) used **only** for auto-detect, and leave `ResolveEfSnapshotSource` unchanged.
  - Phase 5: add a requirement to either narrow `InstallationTarget` to `[18.0,19.0)` or build the analyzers against Microsoft.CodeAnalysis 4.x. Replace "explicit skip" with "fail when env `DATAGUARD_VSIX_EXPECTED=1` (set in the VSIX CI job), otherwise return".
  - Docs: add `docs/03-components/tooling/cli.md` and `cli.vi.md` to the Phase 4 docs list if any policy text changes (USAGE.md:208 lists `--ef-*` as rejected).

### Scope audit
- **Not needed for RC1–RC6 (YAGNI):**
  - Phase 1 env isolation and `buildChildEnvironment` (Finding 5).
  - Phase 4 Requirement 2, accepting `--ef-*` under ide-safe (no host sends them).
  - Phase 3 Requirement 3, DGSQL001 → Hint (not part of RC4).
  - Phase 2 Requirement 5, the channel header text.
  - Phase 4 ADR, plus the `Configuration.cs:206-209` stub edit (cosmetic).
  - Phase 5 deletion of `InfoBarManager.cs` and its tests (removing the vsct button is enough to fix the dead menu).
  - Phase 3 "in-process `Dispatch` seam": top-level `Program` is internal and LanguageServer has no `InternalsVisibleTo`, and the process tests already cover it.
  - Phase 3 `ProjectReference` to the LanguageServer Exe: `CliProcessTestRunner` locates DLLs by repo path (`CliProcessTestRunner.cs:22`), so the same path lookup works without the reference.
- **Missing but needed:**
  - The `finally` status reset and stale-outcome guard (Finding 1).
  - Handling for the auto-detect exception and size check (Finding 2).
  - LSP diagnostics re-filter after a CLI run, CTS cancel on `shutdown`, and the new test files added to the `package.json` test list (Finding 3).
  - Command-aware handling of `assess` exit 4 (Finding 4).
  - The Roslyn 5.9 vs VS 17.x load blocker (Finding 6).
  - The VS Code `formatProgressEvent` Summary for `assess` must also render `CriticalCount`/`ToolErrorCount` (`AssessCommand.cs:171-179`). Phase 2 only mentions the validate counts.
  - Pre-existing (not caused by the plan, but it affects RC4): the extension declares `engines.vscode ^1.85.0` (`package.json:17`) while `vscode-languageclient` 10.1.2 requires `^1.91.0` (`node_modules/vscode-languageclient/package.json:8`).

### Assumption table
| # | Assumption | file:line | Verdict |
|---|---|---|---|
| **P1-1** | Tests can load `test/fixtures/cli-contract.json` | `tsconfig.json` `"rootDir": "src"`, `"include": ["src"]`, `outDir out` | HOLDS at runtime: from `out/x.test.js`, `path.join(__dirname,"..","test","fixtures",…)` resolves to the package `test/`. BROKEN if written as a TS `import` (TS6059 rootDir, no `resolveJsonModule`). Use `fs.readFileSync` or `require`. |
| P1-2 | Fixture not shipped in the VSIX | `.vscodeignore` `test/**` | HOLDS |
| P1-3 | Fixture not git-ignored | `git check-ignore` returned rc=1 | HOLDS |
| P1-4 | `assess` accepts `--progress` | `AssessCommand.cs:19,45` | HOLDS |
| P1-5 | `assess` has no `--provider` | `AssessCommand.cs:35-47` | HOLDS |
| P1-6 | Host sends `--provider` to assess at `:84` | `command-args.ts:83` | HOLDS (line is 83) |
| P1-7 | System.CommandLine pinned 2.0.12 | `DataGuard.Cli.csproj:24`; `packages.lock.json:46` | HOLDS |
| P1-8 | `Parse(argv).Errors` is the right API, with existing test precedent | grep `.Parse(` in tests: no System.CommandLine usage | HOLDS for the 2.0 GA API, but there is no in-repo precedent. Use `root.Parse(args, new ParserConfiguration())`, as `Program.cs:31` does. |
| P1-9 | Root assembled inline; 13 commands in this order | `Program.cs:17-29` | HOLDS |
| P1-10 | Commands are reachable from tests | `DataGuard.Cli.csproj:29` `InternalsVisibleTo DataGuard.Core.Tests` | HOLDS |
| P1-11 | Factory per call is safe (shared `CommonOptions` instances) | `Program.cs:13` single `CommonOptions` shared by 10 commands | HOLDS if the factory creates a fresh `CommonOptions` per `Create` |
| P1-12 | `command-args.ts` has no vscode import | `command-args.ts` (grep found no import or require) | HOLDS |
| P1-13 | VS assess argv already includes `--progress` | `CliArgumentBuilder.cs:97-101` | HOLDS |
| P1-14 | Old-CLI regex only knows two flags | `ide-safe-contract.ts:20` | HOLDS. Broadening it attaches the "update CLI" hint to host-side drift (misleading for RC1). |
| P1-15 | Modal is wrong about the target when no credential | `live-database-confirmation.ts:98-99` | BROKEN: the modal names `DATAGUARD_CONNECTION_STRING` |
| P1-16 | Env inheritance is undocumented | `USAGE.md:565`; VS Code `README.md:47` | BROKEN: it is documented behaviour |
| **P2-1** | Status and coordinator are module-global | `extension.ts:53,56` | HOLDS |
| P2-2 | A replaced run's late outcome is ignored | `extension.ts:517` vs awaits at `:530`/`:540` | BROKEN (Finding 1) |
| P2-3 | `setStatus(outcome)` persists | `extension.ts:612-614` | BROKEN: reset to idle |
| P2-4 | A `setStatus` ordering test exists | grep `*.test.ts` | BROKEN: none |
| P2-5 | `Summary` emitted by validate | `ValidateCommand.cs:489-499` | HOLDS |
| P2-6 | `Summary` emitted by scan/verify-shape | grep `ProgressEventKind.Summary` | BROKEN: only validate (`:489`) and assess (`AssessCommand.cs:171`). The plan correctly uses summary.json for these. |
| P2-7 | Assess Summary has validate's counts | `AssessCommand.cs:174-179` | BROKEN: it has `CriticalCount/ToolErrorCount` |
| P2-8 | Under ide-safe no-ground-truth is a `Warning:` (not exit 3) | `ValidateCommand.cs:129`, `:339-346` | HOLDS |
| P2-9 | `formatProgressLine` drops `Warning:`/`UNEVALUATED:` | `ide-safe-contract.ts` (lines 60-85: only `{…}`, `ide-safe:`/`baseline:`, `[INFO]/[WARN]/[ERROR]`) | HOLDS |
| P2-10 | Raw-error echo filter misses `Warning:` | `extension.ts:506` | HOLDS |
| P2-11 | Header says "Local assessment" for scan | `extension.ts:450` | HOLDS |
| P2-12 | summary.json is read for every command | `extension.ts:538` (outside `if expectsSarif`) | HOLDS |
| P2-13 | Exit 4 = output-path refusal | `OutputSinks.cs:20`; `AssessCommand.cs:168,190` | BROKEN for assess (tool errors) |
| P2-14 | New test file runs automatically | `package.json:247` explicit list | HOLDS only because the plan edits the `test` script (Phase 2 does; Phase 3 does not) |
| P2-15 | Refresh Snapshot action still goes through the modal | `extension.ts:391` | HOLDS if the action calls the command id |
| **P3-1** | languageclient v10 has `middleware.handleDiagnostics` | `client.d.ts:222` | HOLDS (10.1.2) |
| P3-2 | Middleware dedupe removes the already-published LSP DG017 | `Program.cs:23`; `client.d.ts:361` | BROKEN: publish-time only. Use `client.diagnostics`. |
| P3-3 | `exit` without `shutdown` → 1 | LSP spec | HOLDS |
| P3-4 | Main loop drops all other requests | `Program.cs:15-53` | HOLDS |
| P3-5 | `shutdown` CTS exists to cancel pending publishes | `Program.cs:8` | HOLDS; it is never cancelled, so the plan must cancel it |
| P3-6 | `pendingDiagnostics` single-threaded | `Program.cs:7,85-87` | BROKEN: mutated from async continuations |
| P3-7 | Scanner exposes the literal span | `CSharpStringLiteralScanner.cs:7,16,22` (`internal readonly struct`, `ContentStart`, `Content`) | HOLDS (internal to the same assembly, enough for `SqlClassifier`) |
| P3-8 | Range is the keyword only | `SqlClassifier.cs:200-207`; `Program.cs:71` | HOLDS |
| P3-9 | Whole-literal range is unique per diagnostic | `SqlClassifier.cs:115` loops statements per literal | BROKEN for multi-statement literals: identical ranges |
| P3-10 | `SqlClassification` is only used by the LSP | `SqlCallSyntax.cs:11`, `SqlTextHeuristics.cs:11` (netstandard2.0 analyzer) | BROKEN: shared with the analyzer. Add new ctor parameters as optional/trailing. |
| P3-11 | No `SqlClassifier` tests exist | `tests/DataGuard.Core.Tests/SqlClassifierTests.cs`, `SqlClassifierPropertyTests.cs` | BROKEN: they exist; extend them |
| P3-12 | Oversize → non-zero exit | `Program.cs:146-150` (throw, unhandled) | HOLDS |
| P3-13 | Process tests can locate the server DLL | `CliProcessTestRunner.cs:22` (repo-path lookup) | HOLDS without a `ProjectReference` |
| P3-14 | LSP in CrossPlatform slnf | `DataGuard.CrossPlatform.slnf:21` | HOLDS |
| P3-15 | SARIF lines line up with LSP lines | `extension.ts:812` (1-based → 0-based) | Needs explicit conversion in the dedupe key |
| **P4-1** | `AutoDetectEFContext` is a dead stub | `Configuration.cs:207-209`; only config plumbing in `ConfigDocument.cs:63,135`, `ConfigLoader.cs:257` | HOLDS |
| P4-2 | `ResolveEfSnapshotSource` does the directory scan | `ValidateCommand.cs:568-576` | HOLDS: unbounded, no `node_modules`, follows reparse points |
| P4-3 | Not consulted without EF options | `ValidateCommand.cs:547-550` | HOLDS |
| P4-4 | `EntityDescriptor` lifts the gate | `ValidateCommand.cs:337` | HOLDS |
| P4-5 | EF path is independent of the forced Snapshot mode | `IdeSafePolicy.cs:84-88`; `ValidateCommand.cs:230` | HOLDS |
| P4-6 | EF added only when there is no `EntityDescriptor` "today" | `ValidateCommand.cs:230-233` | BROKEN: unconditional |
| P4-7 | Parser has size limits | `ModelSnapshotCSharpParser.cs:13,15,49,56` | HOLDS, but after the full read (`EfModelSource.cs:252`) |
| P4-8 | Parse failure degrades gracefully | `EfModelSource.cs:270-273`; `ValidateCommand.cs:507-515` | BROKEN: exit 1 |
| P4-9 | Policy rejects the three EF options | `IdeSafePolicy.cs:188-200` | HOLDS |
| P4-10 | Relaxing the policy keeps existing tests green | `IdeSafePolicyTests.cs:88-90` | BROKEN: 3 rows flip |
| P4-11 | No `ModelSnapshot.cs` fixture exists | glob: only parser, test and benchmark sources | HOLDS |
| P4-12 | Fixture `.cs` files are safe in the test project | `Fixtures/CSharpSql/*.cs.txt`; no `Compile Remove` | BROKEN: they would compile |
| P4-13 | `CliExitCodeTests` relies on non-ide-safe `--ef-*` | `CliExitCodeTests.cs:273-367` | HOLDS: unaffected |
| P4-14 | Docs list `--ef-*` as rejected | `USAGE.md:208`; `cli.md:62-68`; `cli.vi.md` | HOLDS: USAGE must change; cli.md/cli.vi.md are not in the plan |
| P4-15 | README claims init writes a snapshot | `README.md:20`; `README.vi.md:24` | HOLDS |
| P4-16 | A reusable bounded enumerator exists | `AutoDetectionEngine.cs:487-545` (internal, no IVT) | HOLDS: copy the pattern; it cannot be called from Cli |
| **P5-1** | `InitCommand` only in vsct (plus InfoBar exec) | `Menus.vsct:66,87`; `InfoBarManager.cs:136` | HOLDS |
| P5-2 | `InfoBarManager` only instantiated by tests | `InfoBarManagerTests.cs:68,90` | HOLDS |
| P5-3 | `ExitCodeExplainer` single call site | `DataGuardPackage.Publishing.cs:117` (prod) | HOLDS |
| P5-4 | Explainer signature `(int, RunSummary?)` | `ExitCodeExplainer.cs:18` | BROKEN: `(string, CliRunOutcome, bool)` |
| P5-5 | Exit 0 → `[OK] No issues found.` | `ExitCodeExplainer.cs:37-40` | HOLDS |
| P5-6 | Summary parser reads only Error/Warning/Critical | `ProgressLineParser.cs:155-158` | HOLDS |
| P5-7 | Contracts/SqlClassification are netstandard2.0 | both csproj `:3` | HOLDS |
| P5-8 | NuGet packs dependencies as analyzer assets | `DataGuard.Analyzers.csproj:40-45` (None → `analyzers/dotnet/cs`) | HOLDS |
| P5-9 | SqlClassification kept out of the nuspec | `DataGuard.Analyzers.csproj` `ProjectReference … PrivateAssets="all"` | HOLDS |
| P5-10 | VSIX ships only 2 DLLs | `DataGuard.VisualStudio.csproj:108-109`; `vsixmanifest:30-35` | HOLDS |
| P5-11 | `assert-vsix.ps1` lacks the dependencies | `assert-vsix.ps1:22-29` | HOLDS |
| P5-12 | VSIX test silently passes | `VsixAnalyzerPackagingTests.cs:50` | HOLDS |
| P5-13 | Explicit skip possible without a new package | test csproj `:20` xunit 2.9.3 | BROKEN |
| P5-14 | Adding the DLLs makes the analyzer load on VS 17.x | `Analyzers.csproj:24` 5.9.0; `vsixmanifest:12` `[17.0,19.0)` | BROKEN [UNVERIFIED on Windows]: compiler-version gate |
| P5-15 | Assess exit 4 text can become "output path" | `ExitCodeExplainer.cs:76-77` | BROKEN |