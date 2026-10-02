# Phase 1: InfoBar Discovery

## Overview
Implement an InfoBar that appears when a solution is loaded without a `.dataguard.yml` file. This addresses the discoverability requirement without intrusive modal dialogs. The InfoBar will feature an "Initialize Configuration" button that invokes the Init command.

## Requirements
- **Functional**:
  - Hook into Solution Load events.
  - Check for `.dataguard.yml` in the solution root.
  - Display an InfoBar if missing.
  - Track dismissal state (session-level) so it doesn't repeatedly nag if closed manually.
  - Close InfoBar automatically if the file is created.
- **Non-functional**:
  - Must not block the UI thread during file I/O.
  - Clean up event handlers on package disposal.

## Architecture
- Use `IVsInfoBarUIFactory` and `IVsInfoBarHost` to display the banner.
- Add an `IVsSolutionEvents` listener in `DataGuardPackage` (or a dedicated service) to trigger the check.

## Tests (TDD)
1. Write `InfoBarManagerTests.cs` (mocking `IVsInfoBarUIFactory` and `SVsSolution`).
2. Test: Missing file -> InfoBar displayed.
3. Test: Existing file -> No InfoBar.

## Implementation Steps
1. Create `src/DataGuard.VisualStudio/UI/InfoBarManager.cs`.
2. Implement `SVsSolution` event listener to check `Path.Combine(solutionDir, ".dataguard.yml")`.
3. If missing, construct `InfoBarModel` with text "DataGuard is inactive. Configuration file missing." and an `InfoBarActionItem` mapped to the new Init command ID.
4. Hook `InfoBarManager` initialization into `DataGuardPackage.InitializeAsync`.
5. Ensure `InfoBarManager` implements `IDisposable` to unhook events.
## Success Criteria
- [x] Verifiable acceptance criteria (Missing .dataguard.yml triggers InfoBar check, existing file skips)
- [x] Test validation commands (`dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj`)
## Related Files
- Create: `src/DataGuard.VisualStudio/UI/InfoBarManager.cs`
- Create: `tests/DataGuard.VisualStudio.Tests/InfoBarManagerTests.cs`
- Modify: `src/DataGuard.VisualStudio/DataGuardPackage.cs`
