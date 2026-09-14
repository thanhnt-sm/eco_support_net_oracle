using System.Diagnostics;
using System.Text.Json;
using DataGuard.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class ObservabilityFileSinkTests
{
    [Fact]
    public async Task Collector_WritesStandardNdjsonAndRedactsDetails()
    {
        using var directory = new TemporaryDirectory("dg-observability-");
        using var activity = new Activity("validation").SetIdFormat(ActivityIdFormat.W3C).Start();
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600)
            {
                FileSinkDirectory = directory.FullName,
                ServiceName = "dataguard-tests",
                ServiceVersion = "1.2.3",
                IncludeEventDetails = true,
            });

        collector.RecordEvent(
            "validation.completed",
            "PAN=4111111111111111; Authorization: Bearer auth-secret-token; bearer super-secret-token; customer@example.com",
            new Dictionary<string, object?>
            {
                ["operation"] = "banking.account.balance.read",
                ["customer.id"] = "must-not-be-written",
                ["result"] = "success",
            });

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        collector.LastObservabilityFilePath.Should().NotBeNull();

        var line = (await File.ReadAllLinesAsync(collector.LastObservabilityFilePath!)).Single();
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        root.GetProperty("signal").GetString().Should().Be("log");
        root.GetProperty("event_name").GetString().Should().Be("validation.completed");
        root.GetProperty("service_name").GetString().Should().Be("dataguard-tests");
        root.GetProperty("resource").GetProperty("service.name").GetString().Should().Be("dataguard-tests");
        root.GetProperty("trace_id").GetString().Should().Be(activity!.TraceId.ToHexString());
        root.GetProperty("attributes").TryGetProperty("customer.id", out _).Should().BeFalse();
        root.GetProperty("body").GetString().Should().NotContain("4111111111111111");
        root.GetProperty("body").GetString().Should().NotContain("super-secret-token");
        root.GetProperty("body").GetString().Should().NotContain("auth-secret-token");
        root.GetProperty("body").GetString().Should().NotContain("customer@example.com");
    }

    [Fact]
    public async Task Collector_OmitsEventBodyByDefault()
    {
        using var directory = new TemporaryDirectory("dg-observability-no-body-");
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600)
            {
                FileSinkDirectory = directory.FullName,
            });

        collector.RecordEvent("diagnostic.completed", "should-not-be-archived");

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        var line = (await File.ReadAllLinesAsync(collector.LastObservabilityFilePath!)).Single();
        using var document = JsonDocument.Parse(line);
        document.RootElement.TryGetProperty("body", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Collector_RecordsMetricsWithFiniteAllowlistedAttributes()
    {
        using var directory = new TemporaryDirectory("dg-observability-metrics-");
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600)
            {
                FileSinkDirectory = directory.FullName,
                MaxQueuedEvents = 10,
            });

        collector.IncrementCounter(
            "business.operation.count",
            tags: new Dictionary<string, object?>
            {
                ["operation"] = "banking.transfer.initiate",
                ["result"] = "success",
                ["transaction.id"] = Guid.NewGuid().ToString("N"),
            });
        collector.RecordHistogram(
            "business.operation.duration",
            12.5,
            new Dictionary<string, object?>
            {
                ["operation"] = "banking.transfer.initiate",
                ["trace_id"] = Guid.NewGuid().ToString("N"),
                ["exception.message"] = "secret",
            });

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        var lines = await File.ReadAllLinesAsync(collector.LastObservabilityFilePath!);
        lines.Should().HaveCount(2);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            root.GetProperty("signal").GetString().Should().Be("metric");
            root.GetProperty("attributes").TryGetProperty("transaction.id", out _).Should().BeFalse();
            root.GetProperty("attributes").TryGetProperty("trace_id", out _).Should().BeFalse();
            root.GetProperty("attributes").TryGetProperty("exception.message", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Collector_AutomaticallyWritesValidationSpanSummary()
    {
        using var directory = new TemporaryDirectory("dg-observability-validation-");
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, FlushIntervalSeconds: 3600)
            {
                FileSinkDirectory = directory.FullName,
            });

        collector.RecordValidationSummary(
            contractCount: 3,
            violationCount: 2,
            errorCount: 1,
            warningCount: 1,
            totalDuration: TimeSpan.FromMilliseconds(25));

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        var lines = await File.ReadAllLinesAsync(collector.LastObservabilityFilePath!);
        lines.Any(IsValidationFailureSpan).Should().BeTrue();
    }

    [Fact]
    public async Task FileSink_PartitionsRecordsByUtcDay()
    {
        using var directory = new TemporaryDirectory("dg-observability-days-");
        await using var sink = new FileObservabilitySink(new ObservabilityFileOptions
        {
            DirectoryPath = directory.FullName,
            ServiceName = "day-test",
        });
        var records = new[]
        {
            CreateRecord(new DateTimeOffset(2026, 9, 13, 23, 59, 0, TimeSpan.Zero)),
            CreateRecord(new DateTimeOffset(2026, 9, 14, 0, 1, 0, TimeSpan.Zero)),
        };

        var result = await sink.WriteBatchAsync(records);

        result.WrittenRecords.Should().Be(2);
        result.Files.Should().HaveCount(2);
        File.Exists(Path.Combine(directory.FullName, "2026", "09", "13", "observability-2026-09-13.ndjson")).Should().BeTrue();
        File.Exists(Path.Combine(directory.FullName, "2026", "09", "14", "observability-2026-09-14.ndjson")).Should().BeTrue();
    }

    [Fact]
    public async Task Collector_DropsBoundedRecordsWithoutOpeningEndpoint()
    {
        using var directory = new TemporaryDirectory("dg-observability-bounded-");
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "http://127.0.0.1:1/should-not-connect", FlushIntervalSeconds: 3600)
            {
                FileSinkDirectory = directory.FullName,
                MaxQueuedEvents = 1,
            });

        collector.RecordEvent("first.event", "details");
        collector.RecordEvent("second.event", "details");

        collector.DroppedObservabilityRecordCount.Should().Be(1);
        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.Exported);
        collector.LastObservabilityFilePath.Should().NotBeNull();
        (await File.ReadAllTextAsync(collector.LastObservabilityFilePath!)).Should().Contain("first.event");
        (await File.ReadAllTextAsync(collector.LastObservabilityFilePath!)).Should().NotContain("second.event");
    }

    [Fact]
    public async Task RemoteExport_IsDisabledUnlessExplicitlyEnabled()
    {
        await using var collector = new TelemetryCollector(
            new TelemetryConfig(Enabled: true, ExportEndpoint: "http://127.0.0.1:1/should-not-connect", FlushIntervalSeconds: 3600)
            {
                FileSinkEnabled = false,
            });

        collector.RecordEvent("remote.disabled", "details");

        (await collector.FlushAsync()).Should().Be(TelemetryFlushResult.NoWork);
        collector.DroppedEventCount.Should().Be(1);
    }

    private static ObservabilityRecord CreateRecord(DateTimeOffset timestamp)
        => new(
            Guid.NewGuid().ToString("N"),
            timestamp,
            "log",
            "day.test",
            "day-test",
            "1.0.0",
            null,
            null,
            "INFO",
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["service.name"] = "day-test",
            });

    private static bool IsValidationFailureSpan(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return root.GetProperty("signal").GetString() == "span"
            && root.GetProperty("operation_name").GetString() == "dataguard.validation"
            && root.GetProperty("attributes").GetProperty("result").GetString() == "validation_failure";
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory;

        public TemporaryDirectory(string prefix)
        {
            _directory = Directory.CreateTempSubdirectory(prefix);
        }

        public string FullName => _directory.FullName;

        public void Dispose()
        {
            if (_directory.Exists)
            {
                _directory.Delete(recursive: true);
            }
        }
    }
}
