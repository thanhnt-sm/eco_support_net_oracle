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
