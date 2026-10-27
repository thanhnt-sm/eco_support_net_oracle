# Error List missing-document unit contract

The original `NavigationTests.Navigate_WhenFileMissing_WritesOutputMessage` was an unconditionally skipped, comment-only placeholder. Removing its skip alone would not test anything. The same test name now has executable assertions in `NavigationTests.MissingDocument.cs`; those guard tests also compile into `DataGuard.Core.Tests` on net9.

`DocumentNavigation.CanNavigateAsync` is the production guard called by the Error List event handler. A missing document returns false after awaiting the existing message through the optional output sink. An existing document returns true without writing a missing-file message. Writer completion and failures remain part of the returned task. The presenter returns immediately when the guard denies navigation.

The existing shell body stays inline in its original JoinableTaskFactory operation, after its existing UI-thread switch. Document opening, caret placement, fallback provider navigation and output strings are unchanged. A temporary extraction made these existing shell lines appear as uncovered additions; that extraction was unnecessary for testing the missing-file guard and was removed. No shell behavior or defense was removed to change coverage.

Run the four shared guard contracts with:

```sh
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~NavigationTests.Navigate_
```

They assert exact missing-file output, false decisions with/without an output sink, awaited writer failure and a true decision for an existing owned temporary file. Mutation checks must fail executed assertions if the message is removed or the missing-file decision permits navigation. Fixture files are uniquely created and only the owned file is deleted.

`NavigationTests.EventRouting.cs` additionally invokes the real task's `IVsTaskItem.NavigateTo` in net472 unit tests with a NoOp JoinableTaskContext and a mocked service provider. With an output sink it holds the writer incomplete, asserts that the event operation is pending, completes the writer, then joins that specific operation with a 15-second cancellation bound so exceptions propagate. The outer factory is separate from the event's collection to avoid self-joining. Without an output sink it asserts that no event operation remains. Both cases assert exact output behavior and zero shell service requests; collection emptiness alone is not exception-success evidence. These cases require exact-head Windows CI; source compilation or guard tests on net9 do not prove they ran.

These tests do not establish real IDE event dispatch, real COM service behavior, editor/frame/caret navigation or VSIX installation/load. A NoOp context does not verify UI thread affinity. Those remain separate integration acceptance work. No workflow, installer, trust/consent, credentials or security settings are changed.
