// <copyright file="ProgressPump.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Bounded queue between the stderr parser and the Output pane. <see cref="Enqueue"/> never blocks
/// (oldest lines are dropped and counted when the pane cannot keep up); a single background loop
/// flushes batches of at most <see cref="MaxBatchLines"/> lines, coalesced for at most
/// <see cref="MaxBatchDelay"/>, with one UI hop per batch.
/// </summary>
internal sealed class ProgressPump
{
    internal const int MaxBatchLines = 32;
    internal static readonly TimeSpan MaxBatchDelay = TimeSpan.FromMilliseconds(50);

    private readonly object gate = new();
    private readonly Queue<string> queue = new();
    private readonly SemaphoreSlim wake = new(0);
    private readonly Func<IReadOnlyList<string>, Task> flushBatch;
    private readonly int capacity;
    private int dropped;
    private bool completed;

    public ProgressPump(Func<IReadOnlyList<string>, Task> flushBatch, int capacity = 4096)
    {
        this.flushBatch = flushBatch ?? throw new ArgumentNullException(nameof(flushBatch));
        this.capacity = capacity < 1 ? 1 : capacity;
        this.FlushCompletion = Task.Run(this.PumpAsync);
    }

    /// <summary>Completes once <see cref="Complete"/> was called and every queued batch was flushed.</summary>
    public Task FlushCompletion { get; }

    /// <summary>Queues Output text; never blocks the caller.</summary>
    public void Enqueue(string text)
    {
        lock (this.gate)
        {
            if (this.completed)
            {
                return;
            }

            if (this.queue.Count >= this.capacity)
            {
                this.queue.Dequeue();
                this.dropped++;
            }

            this.queue.Enqueue(text);
        }

        this.wake.Release();
    }

    /// <summary>Signals that no more text will be queued; idempotent.</summary>
    public void Complete()
    {
        lock (this.gate)
        {
            this.completed = true;
        }

        this.wake.Release();
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            await this.wake.WaitAsync().ConfigureAwait(false);
            if (!this.IsBatchReady())
            {
                await Task.Delay(MaxBatchDelay).ConfigureAwait(false);
            }

            while (true)
            {
                var batch = this.Dequeue();
                if (batch.Count == 0)
                {
                    break;
                }

                await this.FlushSafelyAsync(batch).ConfigureAwait(false);
            }

            lock (this.gate)
            {
                if (this.completed && this.queue.Count == 0)
                {
                    return;
                }
            }
        }
    }

    private bool IsBatchReady()
    {
        lock (this.gate)
        {
            return this.completed || this.queue.Count >= MaxBatchLines;
        }
    }

    private List<string> Dequeue()
    {
        lock (this.gate)
        {
            var batch = new List<string>(MaxBatchLines);
            if (this.dropped > 0)
            {
                batch.Add("[DataGuard] " + this.dropped + " Output lines were dropped because the Output pane could not keep up.\r\n");
                this.dropped = 0;
            }

            while (batch.Count < MaxBatchLines && this.queue.Count > 0)
            {
                batch.Add(this.queue.Dequeue());
            }

            return batch;
        }
    }

    private async Task FlushSafelyAsync(IReadOnlyList<string> batch)
    {
        try
        {
            await this.flushBatch(batch).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Output pane flush failed; " + batch.Count + " lines were not shown: " + DataGuardLogger.Redact(ex.Message));
        }
    }
}
