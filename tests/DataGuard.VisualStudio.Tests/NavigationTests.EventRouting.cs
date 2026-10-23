using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Moq;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

public partial class NavigationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateTask_NavigateMissingDocument_ReportsAndNeverRequestsShell(bool hasOutput)
    {
        using var context = JoinableTaskContext.CreateNoOpContext();
        var navigationTasks = context.CreateCollection();
        var navigationFactory = context.CreateFactory(navigationTasks);
        var shell = new Mock<IServiceProvider>();
        shell.Setup(service => service.GetService(It.IsAny<Type>())).Returns((object?)null);
        var messages = new List<string>();
        TaskCompletionSource<bool>? writerCompletion = null;
        var document = MissingDocument();
        Func<string, Task>? output = hasOutput ? text =>
        {
            messages.Add(text);
            messages.Count.Should().Be(1, "duplicate output must fail before creating another pending task");
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            writerCompletion = completion;
            return completion.Task;
        }
        : null;
        var presenter = new ErrorListPresenter(
            _ => null, shell.Object, navigationFactory, output);
        var task = presenter.CreateTask(
            new SarifDiagnostic { Document = document, Line = 2, Column = 3 },
            new Dictionary<string, IVsHierarchy?>());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            // The outer factory is outside navigationTasks; joining the event
            // operation cannot wait for the joining task itself.
            context.Factory.Run(async () =>
            {
                await context.Factory.SwitchToMainThreadAsync();
                ((IVsTaskItem)task).NavigateTo().Should().Be(0);
                if (hasOutput)
                {
                    Assert.NotNull(writerCompletion);
                    var operation = navigationTasks.Single();
                    operation.IsCompleted.Should().BeFalse("the event must await output completion");
                    writerCompletion!.SetResult(true);
                    await operation.JoinAsync(timeout.Token);
                }
                else
                {
                    navigationTasks.ToArray().Should().BeEmpty();
                }
            });
        }
        finally
        {
            writerCompletion?.TrySetResult(true);
        }

        if (hasOutput)
        {
            messages.Should().Equal($"[StackAccord] Cannot navigate: file not found '{document}'.\r\n");
        }
        else
        {
            messages.Should().BeEmpty();
        }

        shell.Verify(service => service.GetService(It.IsAny<Type>()), Times.Never);
    }
}
