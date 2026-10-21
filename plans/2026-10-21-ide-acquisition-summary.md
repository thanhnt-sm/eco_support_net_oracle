# IDE acquisition summary regression — 2026-10-21

## Contract and scope

A successful validation can load ground truth while source acquisition still leaves gaps. The CLI's `WarningCount` counts rule findings; `AcquisitionDiagnosticCount` is a separate existing summary field. Both Visual Studio and VS Code must warn on a positive acquisition count instead of reporting `[OK] No issues found.` The status text must show that count separately from errors and rule warnings.

The change consumes existing summary fields only. It does not change CLI exits, diagnostic severity, acquisition extraction, schemas or intentional skips (the CLI already excludes skips from this count). Syntactic-only notices, cancellation, timeout, missing-result errors and other nonzero-exit explanations retain precedence. Zero or absent acquisition counts retain existing behavior.

## Implementation and regressions

- Change `ExitCodeExplainer.cs` and `ide-safe-contract.ts` completion/status formatters.
- Use the neutral VS Code warning tooltip `DataGuard validation needs attention`, which also covers acquisition-only warnings without implying rule findings or cancellation.
- Add shared `AcquisitionSummaryTests.cs` in the Windows test project and link it into `DataGuard.Core.Tests`, alongside real formatter/model sources and their pure dependency closure. Import the original StyleCop settings for linked source headers; do not duplicate production DTOs.
- Exercise TypeScript parsing through explanation/status, loaded ground truth with zero and positive rule warnings, syntactic-only status, zero/absent counts, missing SARIF, missing summary, cancellation and error precedence. C# additionally pins the timeout explanation.
- Add `IdeAcquisitionSummaryProcessTests`: run the actual CLI with `--ide-safe --progress --format sarif`, autodetect EF source ground truth, and adapt actual JSON `Summary.Data` properties into the production `ProgressReadResult`. Assert loaded `entities` ground truth, positive acquisition count, zero rule errors/warnings, an existing SARIF file, warning completion and visible status count. This test does not call the Windows host parser: `ProgressLineParser` depends on the VSSDK-backed logger.
- Update the paired [English](../docs/03-components/tooling/vscode-extension.md) and [Vietnamese](../docs/03-components/tooling/vscode-extension.vi.md) IDE documentation.

## Verification record

Execution evidence is local to `/workspace/.tmp/risk-maintenance/ide-acquisition-summary` and is not a repository artifact.

- TypeScript red: compilation passed; `node --test out/ide-safe-contract.test.js` ran 15 tests, with 13 passing and 2 expected failures before formatter edits. The failures expose a green completion with acquisition gaps and a missing acquisition status count.
- Initial C# linked-source build exposed a StyleCop copyright-settings mismatch. The test project now imports the source project's existing settings; this setup failure is preserved separately and is not counted as behavioral red evidence.
- C# behavioral red: `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~AcquisitionSummaryTests --logger 'console;verbosity=normal'` ran 13 cases, with 10 passing and 3 expected failures before formatter edits. Two failures expose the missing acquisition warning with loaded ground truth, and one exposes the missing status count.
- C# green: the same Release test command with filter `'FullyQualifiedName~AcquisitionSummaryTests|FullyQualifiedName~IdeAcquisitionSummaryProcessTests'` passed all 14 cases, including the actual CLI process boundary. The first process-fixture attempt correctly rejected the explicit `--ef-snapshot` option under IDE-safe; the final fixture uses the host's EF autodetection path.
- `./scripts/verify_docs_sync.sh` and `git diff --check` passed.
- TypeScript green: `npm run compile` passed, and `node --test out/ide-safe-contract.test.js` passed 15/15 tests.
- Full extension suite: `npm test` passed its compile and generated-contract TypeScript checks and all 192 Node tests. No coverage run was performed.
- Full cross-platform Release build remains the coordinator's integration gate; the scoped C# Release test command compiled the affected linked formatter and models successfully.

These checks exercise the real formatter under .NET 9 on Linux, a real CLI subprocess with an explicit test-side JSON-to-model adaptation, and TypeScript helpers under Node. They do not establish the Windows host parser integration, .NET Framework 4.7.2 runtime behavior, Windows IDE hosting, VSIX installation, live database behavior or Marketplace readiness. No coverage collection, commits, pushes or external publication is part of this lane.

The existing raw progress summary can still display its process-completion check mark. That line alone does not establish validation completeness; this slice changes the final completion explanation and status warning policy.
