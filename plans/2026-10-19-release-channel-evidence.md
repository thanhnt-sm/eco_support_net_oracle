# Release helper channel evidence / Bằng chứng từng kênh phát hành

Baseline: main `65047c2c2c2428636780b997042c5984fa1476cf`. Branch: `fix/release-channel-evidence`.

## Scope and priority / Phạm vi và ưu tiên

P1 SA-SRCBUG-003: remove unconditional all-platform success. Restore missing workflow API response retrieval and report the actual publication step, particularly NuGet skipped inside a green job. No SQL Server changes, dependency upgrades, workflow changes or real publication.

1. Reproduce with hermetic git/curl stubs (no real credentials or network): original six cases produced one false-banner assertion failure and five monitoring timeouts.
2. Preserve callers: `dg-git release` uses exec; Windows launcher delegates Bash. Exit 0 remains compatible with tag submission without monitoring and successful workflows with skipped/unverified channels; observed failures and monitoring errors return nonzero. No existing banner-dependent caller found.
3. Require exact tag/SHA/push event, newest matching run and selected attempt jobs. Use final GitHub publication step, not draft creation. Missing/duplicate/renamed/truncated evidence remains unverified; no blanket success claim.
4. Run jq/Python parity and adversarial cases, existing shell tests, Python suite, docs checks and canonical pre-push gates. Independent review before completion. Hosted checks after an authorized push; never bypass review or CI.

## Source / docs / tests

- `tools/git-tools/dg-release`: monitor, classify, report; no publish/dispatch behavior added.
- `scripts/tests/test_dg_release_status.py`: isolated synthetic orchestration, API errors, wrong identity, skipped/failing/missing publication steps, stale success, draft-only evidence, OIDC fallback, parser parity.
- `docs/operations/release_guide.md` and `.vi.md`: status/exit-code contract and verification limits.
- `docs/product/research-backlog.md`: source fix status without claiming real registry validation.

## Acceptance / Tiêu chí chấp nhận

Each channel is `succeeded`, `skipped`, `failed` or `unverified`. No-token tag submission does not claim publication. NuGet skipped push never becomes succeeded. API errors remain visible; timeouts do not silently become success. Marketplace stays skipped/paused. Independent registry/signature/editor acceptance remains outside synthetic evidence.

Verification: .NET SDK 9.0.319 and Node 22.23.3 verified before edits. All 28 new jq/Python tests passed; complete Python suite 292 tests (291 passed, one existing unavailable GitHub API check skipped). Existing git-tools shell suites passed. Node compile, strict generated-contract type check and 19 test entries passed. Independent review reproduced parser discrepancies; fixes and regression cases passed the independent 28-test rerun, with no remaining blockers.

Canonical local gates passed: build, analyzers, format, actionlint, docs synchronization, .NET tests/coverage and NuGet audit. Linux runs the cross-platform solution: 2,259 passed, five Windows junction cases skipped. Measured C# source-line union is 17,084/21,006 (81.33%); this does not measure shell/Python code or whole-product/Windows coverage. The stock SKIP_ACT=1 hook path does not run heavy TruffleHog history/act Docker simulation locally; no claim those ran. No authenticated publication, Windows launcher/editor acceptance or independent registry/signature verification. Hosted PR CI remains pending until publication is authorized and succeeds.
