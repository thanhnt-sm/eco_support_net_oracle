using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class HealthHostIntegrationTests
{
    [Fact]
    public async Task Host_ExposesLiveStartupAndReadyOnLoopback()
    {
        var fixture = Directory.CreateTempSubdirectory("dg-health-host");
        var snapshot = Path.Combine(fixture.FullName, "snapshot.json");
        var baseline = Path.Combine(fixture.FullName, "baseline.json");
        await File.WriteAllTextAsync(snapshot, "{}");
        await File.WriteAllTextAsync(baseline, "{}");

        using var process = StartHost(snapshot, baseline);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            var baseAddress = await WaitForListeningAddressAsync(process);
            var live = await GetWhenAvailableAsync(client, $"{baseAddress}/health/live");
            var startup = await GetWhenAvailableAsync(client, $"{baseAddress}/health/startup");
            var ready = await GetWhenAvailableAsync(client, $"{baseAddress}/health/ready");

            live.StatusCode.Should().Be(HttpStatusCode.OK);
            startup.StatusCode.Should().Be(HttpStatusCode.OK);
            ready.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ready.Content.ReadAsStringAsync()).Should().Contain("startupComplete").And.Contain("components");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            fixture.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Host_RejectsNonLoopbackBindingBeforeListenerStarts()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(HealthHostBinding).Assembly.Location);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://0.0.0.0:0");
        startInfo.ArgumentList.Add("--DataGuardHealth:EnableHost");
        startInfo.ArgumentList.Add("true");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start DataGuard.Host.");
        using (var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try
            {
                await process.WaitForExitAsync(exitTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                // Asserted below: the host did not exit within the timeout.
            }
        }

        var exitedPromptly = process.HasExited;
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        exitedPromptly.Should().BeTrue("host must reject non-loopback binding promptly");
        process.ExitCode.Should().NotBe(0);
        (await process.StandardError.ReadToEndAsync()).Should().Contain("loopback");
    }

    [Fact]
    public async Task HealthRefreshService_PeriodicallyRefreshesAndStopsCleanly()
    {
        var probe = new CountingProbe();
        var coordinator = new DataGuard.Core.Health.HealthProbeCoordinator(new[] { probe }, new DataGuard.Core.Health.HealthStateStore());
        var service = new HealthRefreshService(coordinator, TimeSpan.FromMilliseconds(10));

        await service.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => probe.Calls >= 2, TimeSpan.FromSeconds(5)))
            .Should().BeTrue("the refresh loop must tick at least twice at a 10 ms interval");

        // StopAsync awaits the refresh loop, so once it returns no further probe call may happen. Any late call is
        // observed as soon as it occurs (polling), instead of sleeping a fixed time and hoping it was long enough.
        await service.StopAsync(CancellationToken.None);
        var callsAfterStop = probe.Calls;
        var lateCall = await WaitUntilAsync(() => probe.Calls != callsAfterStop, TimeSpan.FromMilliseconds(100));

        lateCall.Should().BeFalse("no refresh may run after StopAsync completed");
        probe.Calls.Should().Be(callsAfterStop);
    }

    [Fact]
    public void HealthRefreshService_RejectsInvalidInterval()
    {
        var coordinator = new DataGuard.Core.Health.HealthProbeCoordinator(
            Array.Empty<DataGuard.Core.Health.IHealthProbe>(),
            new DataGuard.Core.Health.HealthStateStore());

        var act = () => new HealthRefreshService(coordinator, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Process StartHost(string snapshot, string baseline)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var rollForward = Environment.GetEnvironmentVariable("DOTNET_ROLL_FORWARD");
        if (!string.IsNullOrEmpty(rollForward))
        {
            startInfo.Environment["DOTNET_ROLL_FORWARD"] = rollForward;
        }
        startInfo.ArgumentList.Add(typeof(HealthHostBinding).Assembly.Location);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add("http://127.0.0.1:0");
        startInfo.ArgumentList.Add("--DataGuardHealth:SnapshotPath");
        startInfo.ArgumentList.Add(snapshot);
        startInfo.ArgumentList.Add("--DataGuardHealth:BaselinePath");
        startInfo.ArgumentList.Add(baseline);
        startInfo.ArgumentList.Add("--DataGuardHealth:MinimumFreeDiskBytes");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--DataGuardHealth:MaximumManagedMemoryBytes");
        startInfo.ArgumentList.Add(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--DataGuardHealth:ExposeEndpoints");
        startInfo.ArgumentList.Add("true");
        startInfo.ArgumentList.Add("--DataGuardHealth:EnableHost");
        startInfo.ArgumentList.Add("true");

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start DataGuard.Host.");
    }

    private static async Task<string> WaitForListeningAddressAsync(Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                const string listeningPrefix = "Now listening on: ";
                var prefixIndex = line.IndexOf(listeningPrefix, StringComparison.Ordinal);
                if (prefixIndex >= 0 &&
                    Uri.TryCreate(line[(prefixIndex + listeningPrefix.Length)..], UriKind.Absolute, out var address) &&
                    address.IsLoopback)
                {
                    return address.GetLeftPart(UriPartial.Authority);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The exception below reports the process state and remains stable across host logging implementations.
        }

        throw new TimeoutException("DataGuard.Host did not report its loopback listening address.");
    }

    /// <summary>Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses.</summary>
    /// <returns><see langword="true"/> when the condition held before the timeout.</returns>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed >= timeout)
            {
                return condition();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        return true;
    }

    private static async Task<HttpResponseMessage> GetWhenAvailableAsync(HttpClient client, string url)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        Exception? lastFailure = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException or TimeoutException)
            {
                lastFailure = exception;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"DataGuard.Host did not become ready at {url}.", lastFailure);
    }

    private sealed class CountingProbe : DataGuard.Core.Health.IHealthProbe
    {
        public string Name => "counting";

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<DataGuard.Core.Health.HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new DataGuard.Core.Health.HealthComponentStatus(Name, DataGuard.Core.Health.HealthComponentState.Healthy));
        }
    }
}
