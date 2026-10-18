# Manual Marketplace release workflow

[Tiếng Việt](marketplace-automation.vi.md) · [Browser upload without a PAT](marketplace-publishing.md)

`.github/workflows/marketplace-release.yml` is a separate, opt-in `workflow_dispatch` entry point. Its default `publish=false` only tests and prepares packages. It accepts dispatches from this repository's `main` only. Existing `marketplace.yml` remains test/package-only, the release helper's Marketplace pause stays intact, and `installers.yml` keeps its documented rolling-nightly behavior. This workflow is not release-ready merely because local tests pass: no hosted preparation, Windows toolchain, editor acceptance, environment access or publisher authentication has been exercised by this change.

## Prerequisites and current blockers

Preparation is available before release metadata is ready. With `publish=false`, an empty `release_tag` uses the immutable `source_sha` and its actual source version. Version 0.3.0 and the current VS Code engine mismatch are reported as build-only blockers in the run summary, not silently treated as supported or publishable. Matching embedded versions/identities and all test/package assertions remain mandatory. Publication still requires a stable tag at least 0.4.0 and a compatible engine floor. No versions, publishers, tags or settings are changed automatically.

Publication reuses the repository's `release` environment, already referenced by `release.yml`. Validation checks that it exists before publisher jobs become eligible; missing/inaccessible environment reads fail without creating anything. GitHub enforces whatever deployment rules are already configured. There is no new requirement for a second person, self-review prevention, administrator-bypass policy or branch-protection policy. A solo maintainer explicitly chooses the accepted hashes, target and confirmation. Independent reviewers are an optional hardened setup, not a prerequisite introduced by this patch. Existing protections must not be disabled to make a run pass. See [GitHub environment behavior](https://docs.github.com/actions/using-jobs/using-environments-for-jobs).

## Prepare exact bytes

1. In Actions, select **Manual Marketplace Release**, **Run workflow**, and branch **main**. Set `source_sha` to a full lowercase 40-character reviewed-main commit SHA; leave `release_tag` empty for an untagged build-only check, or supply its existing stable tag, and `target` to `vscode`, `visualstudio`, or `both`. Leave `publish=false`. Preparation always builds both packages.
2. Source must match the tag when one is supplied, be an ancestor of the selected main workflow revision, and have matching versions, product IDs and publisher IDs. Both Linux product/Node tests and Windows extension tests must pass. Preparation uses .NET 9.0.319, Node 22.23.3, the locked VS Code tooling and Windows build tools.
3. Retain the successful first-attempt run ID and the exact `package.vsix` files from `marketplace-release-vscode` and `marketplace-release-visualstudio`. Artifacts expire after seven days. Each contains a manifest, package checksum, SBOM and attested package/manifest identity. The VS artifact also includes the source's `vs-publish.json` and `overview.md`, whose hashes are bound to its attested manifest.
4. Install and test these exact bytes. Record source/tag/version, package SHA-256 values, VS Code activation/LSP/quickfix behavior, Visual Studio 18.10.2 editor/navigation acceptance, publisher ownership/Publisher Agreement, listing and license approval. Use a repository issue or immutable commit/blob URL as the readiness record. A readiness URL is human evidence: syntax validation does not establish that editor testing or approval happened.

A successful preparation run is a packaging result, not permission to publish. Review the actual packages and the VS listing files. Browser upload remains a separate owner-authorized path.

## Exact path from current source to acceptance

1. After this workflow is reviewed and available on main, choose the current full source SHA, blank `release_tag`, `publish=false`, and `target=both`. No PAT, new environment, tag, release or Marketplace submission is required for this preparation. Both packages remain at source version 0.3.0. Tests/package-tool errors still stop the run; warnings are not a claim of engine compatibility.
2. Download the resulting artifacts for build/install investigation only. Record the explicit version/license and engine warnings. No hosted build is claimed until this run succeeds; Windows acceptance remains necessary.
3. In a separate owner-approved source change, reconcile release versions/license and the VS Code engine floor (currently ^1.85.0 versus the locked dependency's ^1.91.0). Run relevant tests and installed-editor acceptance. This patch does not perform those edits.
4. Only for an intended release, use an approved existing stable tag at least v0.4.0 and its exact SHA for a new preparation. Accept its new hashes before a separate `publish=true` run. An untagged 0.3 artifact cannot be promoted by renaming it or changing dispatch metadata.

Preparation creates temporary Actions artifacts/attestations only. Introducing the workflow onto main is a separate repository operation: the unchanged `installers.yml` can publish a rolling nightly for qualifying main pushes, so merging this code must not be described as release-free. No merge or dispatch is performed by this local task.

## Publish only the accepted artifacts

A human starts a second dispatch from **main**, with the same tag/SHA and selected target. Set `publish=true`, `prepared_run_id` to the accepted preparation run, and each selected target's `vscode_sha256`/`visualstudio_sha256` to its accepted package hash. Supply `readiness_url` and exact confirmation text `PUBLISH <release_tag> <target>`. If the existing environment requests approval, satisfy its configured rules after reviewing that evidence; the workflow does not invent an additional reviewer.

The preparation run must be successful, first-attempt, in this repository, on this workflow's exact current main SHA. If main has advanced, or an artifact expired, prepare and accept new artifacts; do not substitute a fresh build for accepted bytes. Downloads use validated artifact IDs from that run, not a caller-supplied URL. Verification checks embedded identity/version/payload, manifest source/run binding, human hashes and GitHub attestations tied to the workflow SHA and hosted runners. Publication reruns product tests but does not rebuild the package.

Only the final publisher step receives its target's secret. Dependency installation uses no publication secret; VS Code install scripts are disabled in the publication job. VS Code uses the locked `vsce`; Visual Studio uses `Microsoft.VSSDK.BuildTools` 18.5.40034 from nuget.org. Both target jobs require the validation, Linux and Windows test jobs. Selecting `both` is not atomic: one publisher can succeed while the other fails. Inspect each outcome; retry only the failed target with matching confirmation/hash and still-valid provenance. Never republish an already-successful target merely to make the overall run green.

## Authentication and operating cost

`VSCE_PAT` and `VS_MARKETPLACE_PAT` are secret names, not evidence that credentials exist. This implementation uses a temporary PAT bridge and refuses publication on or after **2026-12-01**, pending review of supported authentication. Browser sign-in is not runner authentication. VS Code documents Entra support through `vsce --azure-credential`; this workflow does not configure an identity or implement experimental `--oidc`. Visual Studio's documented VsixPublisher interface uses `-personalAccessToken`; it does not establish an Entra alternative. Its PAT must be passed as a process argument by that tool's interface: never enable command tracing or echo it, and recognize that masking does not make process arguments a secret vault.

Authentication sources: [VS Code publishing](https://code.visualstudio.com/api/working-with-extensions/publishing-extension), [Visual Studio command-line publication](https://learn.microsoft.com/en-us/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension?view=visualstudio), [global PAT retirement](https://devblogs.microsoft.com/devops/retirement-of-global-personal-access-tokens-in-azure-devops/). Owner configuration and a reviewed migration are prerequisites; do not create broad credentials or weaken protections to unblock this workflow.

GitHub documents free standard hosted runner use for public repositories, but artifact/cache storage and other usage have allowance and billing rules. Seven-day retention limits storage; it does not guarantee every usage pattern is free. Review [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions) for the owner's account.

## Source, tests and acceptance

The workflow delegates fail-closed contracts to `scripts/marketplace_release.py`. `scripts/tests/test_marketplace_release.py` exercises synthetic malformed inputs, provenance swaps, source drift, identity/version failures, ZIP ambiguity, listing substitutions, environment checks, target isolation and default-off publication. `test_marketplace_docs.py` guards browser instructions and existing workflow behavior; workflow-policy tests require publishing jobs to depend on product tests. See [implementation plan](../plans/2026-10-18-manual-marketplace-release.md). Local actionlint, policy, tests and docs checks cannot replace a future owner-authorized, no-publish hosted preparation and real installed-editor acceptance.
