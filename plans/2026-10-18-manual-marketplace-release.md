# Manual Marketplace release / Phát hành Marketplace thủ công

Reconciled base: `7e90bf0c2502e85dfa293bfff64e0f2639322260`. Original 65baf5b worktree and patches preserved. Audit fix is excluded.
Worktree: `/workspace/dataguard-marketplace-main`; earlier worktrees preserved.

## Authorized change and boundaries

The user now requests a human-started GitHub Action that verifies/packages/uploads extensions.
This deliberately expands the earlier documentation-only scope. It does not authorize a run,
credentials, environment/security settings, version/license/publisher changes, merge or publish
by this task. Existing marketplace.yml test/package jobs and PR checks remain unchanged;
installers.yml rolling-nightly side effects remain unchanged. A new workflow must have only
workflow_dispatch, default publication false, and never publish in a PR/push context.

Docs patch first: EN/VI browser instructions, no PAT required for manual browser upload,
owner-approved Publisher Agreement, publisher identity, immutable package/hash acceptance,
VS18.10.2 navigation, visibility differences, and accurate nightly/token-file guidance.
Regression tests must reject old executable publish=true/PUBLISH_MARKETPLACES=true/token-file
examples and compare documented triggers to real workflows. No production logic change.

## Implemented manual automation contract (hosted acceptance pending)

1. Human selects an immutable source SHA and target; release tag is optional for build-only and mandatory for publication. Validate source version and target
   (VS Code, Visual Studio or both). Validate strict numeric stable version, tag-to-SHA equality,
   source ancestry in main and manifest/publisher/version agreement; no moving branch, nightly,
   arbitrary artifact URL, shell interpolation or automatic version increment.
2. Preparation is secret-free and runs full relevant test/package gates. Produce an immutable
   manifest with source/tag/version/target, package IDs, SHA-256, SBOM and provenance. Artifact
   identity must bind to this repository, approved workflow revision and exact successful run.
3. Installed-editor readiness must refer to the exact accepted bytes. A fresh rebuild cannot
   silently replace a package that a human tested. Prefer two-phase dispatch: prepare first;
   publish consumes those verified artifacts by exact run/artifact identity, revalidating the
   provenance and hashes. Never accept an arbitrary run/file solely because it has a checksum.
4. Publication requires an explicit confirmation input and target-specific readiness evidence;
   selected publication jobs verify that the existing release environment exists, honor its native protections without imposing a second reviewer, and use only
   necessary read permissions. No credentials in preparation/test/install steps. VsixPublisher requires
   the PAT as an argument; do not log or trace it, and document this interface limitation.
5. Use pinned vendor publishing tooling, no PR checkout/scripts with credentials, no npm install
   lifecycle scripts in a secret-bearing step. Secret/identity references use names only.
6. Validate target-specific authentication against official research before implementing upload:
   VS Code supported vsce auth and Visual Studio supported publisher tooling may differ. Browser
   sign-in is not a runner credential. Do not invent Entra/PAT interoperability or assume the
   existing environment/secret exists. Missing setup must fail closed and be reported.
7. Tests: malicious/malformed inputs, SHA/tag drift, wrong publisher/version, swapped artifact,
   wrong repository/workflow/event/run, missing readiness/auth, default false and selected-target
   isolation, non-PR triggers, least privilege, and blocked publication after any failed gate.
   Preserve existing paused workflow/helper tests; document the new separate entry point.
8. Independent security review, actionlint/policy/tests/docs, and a hosted no-publish preparation
   run are acceptance requirements. This task does not dispatch a run. GitHub runner quota and
   publisher access are prerequisites; do not promise universally free operation.

## Separate compatibility follow-up (not this docs patch)

VS Code package.json declares engines.vscode ^1.85.0 while locked vscode-languageclient 10.1.2
requires ^1.91.0. Prefer raising the declared minimum to ^1.91.0 in a separate focused patch,
update mirrored root lock metadata if present and supported-host docs, and add a regression
comparing the package engine floor to dependency requirements. Verify installed activation/LSP
on VS Code 1.91 and the target current host. If retaining 1.85 is a product requirement, select
and independently test a compatible dependency instead; no dependency downgrade is assumed.
No version/publisher/license changes are part of that compatibility fix.

Tiếng Việt: Người dùng yêu cầu workflow do human bấm Run, mặc định không công bố. Giữ nguyên
job PR và nightly; không tạo token/setting hay chạy publish. Gói đã nghiệm thu phải giữ đúng
byte/hash, không rebuild rồi tự thay gói. Thiết kế hai bước prepare/publish cần xác minh nguồn,
run/artifact/provenance và quyền tối thiểu. Chọn auth từng Marketplace theo nghiên cứu chính thức,
không suy ra từ browser login. Engine VSCode sửa riêng, ưu tiên minimum 1.91 với test editor thật.

## Local implementation and review

Source: `.github/workflows/marketplace-release.yml`, `scripts/marketplace_release.py`. EN/VI guides: `docs/marketplace-publishing*`, `docs/marketplace-automation*`, `docs/operations/release_guide*`. Tests: `test_marketplace_release.py`, `test_marketplace_docs.py`, publishing-marker regression in `test_check_workflow_policy.py`.

Independent security review found artifact-ID extraction layout, source-vs-tooling listing metadata and ZIP path ambiguity issues; all three were corrected with failing-before/passing-after synthetic regressions. Artifacts now extract directly into the verification directory; VS listing files come from the selected source and their hashes are bound to the attested manifest; case/dot path aliases are rejected.

PAT bridge uses only named secret references and expires December 1, 2026. VS Code Entra is a documented future owner-configured path, not implemented OIDC. No source/version/engine/publisher/security-setting change or dispatch is included. Host authentication, installed editors and Windows publisher/attestation behavior remain acceptance blockers; readiness URL validation is syntax only. Final local evidence is kept outside tracked source and does not claim hosted release readiness.

## Earlier validation before solo adjustment (65baf5b; superseded by new evidence)

- 234 Python tests ran successfully with one existing live GitHub API test skipped; this is 233 passing tests, not 234 passing assertions.
- Official checksum-verified Node 22.23.3: locked npm install succeeded; 189 extension tests passed, zero skipped. Coverage: 95.83% lines across the 16 production TypeScript modules listed in the coverage manifest; editor-host activation is not covered by that claim.
- Release-helper shell fixture passed against disposable local repositories. Actionlint 1.7.12, workflow policy (six workflows), documentation synchronization and diff checks passed.
- A final SBOM regression failed before correction: PackageSupplier incorrectly contained a temp-directory path. Both jobs now identify the committed publisher and scan the final release directory; component discovery still scans source. See Microsoft sbom-tool argument documentation.
- Independent security and documentation reviews found no remaining blockers in the local contract. Opening browser-guide wording now names the legacy workflow pause explicitly.
- No new C# source change is in this patch; the separate exact-main audit fix passed 2242 cross-platform tests plus the unchanged pre-push gates. Those results are not represented as a hosted run of this new workflow.
- The new workflow remains local-only. No version bump, engine fix, publisher/security-setting change, credential setup, workflow dispatch, package publication or Marketplace PR is included.

Docker smoke was attempted with the unchanged pinned Dockerfile and a source-only local build context. The daemon is available, but the Microsoft container registry redirected blob request returned Forbidden before the image could build. No digest, registry credential or security setting was changed; Docker smoke remains blocked. Official SBOM argument reference: https://github.com/microsoft/sbom-tool/blob/main/docs/sbom-tool-arguments.md.

## Solo-maintainer adjustment on exact main 7e90bf0

Remove invented mandatory second-person/self-review/admin/branch-policy requirements. Reuse the existing release environment; validate existence and let GitHub enforce its configured rules. No settings are created or modified. Missing environment/API access remains an explicit publication blocker; preparation does not require that API or any PAT.

Allow untagged immutable-source build-only preparation using the source version; clearly report version/license and engine blockers. Keep exact identity/version/package tests and attestations. Publication remains tag/version/engine gated and cannot consume a renamed untagged preview. EN/VI guides describe the exact current0.3 build-only path, separate source reconciliation, fresh tagged preparation and explicit publication. Existing nightly side effects on main integration are stated. Tests were changed first and failed against the old contract; independent security review supports the bounded adjustment. New run evidence is stored under marketplace-solo-20261018.

## Reconciled local results (7e90bf0 plus Marketplace patch; audit excluded)

Full Release solution build succeeded with two MSB3026 file-copy retry warnings and zero errors. Cross-platform tests: 2240 passed, zero failed, five existing Windows-only skips; measured coverage17074/20995 production lines (81.32%). The known audit isolation defect is still present in this baseline; a passing run does not fix it.

237 Python tests ran:236 passed, one existing live API skip. Node22.23.3:189 tests passed, production TypeScript line coverage95.83%. Actionlint, workflow policy, docs synchronization, diff checks and disposable release-helper fixtures passed.

Actual source verification accepted untagged SHA7e90bf0 as version0.3.0 and reported both license-transition and engine blockers. Local prepare-lsp and locked vsce packaging succeeded without tags, credentials or publication. This exposed a verifier mismatch: vsce emits LICENSE.txt, not LICENSE; the strict required filename was corrected with red/green and missing-license regression tests. The actual preview passed inspection after correction. Preview SHA256:f827559e8cea1e360b6f22c25b1880e47cdcba006f8bb4d59edcfd8953a672a4. No hosted attestation/run identity or editor acceptance is claimed for this local file.

Windows package execution and hosted acceptance remain unavailable. Dockerfile/.dockerignore are identical to the prior baseline; its Microsoft registry Forbidden blocker is retained, not represented as a fresh successful smoke run. Independent review of the solo-maintainer adjustment is recorded with the external evidence. No commit, push, settings mutation, tag or dispatch occurred for this patch.
