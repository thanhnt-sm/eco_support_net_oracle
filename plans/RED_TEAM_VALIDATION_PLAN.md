# Red Team Validated Plan: Visual Studio Extension Rule Validation, Line Mapping & Stored Procedure Scanning
> **Status**: COMPLETED (Phases 1, 2, 3 fully implemented, tested, and verified)  
> **Verification**: 73 passing tests across target suites (0 failed, 1 documented VS shell integration skip)


## Context & Objectives
The Visual Studio Extension's validation pipeline requires rigorous stabilization:
1. **Line Mapping & Caret Navigation**: Ensure diagnostics emitted by `DiagnosticEmitter.cs` and published via `SarifErrorListPublisher.cs` correctly translate to Visual Studio `ErrorTask` entries and buffer caret positions without off-by-one errors.
2. **Stored Procedure Detection**: Ensure ADO.NET and Dapper stored procedure calls (`CommandType.StoredProcedure`) are captured accurately by Roslyn AST extraction in `ProjectCSharpSqlSource.cs` and surfaced through Roslyn analyzers in `DataGuard.Analyzers`.
3. **Validation Engine Concurrency & UI Reporting**: Maintain bounded concurrency, cancellation propagation, and thread-safe progress accumulation between `ConcurrentValidationEngine.cs` (DataGuard.Core) and `RuleInventory` (DataGuard.VisualStudio).

---

## Red Team Review Summary
Adversarially reviewed across 4 hostile lenses (Layering & Architecture, Security & Dialect Boundaries, Concurrency & Reliability, Headless TDD Completeness) with structured adjudication via **TypeSafe System One** (`xd://typesafe_judge`).

| ID | Finding | Severity | Lens | Evidence Citation | TypeSafe Adjudication | Disposition |
|:---|:---|:---:|:---:|:---|:---:|:---:|
| **F1** | **False Coupling of Engine & UI Model**: Grouping `ConcurrentValidationEngine` (Core) with `RuleInventory` (VS UI) breaks layer boundaries. Engine receives rules via abstraction; `RuleInventory` parses CLI stderr. | Critical | Layering & Architecture | `ConcurrentValidationEngine.cs:30-46`<br>`RuleInventory.cs:1-50` | Validity: 0.59<br>Actionability: 1.39 | **Accept** (Decouple layers) |
| **F2** | **Architectural Impossibility in SqlClassifier**: Proposing Roslyn AST / `CommandType.StoredProcedure` scanning inside `SqlClassifier.cs` violates its zero-dependency `netstandard2.0` contract. AST scanning already belongs in `ProjectCSharpSqlSource.cs`. | Critical | Architecture & Dependencies | `DataGuard.SqlClassification.csproj:3-6`<br>`ProjectCSharpSqlSource.cs:223-470` | Validity: 0.95<br>Actionability: 1.96 | **Accept** (Scope to Core AST) |
| **F3** | **Misdiagnosed SARIF Line Indexing**: Altering `DiagnosticEmitter.cs` to 0-based would violate SARIF 2.1.0 standard and break CLI/CI tools. Index translation belongs strictly at the presentation boundary in `ErrorListPresenter.cs`. | High | Format Standards & Integration | `DiagnosticEmitter.cs:114-117`<br>`ErrorListPresenter.cs:116-165` | Validity: 0.85<br>Actionability: 1.88 | **Accept** (Protect SARIF 1-based) |
| **F4** | **Unexecutable Manual Verification**: Requiring manual experimental VS Hive launches prevents automated CI/TDD verification and fails regression coverage. | Medium | CI & TDD Completeness | `NavigationTests.cs:58-67` | Validity: 0.91<br>Actionability: 1.47 | **Accept** (Headless automated tests) |
| **F5** | **Inverted TDD Workflow**: Placing test verification as a trailing step after implementation violates `--TDD`. Plan must enforce Red-Green-Refactor phase structure. | High | TDD Process | `RED_TEAM_VALIDATION_PLAN.md:4-9` | Validity: 0.86<br>Actionability: 1.85 | **Accept** (Phased TDD structure) |

---

## Validation Log (TypeSafe Prioritized Decisions)
Candidate validation questions scored by implementation impact via **TypeSafe System One** (`xd://typesafe_judge`):

1. **Rule Failure Propagation & Concurrency (Impact Score: 2.38 / Confidence: 0.59)**
   - *Question*: How should `ConcurrentValidationEngine` handle rule failure exceptions and progress reporting?
   - *Decision*: Maintain bounded `Channel<ContractViolation>` backpressure; rule failures must fault the channel rather than drop violations silently (`ConcurrentValidationEngine.cs:124-129`). Lock-guard all `_ruleInventory` access sites in VS package (`DataGuardPackage.cs`).

2. **TDD Verification Strategy (Impact Score: 2.36 / Confidence: 0.45)**
   - *Question*: What is the automated test harness strategy for VS Error List navigation and rule execution?
   - *Decision*: Use headless unit/integration tests with in-memory SARIF fixtures (`NavigationTests.cs`, `SarifErrorListPublisherTests.cs`) and Roslyn compilations (`RedTeamRegressionTests.cs`). Keep manual experimental shell launches as non-blocking smoke checks.

3. **Line Mapping & SARIF Boundary (Impact Score: 2.30 / Confidence: 0.52)**
   - *Question*: How should line number discrepancies in the Error List be resolved without breaking external tools?
   - *Decision*: Keep `DiagnosticEmitter.cs` strictly compliant with SARIF 2.1.0 (1-based lines `Location.GetLineSpan().StartLinePosition.Line + 1`). Translate coordinates to VS `ErrorTask` and `IVsTextView` caret positions exclusively inside `ErrorListPresenter.cs`.

4. **Stored Procedure Architecture (Impact Score: 2.16 / Confidence: 0.67)**
   - *Question*: Where should stored procedure AST scanning and analyzer checks reside?
   - *Decision*: House AST extraction in `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` and diagnostics in `src/DataGuard.Analyzers/Analyzers.cs`. Leave `DataGuard.SqlClassification` untouched as a dependency-free SQL tokenizer.

---

## Phased TDD Implementation Plan

### Phase 1: Line Mapping & Navigation Precision (TDD) — [COMPLETED]
**Status**: Completed  
**Target Files**:
- `src/DataGuard.VisualStudio/ErrorListPresenter.cs`
- `src/DataGuard.VisualStudio/SarifErrorListPublisher.cs`
- `tests/DataGuard.VisualStudio.Tests/NavigationTests.cs`
- `tests/DataGuard.VisualStudio.Tests/SarifErrorListPublisherTests.cs`

- **Step 1.T (Red - Write Failing Tests)**:
  - Add test in `NavigationTests.cs`: `ErrorListPresenter_TaskCreation_MapsLineAndColumnCorrectlyForVSCoordinates`. Verify that for SARIF position `(Line: 42, Column: 12)`, `ErrorTask.Line` and `ErrorTask.Column` preserve the exact coordinates expected by the VS Error List UI.
  - Add test for caret navigation calculation: verify `Math.Max(0, task.Line - 1)` correctly converts 1-based SARIF line to 0-based buffer line for `IVsTextView.SetCaretPos`.
- **Step 1.I (Green - Implement)**:
  - Verified and calibrated `ErrorListPresenter.cs:116-165` to guarantee no off-by-one errors between Error List grid row and editor caret.
  - Verified `SarifErrorListPublisher.cs:22-25` (`ConvertSarifPosition`) preserves 1-based SARIF coordinates.
  - **Invariant Maintained**: `DiagnosticEmitter.cs:114-117` unmodified — SARIF 2.1.0 output strictly preserved as 1-based.
- **Step 1.V (Verify)**:
  - Command: `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj --filter "FullyQualifiedName~NavigationTests|FullyQualifiedName~SarifErrorListPublisherTests"`
  - **Outcome**: **PASSED** (17 passed, 0 failed, 1 skipped).
  - **Verification Evidence**:
    - `DiagnosticEmitter.cs` and `SarifErrorListPublisher.cs` preserve 1-based SARIF coordinates.
    - VS Error List coordinates and caret navigation buffer offset `Math.Max(0, task.Line - 1)` verified with zero off-by-one errors.
---

### Phase 2: Stored Procedure AST Scanning & Roslyn Analyzer Coverage (TDD) — [COMPLETED]
**Status**: Completed  
**Target Files**:
- `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs`
- `src/DataGuard.Analyzers/Analyzers.cs`
- `tests/DataGuard.Core.Tests/RedTeamRegressionTests.cs`
- `tests/DataGuard.Analyzers.Tests/AnalyzersTests.cs`

- **Step 2.T (Red - Write Failing Tests)**:
  - In `RedTeamRegressionTests.cs`, add test `SqlSource_DapperPositionalCommandType_DetectsStoredProcedure`: verify positional Dapper overloads passing `CommandType.StoredProcedure` (at positional arguments) are extracted with `IsStoredProcedure = true`.
  - In `RedTeamRegressionTests.cs`, add test `SqlSource_DbCommandInitializer_DetectsStoredProcedure`: verify object initializers (`new SqlCommand { CommandText = "SP_NAME", CommandType = CommandType.StoredProcedure }`).
  - In `DataGuard.Analyzers.Tests`, add test ensuring `IsPotentialSqlCall` recognizes Dapper and ADO.NET SP calls.
- **Step 2.I (Green - Implement)**:
  - In `ProjectCSharpSqlSource.cs`:
    - Extended Dapper scanning to check both named `commandType: CommandType.StoredProcedure` and positional argument values.
    - Extended ADO.NET scanning to inspect object initializers (`InitializerExpressionSyntax`), target-typed `new()`, and eliminated false-positive suffixes.
    - Emits `RawSqlDescriptor` with `IsStoredProcedure = true` and `ProcedureName`.
  - In `src/DataGuard.Analyzers/Analyzers.cs`: updated `IsPotentialSqlCall` to include SP invocations, and added `InternalsVisibleTo` in `AssemblyInfo.cs`.
  - **Explicit Non-Goal Maintained**: `SqlClassifier.cs` in `DataGuard.SqlClassification` remained untouched (`netstandard2.0`, zero-dependency contract).
- **Step 2.V (Verify)**:
  - Commands:
    - `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --filter "FullyQualifiedName~RedTeamRegressionTests"`
    - `dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj`
  - **Outcome**: **PASSED** (37 total: 20/20 Core RedTeamRegressionTests + 17/17 AnalyzersTests).
  - **Verification Evidence**:
    - Positional Dapper and ADO.NET object initializer stored procedures correctly extracted and categorized.
    - Roslyn analyzer diagnostics recognize stored procedures without false positives on method suffixes.
---

### Phase 3: Validation Engine Concurrency & Rule Inventory Thread Safety (TDD) — [COMPLETED]
**Status**: Completed  
**Target Files**:
- `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs`
- `src/DataGuard.VisualStudio/RuleInventory.cs`
- `src/DataGuard.VisualStudio/DataGuardPackage.cs`
- `tests/DataGuard.Core.Tests/ConcurrentValidationExecutionTests.cs`
- `tests/DataGuard.VisualStudio.Tests/RuleInventoryTests.cs`

- **Step 3.T (Red - Write Failing Tests)**:
  - In `ConcurrentValidationExecutionTests.cs`, add concurrency test `StreamAsync_UnderHeavyParallelism_RespectsQueueBoundsAndPropagatesFailures`: simulate high-volume rule outputs with bounded queue size and assert backpressure without deadlocks or missed violations.
  - In `RuleInventoryTests.cs`, add multi-threaded concurrent test asserting thread-safety when stderr reader appends items while UI thread enumerates or clears the inventory.
- **Step 3.I (Green - Implement)**:
  - In `ConcurrentValidationEngine.cs`: implemented bounded channel with backpressure (`BoundedChannelOptions` with `BoundedChannelFullMode.Wait`) and channel exception propagation (`channel.Writer.TryComplete(exception)`).
  - In `DataGuardPackage.cs`: protected all access sites to `_ruleInventory` (`Add`, `Clear`, enumeration) with dedicated `ruleInventoryLock`.
  - Maintained clear architectural separation between Core engine concurrency and UI model `RuleInventory`.
- **Step 3.V (Verify)**:
  - Commands:
    - `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --filter "FullyQualifiedName~ConcurrentValidationExecutionTests"`
    - `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj --filter "FullyQualifiedName~RuleInventoryTests"`
  - **Outcome**: **PASSED** (19 total: 14/14 Core ConcurrentValidationExecutionTests + 5/5 VS RuleInventoryTests).
  - **Verification Evidence**:
    - Bounded channel queue limits and backpressure verified under high parallel load without deadlock.
    - Rule failure propagation terminates gracefully with correct exceptions.
    - Multi-threaded concurrent `Add`, snapshot enumeration, and `Clear` on `RuleInventory` verified thread-safe.
---

## Automated Verification Matrix

| Phase | Automated Test Target | Command | Status | Results |
|:---:|:---|:---|:---:|:---|
| **Phase 1** | VS Error List & Navigation Coordinates | `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj --filter "FullyQualifiedName~NavigationTests\|FullyQualifiedName~SarifErrorListPublisherTests"` | **PASSED** | 17 passed, 0 failed, 1 skipped |
| **Phase 2** | Stored Procedure Roslyn AST & Analyzers | `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --filter "FullyQualifiedName~RedTeamRegressionTests"`<br>`dotnet test tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj` | **PASSED** | 37 passed (20/20 Core, 17/17 Analyzers) |
| **Phase 3** | Engine Concurrency & Thread Safety | `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --filter "FullyQualifiedName~ConcurrentValidationExecutionTests"`<br>`dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj --filter "FullyQualifiedName~RuleInventoryTests"` | **PASSED** | 19 passed (14/14 Core, 5/5 VS) |
| **Summary** | **Full Verification Suite** | Target filter suites above | **PASSED** | **73 passed, 0 failed, 1 skipped** |

---

## Execution & Verification Summary
All 3 implementation phases have been successfully implemented and verified:
1. **Phase 1 (Line Mapping & Navigation)**: Preserved 1-based SARIF coordinate invariant, verified 0-based buffer line conversion `Math.Max(0, task.Line - 1)` for `IVsTextView.SetCaretPos`, 0 off-by-one errors. (17 tests passed)
2. **Phase 2 (Stored Procedure AST Scanning)**: Implemented Dapper positional argument scanning and ADO.NET object initializer / target-typed `new()` scanning in `ProjectCSharpSqlSource.cs`, eliminated suffix false positives, updated `DataGuard.Analyzers`, preserved zero-dependency `SqlClassifier.cs`. (37 tests passed)
3. **Phase 3 (Engine Concurrency & Thread Safety)**: Configured bounded channel backpressure and exception propagation in `ConcurrentValidationEngine.cs`, safeguarded `RuleInventory` access in `DataGuardPackage.cs` with dedicated `ruleInventoryLock`, decoupled Core engine from UI model. (19 tests passed)

**Total Verification Coverage**: 73 passing tests (0 failures, 1 documented VS shell integration skip).
