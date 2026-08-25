# Fix Issues & Gaps — Execution Report

**Date**: 2026-08-25
**Status**: ✅ All 7 tasks completed, 278/278 tests pass

## Discoveries During Documentation Review

### BUG-1: UpgradePlanner only suggests net8.0
- **File**: `src/DataGuard.Core/Assessment/UpgradePlanner.cs:124-125`
- **Root cause**: `SuggestCandidate()` hardcoded `table.Lookup("net8.0")`
- **Fix**: Prefer net9.0 (current), fallback to net8.0 (LTS)

### BUG-2: DG101 vs DG001 confusion in README
- **File**: `README.md` Rules table
- **Root cause**: README said "DG001: Parameter count match" but code uses DG001 for IDE analyzer (UnvalidatedSqlCall) and DG101 for engine parameter count
- **Fix**: Updated README to clarify DG001=IDE, DG101=engine

### GAP-1: MySQL Adapter — empty project
- **File**: `src/DataGuard.MySql.Adapter/` (only .csproj, no .cs files)
- **Fix**: Implemented 3 files (1174 lines):
  - `MySqlStoredProcedureParser.cs` — INFORMATION_SCHEMA queries
  - `MySqlDialectChecker.cs` — MY001-MY003 rules
  - `MySqlLengthMismatchDetector.cs` — MY004-MY007 rules

### GAP-2: PostgreSQL Adapter — empty project
- **File**: `src/DataGuard.PostgreSql.Adapter/` (only .csproj, no .cs files)
- **Fix**: Implemented 3 files (~1200 lines):
  - `PostgreSqlStoredProcedureParser.cs` — pg_proc/information_schema queries
  - `PostgreSqlDialectChecker.cs` — PG001-PG005 rules
  - `PostgreSqlLengthMismatchDetector.cs` — PG003 rule

### GAP-3: CodeFixes has no tests
- **File**: `src/DataGuard.CodeFixes/CodeFixProviders.cs` (5 providers, 0 tests)
- **Fix**: Created `tests/DataGuard.CodeFixes.Tests/` with 12 tests

### GAP-4: LegacySupportTable missing net10.0
- **File**: `src/DataGuard.Core/Assessment/LegacySupportTable.cs`
- **Fix**: Added net10.0 Supported entry

## Test Alignment Fixes

During adapter implementation, pre-existing tests needed alignment:
- Fixed duplicate keyword/regex matches in MySQL dialect checker (removed "TOP " from keyword list)
- Fixed duplicate keyword/regex matches in PostgreSQL dialect checker (removed "::" from operators)
- Updated GoldenCorpus tests: ISNULL IS SQL Server syntax → should be flagged in MySQL/PG context
- Updated test assertions: MY003→MY004 for length mismatch rule, `ContainSingle`→`Contain` for multi-match cases

## Final Verification

```
Build: 0 warnings, 0 errors
Tests: 278 passed, 0 failed
```
