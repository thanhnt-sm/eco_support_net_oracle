# StackAccord runtime branding and compatibility inventory

Baseline: `872974c5f333ef97f142b3bf55c2fe3202816b09` (current `main` when this lane started).
Scope: runtime and packaged extension content in `src/`, with tests in `tests/` and the VS Code project. No root documentation, workflows, scripts, packaging or governance files are changed.

## User-visible surfaces

| Surface | Files | Result / regression coverage |
| --- | --- | --- |
| VS Code extension gallery and workspace capability messages | `src/DataGuard.VSCode/package.json` | StackAccord display name and description; manifest identity assertions |
| VS Code Command Palette, activity bar and Settings UI | `package.json` | All 16 command titles, explorer title, configuration title and setting descriptions use StackAccord; command/view/setting identities asserted unchanged |
| VS Code status bar, Output, Problems, quick picks, notifications and errors | `src/DataGuard.VSCode/src/extension.ts`, `security.ts`, `ide-safe-contract.ts`, `live-database-confirmation.ts` | StackAccord names, prefixes, status text, consent prompts and diagnostic source; entrypoint behavior and source audit tests |
| VS Code dashboard tab, HTML title/header and notices | `ui/dashboard-panel.ts`, `ui/dashboard-view.ts` | StackAccord; real renderer/controller tests |
| VS Code CodeLens, hover, quick actions and empty query tree | `ui/codelens-provider.ts`, `hover-provider.ts`, `quick-fix-provider.ts`, `redaction.ts`, `sql-queries-tree-provider.ts` | StackAccord; provider behavior tests and source audit |
| Packaged VS Code overview and current change log | `src/DataGuard.VSCode/README.md`, `CHANGELOG.md` | StackAccord; README explains retained upgrade/configuration IDs |
| Packaged VS overview | `src/DataGuard.VisualStudio/overview.md` | StackAccord Options paths, status example and CLI label; canonical Options identity is described separately and checked by a focused regression test |
| VS Tools menu and installed product | `src/DataGuard.VisualStudio/Commands/Menus.vsct`, `source.extension.vsixmanifest`, `DataGuardPackage.cs` | Existing StackAccord/Beta display retained; canonical command names/GUIDs retained |
| VS Tools > Options category and descriptions | `DataGuardPackage.cs`, `VSPackage.resx`, `DataGuardOptionsPage.cs`, `DataGuardRulesOptionsPage.cs` | Resource 110 displays StackAccord while the canonical category stays DataGuard; source contract and Windows resource/reflection tests |
| VS Output pane, status, progress, rule catalog and summary | `OutputPaneWriter.cs`, `ProgressLineParser.cs`, `ProgressPump.cs`, `RuleCatalogPrinter.cs`, `RuleInventory.cs`, `ExitCodeExplainer.cs`, `CliRunSession.cs`, `CliRunTimeoutHandler.cs` | StackAccord labels and prefixes; existing outcome assertions updated |
| VS command lifecycle, consent, live database prompts, publishing and navigation | `DataGuardPackage*.cs`, `SolutionTrustGate.cs`, `LiveDatabaseCommands.cs`, `SolutionLifetimeWatcher.cs`, `SarifErrorListPublisher.cs`, `ErrorListPresenter.cs` | StackAccord prompts, guidance and fallback messages; no consent/security policy changes |
| VS diagnostic log heading and local support summary | `DataGuardLogger.cs`, `SupportSummary.cs` | StackAccord; persisted paths and summary schema unchanged |
| CLI help, version banner, reports, config/init and managed-hook feedback | `src/DataGuard.Cli/Program.cs`, `Commands/*.cs`, `Services/SnapshotGuard.cs`, `Hooks/PreCommitHookInstaller.cs` | StackAccord; subprocess help/version tests and legacy-hook upgrade test |
| Generated config heading and contract export comment | `src/DataGuard.Core/Models/ConfigurationYamlWriter.cs`, `Reporting/ContractExport.cs` | StackAccord; YAML keys/defaults unchanged |
| Setup wizard, assessment, plugin and credential-provider feedback | `AutoDetection/AutoDetectionEngine.cs`, `Assessment/AssessmentEngine.cs`, `Plugins/RulePluginManager.cs`, `Security/ZeroTrustCredentialProvider.cs`, `Security/SecretStores/LinuxSecretServiceSecretStore.cs` | Display text only, including the Linux credential display label; lookup attributes, credentials, admission and security behavior unchanged |
| MSBuild and health-host messages | `src/DataGuard.Build/OfflineManifestValidationTask.cs`, `src/DataGuard.Host/Program.cs` | StackAccord; configuration/task identities unchanged |
| Language-server Problems source | `src/DataGuard.LanguageServer/Program.cs` | StackAccord; JSON-RPC methods, diagnostic rule codes and artifact identity unchanged |
| SARIF display name and repository link | `Reporting/SarifTypes.cs`, `DiagnosticEmitter.cs` | `fullName: StackAccord`, real repository URL; scanner `name` stays DataGuard; all three writer paths tested |
| Runtime product, package title, authors and package descriptions | All 17 `src/*/*.csproj` | StackAccord product/title/authors; package and assembly IDs retained |

## Deliberately retained legacy technical identities

These are compatibility data, including when a command, file path or configuration key must be shown to a user. They are not display-brand labels.

| Identity | Why retained |
| --- | --- |
| All `DataGuard.*` namespaces, public types/members, project/assembly names, filenames, reference paths, `InternalsVisibleTo` names and generated fully qualified attribute names | Binary/source compatibility, plugin loading, analyzer loading and build references |
| Package IDs `DataGuard.Analyzers`, `DataGuard.Build`, `DataGuard.Cli`, `DataGuard.CodeFixes`, `DataGuard.Contracts`, `DataGuard.Core`, `DataGuard.Host`, `DataGuard.LanguageServer`, `DataGuard.MySql.Adapter`, `DataGuard.Observability`, `DataGuard.Observability.AspNetCore`, `DataGuard.Observability.Messaging`, `DataGuard.Oracle.Adapter`, `DataGuard.PostgreSql.Adapter`, `DataGuard.SqlClassification`, `DataGuard.SqlServer.Adapter`, `DataGuard.VisualStudio` (where explicit; implicit project identities remain the same) | Existing dependency and extension upgrades; display `Title`/`Product` are separate |
| `dataguard`, `dataguard.exe`, `dataguard.dll`, `DataGuard.LanguageServer.dll`, release/VSIX filenames and published tool install IDs | Existing executable invocation, extension discovery and packaged artifact resolution |
| VS Code publisher `thanhnt-sm`, package `dataguard-vscode`, extension ID `thanhnt-sm.dataguard-vscode` | In-place extension upgrades and existing SecretStorage identity |
| Commands `dataguard.runValidation`, `cancelValidation`, `assess`, `refreshSnapshot`, `createBaseline`, `configureConnection`, `openDashboard`, `refreshFindings`, `clearFindings`, `groupBySeverity`, `groupByRule`, `groupByFile`, `applyQuickFix`, `scanProject`, `refreshQueries`, `verifyShape` (each has the `dataguard.` prefix), and internal `dataguard.applyQuickFixInternal` | Existing keybindings, menus and programmatic callers |
| `dataguard-explorer`, `dataguard.findingsView`, `dataguard.sqlQueriesView`, `dataguard.dashboard`, diagnostic collection `dataguard`, language-client ID `dataguardLanguageServer` | Existing view state, diagnostic ownership and command routing |
| Settings `dataguard.enabled`, `dataguard.configPath`, `dataguard.provider`, `dataguard.cliPath`, `dataguard.timeoutSeconds`, `dataguard.assessmentTimeoutSeconds` and their scopes/defaults | Existing user/workspace settings remain effective |
| VS Code secret key prefix `dataguard.connectionString.` | Existing workspace credentials remain available; no storage migration |
| VSIX identity `DataGuard.VisualStudio`, publisher `thanhnt-sm`, package GUID `04a7c09c-4f79-439f-8298-952900cdb5ae`, command-set GUID `a7ceccae-351c-4d13-9568-b2ba5370ea7d`, Output GUID `b85dce85-998f-4f6a-a4fd-c2b6867d0c2a` | Upgrade, command and Output pane identity |
| VS Options canonical category `DataGuard`, pages `General` and `Validation Rules`; types `DataGuard.VisualStudio.DataGuardOptionsPage` / `DataGuard.VisualStudio.DataGuardRulesOptionsPage`; page GUIDs `6e1a9b24-342a-4a6c-9477-981504d6cfa1` / `5802fc76-4ebd-4ab7-8014-9918c889f048` | Existing automation and import/export paths; DialogPage storage uses the unchanged page type name; resource 110 changes only the visible category |
| VS command-table symbols/IDs and canonical `DataGuard*` command names; `DataGuard/...` FileAndForget task identifiers | Keyboard/automation compatibility and task correlation |
| Consent collection `DataGuard\TrustedSolutions`; existing `DataGuard` installation/log/temp/app-data directories and `dataguard-` temp prefixes | Saved consent, CLI discovery, logs and cleanup remain reachable |
| `.dataguard.yml`, `.dataguard-snapshot.json`, `.dataguard-baseline.json`, `.dataguard-plugin.json`, generated hook filenames/command keys | Existing configuration, snapshots, baselines and plugin admission manifests |
| `DATAGUARD_CONNECTION_STRING`, `DATAGUARD_DATABASECONNECTION`, `DATAGUARD_PROVIDER`, `DATAGUARD_CLI_PATH`, `DATAGUARD_AUDIT_KEY`; `MinDataGuardVersion` | Existing operator configuration and plugin version contract |
| `DataGuardHealth` configuration section; `DataGuardOfflineManifest`, `DataGuardValidateOfflineManifest`, `DataGuard.Build.OfflineManifestValidationTask`, `DataGuard.Build.targets`, and other existing MSBuild property/target names | Existing host/MSBuild configuration |
| SARIF `name: DataGuard` and `DataGuard.Assessment`, fingerprints `dataguard/v2`, existing `DG...` rule IDs | Preserve code-scanning tool identity, suppression/baseline continuity and protocol compatibility; `fullName` carries StackAccord |
| Analyzer categories `DataGuard.IDE`, `DataGuard.Contracts`, `DataGuard.Length`, `DataGuard.Dialect`, `DataGuard.Performance`, `DataGuard.Security`; `// DataGuard:` annotations | Existing `.editorconfig` category configuration and source suppressions |
| Code-fix equivalence/property keys `DataGuard.*`, generated hint `DataGuard.SqlCallModels`, compilation name `DataGuard_SourceAnalysis`, load contexts `DataGuard.Plugin:*` / `DataGuard.TrustedModel:*` | Existing analyzer/code-fix and loader contracts |
| Managed-file marker `DataGuard pre-commit hook` (including native git suffix) and generated `dataguard-validate` hook command key | Previously installed hooks remain identifiable, replaceable and removable; visible shell feedback now says StackAccord |
| Credential entropy/keychain service `DataGuard.Credential.Protection`, `DataGuard.Credential.*`; existing audit/credential storage paths | Cryptographic and credential compatibility; unchanged |
| Telemetry service default `dataguard`, meter/activity sources `DataGuard.Core`, `DataGuard-Validation`, `DataGuard.Observability`, `DataGuard.Observability.Business`, `DataGuard.Observability.Messaging`, existing metric/schema/property names | Existing telemetry consumers and configuration |
| Inbound VS Code diagnostic source/message recognition for DataGuard | Old CLI/LSP diagnostics remain recognizable; newly emitted source and quick-fix titles say StackAccord |

## Validation boundaries and integration follow-up

- `BrandingContractTests` checks CLI subprocess behavior, unchanged IDs, legacy-hook upgrades, generated YAML, all SARIF writers, project metadata and runtime C# presentation strings.
- `src/DataGuard.VSCode/src/branding.test.ts` checks visible manifest text, stable IDs and runtime presentation. Lifecycle, dashboard and provider tests exercise visible behavior with editor/process doubles.
- `BrandingRegistrationTests` validates the embedded VS Options resource and canonical page identity on Windows. Microsoft documents category localization separately from [DialogPage settings storage](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.dialogpage.settingsregistrypath?view=visualstudiosdk-2022).
- Linux compilation, source assertions and editor doubles do not verify the installed Windows Visual Studio UI, upgrade of an installed VSIX, persisted settings import/export or a real VS Code installation. Those acceptance checks remain for integration/Windows verification.
- Shipped `ADDITIONAL-PERMISSIONS.md` / `THIRD-PARTY-NOTICES.md` copies remain byte-identical to the canonical legal files, as the existing license gate requires. Existing legal wording still contains DataGuard. Coordinate a single legal-copy update with the docs lane; do not rename legal identity independently here.
- The docs-owned generated YAML example still has the previous brand in its comment heading at this baseline. The configuration parity test accepts exactly that heading alias while comparing every remaining byte. The docs lane should update `docs/guides/oracle-snapshot.full.yml` to the StackAccord heading.
- No Marketplace publication or merge is performed by this lane.
