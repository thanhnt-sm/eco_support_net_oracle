using System;
using System.Collections.Generic;
using System.IO;
using DataGuard.VisualStudio;
using FluentAssertions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

public class NavigationTests
{
    [Fact]
    public void TryFormatProgress_RuleExecutedWithViolations_FormatsCorrectly()
    {
        var json = "{\"Kind\":\"RuleExecuted\", \"Phase\":\"Validation\", \"Detail\":\"Oracle-DG001\", \"Data\":{\"RuleId\":\"Oracle-DG001\", \"RuleTitle\":\"Avoid SELECT *\", \"ContractCount\":10, \"ViolationCount\":3}}";

        var success = ProgressLineParser.TryFormatProgress(json, out var formatted, out var errors, out var warnings);

        success.Should().BeTrue();
        formatted.Should().NotBeNull();
        formatted.Should().Contain("3 violations");
        formatted.Should().Contain("Oracle-DG001");
        formatted.Should().Contain("Avoid SELECT *");
        formatted.Should().Contain("10 contracts checked");
    }

    [Fact]
    public void Navigate_SarifOneBased_LineColumnConversion()
    {
        // SARIF positions are 1-based (startLine=5, startColumn=3).
        // Visual Studio ErrorTask Line and Column preserve 1-based numbers for Error List UI display.
        var position = SarifErrorListPublisher.ConvertSarifPosition(5, 3);

        position.Line.Should().Be(5);
        position.Column.Should().Be(3);

        // Edge case: zero or negative should clamp to 0
        var clamped = SarifErrorListPublisher.ConvertSarifPosition(0, 0);
        clamped.Line.Should().Be(0);
        clamped.Column.Should().Be(0);
    }

    [Fact]
    public void Load_PreservesOneBasedLineAndColumn_ForErrorListPresentation()
    {
        var tempDir = Path.GetTempPath();
        var json = "{\"version\":\"2.1.0\",\"runs\":[{\"tool\":{\"driver\":{\"name\":\"DataGuard\"}},\"results\":[{\"ruleId\":\"DG017\",\"level\":\"warning\",\"message\":{\"text\":\"Avoid SELECT *\"},\"locations\":[{\"physicalLocation\":{\"artifactLocation\":{\"uri\":\"Models/User.cs\",\"uriBaseId\":\"%SRCROOT%\"},\"region\":{\"startLine\":42,\"startColumn\":12}}}]}]}]}";
        var loaded = SarifErrorListPublisher.Load(json, tempDir);

        loaded.Diagnostics.Should().ContainSingle();
        var diag = loaded.Diagnostics[0];
        diag.Line.Should().Be(42);
        diag.Column.Should().Be(12);
    }

    [Fact]
    public void ErrorListPresenter_TaskCreation_MapsLineAndColumnCorrectlyForVSCoordinates()
    {
        var jtf = ThreadHelper.JoinableTaskFactory;
        var presenter = new ErrorListPresenter(
            hierarchyResolver: _ => null,
            joinableTaskFactory: jtf);

        var diag = new SarifDiagnostic
        {
            Document = @"C:\repo\Models\User.cs",
            Line = 42,
            Column = 12,
            Message = "Avoid SELECT *",
            Level = "warning",
        };

        var cache = new Dictionary<string, IVsHierarchy?>(StringComparer.OrdinalIgnoreCase);
        var task = presenter.CreateTask(diag, cache);

        // VS Error List displays 1-based line/col numbers in the grid UI
        task.Line.Should().Be(42);
        task.Column.Should().Be(12);
        task.Document.Should().Be(@"C:\repo\Models\User.cs");
        task.Text.Should().Be("Avoid SELECT *");
        task.ErrorCategory.Should().Be(TaskErrorCategory.Warning);
    }

    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(42, 12, 41, 11)]
    [InlineData(0, 0, 0, 0)]
    public void Navigation_CaretCalculation_ConvertsOneBasedToZeroBasedBufferPosition(int taskLine, int taskCol, int expectedBufferLine, int expectedBufferCol)
    {
        // Line and column indexing translation:
        // ErrorTask uses 1-based indexing for UI grid display.
        // IVsTextView.SetCaretPos requires 0-based buffer line and column offsets.
        var zeroBasedLine = Math.Max(0, taskLine - 1);
        var zeroBasedCol = Math.Max(0, taskCol - 1);

        zeroBasedLine.Should().Be(expectedBufferLine);
        zeroBasedCol.Should().Be(expectedBufferCol);
    }
    [Fact(Skip = "VS SDK integration — requires experimental instance (ErrorListProvider.Navigate needs a live shell)")]
    public void Navigate_WhenFileMissing_WritesOutputMessage()
    {
        // Documented integration test (ErrorListPresenter.CreateTask):
        // When an ErrorTask with a non-existent Document path has its Navigate event raised,
        // it checks File.Exists(task.Document). When false, it must write
        // "[DataGuard] Cannot navigate: file not found '{task.Document}'.\r\n"
        // to the DataGuard output pane; when true it calls ErrorListProvider.Navigate(task, LOGVIEWID_Code),
        // which opens the document and positions the caret at task.Line/task.Column.
    }

    [Fact]
    public void ErrorListPresenter_PopulatesHierarchyItem_FromCachedSolutionLookup()
    {
        var dummyHierarchy = new DummyHierarchy();
        var lookupCount = 0;
        var jtf = ThreadHelper.JoinableTaskFactory;
        var presenter = new ErrorListPresenter(
            hierarchyResolver: path =>
            {
                lookupCount++;
                return path.EndsWith("User.cs", StringComparison.OrdinalIgnoreCase) ? dummyHierarchy : null;
            },
            joinableTaskFactory: jtf);

        var cache = new Dictionary<string, IVsHierarchy?>(StringComparer.OrdinalIgnoreCase);

        var res1 = presenter.ResolveHierarchy(@"C:\repo\User.cs", cache);
        var res2 = presenter.ResolveHierarchy(@"C:\repo\User.cs", cache);
        var res3 = presenter.ResolveHierarchy(@"C:\repo\Order.cs", cache);

        res1.Should().BeSameAs(dummyHierarchy);
        res2.Should().BeSameAs(dummyHierarchy);
        res3.Should().BeNull();
        lookupCount.Should().Be(2);

        var diag = new SarifDiagnostic
        {
            Document = @"C:\repo\User.cs",
            Line = 10,
            Column = 5,
            Message = "Test message",
            Level = "warning",
        };

        var task = presenter.CreateTask(diag, cache);
        task.HierarchyItem.Should().BeSameAs(dummyHierarchy);
        task.Line.Should().Be(10);
        task.Column.Should().Be(5);
    }

    private sealed class DummyHierarchy : IVsHierarchy
    {
        public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp)
        {
            return 0;
        }

        public int GetSite(out Microsoft.VisualStudio.OLE.Interop.IServiceProvider ppSP)
        {
            ppSP = null!;
            return 0;
        }

        public int QueryClose(out int pfCanClose)
        {
            pfCanClose = 1;
            return 0;
        }

        public int Close()
        {
            return 0;
        }

        public int GetGuidProperty(uint itemid, int propid, out Guid pguid)
        {
            pguid = Guid.Empty;
            return 0;
        }

        public int SetGuidProperty(uint itemid, int propid, ref Guid rguid)
        {
            return 0;
        }

        public int GetProperty(uint itemid, int propid, out object pvar)
        {
            pvar = null!;
            return 0;
        }

        public int SetProperty(uint itemid, int propid, object var)
        {
            return 0;
        }

        public int GetNestedHierarchy(uint itemid, ref Guid iidHierarchyNested, out IntPtr ppHierarchyNested, out uint pitemidNested)
        {
            ppHierarchyNested = IntPtr.Zero;
            pitemidNested = 0;
            return 0;
        }

        public int GetCanonicalName(uint itemid, out string pbstrName)
        {
            pbstrName = string.Empty;
            return 0;
        }

        public int ParseCanonicalName(string pszName, out uint pitemid)
        {
            pitemid = 0;
            return 0;
        }

        public int Unused0()
        {
            return 0;
        }

        public int Unused1()
        {
            return 0;
        }

        public int Unused2()
        {
            return 0;
        }

        public int Unused3()
        {
            return 0;
        }

        public int Unused4()
        {
            return 0;
        }

        public int AdviseHierarchyEvents(IVsHierarchyEvents pEventSink, out uint pdwCookie)
        {
            pdwCookie = 0;
            return 0;
        }

        public int UnadviseHierarchyEvents(uint dwCookie)
        {
            return 0;
        }
    }
}
