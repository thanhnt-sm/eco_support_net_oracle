# DataGuard Marketplace publishing

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
  both listing histories. Current source manifests say **0.3.0**, while license documentation
  preserves MIT through v0.3.0 and states GPL begins at **0.4.0**. Reconcile version/licensing
  before public release; a filename or nightly suffix does not fix embedded metadata.
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
not the CLI; the server targets .NET 9 and starts through `dotnet`. Resolve the separate engine
mismatch (`engines.vscode` ^1.85.0 versus locked languageclient ^1.91.0) before claiming support.
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

The opt-in `marketplace-release.yml` workflow now has a separate [two-phase prepare/publish contract](marketplace-automation.md). Default publication is false. Existing `marketplace.yml` and the release helper remain paused for Marketplace publication. This local implementation still needs owner-authorized hosted preparation, editor acceptance and verified existing authentication/environment access. Browser publication does not depend on restoring the old workflow. Do not dispatch old-tag publishing code or change protections to bypass these gates.
