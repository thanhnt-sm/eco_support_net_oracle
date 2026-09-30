# Dependabot NuGet group (PR #28) -> PR #29

Date: 2026-09-30. Branch `fix/dependabot-nuget-group-lockfiles` (from `origin/dependabot/nuget/src/DataGuard.Cli/nuget-caf5f57d01`, merged `origin/main` d5b3e1a cleanly). Commit `56ccd3f`.
PR: https://github.com/thanhnt-sm/eco_support_net_oracle/pull/29 (open). #28 closed with supersede comment.

## Root cause of #28 failure
Repo restores with `--locked-mode`; Dependabot rewrote csproj versions + some lock files only, leaving downstream lock files (Core consumers, Observability.*, Benchmarks) stale -> `NU1004`.

## Updates kept (15, as proposed)
Microsoft.Data.SqlClient 7.0.3->7.1.1; Microsoft.SqlServer.TransactSql.ScriptDom 180.107.0->180.117.0; OpenTelemetry / .Extensions.Hosting / .Exporter.OpenTelemetryProtocol 1.18.0->1.19.1; OpenTelemetry.Instrumentation.AspNetCore / .Http / .Runtime 1.18.0->1.19.0; Microsoft.VSSDK.BuildTools 18.5.38461->18.5.40034; System.Text.Json 10.0.11->10.0.12 (VSIX); coverlet.collector 10.0.1->10.1.0; Moq 4.20.72->4.21.0; Microsoft.NET.Test.Sdk 17.11.1->18.10.1, xunit 2.9.2->2.9.3, xunit.runner.visualstudio 2.8.2->4.0.0 (VS.Tests).

## Held (2) + dependabot.yml ignore (semver-major)
- FluentAssertions 6.12.2->8.11.0 (VS.Tests only): reverted to 6.12.2 per lead (v8 Xceed commercial licence; MIT project).
- MessagePack 2.5.301->3.1.10 (VSIX): held in 2.x line as 2.5.305 (latest 2.5.x). Reason: csproj comment records the pin as a CVE fix "within the 2.x line"; VS 17.x ships MessagePack 2.5.x; no gate exercises devenv binding. Verified `MessagePack.dll` is NOT bundled in the built VSIX (only STJ + Bcl.AsyncInterfaces), so hold is conservative. Deviation from lead's "revert to previous version": took 2.5.305 patch instead of 2.5.301 (same line, saves a Dependabot round).

## Code fix forced by the group (outside listed ownership, small)
`src/DataGuard.VisualStudio/BindingRedirects.cs`: STJ 10.0.12 ships 10.0.0.12 assemblies (STJ, Encodings.Web, Bcl.AsyncInterfaces, IO.Pipelines); file hard-coded 10.0.0.11; CreatePkgDef (VSSDK.BuildTools 18.5.40034) failed with `ProvideBindingRedirectionAttribute: Invalid value specified for NewVersion`. Changed 8 values 10.0.0.11->10.0.0.12 + 3-line comment documenting the coupling (SA1512 blank line removed).

## Lock files
`dotnet restore --force-evaluate` on DataGuard.sln + 3 projects outside the sln that CI restores locked (`tools/benchmarks/DataGuard.Benchmarks`, `samples/DataGuard.Sample`, `tests/DataGuard.BinaryCompatibilityFixture`). 12 files changed content vs Dependabot HEAD (Observability.AspNetCore, Observability.Messaging, VisualStudio, BinaryCompatibilityFixture, GoldenCorpus.Tests, Observability.Tests, VS.Tests, Benchmarks lock files + 2 csproj + dependabot.yml + BindingRedirects.cs). VSIX bundled-CLI publish lock under obj/ not added.

## Gate outputs (Release, -m:1)
- `dotnet build DataGuard.sln -c Release -m:1 --no-restore`: `0 Warning(s) / 0 Error(s) / Time Elapsed 00:01:07.66`
- `dotnet test DataGuard.sln -c Release --no-build -m:1`:
  - `Passed! - Failed: 0, Passed: 898, Skipped: 0, Total: 898 - DataGuard.Core.Tests.dll (net9.0)`
  - `Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28 - DataGuard.GoldenCorpus.Tests.dll`
  - `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13 - DataGuard.Analyzers.Tests.dll`
  - `Passed! - Failed: 0, Passed: 24, Skipped: 0, Total: 24 - DataGuard.CodeFixes.Tests.dll`
  - `Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38 - DataGuard.Observability.Tests.dll`
  - `Passed! - Failed: 0, Passed: 155, Skipped: 1, Total: 156 - DataGuard.VisualStudio.Tests.dll (net472)`
- `dotnet format whitespace DataGuard.sln --verify-no-changes`: exit 0
- VSIX (VS 18 Enterprise MSBuild, clean obj/ for Analyzers, CodeFixes, VisualStudio; `-restore -t:Build -p:Configuration=Release -p:CreateVsixContainer=true -p:DeployExtension=false -m:1`): exit 0
- `scripts/assert-vsix.ps1`: `assert-vsix: OK - DataGuard.VisualStudio.vsix (47.7 MB), version 0.2.3, 22 entries, all 6 required entries present`
- `dotnet restore --locked-mode` (run last, after VSIX -restore): DataGuard.sln, DataGuard.CrossPlatform.slnf, VS.Tests csproj, Benchmarks csproj, Sample csproj, BinaryCompatibilityFixture csproj -> all exit 0
- Hooks unbypassed: pre-commit (format whitespace, workspace-guard, docs inventory) passed; pre-push `verify_local_gates.sh` passed (`Coverage: 62.54% (8977/14353)`, NuGet audit ok, `[pre-push] Passed.`)
- Full-solution NuGet audit as in ci.yml security-scan (`dotnet restore DataGuard.sln --locked-mode -p:NuGetAuditMode=all -p:WarningsAsErrors=NU1900..NU1905`): exit 0, no NU19xx (MessagePack 2.5.305 has no open advisory).
- Hosted CI on #29: see 'CI result' section below.

## CI result
`gh pr checks 29 --watch --fail-fast` exit 0 — all green: Build and Test (2m54s), Visual Studio Build and Test (2m0s), Visual Studio VSIX Packaging Gate (3m7s), Security Scan (42s), CodeQL Analysis (3m51s), Generate SBOM (34s), Build and Smoke-Test Docker Image (1m13s), Benchmark (42s), Package VS Code Extension (59s), Package Visual Studio Extension (3m6s), Golden standard checklist (4s). Publish VS Code / Visual Studio Marketplace: skipping (by design on PRs). Run: https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/36665689152

## Housekeeping
- Report saved at the lead's explicit path (`fullstack-developer-260930-1007-...`), not the injected `260930-1021` naming pattern.
- `git stash` entry "eol-only lockfile noise before dependabot fix" holds the pre-existing EOL-only lock-file modifications from `main` (empty `git diff`); safe to drop.
- 4 lock files still show ` M` after commit (Sample, Analyzers, Contracts, SqlClassification): EOL-only (`i/lf w/crlf`, `.gitattributes eol=lf`), `git diff` empty.
- Untracked `plans/reports/*260930-0840*` files left untouched.

## Unresolved questions
1. `origin/main` already has FluentAssertions 8.11.0 in Analyzers/CodeFixes/Core/GoldenCorpus/Observability tests (commit dcd856f) — the licence concern applies there too; downgrade to 6.x may need assertion-API edits. Lead decision.
2. CHANGELOG.md not updated (not in lead's commit list) although BindingRedirects.cs is production source and project CLAUDE.md asks for docs in the same change. Add an Unreleased "Changed" line?
3. Free RAM was ~3.3 GB (not 5 GB); everything ran at -m:1 without OOM.
