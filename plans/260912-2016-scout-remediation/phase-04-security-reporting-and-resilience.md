---
phase: 4
title: "Security reporting and resilience"
status: in-progress
priority: P1
effort: "L"
dependencies: [3]
---

# Phase 4: Security reporting and resilience

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Harden actual Core security/reporting behavior after the CLI resolved context exists. This fixes encryption-state truthfulness, path boundaries, redaction, lifecycle/cancellation and deterministic export—not unimplemented remote security products.

## Requirements

- Never label plaintext non-Windows credential storage encrypted. A requested unavailable encryption backend fails closed; existing plaintext behavior remains only by explicit opt-out.
- Apply one sanitizer to buffered SARIF and both direct `StreamingSarifSink` overloads; retain benign fields without secrets.
- Use shared lexical plus resolved-target containment rather than string prefixes in all assessment readers. Document best-effort-at-open TOCTOU limits.
- Complete CS-14: atomic/bounded/cancellable baseline persistence plus serialized bounded telemetry `FlushAsync`; no success after partial persistence and disabled telemetry has no egress.
- Make nested exports deterministic and generate valid TypeScript keys/collision mappings. Audit fields are allowlisted/redacted; auto-detection is bounded/cancellable and does not persist/log discovered secrets.
- Keep plugin ALC non-sandboxed and supply-chain naming a heuristic until signed provenance is supplied. Dependency health/advisory and loopback health-host capabilities are now delivered in phases 9–10 with explicit opt-in and fail-closed boundaries; they are not enabled by this security phase.

## Architecture

Enforcement is at Core write/emission boundaries. An internal assessment path-policy helper validates lexical and resolved targets before readers open files. Telemetry keeps a batch until successful export. Phase 3 owns command token wiring; this phase owns Core completion and tests.

## Related Code Files

- `src/DataGuard.Core/Assessment/Internal/{ProjectInventoryReader,PackagesConfigReader,SecretsPack}.cs` plus new internal path-policy helper
- `src/DataGuard.Core/Security/{CredentialManager,IAuditLogger,SupplyChainVerifier}.cs`
- `src/DataGuard.Core/Reporting/{DiagnosticEmitter,ContractExport,ContractEvidence}.cs`
- `src/DataGuard.Core/{Baseline/BaselineManager,Telemetry/TelemetryCollector,AutoDetection/AutoDetectionEngine,Plugins/RulePluginManager}.cs`
- `tests/DataGuard.Core.Tests/{CredentialManagerFullTests,DiagnosticEmitterFullTests,DiagnosticEmitterTests,ContractExportTests,TelemetryTests,SourceAndBaselineTests,AssessmentPackTests,AutoDetectionEngineTests,SupplyChainVerifierTests}.cs`
- Findings CS-04–CS-14 (except CS-06 docs) and CI-06; authoritative contract `reports/safety-design.md`, ledger `./findings-ledger.md`.

## Implementation Steps

1. Sol approves fail-closed encrypted-store and containment semantics; enumerate exact readers above before edits.
2. Make credential metadata match actual protection; test Windows-capable encryption, non-Windows requested encryption fails before write, explicit plaintext has false metadata, malformed legacy record stays unprotected.
3. Extract shared SARIF sanitization for buffered/file/direct paths; add hostile secret corpus and benign-property tests. Replace audit arbitrary details/errors with event-specific allowlists.
4. Implement bounded path traversal and read policy using lexical plus full ancestor-chain resolution. Apply to `ProjectInventoryReader`, `PackagesConfigReader`, named `SecretsPack` methods, `AssessmentEngine`, `InventoryPack`, `BuildCiPack` and `DependencyHealthPack` enumeration/open seams. Reject outside-root linked ancestors and final targets; cap depth/count/size, propagate cancellation and expose partial/error status instead of returning clean empty findings. Test inside, traversal, rooted, sibling-prefix, directory-link/file-link/junction in/out, and explicit skip if links cannot be created. <!-- RT-07: accepted -->
5. Implement atomic baseline temp/flush/replace and pre-allocation size validation; coordinate with Phase 1 contract and Phase 3 token flow. Preserve old target on cancellation.
6. Add cancellation-aware `FlushAsync` additively, one in-flight export, bounded queue/batch/payload/timeout, retained failed batch, observable overflow, and disabled no-egress; preserve existing constructor/delegate via adapter.
7. Sort every nested export collection and quote/validate TypeScript keys with deterministic collision handling. Bound/cancel auto-detection and redact candidate secret values.
8. CS-10/11 require real trusted plugin DLL fixture lifecycle/metadata proof and spoof-name heuristic tests, even if production plugin loader remains unchanged. Update `docs/03-components/core/{security,reporting,assessment,telemetry,auto-detection,plugins}{,.vi}.md` in this batch with trust/local-only/limit wording, not new sandbox/provenance APIs.

## Success Criteria

<!-- RT-12: plugin caller lifetime accepted -->
Pipeline must own managers created by `WithPlugins`, define repeated-call replace/accumulate policy, release registered rule references and dispose every owned manager on pipeline shutdown. Test through repeated `ValidationPipeline.WithPlugins` + Dispose, not only direct RulePluginManager; incompatible DLL metadata/failed load cannot leak a retained context. Unload is cooperative, not sandbox or forced kill; weak-reference/file replacement evidence must not promise unload if external consumers retain plugin objects.

<!-- RT-06: accepted -->
Credential store also requires bounded reads, same-directory temp/flush/atomic publish and restrictive permissions (Unix owner-only mode; verify applicable Windows owner ACL). Resolve/reject linked/reparse targets and ancestors outside explicitly authorized store root; check immediately before publish, document TOCTOU limits. Test permission, link redirect, oversized record, cancellation and previous-record preservation. Explicit plaintext opt-out does not waive file protection.

<!-- RT-08: accepted -->
Reporting boundary includes every built-in diagnostic sink, not only SARIF. Transform safe output once before Console/File/stream sinks; do not mutate caller-owned violations. Add source-root-aware artifact URI projection: relative normalized in-root paths, suppress external/unresolvable absolute paths and sensitive path components under documented policy. Absolute usernames/customer paths must not leak into uploaded SARIF by default; test buffered/direct/console/file emissions plus benign diagnostic location retention. Third-party sinks/plugins remain trusted arbitrary code, not a promised sandbox.

<!-- RT-05: accepted -->
Telemetry lifecycle: define Active → Stopping → Stopped; additive async disposal performs bounded final flush and coordinates one in-flight exporter. Sync Dispose stays ABI-compatible and has documented best-effort cancellation/drop semantics, never delivery guarantee. No new recording after Stopping; terminal loss is observable, including queued events before first timer, timeout/cancellation and repeated disposal. Tests cover record/flush/dispose race and failed batch at shutdown. Legacy exporter delegates without cancellation cannot be forcibly stopped: bound waiting, detach safely without unbounded task creation, prohibit new exports after stop, document in-flight limitation.

<!-- RT-05 RT-03: legacy completion/config compatibility merged -->
Separate timer callback from public `FlushEvents(object?)`. Timer uses async nonblocking path; public FlushEvents retains synchronous completion for a successful bounded export (not fire-and-forget), with existing immediate-after-call behavior tests and documented timeout/failure semantics. Avoid captured synchronization-context deadlock. TelemetryConfig primary constructor/deconstruction stays unchanged; bounds are non-positional init properties or a separate options object, with old-binary consumer and serialization/default tests.

- [x] Requested unavailable encryption cannot write false-protected plaintext.
- [x] Buffered/direct SARIF and audit logs redact the same hostile corpus.
- [x] Assessment containment rejects escapes with stated link/TOCTOU limits.
- [x] Baseline/telemetry tests prove cancellation, bounds, atomicity and no-egress.
- [x] Nested output is deterministic and emitted TypeScript parses/compiles.

## Risk Assessment

Credential changes affect compatibility; clear failure beats a false protection claim. Path resolution is platform-sensitive. Do not claim hostile-filesystem race safety or add remote/HTTP security capability.
