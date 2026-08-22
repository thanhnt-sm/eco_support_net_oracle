using System;
using System.Threading.Tasks;
using DataGuard.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class TelemetryCollectorTests
{
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
            // fail, success, fail, success — the breaker never trips because
            // failures are not consecutive.
            for (var i = 0; i < 4; i++)
            {
                collector.RecordEvent("test.event", "details");
                collector.FlushEvents(null);
            }
        }

        exportAttempts.Should().Be(4, "non-consecutive failures must not trip the breaker");
    }
}
