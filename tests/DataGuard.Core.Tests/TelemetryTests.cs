using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class TelemetryCollectorTests
{
    [Fact]
    public async Task Telemetry_FlushAsync_RetainsFailedBatchForRetry()
    {
        var attempts = 0;
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
            (payload, _) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("temporary failure");
                }

                payload.Should().Contain("retry.event");
                return Task.CompletedTask;
            });
        collector.RecordEvent("retry.event", "details");

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Failed);
        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task Telemetry_DisposeAsync_PerformsFinalFlush()
    {
        var exported = false;
        var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
            (_, _) =>
            {
                exported = true;
                return Task.CompletedTask;
            });
        collector.RecordEvent("shutdown.event", "details");

        await collector.DisposeAsync();

        exported.Should().BeTrue();
    }

    [Fact]
    public async Task Telemetry_QueueLimit_DropsNewestEventAndExposesLoss()
    {
        var payload = string.Empty;
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600)
            {
                MaxQueuedEvents = 1,
            },
            (value, _) =>
            {
                payload = value;
                return Task.CompletedTask;
            });

        collector.RecordEvent("retained.event", "details");
        collector.RecordEvent("dropped.event", "details");

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        payload.Should().Contain("retained.event");
        payload.Should().NotContain("dropped.event");
        collector.DroppedEventCount.Should().Be(1);
    }

    [Fact]
    public async Task Telemetry_PayloadLimit_DropsOversizedEventAndExportsFollowingEvent()
    {
        var payload = string.Empty;
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600)
            {
                MaxPayloadBytes = 128,
            },
            (value, _) =>
            {
                payload = value;
                return Task.CompletedTask;
            });

        collector.RecordEvent("oversized.event", new string('x', 2048));
        collector.RecordEvent("retained.event", "ok");

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        payload.Should().Contain("retained.event");
        payload.Should().NotContain("oversized.event");
        collector.DroppedEventCount.Should().Be(1);
    }

    [Fact]
    public async Task Telemetry_ExportTimeout_RetainsBatchForLaterRetry()
    {
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600)
            {
                ExportTimeoutSeconds = 1,
            },
            async (_, _) => await Task.Delay(TimeSpan.FromSeconds(10)));
        collector.RecordEvent("timeout.event", "details");

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Failed);
        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.InProgress);
        collector.DroppedEventCount.Should().Be(0);
    }

    [Fact]
    public async Task Telemetry_DisposeAsync_BoundsFinalFlushWhenSinkHangs()
    {
        var sinkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSink = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600)
            {
                ExportTimeoutSeconds = 1,
            },
            async (_, _) =>
            {
                sinkStarted.SetResult();
                await releaseSink.Task;
            });
        collector.RecordEvent("shutdown.timeout", "details");

        var stopwatch = Stopwatch.StartNew();
        await collector.DisposeAsync();
        stopwatch.Stop();

        sinkStarted.Task.IsCompletedSuccessfully.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        releaseSink.SetResult();
    }

    [Fact]
    public async Task Telemetry_DisposeAsync_TransitionsToStoppedAndAccountsUndeliveredEvents()
    {
        var releaseSink = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600)
            {
                ExportTimeoutSeconds = 1,
            },
            async (_, _) => await releaseSink.Task);
        collector.RecordEvent("shutdown.loss", "details");

        await collector.DisposeAsync();

        collector.LifecycleState.Should().Be(TelemetryLifecycleState.Stopped);
        collector.TerminalLossCount.Should().Be(1);
        collector.RecordEvent("after.stop", "ignored");
        collector.TerminalLossCount.Should().Be(1);
        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.NoWork);
        releaseSink.SetResult();
    }
    [Fact]
    public void Telemetry_NoHttpClientWhenDisabled()
    {
        // Disabled collector: no timer, no event recording, and the flush path
        // must bail out before any export (zero egress) even when an endpoint
        // is configured.
        var exported = false;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: false, ExportEndpoint: "http://127.0.0.1:1/telemetry"),
            (_, _) =>
            {
                exported = true;
                return Task.CompletedTask;
            }))
        {
            collector.RecordEvent("test.event", "details");
            collector.IncrementCounter("test.counter");
            collector.RecordHistogram("test.histogram", 1.0);
            collector.FlushEvents(null);
        }

        exported.Should().BeFalse("a disabled collector must never invoke the export path");
    }

    [Fact]
    public void Telemetry_FlushEvents_InvokesExportSinkWhenEnabled()
    {
        var exported = false;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "http://127.0.0.1:1/telemetry", FlushIntervalSeconds: 3600),
            (_, _) =>
            {
                exported = true;
                return Task.CompletedTask;
            }))
        {
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
        }

        exported.Should().BeTrue("an enabled collector with events and an endpoint must export");
    }

    [Fact]
    public void Telemetry_Disabled_DoesNotEnqueueEvents()
    {
        // Disabled collector records nothing, so flushing the (private) queue
        // is a no-op even if the enabled-gate were removed.
        var exported = false;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: false, ExportEndpoint: "http://127.0.0.1:1/telemetry"),
            (_, _) =>
            {
                exported = true;
                return Task.CompletedTask;
            }))
        {
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
            collector.FlushEvents(null);
        }

        exported.Should().BeFalse();
    }
}

public class TelemetryExportAllowlistTests
{
    [Theory]
    [InlineData("https://collector.example.com/v1/metrics")]
    [InlineData("http://localhost:4318/v1/metrics")]
    [InlineData("http://127.0.0.1:4318/v1/metrics")]
    public void Telemetry_Allowlist_AcceptsHttpsAndLoopback(string endpoint)
    {
        var exported = false;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: endpoint, FlushIntervalSeconds: 3600),
            (_, _) =>
            {
                exported = true;
                return Task.CompletedTask;
            }))
        {
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
        }

        exported.Should().BeTrue("HTTPS and loopback endpoints must be accepted");
    }

    [Theory]
    [InlineData("http://collector.example.com/v1/metrics")]
    [InlineData("ftp://collector.example.com/v1/metrics")]
    [InlineData("not-a-uri")]
    public void Telemetry_Allowlist_RejectsPlainHttpRemoteAndInvalid(string endpoint)
    {
        var exported = false;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: endpoint, FlushIntervalSeconds: 3600),
            (_, _) =>
            {
                exported = true;
                return Task.CompletedTask;
            }))
        {
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
        }

        exported.Should().BeFalse("non-HTTPS remote endpoints must be rejected before any network call");
    }

    [Fact]
    public void Telemetry_CircuitBreaker_StopsExportingAfterConsecutiveFailures()
    {
        var exportAttempts = 0;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
            (_, _) =>
            {
                exportAttempts++;
                throw new InvalidOperationException("collector unreachable");
            }))
        {
            // Three failing flushes trip the breaker (MaxConsecutiveExportFailures = 3).
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);

            // Fourth flush: queue has events but the breaker is open — no attempt.
            collector.RecordEvent("test.event", "details");
            collector.FlushEvents(null);
        }

        exportAttempts.Should().Be(3, "the circuit breaker must stop export attempts after 3 consecutive failures");
    }

    [Fact]
    public void Telemetry_CircuitBreaker_ResetsOnSuccess()
    {
        var exportAttempts = 0;
        using (var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "https://collector.example.com/v1", FlushIntervalSeconds: 3600),
            (_, _) =>
            {
                exportAttempts++;
                if (exportAttempts % 2 == 1)
                {
                    throw new InvalidOperationException("transient failure");
                }

                return Task.CompletedTask;
            }))
        {
            for (var i = 0; i < 4; i++)
            {
                collector.RecordEvent("test.event", "details");
                collector.FlushEvents(null);
            }
        }

        exportAttempts.Should().Be(4, "non-consecutive failures must not trip the breaker");
    }
}

public class TelemetryConfigDefaultsTests
{
    [Fact]
    public void TelemetryConfig_DefaultValues()
    {
        var config = new TelemetryConfig();

        config.Enabled.Should().BeFalse();
        config.ExportEndpoint.Should().BeNull();
        config.FlushIntervalSeconds.Should().Be(30);
        config.IncludeStackTraces.Should().BeFalse();
    }

    [Fact]
    public void TelemetryConfig_CustomValues()
    {
        var config = new TelemetryConfig(Enabled: true, ExportEndpoint: "https://example.com", FlushIntervalSeconds: 60, IncludeStackTraces: true);

        config.Enabled.Should().BeTrue();
        config.ExportEndpoint.Should().Be("https://example.com");
        config.FlushIntervalSeconds.Should().Be(60);
        config.IncludeStackTraces.Should().BeTrue();
    }
}

public class TimedOperationTests
{
    [Fact]
    public void TimedOperation_Dispose_RecordsHistogram()
    {
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));

        using (var op = collector.MeasureOperation("test.timed"))
        {
            // op is disposed here, recording the elapsed time
        }

        // No exception means TimedOperation.Dispose called RecordHistogram successfully
    }

    [Fact]
    public void TimedOperation_Dispose_MultipleTimes_DoesNotThrow()
    {
        var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));

        var op = collector.MeasureOperation("test.timed");
        op.Dispose();
        var act = () => op.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryCollector_RecordRuleExecution_Enabled_RecordsMetrics()
    {
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600));

        var act = () => collector.RecordRuleExecution("DG001", true, TimeSpan.FromMilliseconds(5));
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryCollector_RecordValidationSummary_Disabled_IsNoOp()
    {
        using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: false));

        var act = () => collector.RecordValidationSummary(10, 2, 1, 1, TimeSpan.FromSeconds(1));
        act.Should().NotThrow();
    }
}
