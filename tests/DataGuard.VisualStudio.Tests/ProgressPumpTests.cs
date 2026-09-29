using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// Output-pane latency must never delay or lose what the parser learned: parsing completes on the
/// background thread while the UI flush happens in bounded batches, one hop per batch.
/// </summary>
public class ProgressPumpTests
{
    private const string SummaryLine = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":1,\"WarningCount\":0}}";

    [Fact]
    public async Task Reader_WithUiFlushBlocked_StillCompletesParseAndKeepsSummary()
    {
        var flushMayFinish = new SemaphoreSlim(0);
        var flushed = new List<string>();
        var pump = new ProgressPump(async batch =>
        {
            await flushMayFinish.WaitAsync();
            lock (flushed)
            {
                flushed.AddRange(batch);
            }
        });
        var reader = new ProgressStreamReader(new RuleInventory(), pump.Enqueue);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ide-safe: active\n" + SummaryLine + "\n"));
        using var streamReader = new StreamReader(stream);

        var readTask = Task.Run(() => reader.ReadAsync(streamReader));
        var finished = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2)));

        finished.Should().BeSameAs(readTask, "parsing must not wait for the Output pane");
        var result = await readTask;
        result.HasSummary.Should().BeTrue();
        result.ErrorCount.Should().Be(1);

        flushMayFinish.Release(100);
        pump.Complete();
        await pump.FlushCompletion;
        flushed.Should().Contain(line => line.Contains("1 errors, 0 warnings"));
    }

    [Fact]
    public async Task Pump_FlushesInBatchesOfAtMostThirtyTwoLines_PreservingOrder()
    {
        var batches = new List<IReadOnlyList<string>>();
        var pump = new ProgressPump(batch =>
        {
            lock (batches)
            {
                batches.Add(new List<string>(batch));
            }

            return Task.CompletedTask;
        });

        for (var i = 0; i < 100; i++)
        {
            pump.Enqueue("line " + i + "\r\n");
        }

        pump.Complete();
        await pump.FlushCompletion;

        var all = new List<string>();
        foreach (var batch in batches)
        {
            batch.Count.Should().BeLessOrEqualTo(ProgressPump.MaxBatchLines);
            all.AddRange(batch);
        }

        all.Should().HaveCount(100);
        all[0].Should().Be("line 0\r\n");
        all[99].Should().Be("line 99\r\n");
    }

    [Fact]
    public async Task Pump_WhenCapacityExceeded_DropsOldestAndReportsCount()
    {
        var gate = new SemaphoreSlim(0);
        var flushed = new List<string>();
        var pump = new ProgressPump(
            async batch =>
            {
                await gate.WaitAsync();
                lock (flushed)
                {
                    flushed.AddRange(batch);
                }
            },
            capacity: 8);

        var producer = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                pump.Enqueue("line " + i + "\r\n");
            }
        });

        var finished = await Task.WhenAny(producer, Task.Delay(TimeSpan.FromSeconds(5)));
        gate.Release(100);
        finished.Should().BeSameAs(producer, "Enqueue must never block the parser on the UI flush");
        pump.Complete();
        await pump.FlushCompletion;

        flushed.Should().Contain(line => line.Contains("dropped"));
        flushed.Should().Contain("line 19\r\n");
        flushed.Count.Should().BeLessThan(20);
    }

    [Fact]
    public async Task Pump_WhenFlushThrows_ContinuesWithNextBatch()
    {
        var calls = 0;
        var firstFlushAttempted = new TaskCompletionSource<bool>();
        var flushed = new List<string>();
        var pump = new ProgressPump(batch =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstFlushAttempted.TrySetResult(true);
                throw new InvalidOperationException("pane unavailable");
            }

            lock (flushed)
            {
                flushed.AddRange(batch);
            }

            return Task.CompletedTask;
        });

        pump.Enqueue("first\r\n");
        (await Task.WhenAny(firstFlushAttempted.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(firstFlushAttempted.Task);
        pump.Enqueue("second\r\n");
        pump.Complete();
        await pump.FlushCompletion;

        flushed.Should().Contain("second\r\n");
    }
}
