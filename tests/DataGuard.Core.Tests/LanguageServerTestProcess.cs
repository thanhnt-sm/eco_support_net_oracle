using System.ComponentModel;
using System.Diagnostics;

namespace DataGuard.Core.Tests;

/// <summary>Owns one synthetic server and drains stderr without retaining or printing its contents.</summary>
internal sealed class LanguageServerTestProcess : IAsyncDisposable
{
    private readonly CancellationTokenSource stderrStop = new();
    private readonly Task stderrDrain;
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private long stderrBytes;
    private string? stderrFailure;
    private int disposed;

    internal LanguageServerTestProcess(Process process)
    {
        Process = process;
        stderrDrain = DrainStderrAsync();
    }

    internal Process Process { get; }

    internal string DescribeState()
    {
        // Only scalar transport/runtime state: never stderr text, paths, environment or request contents.
        var state = Process.HasExited ? $"exited({Process.ExitCode})" : "running";
        var cpu = "unavailable";
        try
        {
            cpu = Process.TotalProcessorTime.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            // A child may exit between the state and CPU snapshots.
        }
        catch (Win32Exception)
        {
            // Some platforms cannot read CPU counters after the process exits.
        }

        return FormattableString.Invariant($"{state} stderrBytes={Interlocked.Read(ref stderrBytes)} stderrFailure={stderrFailure ?? "none"} sessionElapsedMs={lifetime.Elapsed.TotalMilliseconds:F0} childCpuMs={cpu} hostWorkers={ThreadPool.ThreadCount} hostQueued={ThreadPool.PendingWorkItemCount}");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!Process.HasExited)
            {
                try
                {
                    Process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (Process.HasExited)
                {
                    // Exit raced the kill; the wait below still observes and reaps it.
                }
                catch (Win32Exception) when (Process.HasExited)
                {
                    // Same race on platforms reporting an OS error for an already-exited child.
                }
            }

            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            stderrStop.Cancel();
            try
            {
                await stderrDrain.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Process.Dispose();
                stderrStop.Dispose();
            }
        }
    }

    private async Task DrainStderrAsync()
    {
        var buffer = new byte[512];
        try
        {
            while (await Process.StandardError.BaseStream.ReadAsync(buffer, stderrStop.Token) is var count && count > 0)
            {
                Interlocked.Add(ref stderrBytes, count);
            }
        }
        catch (OperationCanceledException) when (stderrStop.IsCancellationRequested)
        {
            // Owned cleanup stops the drain after the child has exited.
        }
        catch (IOException exception)
        {
            stderrFailure = exception.GetType().Name;
        }
    }
}
