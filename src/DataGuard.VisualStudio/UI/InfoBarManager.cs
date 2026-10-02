using System;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace DataGuard.VisualStudio.UI
{
    public class InfoBarManager : IVsSolutionEvents, IVsInfoBarUIEvents, IDisposable
    {
        internal JoinableTask? LastInitializationTask { get; private set; }

        private readonly System.IServiceProvider _serviceProvider;
        private readonly IVsInfoBarHost _infoBarHost;
        private IVsSolution? _solution;
        private uint _solutionEventsCookie;
        private uint _infoBarEventsCookie;
        private IVsInfoBarUIElement? _currentInfoBar;
        private bool _hasBeenDismissed;

        public InfoBarManager(System.IServiceProvider serviceProvider, IVsInfoBarHost infoBarHost)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _infoBarHost = infoBarHost ?? throw new ArgumentNullException(nameof(infoBarHost));

            ThreadHelper.ThrowIfNotOnUIThread();
            _solution = _serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            if (_solution != null)
            {
                _solution.AdviseSolutionEvents(this, out _solutionEventsCookie);
            }
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            if (_hasBeenDismissed)
            {
                return VSConstants.S_OK;
            }

            string? solutionDir = GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
            {
                return VSConstants.S_OK;
            }

            string configPath = Path.Combine(solutionDir!, ".dataguard.yml");

#pragma warning disable VSSDK007 // Await/join tasks created from ThreadHelper.JoinableTaskFactory in the same block
            LastInitializationTask = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await TaskScheduler.Default; // Switch to background thread for file I/O
                bool exists = File.Exists(configPath);

                if (!exists)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    ShowInfoBar();
                }
            });
#pragma warning restore VSSDK007 // Await/join tasks created from ThreadHelper.JoinableTaskFactory in the same block

            LastInitializationTask.FileAndForget("DataGuard/InfoBar");

            return VSConstants.S_OK;
        }

        private string? GetSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_solution == null)
            {
                return null;
            }

            _solution.GetProperty((int)__VSPROPID.VSPROPID_SolutionDirectory, out object dirObj);
            return dirObj as string;
        }

        private void ShowInfoBar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_currentInfoBar != null)
            {
                return; // Already showing
            }

            var factory = _serviceProvider.GetService(typeof(SVsInfoBarUIFactory)) as IVsInfoBarUIFactory;
            if (factory == null)
            {
                return;
            }

            var textSpans = new[]
            {
                new InfoBarTextSpan("DataGuard is inactive. Configuration file missing.")
            };

            var actionItems = new[]
            {
                new InfoBarHyperlink("Initialize Configuration", "InitDataGuard") // We will handle this action in OnActionItemClicked
            };

            var model = new InfoBarModel(textSpans, actionItems, KnownMonikers.Settings, isCloseButtonVisible: true);
            _currentInfoBar = factory.CreateInfoBar(model);

            if (_currentInfoBar != null)
            {
                _currentInfoBar.Advise(this, out _infoBarEventsCookie);
                _infoBarHost.AddInfoBar(_currentInfoBar);
            }
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _hasBeenDismissed = true;
            CleanupInfoBar();
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (actionItem.ActionContext as string == "InitDataGuard")
            {
                var commandService = _serviceProvider.GetService(typeof(SUIHostCommandDispatcher)) as IOleCommandTarget;
                if (commandService != null)
                {
                    Guid cmdGroup = new Guid("a7ceccae-351c-4d13-9568-b2ba5370ea7d"); // DataGuardCommandSet
                    commandService.Exec(ref cmdGroup, 0x0106, 0, IntPtr.Zero, IntPtr.Zero);
                }

                CleanupInfoBar();
            }
        }

        private void CleanupInfoBar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_currentInfoBar != null)
            {
                if (_infoBarEventsCookie != 0)
                {
                    _currentInfoBar.Unadvise(_infoBarEventsCookie);
                    _infoBarEventsCookie = 0;
                }

                _infoBarHost.RemoveInfoBar(_currentInfoBar);
                _currentInfoBar = null;
            }
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_solutionEventsCookie != 0 && _solution != null)
            {
                _solution.UnadviseSolutionEvents(_solutionEventsCookie);
                _solutionEventsCookie = 0;
            }

            CleanupInfoBar();
        }

        // Unused IVsSolutionEvents methods
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
        public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;
        public int OnAfterCloseSolution(object pUnkReserved) => VSConstants.S_OK;
    }
}