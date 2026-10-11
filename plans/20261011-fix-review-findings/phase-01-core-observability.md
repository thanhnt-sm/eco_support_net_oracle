---
phase: 1
title: "Core Concurrency & Observability Exception Fixes"
status: pending
priority: P1
effort: "4h"
dependencies: []
---

# Phase 1: Core Concurrency & Observability Exception Fixes

## Overview
Remediate the unbounded task leak in `ConcurrentValidationEngine` and the false-success reporting in `AspNetCoreObservability` due to exception masking.

## Requirements
- Functional: `ConcurrentValidationEngine.StreamAsync` must cancel its internal producer task if the consumer enumerator breaks early or throws. `AspNetCoreObservability` must propagate exceptions thrown by MVC actions and log them correctly.
- Non-functional: No deadlocks in channel production. Observability must not swallow errors.

## Architecture
- **Validation Engine:** Introduce a local `CancellationTokenSource` linked to the external token. Wrap the `await foreach` consumer in a `try/finally` block. In the `finally`, cancel the local token. Pass this local token to `ProduceAsync`.
- **Observability Filter:** Capture `var executed = await next().ConfigureAwait(false);`. Check `executed.Exception`. If not null and not handled, use `ExceptionDispatchInfo.Throw()`.

## Related Code Files
- Modify: `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs`
- Modify: `src/DataGuard.Observability.AspNetCore/AspNetCoreObservability.cs`
- Modify/Create: `tests/DataGuard.Core.Tests/Validation/ConcurrentValidationEngineTests.cs`
- Modify/Create: `tests/DataGuard.Observability.Tests/AspNetCoreObservabilityTests.cs`

## Tests Before (TDD)
1. Write a test for `ConcurrentValidationEngine` that intentionally breaks out of the `IAsyncEnumerable` after 1 item, verifying that the producer task completes and does not hang.
2. Write a test for `AspNetCoreObservability` simulating an MVC action that throws an exception, verifying that the exception propagates out of the filter and the telemetry records a failure.

## Implementation Steps
1. Execute tests to confirm they fail.
2. Update `ConcurrentValidationEngine.cs` line ~100 to link the cancellation token and use `try/finally`.
3. Update `AspNetCoreObservability.cs` line ~65 to inspect and re-throw `ActionExecutedContext.Exception`.
4. Use `xd://typesafe_judge` to evaluate the concurrency safety of the `ConcurrentValidationEngine` fix.
5. Run tests to confirm they pass.

## Success Criteria
- [ ] TDD tests written and passing.
- [ ] TypeSafe Judge (`jev`) confirms producer task cancellation safety.
- [ ] No exception masking in APM.

## Risk Assessment
- **Risk:** Improper CTS disposal leading to `ObjectDisposedException`.
- **Mitigation:** Rely on `using var cts = CancellationTokenSource.CreateLinkedTokenSource(...)`. Consult advisor on lifetime management.