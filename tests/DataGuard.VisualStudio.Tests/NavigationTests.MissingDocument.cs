using System;
using System.IO;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Tests the production navigation guard without a VS shell, event routing or COM.</summary>
public partial class NavigationTests
{
    [Fact]
    public async Task Navigate_WhenFileMissing_WritesOutputMessage()
    {
        var document = MissingDocument();
        string? message = null;
        var navigations = 0;

        await DocumentNavigation.NavigateAsync(
            document,
            text =>
            {
                message = text;
                return Task.CompletedTask;
            },
            () =>
            {
                navigations++;
                return Task.CompletedTask;
            });

        message.Should().Be($"[StackAccord] Cannot navigate: file not found '{document}'.\r\n");
        navigations.Should().Be(0, "a missing file must never reach shell navigation");
    }

    [Fact]
    public async Task Navigate_WhenFileMissingWithoutOutput_DoesNotNavigate()
    {
        var navigations = 0;
        await DocumentNavigation.NavigateAsync(
            MissingDocument(), null, () =>
            {
                navigations++;
                return Task.CompletedTask;
            });
        navigations.Should().Be(0);
    }

    [Fact]
    public async Task Navigate_WhenFileMissing_AwaitsWriterAndPropagatesFailure()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigations = 0;
        var pending = DocumentNavigation.NavigateAsync(
            MissingDocument(), _ => completion.Task, () =>
            {
                navigations++;
                return Task.CompletedTask;
            });

        pending.IsCompleted.Should().BeFalse("output must be awaited, not fired and forgotten");
        completion.SetException(new InvalidOperationException("synthetic output failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        navigations.Should().Be(0);
    }

    [Fact]
    public async Task Navigate_WhenFileExists_InvokesAndAwaitsNavigationWithoutMissingMessage()
    {
        var document = Path.GetTempFileName();
        try
        {
            var messages = 0;
            var navigations = 0;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = DocumentNavigation.NavigateAsync(
                document,
                _ =>
                {
                    messages++;
                    return Task.CompletedTask;
                },
                () =>
                {
                    navigations++;
                    return completion.Task;
                });

            navigations.Should().Be(1);
            messages.Should().Be(0);
            pending.IsCompleted.Should().BeFalse("the shell callback must not become async void");
            completion.SetResult(true);
            await pending;
            navigations.Should().Be(1);
        }
        finally
        {
            File.Delete(document);
        }
    }

    private static string MissingDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), "dg-navigation-" + Guid.NewGuid().ToString("N"), "missing.cs");
        File.Exists(path).Should().BeFalse();
        return path;
    }
}
