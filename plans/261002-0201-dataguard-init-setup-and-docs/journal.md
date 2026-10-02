# Technical Journal: DataGuard Visual Studio Extension - Phase 1 (InfoBar Discovery)

**Date:** 2026-10-02  
**Context:** `plans/261002-0201-dataguard-init-setup-and-docs/`  
**Feature:** Phase 1 - InfoBar Discovery (`InfoBarManager`)

---

## 1. Context & Architectural Motivation

To make DataGuard discoverable without imposing intrusive modal dialogs on solution load, an InfoBar model was selected. When a user opens a solution lacking `.dataguard.yml`, Visual Studio displays an ambient, non-blocking banner at the top of the solution editor/window host offering an action button to "Initialize Configuration".

Key architectural constraints:
- Must not block the UI thread on solution load (especially critical on large network shares, slow disk I/O, or during heavy IDE startup).
- Must avoid repeatedly nagging the user across solution changes or after dismissal during a session.
- Must cleanly clean up COM/OLE event listeners on package teardown.

---

## 2. Key Technical Decisions & Design Rationale

### 2.1 Asynchronous Solution File Check & Thread Marshalling

**Decision:**  
Offload file system inspection (`File.Exists(configPath)`) completely to a background thread pool worker, then marshal back to the UI thread only if the banner needs to be rendered.

**Implementation Details:**
- `InfoBarManager.OnAfterOpenSolution` initiates an asynchronous task tracked via `LastInitializationTask`:
  ```csharp
  LastInitializationTask = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
  {
      await TaskScheduler.Default; // Explicitly switch off UI thread to background thread pool
      bool exists = File.Exists(configPath);

      if (!exists)
      {
          await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); // Marshal back to UI thread
          ShowInfoBar();
      }
  });
  ```
- **Rationale:** Standard VSSDK rules prohibit disk I/O on the main thread because it triggers GoldBar / UI unresponsive alerts. `TaskScheduler.Default` reliably yields execution to thread-pool threads, while `SwitchToMainThreadAsync()` ensures that COM interaction with `IVsInfoBarUIFactory` and `IVsInfoBarHost` occurs safely on the STA UI thread.

### 2.2 Dismissal State and Lifecycle Management

**Decision:**  
Maintain session-level dismissal state and implement full `IDisposable` cleanup.

**Implementation Details:**
- `_hasBeenDismissed` prevents resurfacing the InfoBar if closed manually via the UI close button (`OnClosed` event from `IVsInfoBarUIEvents`).
- Implements `IVsSolutionEvents` and `IVsInfoBarUIEvents`:
  - `AdviseSolutionEvents` on construction.
  - `UnadviseSolutionEvents` and `UnadviseInfoBarUIEvents` on `Dispose()`.
  - Removes the active InfoBar from `IVsInfoBarHost` upon cleanup or if `.dataguard.yml` is created/discovered.

### 2.3 Unit Testing & UI Thread Workarounds

**Problem:**  
Visual Studio SDK's `ThreadHelper.ThrowIfNotOnUIThread()` and `ThreadHelper.JoinableTaskFactory` rely on an active `JoinableTaskContext` and an initialized UI synchronization context. In standalone xUnit runner processes outside `devenv.exe`, `ThreadHelper.JoinableTaskContext` is null, causing unhandled `NullReferenceException` or `InvalidOperationException` crashes.

**Solution / Workarounds:**
1. **Reflection-Based `EnsureUIThread` Setup:**  
   Before running tests in `InfoBarManagerTests`, `EnsureUIThread()` initializes a real `SynchronizationContext` and dynamically binds a new `JoinableTaskContext` to `ThreadHelper`'s internal static fields across different VSSDK version signatures (`_joinableTaskContextCache`, `joinableTaskContext`, `s_joinableTaskContext`, etc.):
   ```csharp
   private static void EnsureUIThread()
   {
       if (SynchronizationContext.Current == null)
       {
           SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
       }

       var jtc = new JoinableTaskContext(Thread.CurrentThread, SynchronizationContext.Current);
       var field = typeof(ThreadHelper).GetField("_joinableTaskContextCache", BindingFlags.Static | BindingFlags.NonPublic)
                   ?? typeof(ThreadHelper).GetField("joinableTaskContext", BindingFlags.Static | BindingFlags.NonPublic)
                   ?? typeof(ThreadHelper).GetField("s_joinableTaskContext", BindingFlags.Static | BindingFlags.NonPublic)
                   ?? typeof(ThreadHelper).GetField("_joinableTaskContext", BindingFlags.Static | BindingFlags.NonPublic);
       if (field != null)
       {
           field.SetValue(null, jtc);
       }
       else
       {
           var prop = typeof(ThreadHelper).GetProperty("JoinableTaskContext", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
           prop?.SetValue(null, jtc, null);
       }
   }
   ```
2. **Sequential Test Execution (`DisableTestParallelization`):**  
   Because `ThreadHelper` static context is process-global, parallel test runs in xUnit can cause race conditions or cross-thread assertion violations. Applied:
   ```csharp
   [assembly: CollectionBehavior(DisableTestParallelization = true)]
   ```
3. **Deterministic Async Join in Assertions:**  
   To prevent flaky assertions where the background file check finishes after the test asserts mock invocations, `InfoBarManager.LastInitializationTask` is exposed as `internal JoinableTask?` and explicitly awaited via `manager.LastInitializationTask?.JoinAsync()` in the unit tests prior to calling `_infoBarHostMock.Verify(...)`.

---

## 3. Verification & Evidence

- Unit tests: `tests/DataGuard.VisualStudio.Tests/InfoBarManagerTests.cs`
  - `OnAfterOpenSolution_MissingFile_ShowsInfoBar`: Asserts InfoBar factory and host are invoked when `.dataguard.yml` is absent.
  - `OnAfterOpenSolution_ExistingFile_DoesNotShowInfoBar`: Asserts InfoBar is never shown if `.dataguard.yml` exists.
- Target framework: `net472` (VSSDK compatibility profile).
