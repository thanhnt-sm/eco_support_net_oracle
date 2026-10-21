# Visual Studio candidate acceptance

[Tiếng Việt](vs-beta-acceptance.vi.md) · [Publication contract](../marketplace-automation.md) · [Support summary](../operations/support-summary.md)

This is an operator checklist, not evidence of installation or permission to publish. Record pass/fail/unavailable, exact VS build/edition, candidate SHA-256, source SHA, run ID, time and redacted evidence for every scenario. Use a disposable synthetic project outside user repositories; no production databases, secrets, migrations or user-source edits.

## Prepare → test → publish

Reuse `.github/workflows/marketplace-release.yml`; do not add another publisher workflow. Preparation defaults to `publish=false`. The existing flow builds and gates packages, records checksums/SBOM/provenance and keeps artifacts for 7 days. For a future authorized publication, use an approved existing stable numeric release tag (at least v0.4.0) matching the source. The validator rejects `v0.4.0-beta.1`: the workflow currently does not implement a separate private/prerelease beta channel. Decide distribution semantics before describing a package as a Marketplace beta.

Download the exact prepared artifact, verify its hash, embedded VSIX/publisher/version, DLL and bundled CLI identity, license/notices and provenance. Preserve these bytes through acceptance. DataGuard technical identities remain unchanged; StackAccord is the candidate display name; this does not establish publication or installed acceptance. The publish contract requires the same current main SHA as preparation, successful first attempt and accepted artifact IDs/hashes; any main advance requires fresh preparation and acceptance. Never rebuild an accepted artifact before upload.

After installed acceptance and explicit publication authorization, reuse the manual workflow with target `visualstudio`, `publish=true`, exact tag/source/prepared-run ID, accepted VS hash, readiness evidence and confirmation `PUBLISH <tag> visualstudio`. Verify the existing release environment/authentication with the owner; a secret name in source does not prove access. Do not copy credentials into chat/logs or create tokens. The current validator blocks publication from 2026-12-01 pending authentication review. A valid readiness URL proves syntax, not that testing occurred. Legacy marketplace publication and release-helper Marketplace publication remain paused.

## Installed VS 18.10.2 scenarios

1. Obtain installation authorization; verify the actual host build rather than assuming the requested version is installed. Install the hash-verified candidate and record activation, bundled CLI identity and settings.
2. Validate an offline synthetic fixture in IDE-safe mode. Check Error List and CLI/SARIF correspondence. Check cancellation, timeout/process cleanup and UI responsiveness.
3. Exercise 1:1 and 42:12, multiline SQL, Unicode, tabs and both CRLF/LF. Compare SARIF coordinates, Error List display and double-click caret position. Exercise Roslyn squiggles separately.
4. Exercise DG019 with explicit Oracle configuration. Unsupported syntax stays unevaluated (normally exit 3); an allow-unevaluated option does not mean validation succeeded. Offline fixtures do not prove live Oracle behavior. A live test requires separately authorized disposable resources.
5. Preview quick fixes only in the disposable fixture, inspect the diff, undo and retest. Do not edit real user code without authorization.
6. Test upgrade from an approved previous version, settings retention, uninstall/reinstall and duplicate analyzer behavior.
7. Test support preview, cancel, save-dialog cancel and exact preview/file equality. Confirm absence of synthetic secrets, SQL, raw paths and diagnostic messages. Check bounded logs and rotation. Only transfer an explicitly approved summary; never automatically upload ActivityLog, audit files, source or crash dumps.

## Release blockers and bounded backlog

Block release until installed-host/navigation acceptance, immutable package identity/license, distribution semantics, listing/publisher authority and final readiness approval are recorded. Mark Windows/editor checks unavailable when no host is accessible. Full provider grammar parity and future enterprise/agent features are backlog unless a defect contradicts an advertised candidate feature. VS Code acceptance remains separate if this release targets only Visual Studio. Computer assistance must respect these boundaries; this document does not assert that a computer-use tool is available.

Display-only Beta transition: the menu/Output pane is **StackAccord (Beta)**; **Tools > Options > DataGuard**, command IDs, CLI/config, diagnostics and file names remain unchanged. Verify identity/settings retention on the exact Windows artifact.

Windows package unit tests execute `OutputPaneWriter` from the production assembly with a no-op JTF context and VS Output service doubles. They check pane identity/display, offline/live banners, redaction and unavailable/failed pane services. These tests do not verify UI-thread affinity or instantiate an installed VS host or exercise consent/export modal dialogs; retain the manual acceptance steps above. Source-reading branding tests are not runtime coverage.
