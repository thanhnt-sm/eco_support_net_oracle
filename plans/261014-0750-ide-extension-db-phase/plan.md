---
title: "IDE extensions (VS Code + Visual Studio): fix broken CLI contract, surface database ground-truth state, make LSP protocol-correct"
description: "Root-cause and fix why the DataGuard IDE extensions misbehave for .NET developers in the database phase: assess is rejected by the CLI, no-ground-truth runs look clean, the language server ignores shutdown/hover, EF projects never get entity contracts under --ide-safe, and the VS VSIX cannot load its analyzers."
status: pending
priority: P1
effort: 14h
branch: fix/ide-extension-db-phase-plan
tags: [ide, vscode, visual-studio, lsp, cli-contract, database, tdd]
blockedBy: []
blocks: []
created: 2026-10-14
---

# IDE extensions: CLI contract, ground-truth visibility, LSP correctness

## Overview

Both IDE hosts (`src/DataGuard.VSCode`, `src/DataGuard.VisualStudio`) drive the same CLI (`src/DataGuard.Cli`) and the VS Code host additionally runs `src/DataGuard.LanguageServer`. All existing suites are green (VS Code 97/97 node tests, .NET 1997 pass / 5 skip on `DataGuard.CrossPlatform.slnf`, verified in this session), yet the product misbehaves because the **integration contract between host and CLI is only pinned on the host side**. Reproduced in this session with the Release build of the CLI and the exact argv the VS Code host builds (`src/DataGuard.VSCode/src/command-args.ts`), against `samples/DataGuard.Sample`:

| # | Symptom a developer sees | Root cause (evidence) | Fixed in |
|---|---|---|---|
| RC1 | **"DataGuard: Assess Workspace" always fails** with `DataGuard CLI did not confirm IDE-safe mode; results were discarded`. | Host passes `--provider <x>` to `assess` (`command-args.ts:84`), but `assess` has no such option (`src/DataGuard.Cli/Commands/AssessCommand.cs:45-70`). System.CommandLine prints `Unrecognized command or argument '--provider'.`, exits 1, the action never runs so `ide-safe: active` is never printed. The unit test pins the wrong argv (`command-args.test.ts:44-47`). The old-CLI hint regex only knows `--ide-safe|--allow-env-connection` (`ide-safe-contract.ts:20`), so the user gets no actionable message. | Phase 1 |
| RC2 | **Validation looks clean/"found findings" while no database rule ran.** In VS Code the CLI line `Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` (`ValidateCommand.cs:346`) is dropped by `formatProgressLine` (`ide-safe-contract.ts:60-85`, only `[INFO]/[WARN]/[ERROR]/ide-safe:/baseline:` lines survive); `UNEVALUATED:`/`ACQUISITION:`/`Rule X not evaluated:` lines are dropped the same way. Exit 3 is reported as generic "could not complete". Visual Studio reads only `ErrorCount/WarningCount/CriticalCount` of the `Summary` event (`ProgressLineParser.cs:155-168`) and prints `[OK] No issues found.` (`ExitCodeExplainer.cs:37-40`) although the CLI emitted `UnavailableRuleCount/UnevaluatedContractCount/AcquisitionDiagnosticCount` (`ValidateCommand.cs:494-499`). | Phase 2 (VS Code), Phase 5 (VS) |
| RC3 | **Exit code 1 is treated as "findings"** (`extension.ts:598-600`) but the CLI also exits 1 on unhandled exceptions and parse errors (`ValidateCommand.cs:507-515`, `BaselineCommand.cs:268`, `ScanCommand.cs:151`). `baseline` without any source prints `Baseline creation failed: ...` exit 1 and the host says "found findings". | Phase 2 |
| RC4 | **Language server is not protocol-correct**: no response to `shutdown` (client `stop()` waits for its timeout), no response to any request other than `initialize` (a `textDocument/hover` request never gets an answer), `exit` is not handled (process stays alive until stdin closes). Reproduced with a raw JSON-RPC probe (`research/lsp-probe.md`). Diagnostics span only the first SQL keyword (`SqlClassifier.cs:200-207`: `token.Length`), and DG017 is reported both by the LSP and by the CLI SARIF under the same `source: "DataGuard"` (`LanguageServer/Program.cs:66-70`, `extension.ts:797-798`) so **SELECT \*** shows twice in Problems. | Phase 3 |
| RC5 | **EF Core projects never get entity contracts in the IDE.** Under `--ide-safe` the CLI rejects `--ef-snapshot/--ef-project/--ef-context` (`IdeSafePolicy.cs:188-200`) and forces Snapshot mode; Manual mode (attributes) needs assembly loading and is also forbidden; `AutoDetectEFContext` is a no-op stub (`src/DataGuard.Core/Models/Configuration.cs:206-209`). `dataguard init` writes only `.dataguard.yml`, not the snapshot the README promises (root `README.md` Quickstart). So in the database phase, without a committed snapshot or a stored credential, DG002/003/005/101 can never run from the IDE, and RC2 hides that. | Phase 4 |
| RC6 | **Visual Studio VSIX ships analyzers that cannot load** [UNVERIFIED on Windows]: the VSIX includes only `DataGuard.Analyzers.dll` + `DataGuard.CodeFixes.dll` (`DataGuard.VisualStudio.csproj:103-110`) while the analyzer depends on `DataGuard.Contracts` and `DataGuard.SqlClassification` (`ContractValidationAnalyzer.cs:12`, `SqlCallSyntax.cs:11`) and is compiled against `Microsoft.CodeAnalysis.CSharp 5.9.0` (`DataGuard.Analyzers.csproj:24`) though the manifest targets VS `[17.0,19.0)`. `InitCommand` (0x0106) is declared in `Menus.vsct:66-72` but never registered (`DataGuardPackage.cs:84-92`). VS has no credential path at all (`CliArgumentBuilder.cs:117-135` sets only `DOTNET_ROLL_FORWARD`). | Phase 5 |

Design principles for the fix (YAGNI/KISS/DRY):
- **One contract, pinned on both sides.** A JSON fixture lists every argv the hosts build; the C# test suite parses each through the real `System.CommandLine` root (no process spawn), the TypeScript suite asserts the hosts build exactly those vectors. The fixture is the single source of truth for the CLI surface the IDE uses.
- **Do not widen the CLI surface for the host.** `assess` does not need a provider; the host stops sending it. Host-visible state (ground truth missing, unevaluated contracts) already exists in the CLI's `Summary` event and policy lines; the hosts must read what is already emitted.
- **The language server stays a thin classifier.** Add only the JSON-RPC lifecycle it is missing; no new features.
- **Ground truth under `--ide-safe` may come from source files, never from loading code.** Parsing `*ModelSnapshot.cs` is a Roslyn source parse (`EfModelSource`), which is exactly as safe as the inline-SQL extraction the IDE already does.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Host–CLI argv contract pinned on both sides (fixes Assess)](./phase-01-cli-contract.md) | Pending |
| 2 | [VS Code surfaces ground-truth and run state truthfully](./phase-02-vscode-run-state.md) | Pending |
| 3 | [Language server lifecycle, ranges and DG017 de-duplication](./phase-03-language-server.md) | Pending |
| 4 | [EF ModelSnapshot ground truth under --ide-safe (database phase in the IDE)](./phase-04-ide-ground-truth.md) | Pending |
| 5 | [Visual Studio parity: summary counts, dead Init command, analyzer packaging](./phase-05-visual-studio-parity.md) | Pending |

## Dependencies

- Phase 2 depends on Phase 1 (both touch `extension.ts`/`ide-safe-contract.ts`; Phase 1 lands the fixture that Phase 2's tests extend). Not parallel-safe with Phase 1.
- Phase 3 is independent (C# `DataGuard.LanguageServer` + one new test file + a small `extension.ts` change in `loadDiagnostics`); if run with `--parallel`, it must run after Phase 2 because both edit `extension.ts`.
- Phase 4 is CLI/Core only (`IdeSafePolicy.cs`, `ValidateCommand.cs`, `EfModelSource`, docs); parallel-safe with Phases 1–3.
- Phase 5 is Windows-only to verify (`tests/DataGuard.VisualStudio.Tests` run on `windows-latest` in CI); the C# changes compile on Linux through `DataGuard.sln` evaluation parity, but the VSIX gate is CI-only.
- External: `dotnet` SDK 9.0.100 (`global.json`), Node 22 (`.nvmrc`). This container had no `dotnet` by default; it was installed with `dotnet-install.sh --version 9.0.100` into `/root/.dotnet` for this planning session.

## Verification commands used in this session (reproduce before Phase 1)

```bash
export PATH=/root/.dotnet:$PATH
dotnet restore DataGuard.CrossPlatform.slnf && dotnet build DataGuard.CrossPlatform.slnf -c Release --no-restore
CLI=src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll; WS=$PWD/samples/DataGuard.Sample
dotnet $CLI assess --workspace $WS --provider sqlserver --format sarif --output /tmp/a.sarif --ide-safe   # exit 1, "Unrecognized command or argument '--provider'."
dotnet $CLI validate --provider sqlserver --format sarif --output /tmp/v.sarif --project $WS --progress --ide-safe  # exit 1, stderr has "Warning: no ground truth"
cd src/DataGuard.VSCode && npm ci && npm test   # 97/97 green today: the suites do not see RC1–RC6
```
