> **Product identity:** [StackAccord](product/strategy.md) is the product name; [DataGuard technical identifiers](product/branding.md) remain stable for compatibility. The [roadmap](product/roadmap.md) and [Marketplace draft](product/marketplace-copy.md) distinguish implementation, validation, release and plans. The instructions below were reconciled against source in SA-DOC-015; they are not publication evidence. The legacy Marketplace workflow remains paused; the separate manual route requires readiness gates.

# StackAccord Marketplace publishing

[Tiếng Việt](marketplace-publishing.vi.md) · [Other release channels](operations/release_guide.md)

## Scope and current state

Publication through the legacy `.github/workflows/marketplace.yml` remains paused. It contains
only `test`, `vscode-package` and `visual-studio-package`; it has no publication jobs or `publish`
input. Packaging, tests, checksums, SBOMs and non-PR attestations remain. `dg-release` rejects
`--publish-marketplaces` and `PUBLISH_MARKETPLACES=true`; `--no-marketplaces` overrides an old
configuration. A signed-in browser does not enable these jobs or repair GitHub CLI authentication.

Owner-authorized browser publication is a separate route after the readiness gates below.
No PAT is required for manual upload through the signed-in Marketplace portal; do not create
`VSCE_PAT`, `VS_MARKETPLACE_PAT`, a GitHub token or new settings just to use that route.
This guide documents the route, not evidence that a package is ready or has been published.

## Publisher and release gates

- Open <https://marketplace.visualstudio.com/manage> with the owner's Microsoft account.
  Verify authority to manage the intended publisher and existing listing. GitHub ownership
  does not establish Marketplace ownership. Configured IDs are publisher `thanhnt-sm`,
  VS Code `thanhnt-sm.dataguard-vscode`, and VS `thanhnt-sm.DataGuard.VisualStudio`.
  If unavailable or different, stop and resolve identity rather than silently changing it.
- A new publisher may present a **Publisher Agreement**. The owner must review and explicitly
  approve acceptance of the displayed terms. Signing in or asking for upload assistance alone
  is not authorization for an assistant to accept a new legal agreement. Record the approval
  without copying account secrets. Do not create another publisher to evade access problems.
- Freeze the final merged source SHA and select an unused release version after inspecting
  both listing histories. Current source manifests say **0.4.0**, matching the documented GPL
  transition; historical releases through v0.3.0 retain their MIT terms. Verify version/licensing
  in the actual package before public release; a filename or nightly suffix does not fix embedded metadata.
- Record each exact VSIX filename, embedded publisher/ID/version, source SHA, workflow run,
  **SHA-256**, sidecar checksum and provenance. Compare package contents with SBOM/dependencies
  and included license/notices; inspect for unintended configuration or secrets. Keep the
  approved bytes immutable; never rebuild or swap them between acceptance and upload.
- Validate support/help/privacy links and claims in the rendered listing. Retain package
  evidence outside the mutable nightly URL. A checksum proves identity, not product acceptance.

## Installed-editor acceptance

Install the actual candidate VSIX, not only a development build. For Visual Studio **18.10.2**,
record the full IDE version and package SHA-256. Check activation, bundled CLI version and
IDE-safe behavior, Error List, cancellation and process cleanup. Use saved synthetic fixtures
at 1:1 and 42:12 plus multiline SQL, Unicode, tabs and CRLF/LF: compare SARIF positions,
Error List display and double-click caret location; test Roslyn squiggles separately.
CI packaging or coordinate unit tests do not replace this live-editor acceptance.

For VS Code, verify installed activation, LSP/runtime discovery, diagnostic ranges, trusted and
untrusted workspaces, compatible CLI, timeout and deactivation. It bundles the language server,
not the CLI; the server targets .NET 9 and starts through `dotnet`. The declared engine now matches locked languageclient at ^1.91.0; installed-host acceptance
is still required before claiming support.
Test upgrade from the published version and uninstall/reinstall, including retained settings,
trust and synthetic credential removal. No live production credentials are needed.

## Browser upload: Visual Studio

Choose the verified publisher, then **New extension → Visual Studio**; for an existing listing,
use its edit/upload action. Select the approved VSIX and review populated identity and metadata.
**Save & Upload** stages the Visual Studio entry; the documented public transition is
**Make Public**. Review the preview and current listing state before that action, including
when updating an existing public listing. After publication, verify availability and install.
See the [official Visual Studio walkthrough](https://learn.microsoft.com/en-us/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension?view=visualstudio).

## Browser upload: Visual Studio Code

Choose the publisher and **New extension → Visual Studio Code**, or the existing listing's
update action, then submit the approved VSIX. Microsoft documents manual upload as an
alternative to CLI publishing. Treat submission as a publication action: do not assume the
Visual Studio **Save & Upload → Make Public** staging sequence applies to VS Code. Check the
actual validation/visibility state and listing after submission; a pending result is not success.
See the [official VS Code publishing guide](https://code.visualstudio.com/api/working-with-extensions/publishing-extension#publish-an-extension).

## Computer assistance boundaries

An assistant can inspect the signed-in page, select the intended publisher, prepare metadata,
verify local artifacts and fill the upload flow within the owner's authorization. Before an
irreversible/public step, present the concrete product, publisher, version, hash and visibility
change; use existing explicit authorization when it covers that exact action. Never infer new
agreement acceptance, publisher creation, credential creation or permission changes. If account
access, an agreement or a validation error blocks the flow, report that state; do not bypass it.
Uploading this documentation patch performs none of those Marketplace actions.

## Rolling nightly is a different publication channel

`.github/workflows/installers.yml` runs on main pushes except `paths-ignore` matches
(`**/*.md`, `docs/**`, `plans/**`) and on manual dispatch. Qualifying main pushes publish a
rolling **nightly** GitHub prerelease after required gates. Manual dispatch defaults to
`publish_prerelease=true`; choose `publish_prerelease=false` explicitly for artifact-only work.
This is not Marketplace publication. Adding regression-test code to a docs change means the
change is not necessarily covered by the documentation-only path exclusions.

The publish job deletes the previous nightly release and its tag (`--cleanup-tag`), removes a
dangling nightly tag if needed, then creates the release/tag at the current commit. It does not
replace versioned `v*` tags. Thus the nightly URL/tag is mutable and is not immutable release
provenance. Nightly VS filenames can carry a prerelease suffix while their embedded VSIX
version stays at the committed fallback; do not assume that is a valid Marketplace update.

For a versioned artifact-only bundle, `build_release.yml` packages without Marketplace,
NuGet, GitHub Release or GHCR publication; select and record the dispatch revision and inputs.
`dg-release` is different: it can create/push a tag and trigger NuGet/GitHub/GHCR release effects.
Do not use it as a browser-upload prerequisite or an extension-only shortcut.

## Separate manual automation

The opt-in `marketplace-release.yml` workflow now has a separate [two-phase prepare/publish contract](marketplace-automation.md). Default publication is false. Existing `marketplace.yml` and the release helper remain paused for Marketplace publication. The exact hosted preparation recorded in [release-claims preparation evidence](product/release-claims.md) passed. Installed-editor acceptance and verification of existing publication authentication/environment access remain pending; future artifacts require their own authorized preparation and acceptance. Browser publication does not depend on restoring the old workflow. Do not dispatch old-tag publishing code or change protections to bypass these gates.

### Visual Studio 0.4.0 preparation

The current Visual Studio source manifest and registration fallback target 0.4.0, matching the existing GPL transition documented in the bundled notices. Historical releases through v0.3.0 keep their existing terms; this change does not rewrite license text. `scripts/assert-vsix.ps1` now invokes `check-license-consistency.py --vsix <file> --expected-version 0.4.0` to check the embedded version and byte-identical canonical license/notices. A nightly filename is not evidence of the embedded version or a prerelease marker.

This is source preparation, not release approval. Both extension manifests, the VS Code lockfile and the build-helper fallback are aligned to 0.4.0 solely for the existing shared release contract. Visual Studio remains the intended publication target. The engine declaration now matches the locked vscode-languageclient 10.1.2 minimum, ^1.91.0, in package.json and the lockfile. This resolves the source contract mismatch, without claiming installed VS Code acceptance. The baseline includes PR73’s omitted optional-input fix; this readiness patch preserves that normalization and its publication checks. Build from an approved immutable release source on Windows; inspect catalog, registration, extension/analyzer/CLI informational versions, run packaging tests and record actual installation on the intended Visual Studio host before accepting the exact VSIX hash. Without an explicit override, MinVer derives development versions from Git history; do not assume they match the VSIX version. Manual preparation now passes its validated PACKAGE_VERSION through MinVerVersionOverride to the VS build and the separate CLI publish; nested analyzer builds inherit it. RequireReleaseIdentity checks actual packaged DLL/CLI file and product versions against the VSIX version, preserving MinVer 8 assembly binding identity major.0.0.0. Local explicit builds demonstrated 0.4.0 metadata; no release tag was created. Windows packaging, PE CLI inspection and the PowerShell rejection tests passed for preparation run `39176474395`, as recorded in [release claims](product/release-claims.md). These checks remain mandatory for each future prepared artifact; installed-editor acceptance is still pending. The standalone build helper derives its artifact name from VS Code; aligned filenames still do not prove the embedded Visual Studio assembly identity. No tag, workflow dispatch or Marketplace publication is authorized by these checks.

The bounded build-only result for source `121b05d` is recorded in [release-claims preparation evidence](product/release-claims.md). Its Windows identity gate passed; installed-editor acceptance and Marketplace publication remain unverified/unperformed. This does not approve later artifacts or release tags.

See [Visual Studio candidate checklist](testing/vs-beta-acceptance.md) for the bounded synthetic prepare/test/publish acceptance record and support-summary privacy checks. This checklist is not publication evidence.
