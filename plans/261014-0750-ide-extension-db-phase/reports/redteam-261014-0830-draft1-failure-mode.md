# Red-team report (draft 1 of the plan, before the Visual Studio re-scope)

# Hostile review: Failure Mode Analyst and Flow Tracer for plan `plans/261014-0750-ide-extension-db-phase`

I traced every phase against the code; nothing was modified. Phase 1 holds up. Phases 2–5 each contain at least one claim the code refutes, and two of those make a stated success criterion unreachable. Six findings follow, most severe first, then a flow-trace table per phase.

---

## Findings

### F1 — **High** — Phase 2, Architecture / Requirements 1–4 ("ends in status `warning`/`error`")
**Flaw and failure scenario:** The `finally` block of `runCliCommand` forces the status bar back to `idle` after every run that was not cancelled or timed out. It runs after any `warning` or `error` the outcome branch sets. The plan only rewires the outcome branch at `:595-603` and never touches the `finally`. So every new outcome Phase 2 adds (syntactic-only warning, exit 3/4/2 errors, exit 1 with no artifact) flashes and is immediately replaced by `idle`. The success criterion "status bar `warning`" fails on the first manual run. This is also a live bug today: exit 1 already resets to idle.

**Evidence:**
- `src/DataGuard.VSCode/src/extension.ts:599` `setStatus("warning");`
- `extension.ts:606` `runCoordinator.clear(run);` sets `activeRun` to undefined (`run-coordinator.ts:37-41`).
- `extension.ts:612-613` `if (runCoordinator.isReservationCurrent(reservationToken) && !runCoordinator.current && !run.timedOut && !run.cancelled) { setStatus("idle");`
- The reservation is still current unless another run started: `run-coordinator.ts:11-17`. The token only moves in `cancel()`.
- This also overrides `reportRunError` → `setStatus("error")` (`extension.ts:897-903`), including the ide-safe-handshake failure at `:523-526`.

**Suggested fix:**
- Add to Phase 2 Step B: `classifyRunOutcome` returns `status`. `runCliCommand` stores it in a local `let finalStatus: Status | undefined`, and the `finally` calls `setStatus("idle")` only when `finalStatus === undefined`.
- Add a 3.T pin documenting the current overwrite. Add a 3.V test on a pure helper `resolveFinalStatus(outcomeStatus, reservationCurrent, timedOut, cancelled)`.
- Add `extension.ts:612-613` to "Related Code Files".

### F2 — **High** — Phase 3, Requirement 4 / Success criterion "only one DG017"
**Flaw and failure scenario:** The de-duplication runs inside `middleware.handleDiagnostics`, which only fires when the server publishes. The sequence is:
1. The user opens a file. `didOpen` triggers an LSP publish of DG017. The CLI set is empty at that point, so the diagnostic is kept.
2. The user runs validation. `runCliCommand` first clears the CLI collection (`:434`), then `loadDiagnostics` fills it.
3. No new LSP publish happens until the next keystroke, so Problems shows two DG017 rows.

The `(file, startLine)` key also cannot match in general. CLI locations are the invocation, variable or assignment node, not the literal. After Phase 3, the LSP range starts at the literal. They differ for multi-line calls (`conn.Query<Order>(\n "SELECT * ...")`) and for `const string Sql = ...` used on another line.

**Evidence:**
- `extension.ts:434` `diagnostics?.clear();`
- `extension.ts:797-798` `diag.code = result.ruleId ...; diag.source = "DataGuard";`
- `src/DataGuard.Core/Sources/CSharp/ProjectCSharpSqlSource.Passes.cs:180` `Location = variable.GetLocation(),`
- `ProjectCSharpSqlSource.Passes.cs:323` `Location = invocation.GetLocation(),`
- `ProjectCSharpSqlSource.Passes.cs:390` `Location = assignment.GetLocation(),`
- `src/DataGuard.LanguageServer/Program.cs:23-40`: publishing happens only on `didOpen`/`didChange`.

**Suggested fix:**
- The middleware keeps the last raw LSP diagnostics per URI (`Map<string, Diagnostic[]>`).
- After `loadDiagnostics`, and after `clearFindingsAndDiagnostics`, re-push the filtered set with `languageClient.diagnostics?.set(uri, dedupe(raw, cliIndex))`.
- Match on range overlap between the CLI region (startLine..endLine) and the LSP literal range, not on equal start line.
- Add 3.V tests: "LSP published before the CLI run is deduped after `loadDiagnostics`" and "multi-line invocation DG017 dedupes".

### F3 — **High** — Phase 4, Requirement 3 / Architecture / Related Code Files (`:105-113` injection, "same as :230-236 today")
**Flaw and failure scenario:**

*(a) The precedence claim is false.* EF extraction is merged unconditionally whenever `efSnapshotPath` is set. Snapshots and connections never produce `EntityDescriptor`; they produce `DatabaseSchemaDescriptor` and procedures. So "EF only when those give no EntityDescriptor" describes no code path.

*(b) The proposed injection point is too early.* `:105-113` runs before configuration resolution (`:119`), before `IdeSafePolicy.Apply` decides on a kept connection (`:139`), and before default-snapshot discovery (`:202`). The condition "no snapshot file and no kept connection" cannot be evaluated there. An implementer who follows the plan will either auto-detect even when a snapshot exists, which breaks the plan's own precedence pin, or move the code without guidance.

*(c) The test oracles do not prove the feature.*
- DG004 already fires without any ground truth, through the `RawSqlDescriptor.ExpectedProperties` branch. The research shows DG004×2 on the sample with "no ground truth".
- DG005 needs a `DatabaseSchemaDescriptor`, so it can never fire from EF-only input. The success criterion "DG004/DG005/DG006 in Problems without a snapshot or credential" is therefore unreachable for DG005 and non-discriminating for DG004.

**Evidence:**
- `src/DataGuard.Cli/Commands/ValidateCommand.cs:230-235` `if (!string.IsNullOrWhiteSpace(efSnapshotPath)) { var snapshotExtraction = await EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync(...); contracts.AddRange(snapshotExtraction.Entities);`
- `ValidateCommand.cs:105` `ResolveEfSnapshotSource(...)` comes before `:119` `ResolveCommandConfigurationAsync`, `:139` `IdeSafePolicy.Apply(` and `:202` `FindDefaultSnapshot(`.
- `src/DataGuard.Cli/Services/ContractAcquisition.cs:93-101`: the snapshot yields only a schema descriptor and procedures.
- `src/DataGuard.Core/Rules/NullableMismatchRule.cs:34-37` `var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault(); if (schema == null ...) return`
- `ColumnShapeMatchRule.cs:93` `else if (contract is RawSqlDescriptor rawSql && rawSql.ExpectedProperties != null ...`
- `research/cli-reproduction.md` row 2: "DG004×2 … no ground truth".

**Suggested fix:**
- Inject the locator after `ValidateCommand.cs:207` (after `FindDefaultSnapshot` and after `IdeSafePolicy.Apply`). Guard it with `ideSafe && config.AutoDetectEFContext && efSnapshotPath is null && string.IsNullOrEmpty(config.SnapshotFilePath) && string.IsNullOrEmpty(config.ConnectionString)`.
- Restate Requirement 3 as "auto-detect only when no snapshot or connection; explicit EF options merge additively (today's `:230`)".
- Replace the e2e oracles with:
  - a `ContractDiscovered` progress event with Detail `EntityDescriptor`,
  - DG006 (an entity-only rule),
  - a SARIF result that is **absent** without the snapshot file.
- Drop DG005 from the success criterion.

### F4 — **High** — Phase 4, Requirement 1 / Risk ("size limit like the existing `ModelSnapshotCSharpParser`")
**Flaw and failure scenario:**
- **Parse failure turns into exit 1 with no SARIF.** `ExtractFromModelSnapshotWithDiagnosticsAsync` throws when the parser yields diagnostics and zero entities. With auto-detect, any EF repo whose `*ModelSnapshot.cs` the parser cannot handle (newer EF API shapes, or a decoy file named `FooModelSnapshot.cs`) throws inside the validate `try`. That becomes `Validation failed: …`, exit 1, with no SARIF and no summary.
  - Today the same repo gets a working syntactic run.
  - After Phase 2 this becomes an error notification on every validate. The IDE regresses from "some results" to "no results".
- **No size cap exists.** The plan's mitigation cites a size cap that does not exist. The file is read with an unbounded `File.ReadAllTextAsync`, and the parser has no length guard. A repo-controlled multi-hundred-MB file named `*ModelSnapshot.cs` is parsed in the IDE process tree.

**Evidence:**
- `src/DataGuard.Core/Sources/EfModelSource.cs:271-273` `if (result.Diagnostics.Count > 0 && result.Entities.Count == 0) { throw new EfModelExtractionException(result.Diagnostics[0].Message);`
- `EfModelSource.cs:252` `var source = await File.ReadAllTextAsync(snapshotFilePath, cancellationToken)`
- `ValidateCommand.cs:507-515` `catch (Exception ex) { Console.Error.WriteLine($"Validation failed: {ex.Message}"); … Environment.ExitCode = 1;`
- `ModelSnapshotCSharpParser.cs` has no length or byte cap. The only "Max" hit is the facet name `"HasMaxLength"` at `:43`.

**Suggested fix:**
- In the auto-detect path only, catch `EfModelExtractionException`, `IOException` and `UnauthorizedAccessException`. Convert them to `ACQUISITION: <relpath>: EF model not used: <msg>` and continue the syntactic run.
- Before reading, check `FileInfo.Length` ≤ 4 MiB; otherwise emit an ACQUISITION line.
- Add 3.V e2e tests: a malformed snapshot gives exit 0 or 1 with SARIF present plus an `ACQUISITION:` line, and an oversized decoy is skipped.
- Correct the Risk bullet: no cap exists today, and the plan must add one.

### F5 — **Medium** — Phase 2 Requirements 3–4 and Phase 5 Requirement 1 / 3.V (exit-code table)
**Flaw and failure scenario:**

*(a) Exit 4 is overloaded.* The plan maps exit 4 to "DataGuard refused the output path" (Phase 2) and `Explain(4,…)` to the output-path refusal (Phase 5). But `assess` writes its SARIF and then exits 4 whenever `report.Errors.Count > 0` (tool errors), and also on any assess exception. One unreadable project file in the workspace would make VS Code report an output-path refusal while the SARIF is loaded. VS already has the correct text for assess exit 4, and the plan overwrites it.

*(b) `firstDiagnosticLine` misses the real exit-1 messages.* Its definition is "first `UNEVALUATED:`/`ACQUISITION:`/`Error:` line", and its fallback is "first non-JSON stderr line". For validate, the real exit-1-without-artifact messages are `Validation failed: …` and System.CommandLine's `Unhandled exception: …`. Neither matches the prefix list, and the fallback is always `ide-safe: active`. The notification would read "…: ide-safe: active".

*(c) Phase 5's seam does not match the code.* There is no `Explain(int)` overload to delegate from. The existing signature is `(command, outcome, sarifExists)`, and `outcome.Progress` (`ProgressReadResult`) is already the summary model. The `Warning: no ground truth` line is not JSON, so `ProgressLineParser` never sees it as a signal.

**Evidence:**
- `src/DataGuard.Cli/Commands/AssessCommand.cs:143` `await WriteSarifAssessment(report, output!, ct);`
- `AssessCommand.cs:168` `Environment.ExitCode = report.Errors.Count > 0 ? 4 : report.Findings.Count > 0 ? 1 : 0;`
- `AssessCommand.cs:190` `Environment.ExitCode = 4;`
- `src/DataGuard.VisualStudio/ExitCodeExplainer.cs:76-77` `case 4: return "[WARN] Assessment completed with tool errors …";`
- `ExitCodeExplainer.cs:18` `internal static string Explain(string command, CliRunOutcome outcome, bool sarifExists)`
- `src/DataGuard.VisualStudio/ProgressReadModels.cs:40-46` `internal sealed class ProgressReadResult { ErrorCount; WarningCount; HasSummary`
- `src/DataGuard.VisualStudio/ProgressStreamReader.cs:89-95`: only `parsed.IsSummary` updates the counts.
- `ValidateCommand.cs:509` `Console.Error.WriteLine($"Validation failed: {ex.Message}");`
- `ValidateCommand.cs:77`: the ack is the first stderr line.

**Suggested fix:**
- Make `classifyRunOutcome` command-aware:
  - `assess` + exit 4 + artifact → `warning` "completed with tool errors".
  - exit 4 is "refused output path" only when a stderr line starts with `Refusing to write`.
- Define `firstDiagnosticLine` as the first stderr line that does not start with `ide-safe:`, `{`, `[INFO]` or `baseline:`. Add test vectors `Validation failed: x` and `Unhandled exception: x`.
- Phase 5:
  - Extend `ProgressReadResult` and `ParsedProgress` with `UnevaluatedContractCount`, `UnavailableRuleCount`, `AcquisitionDiagnosticCount` and `GroundTruthMissing`. Set `GroundTruthMissing` in `ProgressStreamReader.ProcessLine` from the raw `Warning: no ground truth` line.
  - Change the `[OK]` branch at `ExitCodeExplainer.cs:37-40` to consult `outcome.Progress`.
  - Keep `ExplainAssess(4)` and add `ExplainValidate` case 4.
  - Delete `RunSummary` and the `Explain(int, RunSummary?)` overload from the plan.

### F6 — **Medium** — Phase 3, Step B/C (lifecycle) and 3.T "didChange supersedes" / test harness
**Flaw and failure scenario:**

*(a) Existing race.* `pendingDiagnostics` is a plain `Dictionary` mutated from two places: the thread-pool continuation of `PublishDebouncedAsync` (its `finally`) and the main read loop. The `finally` also disposes the CTS that the loop may already have taken out and is about to `Cancel()`. `CancellationTokenSource.Cancel()` after `Dispose()` throws `ObjectDisposedException` on the main loop, which crashes the server. The plan's new process test (`didOpen` then `didChange` around the 150 ms boundary) lands exactly in this window on slow CI, so it is flaky or crashes.

*(b) Shutdown cancellation.* The `shutdown` CTS is never cancelled anywhere. If the plan only answers `shutdown` and returns on `exit`, pending debounced publishes can still write after the `shutdown` response.

*(c) Harness.* The plan's `[UNVERIFIED]` question about a `ProjectReference` is moot. `CliProcessTestRunner` finds the dll by repo path, not by reference, and the solution filter already builds the server. Adding a reference would only make sense if an in-process `Dispatch` test is wanted, and that would require `InternalsVisibleTo` on a second top-level-statements `Program` in the same test assembly.

**Evidence:**
- `src/DataGuard.LanguageServer/Program.cs:32-35` `if (pendingDiagnostics.Remove(key, out var previous)) { previous.Cancel(); previous.Dispose();`
- `Program.cs:85-90` `if (pendingDiagnostics.TryGetValue(...) && ReferenceEquals(current, pending)) { pendingDiagnostics.Remove(uri.AbsoluteUri); } pending.Dispose();`
- `Program.cs:8` `using var shutdown = new CancellationTokenSource();` is never `.Cancel()`ed.
- `tests/DataGuard.Core.Tests/CliProcessTestRunner.cs:20-22` `CliDllPath = Path.Combine(RepoRoot, "src", "DataGuard.Cli", "bin", …, "net9.0", "DataGuard.Cli.dll")`
- `DataGuard.CrossPlatform.slnf` lists `src\\DataGuard.LanguageServer\\DataGuard.LanguageServer.csproj`.

**Suggested fix:**
- Step C:
  - Guard `pendingDiagnostics` with a lock, or use `ConcurrentDictionary`. Remove only by reference (`TryRemove(KeyValuePair)`).
  - Never dispose a CTS in two places: the publisher owns disposal, the loop only calls `Cancel()` inside `try { } catch (ObjectDisposedException) { }`.
  - On `shutdown`, call `shutdown.Cancel()` before writing `{"result":null}`.
- Locate the server dll the same way as `CliDllPath` (`src/DataGuard.LanguageServer/bin/<cfg>/net9.0/DataGuard.LanguageServer.dll`) and drop the `ProjectReference` from Related Code Files.
- Write the didOpen and didChange pin as a single stdin write, so the supersede assertion is deterministic.
- Add a 3.V stress test: 200 rapid didChange messages, then the server is still alive and answers `shutdown`.

---

## Flow-trace tables

### Phase 1
| Claim | Trace | Verdict |
|---|---|---|
| Regex timeout is applied before any command type | `Program.cs:7` `RegexMatchTimeoutStartup.Apply();` → `:13` options → `:15-29` | Confirmed. The factory must be called after `:7`; the call stays in Program.cs. |
| Root is assembled inline in top-level statements | `Program.cs:15-29` | Confirmed |
| One `CommonOptions` instance is shared by all commands | `Program.cs:13`; `CommonOptions.cs:5` doc "one instance is created per process" | Confirmed. The factory must create it once per `Create()` call. Repeated test calls are safe (new instances). |
| `CommonOptions` is internal | `CommonOptions.cs:6` `internal sealed class` | Confirmed. The factory must be `internal` (works through IVT, `DataGuard.Cli.csproj:29`). |
| Commands expose `public static Create(CommonOptions)` | `AssessCommand.cs:12,14` (class is `internal static`) | Partial: the method is public but its class is internal. Fine with IVT. |
| Exit code = Environment.ExitCode or the invoke result | `Program.cs:46-47` | Confirmed. Unchanged by the factory. |
| Cancel → 130 | `Program.cs:33-43,49-51` | Confirmed |
| No test builds commands in-process today | grep `new CommonOptions(`: only `Program.cs:13` | Confirmed. This would be new territory. |
| Core.Tests references the CLI | `DataGuard.Core.Tests.csproj:15` | Confirmed |
| No test asserts Program.cs text ordering | `CliFollowUpEndToEndTests.cs:10` mentions Program.cs only in a doc comment | Confirmed. Extraction is safe. |
| Assess ack is printed inside the action | `AssessCommand.cs:60-61` | Confirmed. A parse error means no ack. |
| Assess accepts `--progress` | `AssessCommand.cs:45` (option list) | Confirmed |
| Host sends `--provider` to assess | `command-args.ts:83-84` | Confirmed |
| The old-CLI regex only covers 2 flags | `ide-safe-contract.ts:20` | Confirmed |
| The fixture under `src/DataGuard.VSCode/test/fixtures` is not packaged | `.vscodeignore` `test/**` | Confirmed. TS tests compile to `out/` (`tsconfig.json` rootDir `src`), so the fixture path is `../test/fixtures/...` from `__dirname`. |
| Linking the fixture into Core.Tests | Relative path `..\..\src\DataGuard.VSCode\test\fixtures\cli-contract.json` (2 levels up) | Feasible. `IsPackable=false`, so pack is irrelevant. |
| `env: undefined` inherits the shell credential | `extension.ts:459-461` | Confirmed. On Windows, `{...process.env}` keeps original key casing, so `delete env.DATAGUARD_CONNECTION_STRING` misses `DataGuard_Connection_String`; the helper should delete case-insensitively. |

### Phase 2
| Claim | Trace | Verdict |
|---|---|---|
| `Warning: no ground truth` goes to stderr | `ValidateCommand.cs:346` `Console.Error` | Confirmed |
| `UNEVALUATED:` lines go to stderr | `ValidateCommand.cs:309,341,398,480`; `ValidationRunner.cs:133` | Confirmed. Follow-up lines `  DGxxx id: reason` have no prefix, so they are dropped. |
| `ACQUISITION:` goes to stderr | `ValidateCommand.cs:469` | Confirmed |
| `Rule X not evaluated:` goes to stderr | `ValidateCommand.cs:364` | Confirmed |
| Some `Warning:` lines go to stdout | `SnapshotCommands.cs:291,330,450` `Console.WriteLine`; `ValidateCommand.cs:206` "Using snapshot" (stdout) | Partly refutes "all on stderr". Feed signals from both streams. |
| Summary counts exist | `ValidateCommand.cs:489-499` | Confirmed. Emitted last, and only on the full path; there is no Summary on the early exits at `:309/:341`. |
| SARIF is written before summary.json, before exit | `:419` sink → `:428` summary.json (non-fatal catch at `:444`) → `:463` unevaluated → `:478-486` exit code | Confirmed |
| Exit 1 can happen with SARIF already written | After `:419`, only `WriteUnevaluated`/ACQUISITION writes remain, and SARIF writes are atomic (`DiagnosticEmitter.cs:349-352` temp file). | Practically no. The artifact-presence heuristic is sound for validate. |
| Exit 1 can happen without SARIF | `:507-515` catch; uncaught pre-try exceptions go to System.CommandLine's default handler | Confirmed |
| Under ide-safe, exit 3 only comes from early returns | `:129,132` `allowSyntacticOnly`/`allowUnevaluated` `|| ideSafe` | Confirmed. Exit 3 means no SARIF. |
| Exit 4 means output refusal | `OutputSinks.cs:20` and also `AssessCommand.cs:168,190` | Refuted as exclusive. See F5. |
| Host drops policy lines | `ide-safe-contract.ts:60-85` | Confirmed |
| Summary is rendered only through Detail/Phase | `ide-safe-contract.ts:91-116` | Confirmed |
| The raw echo filter misses `Warning:` | `extension.ts:502-511` | Confirmed |
| Header says "Local assessment" for scan | `extension.ts:450` | Confirmed |
| Host already reads summary.json for every command | `extension.ts:538` (mis-indented block, runs for every command) | Confirmed |
| The first stderr line is seen before `close` | `extension.ts:679` `firstLine ??=`, `:709` `close`; `:713-731` decoder `end()` and flush of a partial line before return | Confirmed. Node emits `close` after the stdio streams end. |
| Signals fit into `ProgressTextState` | `extension.ts:656-660,689-690`: one state per stream | Needs one **shared** signals object across stdout and stderr, not per state. |
| Cancel/timeout paths are unaffected | `extension.ts:514-521` checked before the ack and the outcome | Confirmed, provided the classifier runs after `:521` |
| Final status is shown | `extension.ts:612-613` | Refuted. See F1. |
| Action commands exist | `package.json:73,81` `dataguard.refreshSnapshot`, `dataguard.configureConnection` | Confirmed |

### Phase 3
| Claim | Trace | Verdict |
|---|---|---|
| The loop branches only on initialize/didOpen/didChange/didClose | `LanguageServer/Program.cs:19-53` | Confirmed |
| Responses (no `method`) are skipped | `Program.cs:15-17` `continue` | Confirmed. Keep this. |
| Shutdown CTS is linked into publishes | `Program.cs:8,38` | Confirmed, but never cancelled. See F6. |
| Debounce is 150 ms, fire-and-forget | `Program.cs:6,40,60` | Confirmed |
| Header via `Console.Out` and body via the raw stream can interleave | `Program.cs:169-178` under `writeGate`; `Console.Out` autoflushes | No interleave and no deadlock (single non-reentrant gate). The order header → body → flush holds. |
| A dictionary race exists | `Program.cs:32-35` vs `:85-90` | Confirmed. See F6. |
| Oversize guard → non-zero exit | `Program.cs:147-150` throw, unhandled | Confirmed. The exit code is the runtime's crash code; assert `!= 0`, not a specific value. |
| Malformed JSON crashes the server | `Program.cs:13` `JsonDocument.Parse` with no try | Confirmed (not covered by the plan; add a pin) |
| Range = keyword token | `SqlClassifier.cs:199-203` `baseOffset + token.Start, token.Length` | Confirmed |
| Literal offset available | `SqlClassifier.cs:117` `ClassifySql(literal.Content, literal.ContentStart, …)` | Confirmed. Several statements in one literal would share one range. |
| `SqlClassification` is a class built in one place | `SqlClassifier.cs:199,582` | Confirmed. The Analyzers (`SqlCallSyntax.cs`) consume it, so additive properties are safe. |
| `SqlClassifierTests.cs` may be absent | It exists in `tests/DataGuard.Core.Tests` | Plan says "create if absent": modify instead |
| CLI dll located by path | `CliProcessTestRunner.cs:20-22` | Confirmed. The same works for the server without a ProjectReference. |
| slnf includes LanguageServer | `DataGuard.CrossPlatform.slnf` | Confirmed |
| Both publish with `source: "DataGuard"` | `Program.cs:74`; `extension.ts:798` | Confirmed |
| CLI location = literal line | `ProjectCSharpSqlSource.Passes.cs:180,323,390` | Refuted. It is the invocation/variable node. See F2. |
| Client has no middleware today | `extension.ts:296` `clientOptions = { documentSelector }` | Confirmed |
| Server dll integrity manifest | `extension.ts:305-312` sha256 of the server dll only | Phase 3 must regenerate `server/manifest.json` (not mentioned) |

### Phase 4
| Claim | Trace | Verdict |
|---|---|---|
| ide-safe ack before the policy check | `ValidateCommand.cs:77,80` | Confirmed |
| EF options are rejected | `IdeSafePolicy.cs:184-198` (7-arg overload; `:80` calls the 10-arg one) | Confirmed. Both overloads must change. |
| `ResolveEfSnapshotSource` runs before config | `ValidateCommand.cs:105-113` before `:119` | Confirmed. Too early for auto-detect. See F3. |
| `IdeSafePolicy.Apply` decides on the kept connection | `ValidateCommand.cs:139`; `IdeSafePolicy.cs:93-107` | Confirmed, after `:105` |
| Snapshot mode is forced | `IdeSafePolicy.cs:83-87`; `ValidateCommand.cs:162-166` | Confirmed |
| Default snapshot is discovered from cwd/config | `ValidateCommand.cs:199-207`; `CliConfigurationResolver.cs:296-312` | Confirmed (host cwd = workspace) |
| EF merge only when no EntityDescriptor | `ValidateCommand.cs:230-235` unconditional | Refuted. See F3. |
| Snapshot yields EntityDescriptor | `ContractAcquisition.cs:93-101` | Refuted: schema and procedures only |
| The gate counts EntityDescriptor | `ValidateCommand.cs:337` | Confirmed |
| EF parse is source-only | `EfModelSource.cs:252-253` ReadAllText → parser | Confirmed (no assembly load) |
| EF parse is bounded | `EfModelSource.cs:252`; parser has no cap | Refuted. See F4. |
| Unparseable snapshot is handled | `EfModelSource.cs:271-273` throw → `ValidateCommand.cs:507-515` exit 1 | Refuted. See F4. |
| `AutoDetectEFContext` is a dead stub | `Configuration.cs:43,207-209` | Confirmed |
| Multi-match logic exists | `ValidateCommand.cs:560-583` | Confirmed. The skip list lacks `node_modules`; there is no depth/file cap. |
| DG005 runs from EF alone | `NullableMismatchRule.cs:34-37` | Refuted |
| DG004 proves EF use | `ColumnShapeMatchRule.cs:93`; research row 2 | Refuted (it fires without EF) |
| Partial-parse diagnostics do not exit 3 in the IDE | `ValidateCommand.cs:132,478` `allowUnevaluated || ideSafe` | Confirmed |

### Phase 5
| Claim | Trace | Verdict |
|---|---|---|
| Summary reads only Error/Warning/Critical | `ProgressLineParser.cs:155-168` | Confirmed |
| Counts land in `ProgressReadResult` | `ProgressStreamReader.cs:89-95`; `ProgressReadModels.cs:40-46` | Confirmed. This **is** the existing summary model. |
| Explainer signature | `ExitCodeExplainer.cs:18` `(string, CliRunOutcome, bool)` | Plan's `Explain(int, RunSummary?)` does not match. See F5. |
| `[OK]` branch | `ExitCodeExplainer.cs:39` | Confirmed (plan cites 37-40) |
| Assess exit 4 text exists | `ExitCodeExplainer.cs:76-77` | Conflicts with the plan's exit-4 rewrite |
| Explainer call site | `DataGuardPackage.Publishing.cs:115-117` (only if HasSummary or exit≠0) | Confirmed. Exit 0 without a Summary prints no line. |
| Status text uses only error/warning counts | `DataGuardPackage.Publishing.cs:121-123` | Confirmed. It needs the new counts too. |
| PublishGate before SARIF | `DataGuardPackage.Publishing.cs:38-45` | Confirmed |
| SARIF published for exit 0/1/3 when the file exists | `DataGuardPackage.Publishing.cs:57-60` | Confirmed (exit code independent) |
| Missing SARIF keeps the previous list | `DataGuardPackage.Publishing.cs:61-64` | Confirmed |
| Raw `Warning:` line is not parsed | `ProgressStreamReader.cs:79` → parser JSON-only | Confirmed. A new raw-line flag is needed. |
| `InitCommand` is not registered | `DataGuardPackage.cs:84-92`; `Menus.vsct:66,87` | Confirmed |
| VSIX ships 2 analyzer dlls | `DataGuard.VisualStudio.csproj:103-110` | Confirmed |
| Analyzer dependencies are netstandard2.0 (loadable) | `DataGuard.Contracts.csproj:3`, `DataGuard.SqlClassification.csproj:3` | Confirmed. Adding them is feasible. |
| Roslyn 5.9.0 reference | `DataGuard.Analyzers.csproj:24` | Confirmed (Windows load still unverified) |
| Phase 3 classifier change reaches the VSIX analyzer | `DataGuard.Analyzers.csproj:33` references SqlClassification | Additive properties only. Phase 5 must package the post-Phase-3 dll. |
