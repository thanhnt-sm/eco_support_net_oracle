# Fix DataGuard.VisualStudio Build — Missing `using Microsoft.VisualStudio.Shell`

## Context

`scripts/build-extensions.bat` fails with 15 CS0246 errors in `DataGuardPackage.cs` — every VS SDK type (`AsyncPackage`, `PackageRegistration`, `ProvideBindingPath`, `ErrorListProvider`, `ServiceProgressData`, etc.) unresolved. The csproj correctly references `Microsoft.VisualStudio.SDK 17.14.40265` and MSBuild resolves fine (VS 18 Enterprise). Every other `.cs` file in the project (`BindingRedirects.cs`, `DataGuardLogger.cs`, `DataGuardOptionsPage.cs`, `DataGuardRulesOptionsPage.cs`) already has `using Microsoft.VisualStudio.Shell;`. Only `DataGuardPackage.cs` is missing it.

## Approach

### Step 1: Add missing `using` directive to `DataGuardPackage.cs`

Add `using Microsoft.VisualStudio.Shell;` to `src/DataGuard.VisualStudio/DataGuardPackage.cs` after line 20 (`using Microsoft.VisualStudio.Shell.Interop;`), consistent with the alphabetical-by-sub-namespace ordering already present (lines 19–22: `Microsoft.VisualStudio`, `Microsoft.VisualStudio.Shell.Interop`, then `Microsoft.VisualStudio.TextManager.Interop`).

Insert between lines 19 and 20 so the block reads:
```csharp
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
```

No other file changes required. No new code, no signature changes, no callers affected.

## Verification

```powershell
# From repo root
.\scripts\build-extensions.bat
```

Expected: both VS Code and Visual Studio extension builds succeed. The 15 CS0246 errors disappear. VSIX artifact produced at `artifacts/visualstudio/dataguard-visualstudio-*.vsix`.

## Assumptions & Contingencies

- If `packages.lock.json` is stale after any recent package version change, the locked restore may fail. Fallback: delete `src/DataGuard.VisualStudio/packages.lock.json` and let MSBuild `/restore` regenerate it.

## Status: COMPLETED
- [x] Added `using Microsoft.VisualStudio.Shell;` to `src/DataGuard.VisualStudio/DataGuardPackage.cs`.
- [x] Corrected `inventory` list type in `tests/DataGuard.VisualStudio.Tests/RuleInventoryTests.cs` to `DataGuardPackage.RuleInventoryItem`.
- [x] `DataGuard.VisualStudio.Tests` passes: 60 passed, 0 failed, 1 skipped.
- [x] `scripts/build-extensions.bat` passes cleanly: VS Code and Visual Studio VSIXes built without warnings or errors.
- [x] Artifact generated: `artifacts/visualstudio/dataguard-visualstudio-0.2.3.vsix`.
