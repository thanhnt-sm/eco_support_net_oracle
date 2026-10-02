using System;
using System.IO;
using System.Reflection;
using System.Threading;
using DataGuard.VisualStudio.UI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Moq;
using Xunit;

#pragma warning disable VSSDK005 // Avoid using JoinableTaskContext directly
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DataGuard.VisualStudio.Tests.UI;

public class InfoBarManagerTests : IDisposable
{
    private readonly string _solutionDir;
    private readonly string _configFile;

    private readonly Mock<IVsInfoBarUIFactory> _infoBarFactoryMock;
    private readonly Mock<IVsInfoBarHost> _infoBarHostMock;
    private readonly Mock<IVsSolution> _solutionMock;
    private readonly Mock<IVsInfoBarUIElement> _uiElementMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public InfoBarManagerTests()
    {
        EnsureUIThread();
        _solutionDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_solutionDir);
        _configFile = Path.Combine(_solutionDir, ".dataguard.yml");

        _infoBarFactoryMock = new Mock<IVsInfoBarUIFactory>();
        _infoBarHostMock = new Mock<IVsInfoBarHost>();
        _solutionMock = new Mock<IVsSolution>();
        _uiElementMock = new Mock<IVsInfoBarUIElement>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        _infoBarFactoryMock.Setup(x => x.CreateInfoBar(It.IsAny<IVsInfoBar>()))
            .Returns(_uiElementMock.Object);

        _serviceProviderMock.Setup(x => x.GetService(typeof(SVsSolution)))
            .Returns(_solutionMock.Object);

        _serviceProviderMock.Setup(x => x.GetService(typeof(SVsInfoBarUIFactory)))
            .Returns(_infoBarFactoryMock.Object);

        // Mock solution dir
        object solutionDirObj = _solutionDir;
        _solutionMock.Setup(x => x.GetProperty((int)__VSPROPID.VSPROPID_SolutionDirectory, out solutionDirObj))
            .Returns(Microsoft.VisualStudio.VSConstants.S_OK);
    }

    public void Dispose()
    {
        if (Directory.Exists(_solutionDir))
        {
            Directory.Delete(_solutionDir, true);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task OnAfterOpenSolution_MissingFile_ShowsInfoBar()
    {
        // Arrange
        var manager = new InfoBarManager(_serviceProviderMock.Object, _infoBarHostMock.Object);
        if (File.Exists(_configFile))
        {
            File.Delete(_configFile);
        }

        // Act
        manager.OnAfterOpenSolution(new object(), 1);
        if (manager.LastInitializationTask != null)
        {
            await manager.LastInitializationTask.JoinAsync();
        }

        // Assert
        _infoBarFactoryMock.Verify(x => x.CreateInfoBar(It.IsAny<IVsInfoBar>()), Times.Once);
        _infoBarHostMock.Verify(x => x.AddInfoBar(_uiElementMock.Object), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task OnAfterOpenSolution_ExistingFile_DoesNotShowInfoBar()
    {
        // Arrange
        var manager = new InfoBarManager(_serviceProviderMock.Object, _infoBarHostMock.Object);
        File.WriteAllText(_configFile, "dummy");

        // Act
        manager.OnAfterOpenSolution(new object(), 1);
        if (manager.LastInitializationTask != null)
        {
            await manager.LastInitializationTask.JoinAsync();
        }

        // Assert
        _infoBarFactoryMock.Verify(x => x.CreateInfoBar(It.IsAny<IVsInfoBar>()), Times.Never);
        _infoBarHostMock.Verify(x => x.AddInfoBar(It.IsAny<IVsInfoBarUIElement>()), Times.Never);
    }

    private static void EnsureUIThread()
    {
        if (SynchronizationContext.Current == null)
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

        try
        {
            var dispatcherField = typeof(ThreadHelper).GetField("uiThreadDispatcher", BindingFlags.Static | BindingFlags.NonPublic);
            if (dispatcherField != null)
            {
                dispatcherField.SetValue(null, System.Windows.Threading.Dispatcher.CurrentDispatcher);
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
        catch
        {
            // Ignore if already initialized or not settable
        }
    }
}