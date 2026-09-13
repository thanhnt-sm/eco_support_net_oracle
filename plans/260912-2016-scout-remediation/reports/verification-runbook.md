---
type: verification-plan
date: 2026-09-12
---
# Verification runbook — implementation session

## Context

These are **future verification commands**, not results from this planning turn. Baseline results live in the Luna verification report. Terra records cwd/exit/time/tree fingerprint and actual assertions for each run; Sol independently checks evidence before closure. Use the selected ck:test instructions in the future session. Do not run DB writes/credentials or external workflows without explicit appropriate authority.

## Local deterministic gates

At repository root, after focused failing-before/passing-after fixtures:

```sh
dotnet restore DataGuard.sln --locked-mode
dotnet build DataGuard.sln --configuration Release --no-restore
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/DataGuard.GoldenCorpus.Tests/DataGuard.GoldenCorpus.Tests.csproj --configuration Release --no-build
dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj --configuration Release --no-build
dotnet test tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj --configuration Release --no-build
git diff --check
./scripts/verify_docs_sync.sh
```

Run tests separately to avoid log filename collisions. Add unique TRX names/results directories under a freshly created, policy-approved `.tmp/` run directory. Record assertion counts, not merely exit 0. docs-sync only checks required file presence; also validate changed local links, source claim references, CLI example syntax and semantic EN/VI parity.

In `src/DataGuard.VSCode`:

```sh
npm ci
npm test
npm audit --json
npm audit --omit=dev --json
```

Use installed `node_modules/.bin/vsce package --out <absolute-ignored-run-directory>/dataguard-vscode.vsix` for packaging smoke; do not invoke publish. The placeholder must be replaced with the exact validated run path, not run literally. Avoid writing generated VSIX to tracked source/root. Audit exit nonzero is evidence requiring disposition, not a reason to hide output; no forced major upgrade.

## Integration truth gate

Current package versions: xUnit2.9.3/runner4.0.0, Testcontainers.MsSql4.14.0 (`tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj:19–27`). Select a supported discovery-time skip/test-case mechanism for this installed combination; do not assume v3 runtime skip APIs. First change silent-return fixtures, then verify optional-disabled tests are skipped and required-enabled fixtures fail on unavailable infrastructure.

Required switch: `DATAGUARD_RUN_SQLSERVER_INTEGRATION=1` is accepted by both
SQL Server fixture classes (the historical `DATAGUARD_REQUIRE_LIVE_SQLSERVER=1`
alias remains supported). The dedicated filtered profile must produce DB-backed
assertions. xUnit 2.9.3 does not support dynamic discovery-time skips, so the
optional fixture records infrastructure absence and returns; required mode throws
on startup failure. SQLServer catalog/browse-mode and
all-provider snapshot assertions need actual authorized fixture markers; creating
disposable fixture DDL is distinct from production schema mutation and requires
an authorized isolated test environment.

Oracle/PG/MySQL connection/profile names must be finalized at CP1 and recorded in execution-evidence; avoid invented ready-to-run secret-bearing commands. Credential values never enter report/CLI command history. Database tests must never call arbitrary REF CURSOR routines merely to populate result shape or prove execution.

## Windows and host gates

Visual Studio project is outside `DataGuard.sln`; solution build cannot close it. Existing reference packaging steps: `.github/workflows/marketplace.yml:177–194` and `.github/workflows/build_release.yml:228–247`. Reuse local Windows MSBuild/VSSDK invocation and assert resulting VSIX for the exact tested tree. Do not dispatch/push/publish workflow to get evidence without user authority. Windows host tests include Run/Cancel, timeout/process cleanup, error/SARIF containment, DPAPI and junction behavior. Node pure tests alone do not prove VSCode extension-host behavior; include focused host smoke or leave that AC blocked.

## Evidence and rollback

- Before mutation, capture API compiled-consumer fixture plus serialized legacy fixture; retain source test data, not user secrets.
- Snapshot failure/cancel/disk-full preserves old target. Hooks managed marker/backup tests preserve foreign content. Do not use git reset/checkout to roll back user work; scoped apply_patch only after reviewing own diff.
- Raw logs ignored; sanitized durable excerpts go in `reports/execution-evidence.md` (future artifact). Fingerprint includes untracked source changes so same HEAD alone is not proof of same tested tree.
- F6 retain/owner-blocked, V02–V04 missing infrastructure and F5 release proof do not disappear when local tests pass. Final totals must reconcile all66 rows.

## Next steps

Map every S scenario from [risk-scenarios](risk-scenarios.md) to an AC and owner before phase execution. Resolve test fixture infrastructure early enough that no batch closes on a false PASS. Full integration/platform closure remains phase7; focused verification runs in each implementation batch.
