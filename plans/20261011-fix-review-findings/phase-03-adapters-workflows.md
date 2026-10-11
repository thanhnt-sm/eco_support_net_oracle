---
phase: 3
title: "Database Adapters & GitHub Workflows"
status: pending
priority: P2
effort: "3h"
dependencies: []
---

# Phase 3: Database Adapters & GitHub Workflows

## Overview
Fix the Oracle PL/SQL cursor leak, correct the SQL Server schema normalization for length, and harden GitHub CI/CD workflows against runaway jobs and redundant PR runs.

## Requirements
- Functional: Oracle PL/SQL block must guarantee `DBMS_SQL.CLOSE_CURSOR` runs even on exception. SQL Server schema provider must convert byte lengths to character lengths for unicode types and handle `MAX` (-1) via `NormalizeMaxLength`.
- Non-functional: CI jobs must not run longer than 30 minutes. PR pushes must cancel stale runs.

## Architecture
- **Oracle Adapter:** Wrap the PL/SQL body in a `BEGIN ... EXCEPTION WHEN OTHERS THEN IF DBMS_SQL.IS_OPEN(v_cursor_id) THEN DBMS_SQL.CLOSE_CURSOR(v_cursor_id); END IF; RAISE; END;` block.
- **SQL Server Adapter:** In `SqlServerLiveQuerySchemaProvider.cs`, import `SqlServerParsers.NormalizeMaxLength` and apply it to the `maxLength` field before constructing the `ColumnDescriptor`.
- **Workflows:** Inject `timeout-minutes: 30` (or 15 for fast jobs) across all 38 missing jobs. Add `concurrency` blocks to `ci.yml` and `marketplace.yml`.

## Related Code Files
- Modify: `src/DataGuard.Oracle.Adapter/OracleReaders.cs`
- Modify: `src/DataGuard.SqlServer.Adapter/SqlServerLiveQuerySchemaProvider.cs`
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`, etc.
- Modify/Create: `tests/DataGuard.Core.Tests/Adapters/OracleReadersTests.cs`
- Modify/Create: `tests/DataGuard.Core.Tests/Adapters/SqlServerSchemaProviderTests.cs`

## Tests Before (TDD)
1. Write a test verifying that `SqlServerLiveQuerySchemaProvider` correctly halves the length of an `NVARCHAR(100)` column (returning 50) and handles `-1` properly.
2. (Optional/Mock) Verify Oracle PL/SQL structure via string comparison in unit test to ensure the `EXCEPTION` block is generated.

## Implementation Steps
1. Run local schema provider tests to see them fail on raw byte lengths.
2. Update `OracleReaders.cs` string interpolation.
3. Update `SqlServerLiveQuerySchemaProvider.cs` to use normalization.
4. Update YAML workflows. Use `actionlint` to verify workflow syntax.
5. Use `xd://typesafe_judge` to review the PL/SQL block safety.
6. Verify all tests pass.

## Success Criteria
- [ ] TDD tests passing.
- [ ] Oracle cursor no longer leaks on ORA errors.
- [ ] All CI jobs possess a strict timeout.

## Risk Assessment
- **Risk:** Modifying PL/SQL string can introduce syntax errors.
- **Mitigation:** Rely on integration tests and `jev` evaluation to validate the PL/SQL syntax.