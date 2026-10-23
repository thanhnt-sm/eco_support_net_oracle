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
        var canNavigate = await DocumentNavigation.CanNavigateAsync(
            document,
            text =>
            {
                message = text;
                return Task.CompletedTask;
            });

        message.Should().Be($"[StackAccord] Cannot navigate: file not found '{document}'.\r\n");
        canNavigate.Should().BeFalse("a missing file must never reach shell navigation");
    }

    [Fact]
    public async Task Navigate_WhenFileMissingWithoutOutput_DoesNotNavigate()
    {
        var canNavigate = await DocumentNavigation.CanNavigateAsync(MissingDocument(), null);
        canNavigate.Should().BeFalse();
    }

    [Fact]
    public async Task Navigate_WhenFileMissing_AwaitsWriterAndPropagatesFailure()
    {
        TaskCompletionSource<bool>? completion = null;
        var writes = 0;
        var pending = DocumentNavigation.CanNavigateAsync(
            MissingDocument(), _ =>
            {
                writes++;
                writes.Should().Be(1, "the missing-file message must be written once");
                var writerCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                completion = writerCompletion;
                return writerCompletion.Task;
            });

        pending.IsCompleted.Should().BeFalse("output must be awaited, not fired and forgotten");
        Assert.NotNull(completion);
        completion!.SetException(new InvalidOperationException("synthetic output failure"));
        InvalidOperationException? failure = null;
        try
        {
            await pending;
        }
        catch (InvalidOperationException ex)
        {
            failure = ex;
        }

        failure.Should().NotBeNull();
        failure!.Message.Should().Be("synthetic output failure");
    }

    [Fact]
    public async Task Navigate_WhenFileExists_AllowsNavigationWithoutMissingMessage()
    {
        var document = Path.GetTempFileName();
        try
        {
            var messages = 0;
            var canNavigate = await DocumentNavigation.CanNavigateAsync(
                document,
                _ =>
                {
                    messages++;
                    return Task.CompletedTask;
                });

            canNavigate.Should().BeTrue();
            messages.Should().Be(0);
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
