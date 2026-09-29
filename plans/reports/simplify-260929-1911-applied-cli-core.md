# Simplify — applied (CLI/Core tree)

Branch `feat/vs-extension-hardening` @ 8a2df13, uncommitted. Ownership: `src/DataGuard.Cli`, `src/DataGuard.Core`, `tests/DataGuard.Core.Tests`. Behaviour and every stderr string unchanged; the pre-existing CodeQL fixture edits in `IdeSafePolicyTests.cs` / `PhantomIdentifierRuleRegexTests.cs` kept. No commit, no lock-file edits (the two `packages.lock.json` still show `M` from before this session; `git diff --numstat` on them is empty — eol-only).

## Per finding

| Finding | Status | What changed | Lines |
|---|---|---|---|
| Altitude #2 (`ideSafe` on `ProviderRuleCatalog.Get`) | applied | `ideSafe` param + H1 comment deleted; catalog keeps its one switch (`connectionString == null` → offline `LiveSqlShapeValidationRule`). `GetRulesForProvider(provider, string? connectionString, progress)` — connection now required, no default. | `ProviderRuleCatalog.cs` −5/+2 |
| Altitude #3 (policy owns the "no live rule" fact) | applied | `IdeSafePolicy.Result(Configuration, Suppressed, bool KeptEnvironmentConnection, string? RulesConnectionString)`; `RulesConnectionString` is always null under ide-safe (doc'd as review H1). `ValidateContractsAsync(contracts, config, provider, string? rulesConnectionString, ct, skipRuleIds, progress)` — required positional before `ct` so the three non-ide-safe callers (`snapshot refresh`, `--legacy-violation-diff`, `RunValidationAsync`) pass `config.ConnectionString` explicitly; `validate` hoists `var rulesConnectionString = config.ConnectionString` before the ide-safe block and overwrites it with `safe.RulesConnectionString` (verified: nothing mutates `config.ConnectionString` between `Apply` and the rule run). | `Program.cs` −9/+13, `IdeSafePolicy.cs` (with #3 below) −11/+22 |
| Simplification #3 (kept-connection sentinel in `Suppressed`) | applied | `KeptEnvironmentConnectionNote` no longer enters `Suppressed`; `FormatSuppressionLine` drops its `Where`, `WriteReport` becomes `Suppressed.Count > 0` / `KeptEnvironmentConnection`. `LiveShapeRuleDisabledNote` stays in `Suppressed` at its old position, so `ide-safe: suppressed …; live SQL shape rule disabled (use verify-shape)` and the separate `ide-safe: kept environment connection (--allow-env-connection)` line are byte-identical (pinned by `Validate_IdeSafeAllowEnvConnection_ReportsKeptEnvironmentConnection` and `WriteReport_KeptConnection_WritesSuppressionLineThenKeptLine`, both unchanged and green). Legacy `Apply(config, bool environmentConnectionPresent, …)` signature untouched (there is a single `Apply` with optional params; Simplification #12 deliberately not applied). H1 pin moved to `IdeSafePolicyTests`: matrix asserts `KeptEnvironmentConnection` + `RulesConnectionString == null` in both branches; `Apply_KeptEnvConnection_ReportsLiveShapeRuleDisabled` also pins `Configuration.ConnectionString == env && RulesConnectionString == null`. | 3 filter sites removed |
| Reuse #7 (`MaxQueueSize = 100_000`) | applied | `IdeSafePolicy.MaxQueueSize = ConcurrentValidationEngine.DefaultMaxViolationQueueSize` (already `public const`; zero Core edits). | 1 literal |
| Reuse #1 / Efficiency #8 (duplicate CLI runner) | applied | New `tests/DataGuard.Core.Tests/CliProcessTestRunner.cs`: repo-root + `CliDllPath` discovery, working dir, stdin, `DATAGUARD_CONNECTION_STRING` control, concurrent stdout/stderr drain, 60 s guard; returns `CliRunResult(ExitCode, Stdout, Stderr)`. `CliExitCodeTests` keeps its `RunCli`/`RunCliInDirectory` shims and joins `Stdout + Stderr` in the caller (assertion text unchanged). | `CliExitCodeTests.cs` −50/+3 (506 → 459) |
| Reuse #2 / Simplification #4 (duplicate `mklink` helper + attributes) | applied | New `DirectoryLinkTestHelper.cs` = superset `CreateDirectoryLink` (symlink off Windows) + `WindowsFactAttribute`/`WindowsTheoryAttribute`. `SafeWritablePathTests` calls it; own copy and `using System.Diagnostics` removed. | `SafeWritablePathTests.cs` −28/+4 (122 → 98) |
| Simplification #8 (four hand-rolled junction setups) | applied | New `IDisposable` `WorkspaceWithJunction(prefix, junctionInsideWorkspace, targetSubdirectory?)` + `WriteWorkspaceFile`; replaces the five try/finally blocks (incl. `Assess_OutputUnderJunction_WritesSarif`). Fixture variation preserved per test: target subdir `sub`/`out`/none, workspace file Repo.cs ×3 / App.csproj ×1 / none (oracle-check). `Dispose` deletes the link non-recursively before the recursive root delete (`Directory.Exists` guard is defensive only: a constructor failure never reaches `Dispose`, same leak profile as before). Oracle-check/verify-shape kept as separate facts (different extra assertions), per the finding's own caveat. | 5 try/finally → 5 `using` |
| Efficiency #8 (442-line `IdeSafeEndToEndTests`, serial) | applied | Split by fixture into `IdeSafeHandshakeEndToEndTests` (6 facts), `IdeSafeWritePathEndToEndTests` (4 facts + 1 theory), `CliBaselineSuppressionEndToEndTests` (1 fact); shared constants/`RunCli`/`Lines`/hostile fixture in `IdeSafeEndToEndSupport` (`using static`). Method names and assertions unchanged; xUnit now runs the three classes in parallel. | 442 → 127 + 99 + 61 (+32 support) |

Skipped in this tree (out of scope / instructed): Altitude #1 (shared `AtomicFileWriter` — behaviour change, owner decision pending), Efficiency #3 (defence-in-depth check in `WriteTextAtomicallyAsync` kept by instruction), Reuse #8 (`SafeWritablePath` stays in Cli), Simplification #12 (`environmentConnectionPresent` kept by instruction). Everything else in the four reports targets VS/VSCode/workflow files outside this ownership.

## Test inventory

`ProviderRuleCatalogTests.Get_IdeSafeWithKeptConnection_RegistersConnectionlessLiveShapeRule` → renamed/rewritten 1:1 as `Get_WithoutConnection_RegistersConnectionlessLiveShapeRule` (`Get("sqlserver", connectionString: null)`), same two assertions. The altitude report's claim that a `Get_WithoutConnection…` test already existed was wrong (only two `HasLiveConnection` asserts in the tree), so nothing was dropped: count stays **898**. No new test methods; the H1 pin was folded into existing policy tests.

## File line counts after split (`tests/DataGuard.Core.Tests/`)

| File | Lines |
|---|---|
| CliProcessTestRunner.cs (new) | 82 |
| DirectoryLinkTestHelper.cs (new) | 55 |
| WorkspaceWithJunction.cs (new) | 55 |
| IdeSafeEndToEndSupport.cs (new) | 32 |
| IdeSafeHandshakeEndToEndTests.cs (new) | 127 |
| IdeSafeWritePathEndToEndTests.cs (new) | 99 |
| CliBaselineSuppressionEndToEndTests.cs (new) | 61 |
| IdeSafeEndToEndTests.cs | deleted (was 442) |
| CliExitCodeTests.cs | 459 (was 506) |
| SafeWritablePathTests.cs | 98 (was 122) |

Source: `IdeSafePolicy.cs` +22/−11, `Program.cs` +13/−9, `ProviderRuleCatalog.cs` +2/−5. Test diff (excluding new files) +73/−121. All touched files LF (byte-checked).

## Gates

```
dotnet build DataGuard.sln -c Release -m:1        → 0 Warning(s), 0 Error(s)  (52 s)
dotnet test tests/DataGuard.Core.Tests -c Release  → Passed! 898 / 898, Skipped 0  (50 s)
dotnet test tests/DataGuard.GoldenCorpus.Tests -c Release → Passed! 28 / 28
dotnet format whitespace DataGuard.sln --verify-no-changes → exit 0
```

Two StyleCop errors surfaced and were fixed on the way (CS1587: XML doc on a top-level local function → plain comment; SA1604/SA1642: constructor summary on `WorkspaceWithJunction`). No CS2012 lock encountered despite the concurrent VS build.

## Unresolved questions

1. `IdeSafePolicy.Result` gained two positional members; it is `public` in the CLI assembly. No in-repo consumer other than `Program.cs`/tests, but an external caller constructing `Result` directly would break (none known).

**Status:** DONE
**Summary:** All requested findings applied in the CLI/Core tree with stderr output byte-identical; e2e suite split into three parallel classes over shared runner/link/junction helpers; build, 898 + 28 tests and format gate all green.
**Concerns/Blockers:** None blocking. `ValidateContractsAsync` now requires the rules connection explicitly (no default) — intentional so a future caller cannot silently go connection-less.
