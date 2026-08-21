using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace DataGuard.Core.Telemetry;

/// <summary>
/// Opt-in telemetry collector for performance monitoring.
/// Only activates when explicitly enabled via configuration.
/// No data is sent externally - all metrics are local or exported via standard interfaces.
/// </summary>
public sealed class TelemetryCollector : IDisposable
{
    private readonly Meter _meter;
    private readonly TelemetryConfig _config;
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();
    private readonly ConcurrentQueue<TelemetryEvent> _eventQueue = new();
    private readonly Timer? _flushTimer;
    private readonly Func<string, string, Task> _exportSink;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryCollector"/> class.
    /// The export sink receives (payload, endpoint) and defaults to an NDJSON
    /// HTTP POST; injecting a sink keeps egress testable without a network.
    /// </summary>
    public TelemetryCollector(TelemetryConfig config, Func<string, string, Task>? exportSink = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _meter = new Meter("DataGuard.Core", "1.0.0");
        _exportSink = exportSink ?? ExportEventsAsync;

        if (_config.Enabled)
        {
            var flushInterval = TimeSpan.FromSeconds(Math.Max(1, _config.FlushIntervalSeconds));
            _flushTimer = new Timer(FlushEvents, null, flushInterval, flushInterval);
        }
    }

    /// <summary>
    /// Records a counter increment for a named metric.
    /// </summary>
    public void IncrementCounter(string name, long value = 1, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var counter = _counters.GetOrAdd(name, n =>
            _meter.CreateCounter<long>(n, description: $"Counter for {n}"));

        var tagsList = tags?.ToList() ?? new List<KeyValuePair<string, object?>>();
        counter.Add(value, tagsList.ToArray());
    }

    /// <summary>
    /// Records a histogram value for latency/size measurements.
    /// </summary>
    public void RecordHistogram(string name, double value, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var histogram = _histograms.GetOrAdd(name, n =>
            _meter.CreateHistogram<double>(n, unit: "ms", description: $"Histogram for {n}"));

        var tagsList = tags?.ToList() ?? new List<KeyValuePair<string, object?>>();
        histogram.Record(value, tagsList.ToArray());
    }

    /// <summary>
    /// Records a timed operation with automatic histogram recording.
    /// </summary>
    /// <returns></returns>
    public IDisposable MeasureOperation(string operationName, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return new TimedOperation(this, operationName, tags);
    }

    /// <summary>
    /// Records an event for later analysis.
    /// </summary>
    public void RecordEvent(string eventType, string details, IDictionary<string, object?>? properties = null)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var evt = new TelemetryEvent(
            DateTimeOffset.UtcNow,
            eventType,
            details,
            properties?.ToImmutableDictionary() ?? ImmutableDictionary<string, object?>.Empty);

        _eventQueue.Enqueue(evt);
    }

    /// <summary>
    /// Creates a counter for rule execution.
    /// </summary>
    public void RecordRuleExecution(string ruleId, bool success, TimeSpan duration)
    {
        if (!_config.Enabled)
        {
            return;
        }

        IncrementCounter("rule.executions", 1, new[]
        {
            new KeyValuePair<string, object?>("rule", ruleId),
            new KeyValuePair<string, object?>("success", success.ToString()),
        });

        RecordHistogram("rule.duration", duration.TotalMilliseconds, new[]
        {
            new KeyValuePair<string, object?>("rule", ruleId),
            new KeyValuePair<string, object?>("success", success.ToString()),
        });
    }

    /// <summary>
    /// Records validation summary metrics.
    /// </summary>
    public void RecordValidationSummary(int contractCount, int violationCount, int errorCount, int warningCount, TimeSpan totalDuration)
    {
        if (!_config.Enabled)
        {
            return;
        }

        IncrementCounter("validations.total", 1);
        IncrementCounter("violations.total", violationCount);
        IncrementCounter("violations.errors", errorCount);
        IncrementCounter("violations.warnings", warningCount);
        RecordHistogram("validation.contracts", contractCount);
        RecordHistogram("validation.duration", totalDuration.TotalMilliseconds);
    }

    /// <summary>
    /// Flushes queued events to the configured endpoint (when enabled).
    /// Callable directly; also driven by the flush timer.
    /// </summary>
    public void FlushEvents(object? state)
    {
        // Zero-egress guarantee: a disabled collector never reaches any export path.
        if (!_config.Enabled || _eventQueue.IsEmpty)
        {
            return;
        }

        var events = new List<TelemetryEvent>();
        while (_eventQueue.TryDequeue(out var evt))
        {
            events.Add(evt);
        }

        var exportEndpoint = _config.ExportEndpoint;
        if (!string.IsNullOrEmpty(exportEndpoint))
        {
            var ndjson = string.Join(Environment.NewLine, events.Select(e => JsonSerializer.Serialize(e)));
            try
            {
                _exportSink(ndjson, exportEndpoint).GetAwaiter().GetResult();
            }
            catch
            {
                // Export is best-effort; never throw from a timer callback.
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Telemetry] Flushed {events.Count} events");
        }
    }

    // Shared process-lifetime client (SEC-005): per-call instantiation causes
    // socket exhaustion when the flush timer fires repeatedly.
    private static readonly HttpClient ExportHttpClient = new();

    private static async Task ExportEventsAsync(string ndjson, string endpoint)
    {
        // NDJSON export - one JSON object per line, compatible with OTLP/HTTP collectors.
        using var content = new StringContent(ndjson, System.Text.Encoding.UTF8, "application/x-ndjson");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await ExportHttpClient.PostAsync(endpoint, content, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Best-effort export; never throw from a timer callback.
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _flushTimer?.Dispose();
            _meter.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Configuration for telemetry collection.
/// </summary>
public sealed record TelemetryConfig(
    bool Enabled = false,
    string? ExportEndpoint = null,
    int FlushIntervalSeconds = 30,
    bool IncludeStackTraces = false);

/// <summary>
/// Timed operation helper for automatic histogram recording.
/// </summary>
public sealed class TimedOperation : IDisposable
{
    private readonly TelemetryCollector _collector;
    private readonly string _operationName;
    private readonly IEnumerable<KeyValuePair<string, object?>>? _tags;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _disposed;

    public TimedOperation(TelemetryCollector collector, string operationName, IEnumerable<KeyValuePair<string, object?>>? tags)
    {
        _collector = collector;
        _operationName = operationName;
        _tags = tags;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _stopwatch.Stop();
            _collector.RecordHistogram(_operationName, _stopwatch.Elapsed.TotalMilliseconds, _tags);
            _disposed = true;
        }
    }
}

/// <summary>
/// Telemetry event for event-based tracking.
/// </summary>
public sealed record TelemetryEvent(
    DateTimeOffset Timestamp,
    string EventType,
    string Details,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>
/// Metrics for validation pipeline.
/// </summary>
public static class ValidationMetrics
{
    public const string ValidationsTotal = "validations.total";
    public const string ViolationsTotal = "violations.total";
    public const string ViolationsErrors = "violations.errors";
    public const string ViolationsWarnings = "violations.warnings";
    public const string ValidationContracts = "validation.contracts";
    public const string ValidationDuration = "validation.duration";
    public const string RuleExecutions = "rule.executions";
    public const string RuleDuration = "rule.duration";
}