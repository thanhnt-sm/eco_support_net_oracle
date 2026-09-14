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

        var port = ReserveLoopbackPort();
        using var process = StartHost(port, snapshot, baseline);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var baseAddress = $"http://127.0.0.1:{port}";

        try
        {
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
        await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
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
        await Task.Delay(60);
        var callsBeforeStop = probe.Calls;
        await service.StopAsync(CancellationToken.None);
        var callsAfterStop = probe.Calls;
        await Task.Delay(40);

        callsBeforeStop.Should().BeGreaterThanOrEqualTo(2);
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

    private static Process StartHost(int port, string snapshot, string baseline)
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
        startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");
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

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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
            catch (HttpRequestException exception)
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

        public int Calls { get; private set; }

        public Task<DataGuard.Core.Health.HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new DataGuard.Core.Health.HealthComponentStatus(Name, DataGuard.Core.Health.HealthComponentState.Healthy));
        }
    }
}
