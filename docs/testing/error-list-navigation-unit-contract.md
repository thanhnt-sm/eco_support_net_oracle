# Error List missing-document unit contract

The original `NavigationTests.Navigate_WhenFileMissing_WritesOutputMessage` was an unconditionally skipped, comment-only placeholder. Removing its skip alone would not test anything. The same test name now has executable assertions in `NavigationTests.MissingDocument.cs`; the original class is partial so the guard contracts also compile into `DataGuard.Core.Tests` on net9.

`DocumentNavigation.NavigateAsync` is the production guard called by the Error List event handler. A missing document writes the existing message through the supplied output sink and never calls the navigation callback, including when the sink is absent. An existing document invokes and awaits the callback exactly once. Writer and callback completion/failures remain part of the returned task; the callback is `Func<Task>`, not `async void`.

The presenter still switches to the UI thread before calling the guard. The guard invokes an existing-document callback synchronously before its first await; the extracted VS operation also checks UI affinity before using shell services. Document opening, caret placement, fallback provider navigation and existing output strings are preserved.

Run the shared executable contracts with:

```sh
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~NavigationTests.Navigate_
```

The four unit tests assert exact missing-file output, zero navigation for missing files with/without an output sink, awaited writer failure and exactly one awaited navigation for an existing owned temporary file. Mutation checks must fail assertions if the message is removed or missing files are allowed to navigate. Test fixture files are uniquely created and only the owned file is deleted; missing-file paths are generated without reading private solutions or requiring a database.

These are unit contracts for the shared production guard. They do not exercise `IVsTaskItem.NavigateTo`, Error List event routing, `VsShellUtilities.OpenDocument`, COM, actual document/caret navigation or extension installation/load in a live IDE. Those remain separate integration acceptance work; no live acceptance is gained by replacing the placeholder with unit assertions. The net472 VisualStudio test assembly also includes the same source file and must compile/run on the exact integrated Windows CI head before merge.

No workflow, installer, trust/consent, credentials or security settings are changed by this unit seam.
