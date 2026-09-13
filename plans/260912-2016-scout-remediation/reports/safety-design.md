---
type: researcher
date: 2026-09-12
---
# Safety remediation design

Date: 2026-09-12
Scope: implementation design only, based on `plans/260912-1936-luna-src-audit/reports/`. No source, dependency, test, workflow, or product-doc change is made by this report.

## Decision boundaries

- The remediation closes verified behavior and documentation gaps without adding an HTTP host, CVE/advisory service, remote assessment service, plugin sandbox, or unrelated adapter work.
- Preserve public compatibility through **additive adapters/overloads** where a public surface must evolve. Do not silently repurpose an existing public member.
- “Contained” means normalized path containment plus best-effort resolved-target containment at open time. This mitigates prefix and ordinary link escapes; it is not a promise to be race-proof against a hostile filesystem actor changing links after validation (TOCTOU).
- Plugin allowlisting and plugin sandboxing are different problems. An allowlist can express operator trust; it cannot constrain in-process code.
- Documentation claims are closed to current evidence. Conditional future work remains clearly labeled as a backlog, not shipped capability.

## Finding disposition and implementation contract

| Finding | Disposition | Contract / narrow fix | Required proof |
|---|---|---|---|
| CS-03 | Close: false positive | The existing probe demonstrated sibling `packages.lock.json` discovery. No product fix. | Preserve/re-run the focused probe only when changing assessment inventory. |
| CS-04 | Fix | Create one internal assessment path-policy helper. Resolve with `Path.GetFullPath`, use `Path.GetRelativePath` to reject `..`, rooted relative output, and sibling-prefix paths; before reading an existing file, resolve final symlink/junction target and require it to remain under the resolved workspace target. Use it in `ProjectInventoryReader`, `PackagesConfigReader`, `SecretsPack.AssessFile`, and `SecretsPack.AssessMachinePaths`. On a link-resolution/open failure, return the existing safe error/no-finding behavior rather than reading the target. | Inside path; `..`; absolute escape; `root` vs `root-evil`; symlink/junction outside rejection; symlink inside acceptance. If test infrastructure cannot create a link, emit an explicit named skip, never an early successful return. Document best-effort, resolved-at-open semantics and the TOCTOU limit. |
| CS-05 | Close docs/API claim; conditional backlog | Assessment is local-only. Keep `AssessmentRequest.AllowRemoteLookups` as a documented compatibility-reserved no-op rather than treating it as an enabled feature; removal is major-version work only. Do not implement advisory/CVE lookup, health score, egress, or HTTP hosting. | Request with either value produces the documented local pack set; docs state no remote lookup occurs. A future proposal must define provider, auth, privacy, timeouts, partial errors, and opt-in egress. |
| CS-06 | Clarify, not a defect | “Baseline” is an overlay used with Snapshot, not a missing `GroundTruthMode`. Correct wizard/UI wording; do not add an enum or change behavior. | Fake-console/wizard mapping test proves the displayed choice and selected Snapshot-plus-baseline options. |
| CS-07 | Fix P1 | `EncryptConnectionStringAtRest=true` plus an unavailable encryption backend must fail closed for **local credential-store persistence**. Never write plaintext with `IsEncrypted=true`. Config/env resolution remains portable. Reject or safely ignore legacy records claiming encryption without an `ENC:` payload; do not represent them as protected. DPAPI is Windows-only in this release; non-Windows users use env/external providers or explicitly opt out of local-store encryption. | Windows encrypted round trip and metadata; non-Windows requested encryption throws before write; opt-out plaintext metadata is false; malformed legacy record never gains protected status. |
| CS-08 | Harden | Replace partial masking with an audit sanitizer: event-specific structured allowlisted fields; redact sensitive text fully; hash only where correlation is required. Sanitize arbitrary `details` and `errorMessage` before `FileAuditLogger` persists them. | Password, bearer, JWT, API key, connection string, and arbitrary secret-shaped details/errors are absent from logs; benign structured fields remain. |
| CS-09 | Fix | Extract shared SARIF sanitization (safe message plus property allowlist/value checks). Apply it to buffered output and both public streaming paths, especially `StreamingSarifSink.WriteAsync(IEnumerable<ContractViolation>)`. Do not claim all normal CLI output leaked; the unsafe surface is the direct streaming overload. | Same secret corpus redacted from buffered SARIF, file streaming, and direct streaming; benign properties survive. |
| CS-10 | Close overclaim; prove actual boundary | An explicit `pluginDirectory` is an operator trust grant. Retain no default auto-load. A collectible `AssemblyLoadContext` provides unload/type-resolution isolation, not a sandbox. Do not add a superficial allowlist. Future untrusted plugins require separately approved integrity policy (for example signed/hash manifest) and out-of-process execution. | Fixture DLL from an explicit temp directory loads expected metadata/rule; incompatible metadata is excluded from effective rules; docs say plugin code runs with host-process authority. |
| CS-11 | Narrow semantics | Do not label package-name prefix matching as supply-chain provenance/integrity. Prefer exact recognized package IDs if retaining the helper; report it as a name heuristic, not trust. NuGet signatures, SBOM, and SLSA remain out of scope. | Spoofed prefix name is not classified as recognized after exact-match change; docs do not promise provenance. |
| CS-12 | Fix | Canonically sort every nested collection: entity properties; procedure parameters by ordinal then name; result/table columns by ordinal/name. Generate valid TypeScript identifiers, or quoted property keys, while retaining wire names and deterministic collision handling. | Input/nested permutation produces byte-identical JSON/TypeScript; spaces, hyphens, reserved words, leading digits, Unicode, and colliding normalized names compile/parse as valid TypeScript. |
| CS-13 | Split | Harden auto-detection traversal with explicit excluded generated/metadata directories, enumeration/file/size bounds, and cancellation. Do not redesign legacy auto-detection into a secret store in this remediation. Docs must say discovery can see candidate configuration and must neither log nor persist secret values. A credential-reference redesign is conditional future work. | Generated trees are skipped; cap and cancellation paths are observable; no discovered connection-string text appears in diagnostics/logs. |
| CS-14 | Fix | Replace timer-thread blocking with serialized `FlushAsync(CancellationToken)`, collector-lifetime cancellation, bounded queue/batch/payload caps, and an explicit retry/overflow policy. Preserve a batch until successful export; do not dequeue then silently lose it. Maintain disabled=no-egress and endpoint checks. In parallel, harden baseline persistence: cancellation flows through save/write, file-size/capacity validation precedes mapping/allocation, and persistence uses temp-file write plus flush then atomic replace/move so cancellation or crash cannot publish a partial baseline. | Telemetry: disabled/no-egress, allowed/blocked endpoint, cancellation, timeout, one in-flight flush, bounded queue/batch, retained failed batch, overflow signal, circuit breaker. Baseline: cancellation before publish leaves old baseline intact, bounded oversized input fails predictably, valid atomic replacement leaves readable schema/hash state. |
| TL-01 | Fix | Advertise only IDs for which a provider registers an action. Remove unimplemented IDs from `FixableDiagnosticIds` rather than inventing unsafe fixes. Preserve intentional multi-provider mapping explicitly. | For each advertised provider/ID, registration yields an action; unsupported IDs are not advertised. |
| TL-02 | Docs correction | Canonical documentation table maps diagnostic ID -> exported provider -> actual action. State three exported providers and distinguish IDs/actions from providers. | EN/VI docs review plus action-matrix test source cross-check. |
| TL-03 | Docs correction | Analyzer is limited syntactic/invocation analysis; Core/CLI own database-grounded heavy validation. | Documentation claim sweep against analyzer registration and Core rule wiring. |
| TL-04 | Fix | Remove the dead/unreachable stored-procedure prefix branch that reuses `DG002`; do not add an ID without a product decision. `DG002` retains its parameter-type meaning. | Analyzer source exercising EXEC-prefix input does not emit semantic-mismatched `DG002`; Core parameter-type behavior remains covered. |
| TL-05 | Defer/remove dead helper | Do not generate incomplete `ExpectedSpParameterAttribute` metadata. Remove dead helpers if touched by TL-01, otherwise leave no advertised route to them. | No action emits empty type/direction; code-fix tests inspect transformed syntax. |
| TL-06 | Fix tests | Replace metadata-only assertions with end-to-end Roslyn action tests. | Diagnostic -> registration -> selected `ApplyChangesOperation` -> final document matrix, including duplicate attributes, marker suppression, no-op naming, and null syntax-root guard. |
| CI-04 | Fix | In `PreCommitHookInstaller`, write `hookContent`, not `hookPath`; preserve no-overwrite/force behavior. | Temp repository with `.husky`: installed hook contains shebang/DataGuard script and never equals its own path. |
| CI-06 | Fix | VS Code SARIF artifact resolver permits relative/absolute file locations only when workspace-contained under the same best-effort lexical plus real-path policy. Skip external/non-file/escaping locations. | Pure Node tests for relative, absolute-inside, outside, prefix, URI/malformed, and symlink cases; extension-host/manual smoke proves only in-workspace diagnostics appear. |
| CI-07 | Docs correction | Document shipped VS Code scope: Run/Cancel, workspace trust, bounded CLI process, SARIF diagnostics, redacted lifecycle messages. Do not claim assess, real-time validation, raw streams, provider UI, or detailed summary. | Docs source check and VS Code smoke test. |
| CI-08 | Docs plus narrow trust check | Visual Studio keeps Run/Cancel and its existing timeout/process-tree logic; docs remove settings/provider/real-time/CTS claims. Treat `DATAGUARD_CLI_PATH` as machine/user operator configuration, not solution-controlled input. Apply solution-root SARIF artifact containment before making `ErrorTask.Document`. | Windows unit/seam test for URI containment and command construction; Windows VSIX smoke checks run/cancel/timeout cleanup. |
| CI-10 | Close docs overclaim | Remove shipped `/health/live`, `/health/ready`, `/health/startup` claims. Do not create a web project, route, container endpoint, or health package. | Claim sweep finds no shipped endpoint statement; any future host is a separately owned backlog item. |
| V01 | Compatibility-only dependency update | VS Code npm advisories are dev-only compatibility work. Update only to a non-breaking compatible dependency/lockfile resolution after reviewing release notes; do **not** run `npm audit fix --force`, do not upgrade unrelated major dependencies, and do not present the update as runtime product hardening. | `npm ci`, `npm test`, lockfile review, and VS Code extension build/package check; record advisory IDs, affected dev paths, and chosen version rationale. |

## Implementation shape

### 1. Core security, containment, export, and lifecycle

**Owned files**

- `src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs`
- `src/DataGuard.Core/Assessment/Internal/PackagesConfigReader.cs`
- `src/DataGuard.Core/Assessment/Internal/SecretsPack.cs`
- new internal assessment path-policy helper beside these readers
- `src/DataGuard.Core/Security/CredentialManager.cs`
- `src/DataGuard.Core/Security/IAuditLogger.cs`
- `src/DataGuard.Core/Reporting/DiagnosticEmitter.cs`
- `src/DataGuard.Core/Reporting/ContractExport.cs`
- `src/DataGuard.Core/Telemetry/TelemetryCollector.cs`
- `src/DataGuard.Core/Baseline/BaselineManager.cs`
- `src/DataGuard.Core/Assessment/AssessmentContracts.cs` (XML/API wording only unless a major-version removal is separately approved)

**Public API compatibility**

- Keep existing `TelemetryCollector` constructor/delegate working. Add an overload/adapter that accepts cancellation-aware export rather than changing the current `Func<string, string, Task>` signature.
- Keep public `FlushEvents(object?)` synchronous-completion compatible for successful bounded flushes; use a separate nonblocking timer callback and add `FlushAsync(CancellationToken)`. See RT-05 in Phase 4 for timeout/failure/shutdown rules; do not turn the public method fire-and-forget.
- Do not change serialized export shape or `AssessmentRequest` member names in this remediation.
- Add metadata (for example truncation/drop count) additively; consumers that do not understand it retain existing behavior but documentation must warn that capped results are incomplete.
- Keep the existing baseline format readable. If atomic-write state/version metadata is introduced, readers accept existing v1/v2 persisted baselines and write the new safe form only after successful completion.

**Baseline persistence policy**

1. Validate input and computed payload size before allocating/memory-mapping; reject invalid/oversized data with actionable error rather than casting an unchecked capacity.
2. Pass cancellation to every save/write/flush operation and check it before the publication step.
3. Write a complete serialized baseline to a same-directory uniquely named temporary file, flush it, then atomically replace/move it into the target path. Preserve a valid previous target if cancellation/failure occurs before publication.
4. Clean only this operation's known temporary file best-effort; never broadly clean the workspace.
5. Read paths reject incomplete/corrupt/oversized artifacts without allocating unbounded memory and return the existing controlled baseline error contract.

**Telemetry policy**

1. Timer callback starts/observes `FlushAsync` without `GetAwaiter().GetResult()`.
2. A `SemaphoreSlim` or equivalent ensures one in-flight export. Disposal cancels future/current work and disposes timer resources predictably.
3. Config carries positive bounded limits for queued events, batch event count, payload bytes, and export timeout. Add them with defaults rather than breaking `TelemetryConfig` callers.
4. Peek/copy a bounded batch and remove it only after success. On transient failure retain it; enforce a documented bounded overflow policy with an observable drop/truncation count.
5. Invalid endpoint remains no-egress; disabled remains no timer/no enqueue/no export.

### 2. Plugins and supply-chain wording

**Owned files**

- `src/DataGuard.Core/Plugins/RulePluginManager.cs`
- `src/DataGuard.Core/Security/SupplyChainVerifier.cs`
- plugin/security documentation and fixture tests

Do not describe collectible ALC as permission isolation. The current explicit directory requirement is the trust switch. A future manifest/hash/signature allowlist is possible only with explicit operator-managed keys/manifest lifecycle; it is not a substitute for a sandbox. If arbitrary third-party rules become a supported scenario, require a separate out-of-process protocol and capability boundary design.

### 3. Roslyn and hook behavior

**Owned files**

- `src/DataGuard.CodeFixes/CodeFixProviders.cs`
- `src/DataGuard.Analyzers/Analyzers.cs`
- `src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs`
- `tests/DataGuard.CodeFixes.Tests/CodeFixProviderTests.cs`
- `tests/DataGuard.Analyzers.Tests/*`
- suitable CLI hook test file/project

Provider capability lists are a contract: no ID appears merely because a future fix may be desirable. The action test matrix, not provider metadata, is the source of truth.

### 4. IDE trust boundaries

**Owned files**

- `src/DataGuard.VSCode/src/security.ts`
- `src/DataGuard.VSCode/src/extension.ts`
- `src/DataGuard.VSCode/src/security.test.ts`
- `src/DataGuard.VSCode/package.json`, `package-lock.json` only for V01
- `src/DataGuard.VisualStudio/DataGuardPackage.cs`
- Windows-specific Visual Studio test/seam project only if needed for executable coverage

VS Code already declares `dataguard.cliPath` with `scope: machine` (`src/DataGuard.VSCode/package.json:94–99`); preserve and characterize this operator-setting boundary rather than inventing a workspace-setting defect. Retain `shell: false` and trusted-workspace execution. Visual Studio's CLI environment variable is operator-controlled machine/user configuration. Neither extension should convert untrusted SARIF file paths into editor/Error List documents outside the selected root.

### 5. Documentation closure

Restrict edits to current, canonical docs discovered by exact claim search, especially:

- `docs/03-components/core/{assessment,plugins,security,reporting,telemetry}.md` and relevant Vietnamese pairs;
- `docs/03-components/tooling/{analyzers,code-fixes,vscode-extension,visual-studio-extension,cli}.md` and pairs;
- `docs/PRODUCT.md`, `docs/architecture.md`, `docs/STAGE_FLOW.md`, `docs/USAGE.md`, and feature showcase EN/VI where their current claims overlap;
- `src/DataGuard.VSCode/README.md` and `src/DataGuard.VisualStudio/overview.md` when extension boundaries are stated.

Do not rewrite archived/historical evidence. Mark historical verification with a commit/date and `current-unverified`; never reuse historical test count or coverage as live evidence. Every count must identify whether it means Core rule, analyzer descriptor, exported provider, registered action, or tested action.

## Verification gates

1. Focused Core, analyzer, code-fix, and CLI hook tests run before broad suite.
2. `dotnet build DataGuard.sln --configuration Release` and affected `dotnet test` projects; then full solution test.
3. Database integration tests never early-return as Passed. When opt-in environment `DATAGUARD_RUN_SQLSERVER_INTEGRATION=1` is absent, use an explicit named Skip. In the dedicated integration CI job it is set; unavailable Docker/database is a failure and a DB assertion execution marker is required.
4. VS Code V01: `npm ci`, `npm test`, and extension build/package verification. Do not use `npm audit fix --force`.
5. Windows job: build/test Visual Studio component and a VSIX smoke test covering Run, Cancel, timeout/process cleanup, and SARIF root containment. macOS evidence remains platform-blocked, not a pass.
6. Docs: run `./scripts/verify_docs_sync.sh` plus an exact claim/link sweep; the script's existence checks alone are not proof of semantic accuracy.

## Sequencing and conflict control

1. Terra implements Core security/containment/baseline/telemetry/export changes and their focused tests.
2. Sol reviews the security threat boundaries, additive API adapters, baseline atomicity, telemetry loss semantics, and plugin wording before broad integration.
3. Terra implements tooling/Husky/IDE changes and tests.
4. Terra performs the documentation closure only after the tested behavior is fixed or deliberately deferred.
5. Platform owners supply dedicated Windows and DB CI evidence.

Avoid concurrent edits to `src/DataGuard.Cli/Program.cs` (snapshot/config workstream), adapter readers/rule registration (adapter workstream), and shared product claim docs until their owners provide final capability facts. This safety plan owns only the identified core, tooling, IDE-boundary, test, and claim-accuracy surfaces.
