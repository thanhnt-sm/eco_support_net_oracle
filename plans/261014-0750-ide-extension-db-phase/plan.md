---
title: "Visual Studio extension: make the in-editor analyzer load, report database ground-truth state truthfully, and add the database-phase commands"
description: "Root-cause and fix why DataGuard for Visual Studio misbehaves for .NET developers in the database phase: the bundled Roslyn analyzer is refused by VS 2022 / .NET 9 compilers (no squiggles at all), validate runs with no ground truth are reported as [OK], EF Core ModelSnapshot ground truth is unreachable under --ide-safe, and VS has no snapshot/verify-shape commands at all."
status: pending
priority: P1
effort: 12h
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
| RC2 | **A validate run that compared nothing prints `[OK] No issues found.`** The developer believes the database contract was checked. | The CLI says so on stderr: `Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` (`src/DataGuard.Cli/Commands/ValidateCommand.cs:346`), `UNEVALUATED:` / `ACQUISITION:` / `Rule X not evaluated:` lines, and the `Summary` event carries `UnavailableRuleCount`, `UnevaluatedContractCount`, `AcquisitionDiagnosticCount` (`ValidateCommand.cs:494-499`). VS reads only `ErrorCount/WarningCount/CriticalCount` (`ProgressLineParser.cs:155-168`), echoes the warning as an unremarkable `[DataGuard CLI]` line (`:62-66`) and `ExitCodeExplainer.Explain` returns `[OK] No issues found.` for exit 0 (`ExitCodeExplainer.cs:37-40`); the status bar shows `0 errors, 0 warnings` (`DataGuardPackage.Publishing.cs:118-120`). The only hint exists when `.dataguard.yml` is absent (`DataGuardPackage.Commands.cs:119-122`); with a yml but no snapshot there is none. Validate exit 4 (unsafe output path, `OutputSinks.cs:307`) is "Unexpected exit code 4." | Phase 2 |
| RC3 | **EF Core projects never get entity contracts from the IDE.** Without a committed `.dataguard-snapshot.json`, DG002/003/004/005/101 cannot run; VS has no way to supply a connection. | Under `--ide-safe` the CLI rejects `--ef-snapshot/--ef-project/--ef-context` (`src/DataGuard.Cli/IdeSafePolicy.cs:188-200`) and forces Snapshot mode (`:84-88`); Manual (attribute) mode needs assembly loading and is also forbidden; `AutoDetectEFContext` is an empty stub (`src/DataGuard.Core/Models/Configuration.cs:206-209`). VS passes neither `--provider` nor any EF option (`CliArgumentBuilder.cs:88-96`) and sets no env var but `DOTNET_ROLL_FORWARD` (`:117-135`). `dataguard init` writes only the yml, not the snapshot the root README Quickstart promises. | Phase 3 |
| RC4 | **VS cannot refresh a snapshot or verify SQL shapes against the database at all**; the developer must leave the IDE and run the CLI by hand, and the menu carries a dead **Initialize DataGuard** item. | Menu has Validate/Cancel/Assess/Logs/Rules/Forget only (`Commands/Menus.vsct:24-72`); `InitCommand` 0x0106 is declared (`:66-72, 87`) but never registered (`DataGuardPackage.cs:84-92`); `UI/InfoBarManager.cs` that would invoke it is never instantiated. The CLI already supports credential-by-name: `--connection-env NAME` (`CommonOptions.cs:112`, resolution order `CliConfigurationResolver.cs:95-166`), `snapshot refresh` (`SnapshotCommands.cs:29-32`) and `verify-shape` (`VerifyShapeCommand.cs:33-36`), and VS Code already gates these commands with a modal naming the masked host (`src/DataGuard.VSCode/src/live-database-confirmation.ts`). | Phase 4 |

Design principles (YAGNI / KISS / DRY):
- **Pin the analyzer to the compiler line the IDE actually runs.** `Microsoft.CodeAnalysis.*` 4.12.0 in the four projects that reference it, a Dependabot `ignore` for its major, and a Linux CI gate that compiles a consumer with the built analyzer and fails on `CS9057`. No code change in the analyzer (it compiles unchanged against 4.12.0, verified).
- **Read what the CLI already emits.** The `Summary` counts and policy lines exist; VS only has to render them and stop saying `[OK]`.
- **Ground truth under `--ide-safe` may come from source files, never from loading code.** `EfModelSource` parses `ModelSnapshot.cs` with Roslyn (`src/DataGuard.Core/Sources/EfModelSource.cs:241-253`); that is the trust level `--project` inline-SQL extraction already has.
- **No secret ever stored by VS.** The database commands take only the *name* of an environment variable (`--connection-env`), confirmed by a modal that names the masked host, mirroring the VS Code gate.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Analyzer loads in VS 2022 and .NET 9 SDK (Roslyn 4.12 pin, complete VSIX)](./phase-01-analyzer-roslyn-and-vsix.md) | Pending |
| 2 | [VS reports ground-truth and run state truthfully](./phase-02-vs-run-state.md) | Pending |
| 3 | [EF ModelSnapshot ground truth under --ide-safe (CLI, serves VS)](./phase-03-ide-ground-truth-ef.md) | Pending |
| 4 | [VS database commands: Refresh Snapshot and Verify SQL Shapes; drop dead Init](./phase-04-vs-database-commands.md) | Pending |

## Dependencies

- Phase 1 and Phase 3 are independent of everything (Phase 1: analyzer/CodeFixes/tests/VSIX packaging; Phase 3: `DataGuard.Cli` + `DataGuard.Core.Tests` + docs). Parallel-safe with each other and with Phase 2.
- Phase 2 and Phase 4 both edit `src/DataGuard.VisualStudio` (`ProgressLineParser.cs`, `ExitCodeExplainer.cs`, `DataGuardPackage.*.cs`, `Menus.vsct`): **Phase 4 depends on Phase 2** (Phase 4 reuses the per-command outcome rendering Phase 2 introduces). Not parallel-safe with each other.
- Windows: `tests/DataGuard.VisualStudio.Tests` and the VSIX packaging gate run only on `windows-latest` (`.github/workflows/ci.yml:253-312`). On Linux the gate is compile-only: `dotnet build tests/DataGuard.VisualStudio.Tests -c Release -p:VSSDKBuildToolsAutoSetup=false` (0 warnings today).
- Toolchain: `dotnet` SDK 9.0.100 (`global.json`); this container had none and used `dotnet-install.sh --version 9.0.100` into `/root/.dotnet`.

## Deferred (out of scope by owner direction; evidence kept so nothing is lost)

| Item | Evidence | One-line fix when picked up |
|---|---|---|
| VS Code **Assess Workspace** always fails: host sends `--provider` to `assess`, CLI has no such option → exit 1, no `ide-safe: active`. | `src/DataGuard.VSCode/src/command-args.ts:84`; `src/DataGuard.Cli/Commands/AssessCommand.cs:45-70`; pinned wrong by `command-args.test.ts:44-47` | Drop `--provider` from the `assess` case (VS argv `CliArgumentBuilder.cs:98-102` is correct). |
| VS Code treats exit 1 as "findings" even for exceptions/parse errors; drops `Warning:`/`UNEVALUATED:` lines. | `extension.ts:596-603`; `ide-safe-contract.ts:60-85` | Same rendering as Phase 2, host side. |
| Language server never answers `shutdown`/other requests, ignores `exit`; ranges cover the keyword only; DG017 duplicated with CLI SARIF. | `src/DataGuard.LanguageServer/Program.cs:12-57`; `SqlClassifier.cs:200-207`; probe in `research/lsp-probe.md` | Lifecycle branch + MethodNotFound; range from literal span. |
| VS Code spawns with `env: undefined` when no credential: inherits a shell `DATAGUARD_CONNECTION_STRING` for live commands. | `extension.ts:459-461` | Explicit env with the variable removed. |

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

### Session 2 — 2026-10-14 (VS-focused plan, 4 phases) — see below (appended after the second review)
