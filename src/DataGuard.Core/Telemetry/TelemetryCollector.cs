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
public sealed class TelemetryCollector : IDisposable, IAsyncDisposable
{
    private const int MaxConsecutiveExportFailures = 3;
    internal const int DefaultMaxQueuedEvents = 10_000;
    internal const int DefaultMaxBatchEvents = 1_000;
    internal const int DefaultMaxPayloadBytes = 1_048_576;
    private readonly Meter _meter;
    private readonly TelemetryConfig _config;
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();
    private readonly ConcurrentQueue<TelemetryEvent> _eventQueue = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly Timer? _flushTimer;
    private readonly Func<string, string, Task> _exportSink;
    private int _consecutiveExportFailures;
    private int _queuedEventCount;
    private long _droppedEventCount;
    private long _terminalLossCount;
    private int _lifecycleState;

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
            _flushTimer = new Timer(static state => _ = ((TelemetryCollector)state!).FlushAsync(), this, flushInterval, flushInterval);
        }
    }

    /// <summary>Events discarded because a configured queue or payload limit was reached.</summary>
    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);

    /// <summary>Current collector lifecycle state.</summary>
    public TelemetryLifecycleState LifecycleState => (TelemetryLifecycleState)Volatile.Read(ref _lifecycleState);

    /// <summary>Events that could not be delivered because shutdown completed.</summary>
    public long TerminalLossCount => Interlocked.Read(ref _terminalLossCount);

    /// <summary>
    /// Records a counter increment for a named metric.
    /// </summary>
    public void IncrementCounter(string name, long value = 1, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (!_config.Enabled || LifecycleState != TelemetryLifecycleState.Active)
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
        if (!_config.Enabled || LifecycleState != TelemetryLifecycleState.Active)
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
        if (!_config.Enabled || LifecycleState != TelemetryLifecycleState.Active)
        {
            return;
        }

        var evt = new TelemetryEvent(
            DateTimeOffset.UtcNow,
            eventType,
            details,
            properties?.ToImmutableDictionary() ?? ImmutableDictionary<string, object?>.Empty);

        TryEnqueue(evt);
    }

    /// <summary>
    /// Creates a counter for rule execution.
    /// </summary>
    public void RecordRuleExecution(string ruleId, bool success, TimeSpan duration)
    {
        if (!_config.Enabled || LifecycleState != TelemetryLifecycleState.Active)
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
        if (!_config.Enabled || LifecycleState != TelemetryLifecycleState.Active)
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
        FlushAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Flushes one bounded batch while retaining it when export fails or cancellation occurs.
    /// </summary>
    public async Task<TelemetryFlushResult> FlushAsync(CancellationToken cancellationToken = default)
        => await FlushAsyncCore(allowStopping: false, cancellationToken).ConfigureAwait(false);

    private async Task<TelemetryFlushResult> FlushAsyncCore(bool allowStopping, CancellationToken cancellationToken)
    {
        // Zero-egress guarantee: a disabled collector never reaches any export path.
        if ((!allowStopping && LifecycleState != TelemetryLifecycleState.Active)
            || LifecycleState == TelemetryLifecycleState.Stopped
            || !_config.Enabled || _eventQueue.IsEmpty)
        {
            return TelemetryFlushResult.NoWork;
        }

        // Circuit breaker (SEC-006): after consecutive export failures, stop
        // exporting until a manual reset (new collector instance). Telemetry
        // must never take the validation pipeline down with it.
        if (_consecutiveExportFailures >= MaxConsecutiveExportFailures)
        {
            return TelemetryFlushResult.CircuitOpen;
        }

        if (!await _flushGate.WaitAsync(0, cancellationToken))
        {
            return TelemetryFlushResult.InProgress;
        }

        var releaseGate = true;
        try
        {
            var exportEndpoint = _config.ExportEndpoint;
            if (!IsAllowedExportEndpoint(exportEndpoint))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Telemetry] Export endpoint '{exportEndpoint}' rejected (must be HTTPS, or http://localhost for development). " +
                    "Events are dropped; no data leaves the process.");
                DropQueuedEvents();

                return TelemetryFlushResult.RejectedEndpoint;
            }

            var events = new List<TelemetryEvent>();
            var payloadLines = new List<string>();
            var payloadBytes = 0;
            var maxBatchEvents = NormalizePositive(_config.MaxBatchEvents, DefaultMaxBatchEvents);
            var maxPayloadBytes = NormalizePositive(_config.MaxPayloadBytes, DefaultMaxPayloadBytes);
            while (events.Count < maxBatchEvents && _eventQueue.TryDequeue(out var evt))
            {
                var line = JsonSerializer.Serialize(evt);
                var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (lineBytes > maxPayloadBytes)
                {
                    Interlocked.Decrement(ref _queuedEventCount);
                    Interlocked.Increment(ref _droppedEventCount);
                    continue;
                }

                if (events.Count > 0 && payloadBytes + lineBytes > maxPayloadBytes)
                {
                    _eventQueue.Enqueue(evt);
                    break;
                }

                events.Add(evt);
                payloadLines.Add(line);
                payloadBytes += lineBytes;
            }

            if (events.Count == 0)
            {
                return TelemetryFlushResult.NoWork;
            }

            var ndjson = string.Join(Environment.NewLine, payloadLines);
            Task? exportTask = null;
            try
            {
                var timeout = TimeSpan.FromSeconds(NormalizePositive(_config.ExportTimeoutSeconds, 5));
                exportTask = _exportSink(ndjson, exportEndpoint!);
                await exportTask.WaitAsync(timeout, cancellationToken);
                Interlocked.Add(ref _queuedEventCount, -events.Count);
                _consecutiveExportFailures = 0;
                return TelemetryFlushResult.Exported;
            }
            catch (Exception ex)
            {
                foreach (var evt in events)
                {
                    _eventQueue.Enqueue(evt);
                }
                _consecutiveExportFailures++;

                // Legacy delegates do not accept a cancellation token. Keep the
                // single-flight gate until such a timed-out delegate actually
                // ends, so later timer ticks cannot start unbounded overlapping
                // exports against the same retained batch.
                if (exportTask is { IsCompleted: false })
                {
                    releaseGate = false;
                    _ = ReleaseFlushGateWhenExportCompletesAsync(exportTask);
                }

                // Best-effort export; never throw from a timer callback — but do
                // not swallow the failure silently either.
                System.Diagnostics.Debug.WriteLine(
                    $"[Telemetry] Export failed ({_consecutiveExportFailures}/{MaxConsecutiveExportFailures}): {ex.Message}");
                return cancellationToken.IsCancellationRequested ? TelemetryFlushResult.Cancelled : TelemetryFlushResult.Failed;
            }
        }
        finally
        {
            if (releaseGate)
            {
                _flushGate.Release();
            }
        }
    }

    private async Task ReleaseFlushGateWhenExportCompletesAsync(Task exportTask)
    {
        try
        {
            await exportTask.ConfigureAwait(false);
        }
        catch
        {
            // The failed attempt has already been retained and reported by the
            // caller that observed its timeout/cancellation.
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void TryEnqueue(TelemetryEvent evt)
    {
        if (LifecycleState != TelemetryLifecycleState.Active)
        {
            Interlocked.Increment(ref _terminalLossCount);
            return;
        }

        var maxQueuedEvents = NormalizePositive(_config.MaxQueuedEvents, DefaultMaxQueuedEvents);
        while (true)
        {
            var current = Volatile.Read(ref _queuedEventCount);
            if (current >= maxQueuedEvents)
            {
                Interlocked.Increment(ref _droppedEventCount);
                return;
            }

            if (Interlocked.CompareExchange(ref _queuedEventCount, current + 1, current) == current)
            {
                if (LifecycleState != TelemetryLifecycleState.Active)
                {
                    Interlocked.Decrement(ref _queuedEventCount);
                    Interlocked.Increment(ref _terminalLossCount);
                    return;
                }
                _eventQueue.Enqueue(evt);
                return;
            }
        }
    }

    private void DropQueuedEvents(bool terminalLoss = false)
    {
        while (_eventQueue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedEventCount);
            Interlocked.Increment(ref _droppedEventCount);
            if (terminalLoss)
            {
                Interlocked.Increment(ref _terminalLossCount);
            }
        }
    }

    private static int NormalizePositive(int value, int fallback) => value > 0 ? value : fallback;

    /// <summary>
    /// Only HTTPS endpoints (or loopback HTTP for local development) may receive
    /// telemetry. Anything else is rejected before any network call.
    /// </summary>
    private static bool IsAllowedExportEndpoint(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || uri.Host == "localhost");
    }

    // Shared process-lifetime client (SEC-005): per-call instantiation causes
    // socket exhaustion when the flush timer fires repeatedly.
    private static readonly HttpClient ExportHttpClient = new();

    private static async Task ExportEventsAsync(string ndjson, string endpoint)
    {
        // NDJSON export - one JSON object per line, compatible with OTLP/HTTP collectors.
        using var content = new StringContent(ndjson, System.Text.Encoding.UTF8, "application/x-ndjson");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var response = await ExportHttpClient.PostAsync(endpoint, content, timeout.Token);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _lifecycleState, (int)TelemetryLifecycleState.Stopped, (int)TelemetryLifecycleState.Active)
            == (int)TelemetryLifecycleState.Active)
        {
            _flushTimer?.Dispose();
            DropQueuedEvents(terminalLoss: true);
            _meter.Dispose();
        }
    }

    /// <summary>
    /// Stops timer-driven work and attempts one final asynchronous export before disposal.
    /// A failed final export remains observable through the returned flush result only;
    /// synchronous <see cref="Dispose"/> remains best effort for compatibility.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _lifecycleState, (int)TelemetryLifecycleState.Stopping, (int)TelemetryLifecycleState.Active)
            != (int)TelemetryLifecycleState.Active)
        {
            return;
        }

        _flushTimer?.Dispose();
        _ = await FlushAsyncCore(allowStopping: true, CancellationToken.None).ConfigureAwait(false);
        DropQueuedEvents(terminalLoss: true);
        _meter.Dispose();
        Volatile.Write(ref _lifecycleState, (int)TelemetryLifecycleState.Stopped);
    }
}

/// <summary>Lifecycle state of a telemetry collector.</summary>
public enum TelemetryLifecycleState
{
    Active = 0,
    Stopping = 1,
    Stopped = 2,
}

/// <summary>Observable result from a telemetry flush attempt.</summary>
public enum TelemetryFlushResult
{
    NoWork,
    Exported,
    InProgress,
    CircuitOpen,
    RejectedEndpoint,
    Failed,
    Cancelled,
}

/// <summary>
/// Configuration for telemetry collection.
/// </summary>
public sealed record TelemetryConfig(
    bool Enabled = false,
    string? ExportEndpoint = null,
    int FlushIntervalSeconds = 30,
    bool IncludeStackTraces = false)
{
    /// <summary>Maximum events held across queued and in-flight batches.</summary>
    public int MaxQueuedEvents { get; init; } = TelemetryCollector.DefaultMaxQueuedEvents;

    /// <summary>Maximum events included in one export request.</summary>
    public int MaxBatchEvents { get; init; } = TelemetryCollector.DefaultMaxBatchEvents;

    /// <summary>Maximum UTF-8 payload size for one export request.</summary>
    public int MaxPayloadBytes { get; init; } = TelemetryCollector.DefaultMaxPayloadBytes;

    /// <summary>Maximum wait for one export attempt.</summary>
    public int ExportTimeoutSeconds { get; init; } = 5;
}

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
