# Red-team report (round 2, Visual Studio-focused plan, 4 phases)

# Round 2 review: Security Adversary and Fact Checker (VS-focused plan, 4 phases)

**Bottom line:** the Vault/KeyVault/AWS concern is handled correctly. The plan's real gap is everything else in a repo-controlled `.dataguard.yml` that now drives non-`--ide-safe` commands. That means arbitrary file overwrite, procedure execution on Oracle, and a consent prompt that promises the opposite. The Phase 1 load gate also cannot detect the regression it is built for.

## Findings (most severe first)

### F1. Live commands honour the repo yml settings that `--ide-safe` strips, under a consent the user gave for `--ide-safe`
- **Severity:** Critical
- **Location:** Phase 4, Requirements 2–3 ("consent gate (same key as validate)", argv `--config "<sln>\.dataguard.yml"`) and Risk Assessment (which covers only the Vault risk).
- **Flaw and failure scenario:**
  - `snapshot refresh` and `verify-shape` run without `IdeSafePolicy.Apply`, so every yml key takes effect. A cloned repo ships this yml:
    - `SnapshotFilePath: ..\..\..\Users\dev\.gitconfig`, or any absolute path.
    - `DefaultProvider: oracle` plus `Oracle: { DescribeRefCursors: true }`.
  - The developer has already approved Run Validation for this yml. That approval covers the live commands, because the same key is reused. They then click Refresh Snapshot:
    - **Arbitrary overwrite:** the CLI creates the target directory and overwrites the chosen file. It never calls `RefuseUnsafeOutput`.
    - **Procedure execution (Oracle):** it executes every REF CURSOR procedure in the developer's database, binding NULL inputs. The modal only says "refresh the schema snapshot".
  - **`verify-shape` reads an arbitrary file:** with `GroundTruthMode: Manual` and `ManualAssemblyPath`, it reads a repo-chosen file through MetadataLoadContext. No code runs, but there is no `RefuseAssemblyFromConfig` call, unlike `validate` and `baseline`.
  - **No resource clamp:** `MaxDegreeOfParallelism` and `ValidationTimeoutSeconds` are unbounded, because `ClampResourceBounds` runs only under `--ide-safe`.
  - **The consent prompt says the opposite.** It promises "IDE-safe mode … no database … connections" and "write paths from the config … are ignored".
- **Evidence:**
  - `src/DataGuard.Cli/Commands/SnapshotCommands.cs:75`: `var snapshotPath = config.SnapshotFilePath ?? CliConfigurationResolver.DefaultSnapshotFileName;`
  - `src/DataGuard.Core/Baseline/BaselineManager.cs:239` `Directory.CreateDirectory(directory);` and `:254` `File.Move(tempPath, _baselineFilePath, overwrite: true);`
  - `src/DataGuard.Core/Models/Configuration.cs:147-150`: "catalog extraction (validate/snapshot refresh with a connection) **executes** each procedure or function that returns a REF CURSOR"
  - `src/DataGuard.Oracle.Adapter/OracleCatalog.cs:352`: `var describer = config.DescribeRefCursors ? new RefCursorDescriber(connectionString) : null;`
  - `src/DataGuard.Cli/Services/ContractAcquisition.cs:109-111`: Manual source, with no refusal in `VerifyShapeCommand.cs:38-101`. `RefuseAssemblyFromConfig` callers are only `BaselineCommand.cs:55` and `ValidateCommand.cs:172`.
  - `src/DataGuard.VisualStudio/SolutionTrustGate.cs:85-86`: "the CLI runs in IDE-safe mode (--ide-safe) … connection strings and write paths from the config or environment are ignored"
  - `src/DataGuard.Cli/IdeSafePolicy.cs:118-127`: `AuditLogPath` and telemetry stripping, which is ide-safe only.
- **Suggested fix (Phase 4):**
  - Requirement 3: **never pass `--config`** to `snapshot refresh` or `verify-shape`. The CLI has no default config discovery (`CommonOptions.cs:12`, `ConfigLoader.cs:16-19`).
  - Add VS options **Live Provider** and **Live Schema**, passed as `--provider` and `--schema`. VS validates both: provider must be on the whitelist, schema must be an identifier.
  - Keep cwd = solution directory, so the default `.dataguard-snapshot.json` lands next to the yml where `FindDefaultSnapshot` looks (`CliConfigurationResolver.cs:296-313`).
  - Requirement 2: give the live commands their own consent key (`"live|" + ComputeConsentKey(...)`) and their own prompt text. That text must not repeat the ide-safe promises.
  - Tests After: add a CLI end-to-end test showing that a yml with `SnapshotFilePath: ../escape.json` and `Oracle.DescribeRefCursors: true` has no effect on the VS argv.
  - Alternative if `--config` must stay: add an `IdeSafePolicy.ApplyLive` step to the two commands. It would strip `SnapshotFilePath` (or confine it under the config directory), `Oracle.DescribeRefCursors`, `GroundTruthMode=Manual`/`ManualAssemblyPath`, the secret-store keys, `AuditLogPath` and telemetry, and apply `ClampResourceBounds`.

### F2. The Phase 1 gate cannot catch the regression, and the pin still breaks part of the declared VS range
- **Severity:** High
- **Location:** Phase 1, Key Insights / Requirements 2 and 4 / Success Criteria ("a Dependabot bump is blocked by ignore; if it slips, check-analyzer-loads.sh goes red").
- **Flaw and failure scenario:**
  1. **CI does not run Roslyn 4.12.** `global.json` rolls forward to the latest feature band and CI installs `9.0.x`, so the gate compiles with the newest 9.0.3xx SDK (Roslyn 4.14), not 9.0.100. An analyzer bumped to 4.13 or 4.14 would pass the gate and still raise CS9057 on SDK 9.0.1xx and VS 17.12.
  2. **The Dependabot ignore is major-only.** The grouped `nuget: ["*"]` PR will move 4.12.0 to 4.14.x as a minor update, and CI stays green.
  3. **The manifest range is wider than the fix.** It still targets `[17.0,19.0)`. VS 17.0–17.11 ship Roslyn below 4.12, so the "fixed" analyzer is still refused there. The plan's "VS 2022 17.x ships the Roslyn 4.x line" hides this.
- **Evidence:**
  - `global.json:4` `"rollForward": "latestFeature"`
  - `.github/workflows/ci.yml:17` `DOTNET_VERSION: "9.0.x"`
  - `.github/dependabot.yml:21-23` `groups: nuget: patterns: ["*"]`
  - `.github/dependabot.yml:25-26` `update-types: ["version-update:semver-major"]`
  - `src/DataGuard.VisualStudio/source.extension.vsixmanifest:12` `Version="[17.0,19.0)"`
- **Suggested fix (Phase 1):**
  - Requirement 2: the script writes a `global.json` with `{"sdk":{"version":"9.0.100","rollForward":"disable"}}` into the temp consumer.
  - The CI step installs 9.0.100 explicitly: a second `setup-dotnet` with `dotnet-version: 9.0.100`. The script also asserts that `csc` reports compiler `4.12`.
  - Requirement 4: ignore `Microsoft.CodeAnalysis.CSharp`, `.CSharp.Workspaces` and `.Analyzers` for **all** update types, with a comment pointing at the gate. Use `ignore` with no `update-types`, or `versions: [">4.12.0"]`. This must not block `DataGuard.Core`'s own 5.9 reference (`DataGuard.Core.csproj:23-24`). Either use a per-directory Dependabot entry, or accept that and say so.
  - Raise the manifest `InstallationTarget` lower bound to `17.12`, or pin Roslyn to the oldest supported VS line. Record the decision in the plan.

### F3. Phase 4 outcome rendering relies on output that does not exist; the modal's masking defeats the confirmation
- **Severity:** Medium
- **Location:** Phase 4, Requirement 4 and Tests After (`ConnectionHostMasker`); Phase 2 Key Insights (stdout citation).
- **Flaw and failure scenario:**
  - **No "Snapshot written" line exists, and stdout is discarded anyway.** Requirement 4 takes the path "from stdout `Snapshot written …` line". The CLI prints no such line; it prints only `Snapshot refreshed with N violations`. VS throws stdout away. So VS will always print the default name, even when the CLI wrote elsewhere (see F1).
  - **The masked host cannot tell prod from dev.** The modal is the only check before VS connects with the developer's own credential. Masking `db-prod.corp.example.com` and `db-dev.corp.example.com` gives `db-***.com` for both. The host belongs to the user, so masking it from that user protects nothing and removes the point of confirming. Mask only in logs.
  - **One test expectation is wrong.** The ported algorithm turns `pg.local` into `p***.local`, not `pg***.local` (first label of 3 characters or fewer keeps 1 character).
- **Evidence:**
  - `src/DataGuard.Cli/Commands/SnapshotCommands.cs:97` `Console.WriteLine($"Snapshot refreshed with {baseline.Violations.Count} violations");` (no "Snapshot written" anywhere in `src/`).
  - `src/DataGuard.VisualStudio/ProcessTerminator.cs:142-144`: "Drain without retaining potentially sensitive CLI output."
  - `src/DataGuard.VisualStudio/CliRunSession.cs:68` `var stdoutDrainTask = ProcessTerminator.DrainAsync(process.StandardOutput);`
  - `src/DataGuard.VSCode/src/live-database-confirmation.ts:62` ``const head = first.length > 3 ? `${first.slice(0, 3)}***` : `${first.slice(0, 1)}***`;``
- **Suggested fix (Phase 4):**
  - Requirement 4: `[OK] Snapshot refreshed: <solutionDir>\.dataguard-snapshot.json (verify with File.Exists + LastWriteTimeUtc > run start)`. This follows from F1 (no `--config`, cwd = solution directory). Otherwise print `[ERROR] CLI exited 0 but no snapshot was written in the solution directory`.
  - The modal shows the full host and port, never user or password. Redact the host only in `DataGuardLogger`.
  - Fix the test: `postgresql://u:p@pg.local/db` gives host `pg.local`, shown unmasked in the modal.

### F4. `DataGuardLogger.Redact` gaps on the new `[WARN]` echo path, and the plan's test conflicts with an existing pin
- **Severity:** Medium
- **Location:** Phase 2, Requirement 2 and Tests After ("Redaction: … `Password=x;User Id=sa` … both values masked").
- **Flaw and failure scenario:** `UNEVALUATED: contract acquisition failed: …` and `Snapshot refresh failed: {ex.Message}` carry driver messages and get the new `[WARN]` echo. The regex is `(password|pwd|passwd|secret|token|…)\s*[:=]\s*[^\s;,]{1,512}`, which leaves these gaps:
  - **`User Id=` / `UID=` / `User=` / `Username=`:** never redacted. An existing test pins `User Id=sa` as kept, so the plan's "both values masked" test will fail against that pin.
  - **URI `scheme://user:pw@host`:** not matched at all. The password leaks.
  - **Quoted or braced values:** `Password="a b;c"`, `Password='p;w'` and SqlClient `Password={p;w}` are cut at the first space, `;` or `,`. The rest of the secret leaks.
  - **Oracle EZConnect `user/pw@host`:** not matched.
  - **Oracle `Password=` / `PWD=`:** covered (case-insensitive).
- **Evidence:**
  - `src/DataGuard.VisualStudio/DataGuardLogger.cs:28` (the regex above)
  - `src/DataGuard.VisualStudio/DataGuardLogger.cs:25`, a comment claiming coverage "incl. "User Id=x;Password=y"" that applies only to the Password part.
  - `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs:157` `…Should().Be("Server=x;User Id=sa;Password=[REDACTED];Encrypt=true");`
  - `src/DataGuard.VisualStudio/ProgressLineParser.cs:61-63` (single echo sink).
- **Suggested fix (Phase 2):**
  - Add Requirement 6, extending the regex:
    - (a) `(user\s*id|uid|user(name)?)\s*=\s*[^;]{1,512}`;
    - (b) a value alternative `"[^"]{0,512}"|'[^']{0,512}'|\{[^}]{0,512}\}` before `[^\s;,]{1,512}`;
    - (c) `[a-z][a-z0-9+.-]*://[^:/@\s]+:[^@\s]{1,512}@` → `scheme://[REDACTED]@`;
    - (d) `\b[\w$#]+/[^@\s/]{1,512}@` → `[REDACTED]@`.
  - Update the pin at `CliArgumentBuilderTests.cs:157` to `User Id=[REDACTED]` in Step 3.T as an intended flip.
  - Add one test per form.

### F5. Phase 3 locator: check-then-reopen race, an unbounded read, and containment and escaping gaps
- **Severity:** Medium
- **Location:** Phase 3, Requirement 1 (size, regular-file and containment checks), Requirement 2 (escaping), Architecture (`:230` calls `Extract…` with the path).
- **Flaw and failure scenario:**
  - **The size check does not bound the read.** The locator checks `FileInfo.Length`, then `ValidateCommand` calls the **path-based** `ExtractFromModelSnapshotWithDiagnosticsAsync`. That reopens the file and does an unbounded `File.ReadAllTextAsync`. Swapping the file between check and read, or using a FIFO/device whose `Length` reads 0, gets around the 1 MiB check.
  - **The regular-file test is weak.** `File.ResolveLinkTarget(path,false) is null` is also null for hard links, FIFOs and device files.
  - **Containment details are unspecified.** Without a separator-terminated root and a case-insensitive comparison on Windows, `C:\Sln` would accept `C:\Sln-evil\…`. `\\?\` and UNC roots also need normalising with `Path.GetFullPath` first.
  - **Escaping misses a cross-platform injection vector.** Windows file names cannot contain characters 0x01–0x1F, so on VS the path vector is mostly moot. The **yml unknown-key echo** is cross-platform: a double-quoted YAML key such as `"x\nWarning: no ground truth"` is printed raw and forges a line start. The plan escapes paths only.
  - **Parser scope:** confirmed parse-only. `CSharpSyntaxTree.ParseText` uses the default (non-script) options, so there is no `#load`/`#r` processing and no evaluation (`ModelSnapshotCSharpParser.cs:52`).
- **Evidence:**
  - `src/DataGuard.Core/Sources/EfModelSource.cs:252` `var source = await File.ReadAllTextAsync(snapshotFilePath, cancellationToken).ConfigureAwait(false);`
  - `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs:49` `if (source.Length > options.MaximumSourceBytes)`, which runs after the full read and counts chars, not bytes.
  - `src/DataGuard.Cli/Services/ConfigLoader.cs:58` `Console.Error.WriteLine($"Warning: unknown configuration keys: {string.Join(", ", unknownKeys)}");`
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:469` (existing `ACQUISITION: {diagnostic.Path}` print, which also needs the helper).
- **Suggested fix (Phase 3):**
  - Requirement 1: the locator opens the file once (`new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`).
    - Reject if `File.GetAttributes(path)` has `ReparsePoint|Device`.
    - Read at most 1 MiB + 1 bytes through the stream.
    - Return the **source text**; `ValidateCommand` then calls `EfModelSource.ParseModelSnapshotWithDiagnostics(source, config, relativePath)` (`EfModelSource.cs:264`).
  - Containment: `full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? OrdinalIgnoreCase : Ordinal)`.
  - Requirement 2: apply `PathText.EscapeControlCharacters` to `ValidateCommand.cs:469` and to the `ConfigLoader.cs:25,29,44,53,58` echoes.
  - Add a test with a yml key containing `\n`.

### F6. Phase 1 duplicates an existing analyzer-consumer script whose check passes even under CS9057
- **Severity:** Medium
- **Location:** Phase 1, Architecture / Related Code Files ("Create: `scripts/check-analyzer-loads.sh`").
- **Flaw and failure scenario:**
  - `scripts/verify-analyzer-packaging.sh` already builds a temp consumer against the packed analyzer.
  - It is not wired into any workflow.
  - Its "load" assertion only checks that `csc` received `/analyzer:…DataGuard.Analyzers.dll`. That is still true under CS9057, which is exactly why RC1 went unnoticed.
  - A second consumer script adds maintenance. The old one keeps giving false assurance, and the docs journal cites it as a passed gate.
- **Evidence:**
  - `scripts/verify-analyzer-packaging.sh:91-92`: "The layout change must not stop the compiler from loading the analyzer: csc gets it as /analyzer:" → `check "csc loads …" "$(grep -E '/analyzer:.*…DataGuard\.Analyzers\.dll' …)" 1`
  - `grep verify-analyzer-packaging .github/` returns no hits.
  - `scripts/check-analyzer-loads.sh` does not exist (confirmed).
- **Suggested fix (Phase 1):** instead of a new script, extend `verify-analyzer-packaging.sh`:
  - add the `SELECT *` Dapper source;
  - assert `grep -c 'warning CS9057'` = 0 and `grep -c 'DG017'` ≥ 1;
  - add the pinned-SDK `global.json` from F2.

  Call it from `build-and-test` after "Build solution" (ci.yml:80) with `dotnet pack … -o $RUNNER_TEMP/nupkg`. Note that the script needs nuget.org for `System.Composition.Hosting`. The licence gate is unaffected: 4.12.0 Common and Analyzers 3.11.0 are MIT, and Humanizer.Core 2.14.1, System.Composition and System.IO.Pipelines are already in the graph via 5.9 (`src/DataGuard.CodeFixes/packages.lock.json`).

## Fact-check tables

### plan.md

| Claim | file:line | Verdict |
|---|---|---|
| Analyzer on CSharp 5.9.0 | `DataGuard.Analyzers.csproj:24-25` | OK |
| CodeFixes Workspaces 5.9.0 | `DataGuard.CodeFixes.csproj:16` | OK |
| VSIX `[17.0,19.0)` | `source.extension.vsixmanifest:12-19` | OK (but see F2) |
| VSIX bundles only Analyzers + CodeFixes | `DataGuard.VisualStudio.csproj:103-110` | OK |
| NuGet bundles Contracts/SqlClassification | `DataGuard.Analyzers.csproj:38-43` | OK (items at :40-45) |
| No-ground-truth warning | `ValidateCommand.cs:346` | OK |
| Summary counts | `ValidateCommand.cs:494-499` | OK |
| Validate exit 4 at `OutputSinks.cs:307` | — | WRONG → `src/DataGuard.Cli/Services/OutputSinks.cs:20` (file has 90 lines; round-1 correction not applied in the RC2 row) |
| Status bar `Publishing.cs:118-120` | — | WRONG → `DataGuardPackage.Publishing.cs:121-123` |
| `--connection-env` at `CommonOptions.cs:112` | — | WRONG → `src/DataGuard.Cli/Commands/CommonOptions.cs:60` |
| Resolution order | `CliConfigurationResolver.cs:95-166` | OK |
| `IdeSafePolicy` rejects `--ef-*` | `IdeSafePolicy.cs:188-200` | OK |
| `AutoDetectEFContext` stub | `Configuration.cs:206-209` | OK |
| Init declared, not registered | `Menus.vsct:66-72,87`; `DataGuardPackage.cs:84-92` | OK |
| VS sets only `DOTNET_ROLL_FORWARD` | `CliArgumentBuilder.cs:133` | OK |

### Phase 1

| Claim | file:line | Verdict |
|---|---|---|
| Analyzers.Tests 5.9 | `tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj:26,30` | OK |
| CodeFixes.Tests 5.9 | `tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj:27-28` | OK |
| "Analyzers 3.11.0 in the four projects" | — | WRONG: only `DataGuard.Analyzers.csproj:25` references `Microsoft.CodeAnalysis.Analyzers`; the other three get it transitively (4.12 → 3.3.4) |
| "5.9 reference is in four projects" | — | INCOMPLETE: also `DataGuard.Core.csproj:23-24` and `BinaryCompatibilityFixture.csproj:13`, which matters for the Dependabot ignore |
| Dependabot ignores only `Microsoft.Build.*` and `FluentAssertions` (`:24-30`) | `.github/dependabot.yml:24-33` | WRONG: `MessagePack` is also ignored (`:31-33`) |
| `CopyLocalLockFileAssemblies` | `DataGuard.Analyzers.csproj:17` | OK |
| `System.Composition.Hosting 10.0.12` | `DataGuard.Analyzers.csproj:26` | OK |
| Analyzer uses Contracts/SqlClassification | `ContractValidationAnalyzer.cs:12`; `SqlCallSyntax.cs:11` | OK |
| `assert-vsix` entries | `scripts/assert-vsix.ps1:22-29` | OK |
| Packaging test returns early | `VsixAnalyzerPackagingTests.cs:47-51` | OK |
| xunit 2.9.3 | `DataGuard.VisualStudio.Tests.csproj:20` | OK |
| DG001 says "dataguard check" | `DiagnosticDescriptors.cs:89`; `UnvalidatedSqlCallGeneratorTests.cs:41` | OK (also `UnvalidatedSqlCallGenerator.cs:16` doc comment, not listed) |
| `IncludeNuGetDepsInVsix` pattern | `DataGuard.VisualStudio.csproj:53-66` | OK |
| `check-analyzer-loads.sh` absent | — | OK (but `verify-analyzer-packaging.sh` exists, see F6) |
| Gate "with the repo SDK" means Roslyn 4.12 | `global.json:4`; `ci.yml:17` | WRONG: CI rolls forward to 9.0.x latest |
| VS jobs | `ci.yml:253-326` | OK |

### Phase 2

| Claim | file:line | Verdict |
|---|---|---|
| `UNEVALUATED:` lines | `ValidateCommand.cs:309,341,398,480` | OK |
| `ACQUISITION:` | `ValidateCommand.cs:469` | OK |
| `Rule … not evaluated` | `ValidateCommand.cs:364` | OK |
| Ide-safe allows syntactic-only | `ValidateCommand.cs:129` | OK |
| Assess Summary `CriticalCount`/`ToolErrorCount` | `AssessCommand.cs:171-179` | OK (:176-179) |
| Assess exit 4 | `AssessCommand.cs:168,190` | OK |
| Summary branch | `ProgressLineParser.cs:155-168` | OK |
| Echo `[DataGuard CLI]` | `ProgressLineParser.cs:62-66` | OK (:59-63) |
| Counts set only from `IsSummary` | `ProgressStreamReader.cs:89-95` | OK |
| `ProgressReadResult` | `ProgressReadModels.cs:40-62` | OK |
| `Explain` signature | `ExitCodeExplainer.cs:18` | OK |
| `[OK]` / validate cases 1-3 / assess 4 | `ExitCodeExplainer.cs:37-40, 51-65, 76-77` | OK. Note `:32-35` returns `[WARN] completed with warnings` first; the new branch must precede it |
| 14 callers | grep `ExitCodeExplainer.Explain(` | OK (14) |
| Stdout drained at `ProcessTerminator.cs:110-124` | — | WRONG → `ProcessTerminator.cs:137-151`; call at `CliRunSession.cs:68` |
| Single call site | `DataGuardPackage.Publishing.cs:117` | OK |
| `overview.md:10` stale text | `overview.md:10` | OK |
| `SpoofLine` pattern | `ProgressStreamReaderTests.cs:18` | OK |
| `User Id` redaction "check first" | `CliArgumentBuilderTests.cs:157` | Pinned as NOT redacted (F4) |

### Phase 3

| Claim | file:line | Verdict |
|---|---|---|
| Source parse | `EfModelSource.cs:241-253` | OK |
| Read before size check | `EfModelSource.cs:252`; `ModelSnapshotCSharpParser.cs:13,49` | OK |
| Zero entities throws | `EfModelSource.cs:270-273` | OK |
| Catch → exit 1 | `ValidateCommand.cs:507-515` | OK |
| `ResolveEfSnapshotSource` | `ValidateCommand.cs:527-593`, caller `:105` | OK |
| Explicit path not confined | `ValidateCommand.cs:542-545` | OK |
| Unbounded enumerate | `ValidateCommand.cs:568-572` | OK |
| Bounded walker pattern | `AutoDetectionEngine.cs:487-545` (`AttributesToSkip` :523, pruned :491-493) | OK |
| `IdeSafePolicy.Apply` | `ValidateCommand.cs:139` | OK |
| `FindDefaultSnapshot` | `ValidateCommand.cs:199-207` | OK |
| EF merge unconditional | `ValidateCommand.cs:230-235` | OK |
| Ground-truth gate | `ValidateCommand.cs:337` | OK |
| Snapshot yields schema + procedures | `ContractAcquisition.cs:93-101` (snapshot branch :85-108) | OK |
| DG004 raw branch | `ColumnShapeMatchRule.cs:93` | OK |
| DG005 needs schema | `NullableMismatchRule.cs:34-37` | OK |
| Policy tests | `IdeSafePolicyTests.cs:85-110` | OK |
| `AutoDetectEFContext` default true | `Configuration.cs:43` | OK |
| README claims `init` writes the snapshot | `README.md:20`; `README.vi.md:24` | OK |
| Explicit-option exit 2 pins | `CliExitCodeTests.cs:355-369` | OK |
| "fixture `None` items, same as `Fixtures/CSharpSql`" | — | WRONG: `DataGuard.Core.Tests.csproj` has no `None`/Fixtures items; fixtures are found by walking up to the source tree (`ProjectCSharpSqlSourceExtractorTests.cs:366-373`) |
| `internal` locator testable | `DataGuard.Cli.csproj:29` `InternalsVisibleTo DataGuard.Core.Tests` | OK |
| Parser has no `#load` / evaluation | `ModelSnapshotCSharpParser.cs:52` | OK |

### Phase 4

| Claim | file:line | Verdict |
|---|---|---|
| `snapshot refresh` options | `SnapshotCommands.cs:29-32` | OK |
| No connection → exit 3 | `SnapshotCommands.cs:54-59` | OK |
| Failure → exit 1 | `SnapshotCommands.cs:107-115` | OK |
| Writes `SnapshotFilePath ??` default relative to cwd | `SnapshotCommands.cs:75` | OK, and that is the F1 risk |
| `verify-shape` options / exit 2 / exit 3 | `VerifyShapeCommand.cs:33-36, 56-61, 95-100` | OK |
| JSON shape, `violationsCount` 0 | `VerifyShapeCommand.cs:190-243` | OK |
| `--connection-env` unset → exit 2 **before** secret stores | `CliConfigurationResolver.cs:113-124` (`return Failed` at :121-122 precedes `secureSource` at :138-145); `ConfigLoader.cs:137-141` exit 2 | OK. `CreateCredentialProviderSource` (`ConfigLoader.cs:111-117`) only builds a lambda; no store or HTTP call happens |
| `--connection-env` beats config `ConnectionString` / `AllowPlaintextConfigFallback` | `CliConfigurationResolver.cs:113-124` vs `:147-163` | OK |
| Provider precedence | `CliConfigurationResolver.cs:71-79` | OK |
| Missing `--config` → exit 2 | `ConfigLoader.cs:29-31` | OK |
| `PublishGate.Decide` | `ProgressReadModels.cs:80-90` | OK |
| Status strings at `CliRunSession.cs:67` | — | WRONG → `CliRunSession.cs:65` |
| Consent gate | `DataGuardPackage.Consent.cs:45-85` | OK |
| Modal | `DataGuardPackage.Consent.cs:63-69` | OK |
| Build hook calls only validate | `DataGuardPackage.Commands.cs:28-36` | OK |
| `InfoBarManager` exec | `UI/InfoBarManager.cs:136`; tests `:68,90` | OK |
| Mask examples | `live-database-confirmation.ts:55-64` | WRONG for URI: `pg.local` → `p***.local` |
| "Snapshot written …" stdout line | — | WRONG: no such line; stdout is discarded (F3) |
| Repo yml can't exfiltrate via `AuditLogPath`/`EnableTelemetry` on live commands | `CliConfigurationResolver.cs:205-207` (audit only inside the uncalled secure source); telemetry consumed only by `PublicApiSurface.cs:71` | OK (not reached), but `SnapshotFilePath`/`DescribeRefCursors` are reached (F1) |
