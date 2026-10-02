---
phase: 1
title: "Dockerfile NuGet Lockfile Pinning"
status: completed
priority: P1
effort: "1h"
dependencies: []
---

# Phase 1: Dockerfile NuGet Lockfile Pinning

## Overview
Remediate OpenSSF Scorecard warning `Warn: nugetCommand not pinned by hash: Dockerfile:40`. Ensure the Docker multi-arch build stage restores NuGet dependencies deterministically by copying committed `packages.lock.json` files and invoking `dotnet restore` with `--locked-mode`.

## Requirements
- **Functional Requirements**:
  - The `Dockerfile` build stage MUST copy `packages.lock.json` alongside `Directory.Build.props` and every referenced project's `.csproj` before restoring.
  - Line 40 `dotnet restore` MUST pass `--locked-mode` to enforce tamper-proof cryptographic hash validation against committed lockfiles.
  - The build MUST succeed across both `linux/amd64` and `linux/arm64` targets without network-floating package dependencies.
- **Non-functional Requirements**:
  - Preserve BuildKit layer caching (copy only props, csproj, and lockfiles in the cacheable restore layer).
  - Comply with Scorecard `Pinned-Dependencies` check (elevating from 9/10 to 10/10).

## Architecture
```
Dockerfile (Build Stage)
├── COPY Directory.Build.props .
├── COPY *.csproj + *.packages.lock.json (9 projects)
└── RUN dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode -r "linux-$arch"
```
Referenced projects requiring lockfiles in Dockerfile:
1. `src/DataGuard.Core/`
2. `src/DataGuard.Contracts/`
3. `src/DataGuard.SqlClassification/`
4. `src/DataGuard.Analyzers/`
5. `src/DataGuard.SqlServer.Adapter/`
6. `src/DataGuard.Oracle.Adapter/`
7. `src/DataGuard.MySql.Adapter/`
8. `src/DataGuard.PostgreSql.Adapter/`
9. `src/DataGuard.Cli/`

## Related Code Files
- Create: `scripts/tests/test_dockerfile_pinning.py` (TDD assertion suite)
- Modify: `Dockerfile` (lines 20–41)
- Verify: `src/*/packages.lock.json` (ensure lockfiles exist and match)

## Implementation Steps (TDD: Red -> Green -> Verify)

### 1. Step 1 (RED): Author Test Suite `scripts/tests/test_dockerfile_pinning.py`
Write Python unit test verifying:
1. `Dockerfile` contains `--locked-mode` in every `dotnet restore` invocation.
2. For every `.csproj` copied with `COPY --link`, a corresponding `packages.lock.json` copy directive exists immediately adjacent or in the restore layer.
Run test:
```bash
python3 -m unittest scripts/tests/test_dockerfile_pinning.py
```
**Expected Outcome**: **FAIL (RED)** — `AssertionError: '--locked-mode' not found in dotnet restore command`.

### 2. Step 2 (GREEN): Update `Dockerfile`
Edit `Dockerfile` around lines 23–40:
```dockerfile
# Copy project files and lock files first for optimal layer caching on restore.
COPY --link Directory.Build.props .
COPY --link src/DataGuard.Core/DataGuard.Core.csproj src/DataGuard.Core/
COPY --link src/DataGuard.Core/packages.lock.json src/DataGuard.Core/
COPY --link src/DataGuard.Contracts/DataGuard.Contracts.csproj src/DataGuard.Contracts/
COPY --link src/DataGuard.Contracts/packages.lock.json src/DataGuard.Contracts/
COPY --link src/DataGuard.SqlClassification/DataGuard.SqlClassification.csproj src/DataGuard.SqlClassification/
COPY --link src/DataGuard.SqlClassification/packages.lock.json src/DataGuard.SqlClassification/
COPY --link src/DataGuard.Analyzers/DataGuard.Analyzers.csproj src/DataGuard.Analyzers/
COPY --link src/DataGuard.Analyzers/packages.lock.json src/DataGuard.Analyzers/
COPY --link src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj src/DataGuard.SqlServer.Adapter/
COPY --link src/DataGuard.SqlServer.Adapter/packages.lock.json src/DataGuard.SqlServer.Adapter/
COPY --link src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj src/DataGuard.Oracle.Adapter/
COPY --link src/DataGuard.Oracle.Adapter/packages.lock.json src/DataGuard.Oracle.Adapter/
COPY --link src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj src/DataGuard.MySql.Adapter/
COPY --link src/DataGuard.MySql.Adapter/packages.lock.json src/DataGuard.MySql.Adapter/
COPY --link src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj src/DataGuard.PostgreSql.Adapter/
COPY --link src/DataGuard.PostgreSql.Adapter/packages.lock.json src/DataGuard.PostgreSql.Adapter/
COPY --link src/DataGuard.Cli/DataGuard.Cli.csproj src/DataGuard.Cli/
COPY --link src/DataGuard.Cli/packages.lock.json src/DataGuard.Cli/

RUN arch="$TARGETARCH"; [ "$arch" = "amd64" ] && arch="x64"; \
    dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode -r "linux-$arch"
```
Re-run test:
```bash
python3 -m unittest scripts/tests/test_dockerfile_pinning.py
```
**Expected Outcome**: **PASS (GREEN)**.

### 3. Step 3 (VERIFY): Local Build Simulation
Run local CLI restore with `--locked-mode` to ensure committed lockfiles are fully consistent with current package references:
```bash
dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode -r linux-x64
```
**Expected Outcome**: Zero NU1004 errors. Clean restore.

## Success Criteria
- [x] `scripts/tests/test_dockerfile_pinning.py` passes 100%.
- [x] `Dockerfile` contains `--locked-mode` on `dotnet restore`.
- [x] All 9 project `packages.lock.json` are copied into the Docker build layer.
- [x] Local deterministic restore passes without lockfile mismatches.

## Risk Assessment
- **Risk**: A developer updates a `<PackageReference>` in `.csproj` without updating `packages.lock.json`, causing the Docker build to fail with `NU1004: The build failed because restore was run with --locked-mode and the lock file was out of sync`.
- **Mitigation**: CI already runs `dotnet restore --locked-mode` on all pull requests; the new test in `scripts/tests/test_dockerfile_pinning.py` guards Dockerfile parity.
