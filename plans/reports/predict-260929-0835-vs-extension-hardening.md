# Prediction Report: DataGuard for Visual Studio — architecture check, red-team, hardening & upgrade

Date: 2026-09-29 · Branch: main · Mode: `/ck:predict` (5 personas) + red-team lenses + CLI/VS Code scouts
Inputs: full read of `src/DataGuard.VisualStudio/**` (2 611 LOC), scout reports (VS Code parity, CLI↔IDE contract), advisor review.
Agent availability: Fable weekly limit hit mid-run (HTTP 429) — 5 of 8 subagents terminated. Lenses "supply chain/CI" and "Core/adapters" were **not** completed by independent agents; their sections below are lead-authored from direct reads and are marked *partial*.

## Scope decision (what "triệt để" means here)

- Subject: the Visual Studio 2022 extension and the CLI↔IDE contract it depends on.
- Red-team: all components enumerated; **implement every Critical/High that touches the VS extension or the CLI contract**; Medium/Low become plan phases, explicitly listed as deferred.
- Out of scope for this cycle: rewriting Core rules/adapters, new VS UI surfaces (tree view, code lens) beyond parity of safety behaviour.

## Verdict: STOP (current design) → CAUTION (proceed with the hardening program below)

STOP trigger met: Security persona identified code execution reachable from repo content with no viable mitigation in the current design. The upgrade program is GO/CAUTION once phases 1–2 land; the extension must not be re-published to the Marketplace before that.

---

## Persona analyses (independent)

### Architect
- Out-of-process CLI with SARIF handoff is the right shape (no DB drivers in devenv). Keep.
- `DataGuardPackage.cs` = 1 629 lines, one class doing command wiring, process lifecycle, progress parsing, SARIF→Error List, temp hygiene, auto-install, build events. Violates the repo's 200-line rule 8×; every fix so far has been a patch inside this file (see 9 `fix(vsix)` commits).
- Contract fragility: SARIF `sourceRoot` is CWD, not `--project` (`DiagnosticEmitter.cs:29-32`, `Program.cs:403`); works only because VS sets `WorkingDirectory`. Exit codes overloaded (1 = findings *and* crash *and* YAML parse failure). Oracle/PostgreSQL `validate` always exits 3 (`ProviderRuleCatalog.cs:49,72` → `Program.cs:346-359`), and VS explains 3 as "no DB connection or snapshot" — wrong.
- CreatePkgDef is a hidden build constraint: three commits fixed TypeLoadExceptions (ValueTuple, IVsTextView, TextManager.Interop). CI never packages the VSIX (`ci.yml:107-125` runs `dotnet test` with `CreateVsixContainer=false`), so this class of failure only appears in `release.yml`.
- Recommendation: split into ≤200-line units (process runner/terminator, progress parser, SARIF publisher, trust gate, temp cleaner, argument builder, exit-code explainer), verified by a real MSBuild `CreateVsixContainer=true` build in CI.

### Security
1. **Critical — code execution from repo content.** `.dataguard.yml` in the solution dir is passed as `--config`; `LoadConfig` maps `GroundTruthMode: Manual` + `ManualAssemblyPath` (`Program.cs:2050`) → `ManualContractSource` → `Assembly.LoadFrom(path)` (`ManualContractSource.cs:26`). A cloned hostile repo + "Run Validation" (or **Run Validation on Build**, unattended) loads attacker DLL into the CLI process. No trust prompt exists (VS Code gates on `workspace.isTrusted`, `extension.ts:352-355`; VS has nothing).
2. **High — hostile connection targets.** Same config can set `ConnectionString` (`Data Source=attacker;Integrated Security=true`) → forced NTLM auth / credential relay from the developer's account; `AuditLogPath`, `TelemetryFileDirectory`, `SnapshotFilePath` → file writes at attacker-chosen relative paths under the user's permissions.
3. **High — silent global install.** `TryAutoInstallCliAsync` runs `dotnet tool install -g DataGuard.Cli` with no prompt, no version pin, no signature check (`DataGuardPackage.cs:1341-1402`). The VSIX bundles `cli\dataguard.exe`, so this path is only hit when the bundle is broken — dead-code-with-teeth.
4. **Medium — relative Custom CLI path resolves against the solution directory** (`DataGuardLogger.cs:213-222`): a user setting `tools\dataguard.exe` is satisfied by a repo-shipped binary.
5. **Medium — SARIF publishing robustness.** Catch filter misses `InvalidOperationException`/`FormatException` from `GetInt32()`/`GetString()` on wrong JSON kinds (`:1447-1460`); no result cap; percent-encoded URIs not decoded; assess SARIF bypasses the CLI sanitizer (`Program.cs:1789,1794`).
6. **Low — redaction gaps.** Two `ex.Message` writes bypass `Redact` (`:1499`, `:1616`); `Redact` regex is narrower than the CLI's `SafeText` (no `User Id=…;Password=` multi-token forms, no JWT `eyJ`). `DATAGUARD_CONNECTION_STRING` in devenv's environment silently switches the CLI to live mode.
7. Supply chain *(partial, lead-read)*: `ci.yml` actions are SHA-pinned; NuGet audit + TruffleHog + CodeQL present; VSIX has SHA256 sidecar but no code signature; no CI job validates VSIX contents.

### Performance
- No cap on SARIF results → `ErrorListProvider.Tasks.Add` per item on the UI thread with no `SuspendRefresh`; 100 000 violations (`MaxViolationQueueSize` default) would freeze devenv. VS Code caps the file at 50 MB; VS caps nothing.
- Whole SARIF read into memory via `ReadToEndAsync` + `JsonDocument.Parse` — acceptable up to tens of MB, but pair with a result cap.
- Every non-JSON stderr line is parsed twice (`TryFormatProgress` then `IsJsonPayload`) — negligible.
- Timeouts (300 s validate / 60 s assess, clamped 5–900) and `taskkill /T /F` are sound. Auto-install blocks a command for up to 30 s + 3 s — remove with the feature.
- Bundled self-contained single-file CLI (~70–100 MB in VSIX) is the real cost; acceptable for now, note for roadmap.

### UX
- Banner promises "Double-click any Error List item to jump directly to code" but `Navigate` only opens the document (`:1485-1507`); no caret positioning since IVsTextView was removed. `TaskProvider.Navigate(TaskListItem, Guid)` in Shell.15.0 does this without TextManager.Interop.
- Missing `.dataguard.yml` is silent: CLI degrades to source-only rules and exits 0 → "[OK] No issues found." even with zero `.cs` files. False assurance. VS has no Init command and no hint.
- Oracle/PostgreSQL users always see exit 3 with a misleading explanation.
- Auto-install without consent surprises users and can fail behind proxies with a cryptic exit code.
- Version shown in Help → About is hardcoded "1.0.0" (`:31`, `:99`) while the manifest is 0.2.3 and `release.yml` rewrites only the manifest.
- Feature parity vs VS Code: no findings tree, no clear-findings command, no status-bar click target. Not safety-relevant; defer.

### Devil's Advocate
- "Why not remove the VS extension and rely on Roslyn analyzers?" — analyzers cover DG001 only; the CLI diff engine is the product. Keep the extension.
- "Why not reuse the VS Code model via the LanguageServer?" — larger rewrite, same trust problem. No.
- "Is a consent prompt over-engineering? Just add `--ide-safe` to the CLI." — `--ide-safe` alone stops assembly loading but not `ConnectionString` to a hostile host unless it also forbids connections; and a CLI flag does not protect users who point VS at an older/global CLI. Both layers are needed: CLI `--ide-safe` (belt) + VS per-solution consent keyed by config hash (braces).
- False assumption exposed: "the bundled CLI is always present, so auto-install is harmless" — if the bundle is present, auto-install is dead code; if absent, it is an unconsented installer. Either way, delete it.
- "Run Validation on Build" is opt-in and defaults off — keep it, but it must never prompt from a build event; if consent is missing it skips with an Output-pane message.

---

## Agreements (all 5 personas)
- Split `DataGuardPackage.cs` before adding behaviour; verify with a real VSIX packaging build.
- Add a solution-trust gate in VS and an `--ide-safe` mode in the CLI; pass it unconditionally from VS.
- Delete auto-install; keep bundled CLI + explicit custom path (rooted only).
- Fix Error List navigation, exception filter, result cap, version source.
- Add a CI job on windows-latest that packages the VSIX and asserts contents.

## Conflicts & Resolutions

| Topic | Architect | Security | Performance | UX | Devil's Advocate | Resolution |
|---|---|---|---|---|---|---|
| Trust gate location | CLI flag keeps VS thin | Need both layers | Prompt is one-time, no cost | Prompt must be clear, once per solution/config hash | Flag alone is insufficient | **Both**: CLI `--ide-safe` + VS consent keyed by (solution path, SHA-256 of `.dataguard.yml`); build-triggered runs never prompt |
| Auto-install | Remove | Remove | Remove (30 s block) | Replace with actionable message | Dead code either way | **Remove**; message with install command + Options path |
| Result cap value | Configurable | Cap hard | ≤ 2 000 tasks keeps Error List responsive | Tell the user how many were truncated | Cap is fine | **2 000** hard cap, banner line "N more not shown; see SARIF" |
| Modularize now vs later | Now | Now (smaller review surface) | Neutral | Neutral | Risk: CreatePkgDef | **Now**, with the packaging build as the phase gate; no TextManager.Interop, no ValueTuple in public/internal signatures |
| Missing config UX | CLI should warn | Not security | — | Show "no .dataguard.yml; source-only rules ran" | Don't add Init command yet | VS prints an explicit warning line when `.dataguard.yml` is absent; Init command deferred |

## Risk Summary

| Risk | Severity | Mitigation |
|---|---|---|
| Repo-controlled `ManualAssemblyPath` → `Assembly.LoadFrom` from VS run / build event | Critical | Phase 1 `--ide-safe` (reject Manual mode, EF assembly paths, plugin dirs) + Phase 2 consent gate |
| Repo-controlled `ConnectionString` → forced auth / hostile DB | High | `--ide-safe` ignores config/env connection strings unless the user consented for that config hash |
| Silent `dotnet tool install -g` | High | Remove |
| Relative Custom CLI path satisfied by repo binary | Medium | Require rooted path; drop solution-relative resolution |
| SARIF parse exception → empty Error List, misleading "Failed to start" | Medium | Per-result try/skip; count skipped |
| Unbounded Error List | Medium | 2 000 cap + `SuspendRefresh` |
| Navigation does not position caret | Medium | `errorListProvider.Navigate(task, LOGVIEWID_Code)` |
| CreatePkgDef regressions only caught at release | Medium | CI job packages VSIX + asserts entries |
| Version drift "1.0.0" | Low | Read `extension.vsixmanifest` at runtime; single constant fallback |
| Exit 3 / exit 1 misexplained | Low | Update explainer; surface CLI stderr reason line |
| Redact bypasses | Low | Route all `ex.Message` through `Redact`; widen regex |

## Recommendations (ordered)
1. **CLI `--ide-safe`** (`Program.cs` validate/assess): force `GroundTruthMode` ∈ {Snapshot}, null out `ManualAssemblyPath`, reject `--ef-*`, ignore `ConnectionString` from file and `DATAGUARD_CONNECTION_STRING`, print one stderr line `ide-safe: <what was suppressed>`; unit tests in `tests/DataGuard.Core.Tests/CliExitCodeTests.cs` style.
2. **VS modularization + trust gate**: new files under `src/DataGuard.VisualStudio/` (`CliProcessRunner.cs`, `ProcessTerminator.cs`, `CliArgumentBuilder.cs`, `ProgressLineParser.cs`, `RuleInventory.cs`, `SarifErrorListPublisher.cs`, `SolutionTrustGate.cs`, `TempDirectoryCleaner.cs`, `ExitCodeExplainer.cs`, `ExtensionVersion.cs`); `DataGuardPackage.cs` becomes wiring only. Tests updated to the new types.
3. **Remove auto-install; rooted-only custom CLI path; Navigate fix; result cap; exception filter; redact fixes; assess clears inventory; missing-config warning.**
4. **CI**: `visual-studio-vsix-package` job (windows-latest, MSBuild `/p:CreateVsixContainer=true`) asserting `cli/dataguard.exe`, `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, manifest version.
5. **Docs**: README IDE section, `docs/USAGE.md`/security posture note on ide-safe + consent; changelog; fold `FIX_DATAGUARDVISUALSTUDIO_BUILD_PLAN.md` into the plan dir.
6. Deferred (Medium/Low, tracked in plan): VS findings tree/status-bar parity, Init command, VSIX code signing, assess SARIF sanitizer parity, `originalUriBaseIds`, Oracle/PG exit-3 rule availability redesign, `Program.cs` 2 430-line split, independent supply-chain and Core/adapters red-team lenses (re-run when Fable quota resets 2026-10-01 07:00 Asia/Bangkok).

## Evidence index
- VS Code parity scout: `extension.ts:352-355` (trust), `package.json:43-52,217` (machine-scoped cliPath), no auto-install (`extension.ts:864`).
- CLI contract scout: Summary events `Program.cs:446,1740`; exit codes table; `LoadConfig` silent default `Program.cs:1855-1864`; sourceRoot = CWD `DiagnosticEmitter.cs:29-32`.
- Config surface: `Configuration.cs:6-45`, YAML key map `Program.cs:2030-2062`.
- VS SDK: `TaskProvider.Navigate(TaskListItem, Guid)` and `Shell.Settings.ShellSettingsManager` present in Microsoft.VisualStudio.Shell.15.0 17.14.40264; no solution-trust API exists in the SDK.

## Unresolved questions
- Should consent be per-solution only, or per-solution × config hash (proposed)? Hash invalidates consent when a repo update changes `.dataguard.yml`.
- ~~Should `--ide-safe` also become the default for VS Code?~~ Resolved during implementation: VS Code now passes `--ide-safe` on `validate`/`assess` in addition to workspace trust.
- Marketplace re-publish timing after Phase 2.
