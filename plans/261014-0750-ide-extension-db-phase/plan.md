---
title: "Visual Studio extension: make the in-editor analyzer load, report database ground-truth state truthfully, and add the database-phase commands"
description: "Root-cause and fix why DataGuard for Visual Studio misbehaves for .NET developers in the database phase: the bundled Roslyn analyzer is refused by VS 2022 / .NET 9 compilers (no squiggles at all), validate runs with no ground truth are reported as [OK], EF Core ModelSnapshot ground truth is unreachable under --ide-safe, and VS has no snapshot/verify-shape commands at all."
status: completed
priority: P1
effort: 14h
branch: fix/ide-extension-db-phase-plan
tags: [visual-studio, vsix, roslyn-analyzer, cli-contract, database, tdd]
blockedBy: []
blocks: []
created: 2026-10-14
scope: "Owner direction 2026-10-14: focus on src/DataGuard.VisualStudio. VS Code / LanguageServer findings are recorded under 'Deferred' and not planned here."
---

# Visual Studio extension: analyzer load, truthful run state, database-phase commands

## Overview

`src/DataGuard.VisualStudio` (VSIX, net472) drives the bundled self-contained CLI `cli\dataguard.exe` (`DataGuard.VisualStudio.csproj:114-135`) and ships the Roslyn analyzers for in-editor squiggles. All suites are green (`tests/DataGuard.VisualStudio.Tests` on `windows-latest`; .NET cross-platform suites 1997 pass / 5 skip; the VS test project also **compiles** on Linux with `-p:VSSDKBuildToolsAutoSetup=false`, verified in this session, but cannot execute here: no mono), yet the product misbehaves. Reproduced in this session:

| # | Symptom a VS developer sees | Root cause (evidence) | Fixed in |
|---|---|---|---|
| RC1 | **No DataGuard squiggles ever appear in the editor** (DG001/DG017/DG097…), on VS 2022 and on any `dotnet build` with the .NET 9 SDK. | The analyzer is compiled against `Microsoft.CodeAnalysis.CSharp 5.9.0` (`src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:24-25`, same in `DataGuard.CodeFixes.csproj:16`). Roslyn refuses analyzers newer than itself: compiling a consumer with SDK 9.0.100 (Roslyn 4.12) gives `warning CS9057: The analyzer assembly … references version '5.9.0.0' of the compiler, which is newer than the currently running version '4.12.0.0'` and **no DG diagnostics**; the same analyzer rebuilt against 4.12.0 emits DG001 ×2 + DG017 on the same file (`research/analyzer-roslyn-version.md`). VS 2022 17.x ships the Roslyn 4.x line; the VSIX manifest targets `[17.0,19.0)` (`source.extension.vsixmanifest:11-19`). Additionally the VSIX bundles only `DataGuard.Analyzers.dll` + `DataGuard.CodeFixes.dll` (`DataGuard.VisualStudio.csproj:103-110`) while the analyzer needs `DataGuard.Contracts.dll` and `DataGuard.SqlClassification.dll` at load time — the NuGet package bundles them for exactly that reason (`DataGuard.Analyzers.csproj:38-43`), the VSIX does not; `scripts/assert-vsix.ps1:22-29` does not check them. | Phase 1 |
| RC2 | **A validate run that compared nothing prints `[OK] No issues found.`** The developer believes the database contract was checked. | The CLI says so on stderr: `Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` (`src/DataGuard.Cli/Commands/ValidateCommand.cs:346`), `UNEVALUATED:` / `ACQUISITION:` / `Rule X not evaluated:` lines, and the `Summary` event carries `UnavailableRuleCount`, `UnevaluatedContractCount`, `AcquisitionDiagnosticCount` (`ValidateCommand.cs:494-499`). VS reads only `ErrorCount/WarningCount/CriticalCount` (`ProgressLineParser.cs:155-168`), echoes the warning as an unremarkable `[DataGuard CLI]` line (`:62-66`) and `ExitCodeExplainer.Explain` returns `[OK] No issues found.` for exit 0 (`ExitCodeExplainer.cs:37-40`); the status bar shows `0 errors, 0 warnings` (`DataGuardPackage.Publishing.cs:121-123`). The only hint exists when `.dataguard.yml` is absent (`DataGuardPackage.Commands.cs:119-122`); with a yml but no snapshot there is none. Validate exit 4 (unsafe output path, `src/DataGuard.Cli/Services/OutputSinks.cs:12-20`) is "Unexpected exit code 4." | Phase 2 |
| RC3 | **EF Core projects never get entity contracts from the IDE.** Without a committed `.dataguard-snapshot.json`, DG002/003/004/005/101 cannot run; VS has no way to supply a connection. | Under `--ide-safe` the CLI rejects `--ef-snapshot/--ef-project/--ef-context` (`src/DataGuard.Cli/IdeSafePolicy.cs:188-200`) and forces Snapshot mode (`:84-88`); Manual (attribute) mode needs assembly loading and is also forbidden; `AutoDetectEFContext` is an empty stub (`src/DataGuard.Core/Models/Configuration.cs:206-209`). VS passes neither `--provider` nor any EF option (`CliArgumentBuilder.cs:88-96`) and sets no env var but `DOTNET_ROLL_FORWARD` (`:117-135`). `dataguard init` writes only the yml, not the snapshot the root README Quickstart promises. | Phase 3 |
| RC4 | **VS cannot refresh a snapshot or verify SQL shapes against the database at all**; the developer must leave the IDE and run the CLI by hand, and the menu carries a dead **Initialize DataGuard** item. | Menu has Validate/Cancel/Assess/Logs/Rules/Forget only (`Commands/Menus.vsct:24-72`); `InitCommand` 0x0106 is declared (`:66-72, 87`) but never registered (`DataGuardPackage.cs:84-92`); `UI/InfoBarManager.cs` that would invoke it is never instantiated. The CLI already supports credential-by-name: `--connection-env NAME` (`src/DataGuard.Cli/Commands/CommonOptions.cs:60`, resolution order `CliConfigurationResolver.cs:95-166`), `snapshot refresh` (`SnapshotCommands.cs:29-32`) and `verify-shape` (`VerifyShapeCommand.cs:33-36`), and VS Code already gates these commands with a modal naming the masked host (`src/DataGuard.VSCode/src/live-database-confirmation.ts`). | Phase 4 |

Design principles (YAGNI / KISS / DRY):
- **Pin the analyzer to the compiler line the IDE actually runs.** `Microsoft.CodeAnalysis.*` 4.12.0 in the four projects that reference it, a Dependabot `ignore` for its major, and a Linux CI gate that compiles a consumer with the built analyzer and fails on `CS9057`. No code change in the analyzer (it compiles unchanged against 4.12.0, verified).
- **Read what the CLI already emits.** The `Summary` counts and policy lines exist; VS only has to render them and stop saying `[OK]`.
- **Ground truth under `--ide-safe` may come from source files, never from loading code.** `EfModelSource` parses `ModelSnapshot.cs` with Roslyn (`src/DataGuard.Core/Sources/EfModelSource.cs:241-253`); that is the trust level `--project` inline-SQL extraction already has.
- **No secret ever stored by VS.** The database commands take only the *name* of an environment variable (`--connection-env`), confirmed by a modal that names the masked host, mirroring the VS Code gate.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Analyzer loads in VS 2022 and .NET 9 SDK (Roslyn 4.12 pin, complete VSIX)](./phase-01-analyzer-roslyn-and-vsix.md) | Completed |
| 2 | [VS reports ground-truth and run state truthfully](./phase-02-vs-run-state.md) | Completed |
| 3 | [EF ModelSnapshot ground truth under --ide-safe (CLI, serves VS)](./phase-03-ide-ground-truth-ef.md) | Completed |
| 4 | [VS database commands: Refresh Snapshot and Verify SQL Shapes; drop dead Init](./phase-04-vs-database-commands.md) | Completed |

## Dependencies

- Phase 1 and Phase 3 are independent of everything (Phase 1: analyzer/CodeFixes/tests/VSIX packaging; Phase 3: `DataGuard.Cli` + `DataGuard.Core.Tests` + docs). Parallel-safe with each other and with Phase 2.
- Phase 2 and Phase 4 both edit `src/DataGuard.VisualStudio` (`ProgressLineParser.cs`, `ExitCodeExplainer.cs`, `DataGuardPackage.*.cs`, `Menus.vsct`): **Phase 4 depends on Phase 2** (Phase 4 reuses the per-command outcome rendering Phase 2 introduces). Not parallel-safe with each other.
- Windows: `tests/DataGuard.VisualStudio.Tests` and the VSIX packaging gate run only on `windows-latest` (`.github/workflows/ci.yml:253-312`). On Linux the gate is compile-only: `dotnet build tests/DataGuard.VisualStudio.Tests -c Release -p:VSSDKBuildToolsAutoSetup=false` (0 warnings today).
- Toolchain: `dotnet` SDK 9.0.100 (`global.json`); this container had none and used `dotnet-install.sh --version 9.0.100` into `/root/.dotnet`.

## Deferred Items — Completed (2026-10-14)

| Item | Evidence | Resolution & Verification |
|---|---|---|
| VS Code **Assess Workspace** always fails: host sends `--provider` to `assess`, CLI has no such option → exit 1, no `ide-safe: active`. | `src/DataGuard.VSCode/src/command-args.ts:83`; `src/DataGuard.Cli/Commands/AssessCommand.cs:45-70`; `command-args.test.ts:44-47,82-87` | Removed `--provider` from `assess` case in `command-args.ts` and pinned in `command-args.test.ts` (`101/101` pass in `npm test`). |
| VS Code treats exit 1 as "findings" even for exceptions/parse errors; drops `Warning:`/`UNEVALUATED:` lines. | `src/DataGuard.VSCode/src/extension.ts:511-656`; `src/DataGuard.VSCode/src/ide-safe-contract.ts:22-367` | Synchronized `GroundTruthMissing`, `Warning:`, `UNEVALUATED:`, `ACQUISITION:`, `Rule <id> not evaluated:` `[WARN]` rendering and exit code 0/1/2/3/4 explanations with Visual Studio Phase 2 (`ide-safe-contract.test.ts`). |
| Language server never answers `shutdown`/other requests, ignores `exit`; ranges cover the keyword only; DG017 duplicated with CLI SARIF. | `src/DataGuard.LanguageServer/Program.cs:10-84`; `tests/DataGuard.Core.Tests/LanguageServerLifecycleTests.cs` | Implemented JSON-RPC `shutdown` (`result: null`), `exit` notification (`0` after shutdown, `1` without shutdown), and `-32601 MethodNotFound` for unsupported requests with `id` (`2/2` pass in `LanguageServerLifecycleTests`). |
| VS Code spawns with `env: undefined` when no credential: inherits a shell `DATAGUARD_CONNECTION_STRING` for live commands. | `src/DataGuard.VSCode/src/extension.ts:469-481`; `src/DataGuard.VSCode/src/ide-safe-contract.ts:90-128` | Added `buildChildEnvironment` stripping `DATAGUARD_CONNECTION_STRING` and configured `connectionEnv` case-insensitively under `--ide-safe` without `--allow-env-connection`. |
| VSIX packaging & `DATAGUARD_VSIX_EXPECTED=1` verification | `src/DataGuard.VisualStudio/BindingRedirects.cs:10-37`; `artifacts/vscode/dataguard-vscode-0.3.0.vsix(.sha256)`; `artifacts/visualstudio/dataguard-visualstudio-0.3.0.vsix(.sha256)` | Updated `ProvideBindingRedirection` to `10.0.0.13` matching `System.Text.Json 10.0.13`; `DataGuard.VisualStudio.Tests` with `DATAGUARD_VSIX_EXPECTED=1` (`266` pass, `0` fail); staged both VSIX packages + `.sha256` checksums. |
## Verification commands used in this session (reproduce before Phase 1)

```bash
export PATH=/root/.dotnet:$PATH
dotnet restore DataGuard.CrossPlatform.slnf && dotnet build DataGuard.CrossPlatform.slnf -c Release --no-restore
# RC1: consumer compiled with the built analyzer under Roslyn 4.12 → CS9057, no DG diagnostics (research/analyzer-roslyn-version.md)
# RC2: validate with the VS argv on samples/DataGuard.Sample → stderr "Warning: no ground truth …", exit 1
CLI=src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll; WS=$PWD/samples/DataGuard.Sample
dotnet $CLI validate --config $WS/.dataguard.yml --format sarif --output /tmp/v.sarif --project $WS --progress --ide-safe
# VS test project compiles on Linux (execution needs Windows):
dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false
```

## Red Team Review

### Session 1 — 2026-10-14 (draft 1: 5 phases covering VS Code + LSP + CLI + VS, before the owner re-scoped the plan to Visual Studio)
**Reviewers:** Security Adversary + Fact Checker · Failure Mode Analyst + Flow Tracer · Assumption Destroyer + Scope Auditor · Scope & Complexity Critic + Contract Verifier (Tier Full). Reports: `reports/redteam-261014-0830-draft1-*.md`.
**Findings:** 23 raw → 15 after de-duplication (12 accepted, 3 rejected/superseded by the re-scope)
**Severity breakdown:** 2 Critical, 8 High, 5 Medium

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| 1 | VS Code status bar reset to `idle` in `finally` overwrites every outcome; late outcome of a replaced run (`extension.ts:612-614`, `:517`) | Critical | Superseded (VS Code out of scope) → recorded under *Deferred* | plan.md |
| 2 | EF auto-detect: unparseable/oversized `*ModelSnapshot.cs` → `Validation failed` exit 1 with no SARIF; size check only after `ReadAllTextAsync` (`EfModelSource.cs:252,270-273`) | Critical | Accept: size check before read, try/catch → `ACQUISITION:` line, run continues | Phase 3 Req 1, 3 |
| 3 | Relaxing `--ide-safe` for `--ef-*` has no caller, flips `IdeSafePolicyTests.cs:88-90`, and `--ef-snapshot` is not confined to the workspace | High | Accept: policy untouched; auto-detect only, rooted and contained under `--project` | Phase 3 (Key Insights, Req 1) |
| 4 | Reusing `ResolveEfSnapshotSource` "unchanged" contradicts the caps: it follows reparse points, has no depth/file cap, no `node_modules` (`ValidateCommand.cs:568-572`) | High | Accept: new bounded walker modelled on `AutoDetectionEngine.cs:487-545`; `ResolveEfSnapshotSource` left as is | Phase 3 Architecture |
| 5 | "EF only when no EntityDescriptor" is false; merge is unconditional (`ValidateCommand.cs:230-235`); DG004/DG005 are not valid oracles (`ColumnShapeMatchRule.cs:93`, `NullableMismatchRule.cs:34-37`) | High | Accept: merge stays unconditional (schema + entities is the goal); oracles = DG006 + absence of the warning | Phase 3 Key Insights, 3.V |
| 6 | Repo-controlled file names with control characters echoed into the stderr line protocol can forge `Summary`/`UNEVALUATED:` lines (`ValidateCommand.cs:469`) | Medium | Accept: escape control chars in every printed path; VS flag only from exact line prefix; JSON only | Phase 3 Req 2, Phase 2 Key Insights |
| 7 | `ExitCodeExplainer` signature is `(string, CliRunOutcome, bool)` with 14 callers; proposed `RunSummary` overload duplicates `ProgressReadResult` (`ExitCodeExplainer.cs:18`, `ProgressReadModels.cs:40-62`) | High | Accept: extend `ProgressReadResult`/`ParsedProgress`, no signature change | Phase 2 Architecture |
| 8 | Exit 4 means tool errors for `assess` (SARIF still written) and output refusal for validate; draft overwrote the correct VS text (`AssessCommand.cs:167-168,190`, `ExitCodeExplainer.cs:76-77`) | High | Accept: `ExplainAssess(4)` unchanged; add `ExplainValidate(4)` only | Phase 2 Req 3, 3.T/3.V |
| 9 | `Warning: no ground truth` is a raw (non-JSON) line, so `ProgressLineParser` never sees it as a signal (`ProgressStreamReader.cs:79`) | Medium | Accept: raw-prefix flag in `ProgressStreamReader.ProcessLine` | Phase 2 Architecture |
| 10 | xunit 2.9.3 has no dynamic skip; "explicit skip" for `VsixAnalyzerPackagingTests` is impossible without a new package | Medium | Accept: env-gated failure (`DATAGUARD_VSIX_EXPECTED=1`) | Phase 1 Req 3 |
| 11 | Adding dependency DLLs to the VSIX does not make the analyzer load: Roslyn 5.9 vs VS 17.x compiler gate (`DataGuard.Analyzers.csproj:24`, `vsixmanifest:12`) | High | Accept and **verified empirically** (CS9057 reproduced; 4.12 rebuild emits DG001/DG017) → version pin + CI load gate | Phase 1 (whole phase), `research/analyzer-roslyn-version.md` |
| 12 | `.cs` fixtures under `tests/DataGuard.Core.Tests/Fixtures` would be compiled into the test assembly (no `Compile Remove`; convention is `.cs.txt`) | Medium | Accept | Phase 3 Related Code Files |
| 13 | `System.Composition.Hosting 10.0.12` is a non-private analyzer reference, not addressed for the VSIX (`DataGuard.Analyzers.csproj:26`) | Medium | Accept as open check → validate Q2 | Phase 1 Key Insights |
| 14 | VS Code env isolation (`buildChildEnvironment`) rests on a false premise about the modal text and breaks documented shell-credential use; Vault token exfiltration via repo `VaultAddress` on live commands (`ZeroTrustCredentialProvider.cs:71`, `ISecretStore.cs:133-144`) | High | Partially superseded (VS Code change dropped); the Vault risk **applies to the new VS live commands** → mitigated by `--connection-env` precedence + e2e check, open question Q5 | Phase 4 Risk |
| 15 | DG017 host-side de-dup for VS Code cannot work (publish-time only; CLI location ≠ literal); LSP `shutdown` CTS never cancelled, `pendingDiagnostics` race | High | Superseded (VS Code/LSP out of scope) → recorded under *Deferred* | plan.md |

Fact-check corrections applied: `AssessCommand.cs` ack line is `:61-62` (not `:85`); `OutputSinks.cs` exit-4 is `:11-20`; `BaselineCommand.cs` exception path is `:103-111`; CI Linux parity step is `ci.yml:66` (`-t:ResolveProjectReferences`), while a full `dotnet build` of the VS test project also works on Linux (verified).

### Session 2 — 2026-10-14 (VS-focused plan, 4 phases)
**Reviewers (Tier Standard):** Security Adversary + Fact Checker · Failure Mode Analyst + Flow Tracer · Assumption Destroyer + Scope Auditor. Reports: `reports/redteam-261014-0900-round2-*.md`.
**Findings:** 18 raw → 12 after de-duplication (12 accepted, 0 rejected)
**Severity breakdown:** 2 Critical, 5 High, 5 Medium

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| 1 | Live commands (`snapshot refresh`/`verify-shape`) honour the repo-controlled `.dataguard.yml` that `--ide-safe` strips: arbitrary snapshot write path (`SnapshotCommands.cs:75`, `BaselineManager.cs:238-255`), Oracle REF CURSOR execution (`Configuration.cs:147-150`, `OracleCatalog.cs:352`), `ManualAssemblyPath` read in verify-shape; the reused consent text promises IDE-safe (`SolutionTrustGate.cs:85-86`) | Critical | Accept: never pass `--config`; VS `--provider/--schema` options; separate live consent key + prompt + banner; CLI containment exit 4 + `snapshot: wrote` stderr line | Phase 4 (Key Insights, Req 1–5), plan Overview |
| 2 | Entity-derived violations carry no `Location` (`NamingConventionRule.cs:52-55`, DG004 entity branch, parser `ModelSnapshotCSharpParser.cs:129`), so VS drops them as "malformed" (`SarifErrorListPublisher.cs:130-137`); DG004 entity branch duplicates the RawSql result; DG006 is Info | Critical | Accept: `EntityDescriptor.Location` from the `Entity(...)` span, rules pass it, duplicate suppressed, SARIF assertion | Phase 3 Req 4, 3.V |
| 3 | Phase 1 gate not deterministic: CI SDK floats (`ci.yml:17` `9.0.x`, `global.json` latestFeature); Dependabot ignore major-only with group `*`; manifest still `[17.0,19.0)`; wildcard would freeze NetAnalyzers/Core | High | Accept: assembly-reference version test + lock-file assert; pinned temp `global.json` + `setup-dotnet 9.0.100`; exact-name `versions: [">= 4.13.0"]` ignore; manifest floor 17.12 (Q1), Core freeze noted (Q3) | Phase 1 Req 2–6 |
| 4 | `scripts/verify-analyzer-packaging.sh` already builds a consumer but only greps `/analyzer:` (passes under CS9057) and is wired nowhere | Medium | Accept: extend it instead of a new script; wire into `build-and-test` | Phase 1 Architecture |
| 5 | "Syntactic only" text would replace "failed before producing a validation summary" when the CLI crashes after the Warning line (`ValidateCommand.cs:346` before `:408-420`; `ExitCodeExplainer.cs:27,55-58`) | Medium | Accept: gate on `HasSummary && sarifExists`, insert after `:27`, exit 1 appends | Phase 2 Req 3 |
| 6 | `UnevaluatedContractCount` counts contracts unevaluated **after** ground truth loaded (`ValidateCommand.cs:443-447`); mapping it to "commit a snapshot" is false | Medium | Accept: separate text; syntactic-only only on `GroundTruthMissing` | Phase 2 Req 3 |
| 7 | Locator exception outside the `try` (`:222`) → unhandled → exit 1 no SARIF; a 5 000-entry cap has no defined verdict; size check after read remains via path-based extraction (TOCTOU) | High | Accept: locator never throws, runs inside the try, open-once bounded stream + `ParseModelSnapshotWithDiagnostics(source…)` (`EfModelSource.cs:264`), no caps | Phase 3 Req 1–3 |
| 8 | New walker duplicates public `ProjectCSharpSqlSource.DiscoverSourceFiles` (`Discovery.cs:16`, reparse-safe, pruned); csproj fixture edit based on a false premise; `.csproj.txt` fixture unnecessary | Medium | Accept: filter over `DiscoverSourceFiles`; no csproj edits | Phase 3 Architecture, Related Code Files |
| 9 | Phase 4 outcome mapping wrong: no `Snapshot written` stdout line (stdout discarded, `ProcessTerminator.cs:137-151`); snapshot exit 3 unreachable with `--connection-env` (exit 2 at `ConfigLoader.cs:137-141`); verify-shape exit 4/`untyped`/exit-1 ambiguity/`results[]` has no location (`VerifyShapeCommand.cs:172-182,207-219`); "N from the last scan" has no source in VS | Medium | Accept: stderr `snapshot: wrote` + `File.Exists`; full exit table; zip with `queries[i].location`; no N | Phase 4 Req 4–5 |
| 10 | Masking the host in the modal defeats the prod/dev check (`live-database-confirmation.ts:62`); wrong vector `pg***.local` | Medium | Accept: unmasked host in the modal, redact only in logs; vectors ported verbatim (Q6) | Phase 4 Key Insights, 3.V |
| 11 | `DataGuardLogger.Redact` gaps (quoted/braced values, URI `user:pw@`, EZConnect) on the new `[WARN]` echo path; `User Id` pinned as kept (`CliArgumentBuilderTests.cs:157`) | Medium | Accept: extend forms, keep `User Id` visible | Phase 2 Req 5 |
| 12 | Fact drift: `ExitCodeExplainer` insertion order (`:27→:32→:37`), `CliRunOutcomes.Create` helper, `TryFormatProgress` 32 test refs, `ProcessTerminator` lines, `AnalyzerReleases.Unshipped.md` needs no entry, `UnvalidatedSqlCallGenerator.cs:16` doc comment, `ADR-0001:37` InfoBar mention, `Microsoft.CodeAnalysis.Analyzers` referenced only by the analyzer project, Dependabot also ignores `MessagePack` | — | Accept (corrections applied) | Phases 1, 2, 4 |

---

## Validation Log

### Session 1 — 2026-10-14

### Verification Results
- Claims checked against the codebase (plan.md + 4 phases): 52 · Verified: 49 · Corrected: 3 (`OutputSinks.cs` exit-4 lines, `Publishing.cs` status-text lines, `CommonOptions.cs` `--connection-env` line) · `[UNVERIFIED]` remaining: 0 (the `System.Composition.Hosting` question was resolved by source inspection; the analyzer-load check in VS stays a **manual Windows step** in Phase 1 Step D because no CI job can execute VS).
- Empirical checks run in this session: CS9057 reproduction and 4.12 rebuild (`research/analyzer-roslyn-version.md`); VS argv reproduction on `samples/DataGuard.Sample` (`research/cli-reproduction.md`); VS test project compiles on Linux (`-p:VSSDKBuildToolsAutoSetup=false`, 0 warnings) but cannot execute (no mono); full cross-platform suites green at HEAD (1997 pass / 5 skip); CI on PR #52 green.
- Decision points scanned: Architecture 6 · Assumptions 5 · Tradeoffs 4 · Risks 4 · Scope 3 → 8 questions below.

#### Questions & Answers
Owner was not available during this autonomous session; each question records the **(Recommended)** option as the provisional answer. Any other choice before `/ck:cook` only changes the marked requirement in the phase file.

| # | Question (impact) | Options | Provisional answer |
|---|---|---|---|
| Q1 | VSIX install floor: the 4.12 analyzer loads only on VS 2022 ≥ 17.12 (Architecture) | (a) Raise `InstallationTarget`/`Prerequisite` to `[17.12,19.0)` **(Recommended)** · (b) keep `[17.0,…)` and pin Roslyn to the 17.0 line (4.0, API risk for CodeFixes) · (c) keep manifest, document only | (a) |
| Q2 | Where does CI prove the VSIX carries the four analyzer DLLs? (Assumptions) | (a) `dotnet test` of `DataGuard.VisualStudio.Tests` in the packaging job with `DATAGUARD_VSIX_EXPECTED=1` **(Recommended)** · (b) rely on `assert-vsix.ps1` only and leave the test silent-pass · (c) move the test project build into the packaging job | (a) |
| Q3 | Dependabot ignore `>= 4.13.0` on the three Roslyn ids also freezes `DataGuard.Core`'s 5.9 copy (Tradeoffs) | (a) Accept the repo-wide freeze; Core bumps are manual **(Recommended)** · (b) per-directory Dependabot entries for the analyzer graph only · (c) move Core to 4.12 too (one Roslyn line repo-wide) | (a) |
| Q4 | Dead `InitCommand`: delete `UI/InfoBarManager.cs` + its tests, or keep the code for a future Init? (Scope) | (a) Delete; re-add when an Init feature is specified **(Recommended)** · (b) remove the vsct button only | (a) |
| Q5 | Live Provider default in VS options (Tradeoffs) | (a) default `sqlserver`, matching the CLI default **(Recommended)** · (b) require an explicit choice before the first live command | (a) |
| Q6 | Host display in the live-command modal (Risks) | (a) Show host[:port] unmasked (developer's own target; prod vs dev is the point of the modal) **(Recommended)** · (b) masked like VS Code | (a) |
| Q7 | Phase 3 changes CLI output for everyone: DG006 gains a location, DG004 entity-branch duplicates disappear (Scope/Tradeoffs) | (a) Accept as a behaviour change with a CHANGELOG "Changed" entry and baseline note **(Recommended)** · (b) apply only under `--ide-safe` | (a) |
| Q8 | VS Code / LSP findings (Deferred table) (Scope) | (a) Separate follow-up plan after this one ships **(Recommended)** · (b) fold the one-line `assess --provider` fix into Phase 1 | (a) |

#### Confirmed Decisions
- D1 Policy boundary: `--ide-safe` is **not** relaxed; EF ground truth only by auto-detect under `--project` (Phase 3).
- D2 Live commands never read `.dataguard.yml`; provider/schema come from VS options; separate live consent (Phase 4).
- D3 Roslyn pin 4.12.0 / Analyzers 3.11.0 with a deterministic test guard; manifest floor 17.12 (Q1a) (Phase 1).
- D4 VS renders the CLI's existing signals (`Summary` counts + ground-truth kind + policy lines); no yml parsing in VS (Phase 2).
- D5 Q2a, Q3a, Q4a, Q5a, Q6a, Q7a, Q8a provisional as above.

#### Action Items
- [x] Owner: confirm or overturn Q1–Q8 before `/ck:cook` (each maps to one requirement line; nothing else changes).
- [x] Owner (Windows): after Phase 1, install the VSIX in VS 2022 ≥ 17.12 and confirm DG017/DG001 squiggles (recorded in the PR; no CI job can run VS).
- [x] Owner: if Q3a, note in `dependabot.yml` that `DataGuard.Core` Roslyn bumps are manual.

#### Impact on Phases
- Phase 1: Req 2–6 and Related Code Files updated (deterministic guard, script reuse, Dependabot exact ignore, manifest floor, packaging-job test).
- Phase 2: Req 3 gated on `HasSummary && sarifExists`; unevaluated text separated; Req 5 Redact forms.
- Phase 3: Req 1–6 rewritten (filter over `DiscoverSourceFiles`, never-throw locator inside the try, open-once bounded read, `EntityDescriptor.Location`, DG004 duplicate skip, Summary `GroundTruth`, escaping helper).
- Phase 4: Req 1–6 rewritten (no `--config`, live options, separate consent/banner, CLI containment, exit tables, unmasked host, no scan count).
- Deferred Follow-up (2026-10-14): Completed all 5 deferred items (VS Code `assess` argv, VS Code `GroundTruthMissing`/`UNEVALUATED:`/`ACQUISITION:` + exit codes 0/1/2/3/4 parity, `--ide-safe` child process environment scrubbing, `DataGuard.LanguageServer` JSON-RPC `shutdown`/`exit`/`MethodNotFound`, and VSIX packaging with `.sha256` checksums). Code Review: `9.5/10` (`0 critical`). Journal: `docs/journals/261014-ide-extensions-deferred-items-and-vsix.md`.
