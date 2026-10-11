---
phase: 2
title: "CLI & Language Server Entrypoint Fixes"
status: pending
priority: P1
effort: "4h"
dependencies: []
---

# Phase 2: CLI & Language Server Entrypoint Fixes

## Overview
Resolve the critical process-crashing data race in the Language Server and fix the CLI `InitCommand` returning a success exit code (0) upon failure.

## Requirements
- Functional: Language Server must handle rapid file open/close/change events concurrently without corrupting `pendingDiagnostics` or throwing `ObjectDisposedException`. CLI `init` command must return exit code 2 when a path is refused.
- Non-functional: Thread-safe dictionary access in LSP.

## Architecture
- **Language Server:** Replace `Dictionary<string, CancellationTokenSource>` with `ConcurrentDictionary<string, CancellationTokenSource>`. Use `TryRemove` to safely extract and claim ownership of the CTS before calling `Cancel()` and `Dispose()`. Ensure `finally` block in the background task only disposes if it successfully removed its *own* specific CTS instance.
- **CLI:** Add `Environment.ExitCode = 2;` before returning in `InitCommand.cs` when `!IsSafeWritablePath(output)`.

## Related Code Files
- Modify: `src/DataGuard.LanguageServer/Program.cs`
- Modify: `src/DataGuard.Cli/Commands/InitCommand.cs`
- Create/Modify: `tests/DataGuard.Core.Tests/LanguageServer/LanguageServerConcurrencyTests.cs` (or equivalent)
- Create/Modify: `tests/DataGuard.Core.Tests/Cli/InitCommandTests.cs` (or equivalent)

## Tests Before (TDD)
1. Write a CLI test asserting that `init` targeting a mocked unsafe path results in `Environment.ExitCode == 2`.
2. Write an LSP test simulating highly concurrent `didChange` and `didClose` notifications, asserting no `ObjectDisposedException` or dictionary corruption occurs.

## Implementation Steps
1. Run TDD tests to confirm they fail.
2. Update `Program.cs` to use `ConcurrentDictionary` and atomic `TryRemove(KeyValuePair)`.
3. Update `InitCommand.cs` to set the exit code.
4. Use `xd://typesafe_judge` to rigorously evaluate the LSP thread-safety fix for race conditions and disposal leaks.
5. Consult the Advisor regarding LSP state management.
6. Verify all tests pass.

## Success Criteria
- [ ] TDD tests written and passing.
- [ ] TypeSafe Judge (`jev`) certifies the LSP dictionary is thread-safe.
- [ ] CLI correctly fails automated pipelines on bad paths.

## Risk Assessment
- **Risk:** Race condition still exists if `Cancel()` is called concurrently with `Dispose()`.
- **Mitigation:** Strict single-ownership transfer using `ConcurrentDictionary.TryRemove`. If a thread successfully removes the CTS, it owns the disposal.