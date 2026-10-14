# Analyzer vs compiler version (2026-10-14, Release build @ 3975c06)

Consumer project (scratch, net9.0) with `<Analyzer Include=…/DataGuard.Analyzers.dll|DataGuard.Contracts.dll|DataGuard.SqlClassification.dll>` and this source:

```csharp
public class Repo { public void Go(IDbConnection c) { c.Query<Customer>("SELECT * FROM Customers"); c.Query<Customer>("SELECT CUSTOMER_ID FROM CUSTOMERS WHERE X"); } }
```

| Analyzer built against | `dotnet build` (SDK 9.0.100, Roslyn 4.12.0) result |
|---|---|
| `Microsoft.CodeAnalysis.CSharp 5.9.0` (repo HEAD, `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:24`) | `warning CS9057: The analyzer assembly '…/DataGuard.Analyzers.dll' references version '5.9.0.0' of the compiler, which is newer than the currently running version '4.12.0.0'.` — **no DG diagnostics** |
| `Microsoft.CodeAnalysis.CSharp 4.12.0` + `Microsoft.CodeAnalysis.Analyzers 3.11.0` (throw-away copy of the project, source unchanged, `-p:RestoreLockedMode=false`) | builds with 0 errors; consumer gets `warning DG001` ×2 (`SQL call 'Query' not validated - run 'dataguard check' …`) and `warning DG017: Avoid SELECT *` |

Notes:
- `Microsoft.CodeAnalysis.Analyzers 4.12.0` does not exist on nuget.org (restore resolves 4.14.0 → NU1603 under warnings-as-errors); 3.11.0 is the matching line for Roslyn 4.12.
- The SDK's own `Roslyn/bincore/Microsoft.CodeAnalysis.dll` is 4.12.0.0; VS 2022 17.12 ships the same line.
- The `5.9.0` reference entered with `d0ec928` (PR #40) together with the analyzer rewrite.
- The DG001 message text names a `dataguard check` command that does not exist (`DiagnosticDescriptors.cs:89`); the CLI command is `validate`.
