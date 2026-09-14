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
    private readonly ConcurrentQueue<ObservabilityRecord> _fileRecordQueue = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly Timer? _flushTimer;
    private readonly Func<string, string, Task> _exportSink;
    private readonly FileObservabilitySink? _fileSink;
    private readonly bool _injectedExportSink;
    private int _consecutiveExportFailures;
    private int _consecutiveFileWriteFailures;
    private int _queuedEventCount;
    private int _queuedFileRecordCount;
    private long _droppedEventCount;
    private long _droppedFileRecordCount;
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
        _injectedExportSink = exportSink is not null;
        _exportSink = exportSink ?? ExportEventsAsync;

        // The CLI/library product has no observability endpoint. A local,
        // bounded NDJSON sink is therefore the default path whenever no test or
        // owner-supplied exporter is injected. Remote OTLP remains an explicit
        // compatibility mode and is never selected implicitly.
        if (_config.Enabled && _config.FileSinkEnabled && !_injectedExportSink)
        {
            _fileSink = new FileObservabilitySink(new ObservabilityFileOptions
            {
                DirectoryPath = _config.FileSinkDirectory,
                FilePrefix = _config.FileSinkFilePrefix,
                IncludeEventDetails = _config.IncludeEventDetails,
                ServiceName = _config.ServiceName,
                ServiceVersion = _config.ServiceVersion,
                EnvironmentName = _config.EnvironmentName,
                MaxRecordBytes = NormalizePositive(_config.MaxRecordBytes, FileObservabilitySinkDefaults.MaxRecordBytes),
            });
        }

        if (_config.Enabled)
        {
            var flushInterval = TimeSpan.FromSeconds(Math.Max(1, _config.FlushIntervalSeconds));
            _flushTimer = new Timer(static state => _ = ((TelemetryCollector)state!).FlushAsync(), this, flushInterval, flushInterval);
        }
    }

    /// <summary>Events discarded because a configured queue or payload limit was reached.</summary>
    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);

    /// <summary>Local observability records discarded because the bounded file queue was full or oversized.</summary>
    public long DroppedObservabilityRecordCount => Interlocked.Read(ref _droppedFileRecordCount);

    /// <summary>Most recently written local observability archive file.</summary>
    public string? LastObservabilityFilePath => _fileSink?.LastFilePath;

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

        try
        {
            var counter = _counters.GetOrAdd(name, n =>
                _meter.CreateCounter<long>(n, description: $"Counter for {n}"));

            var tagsList = tags?.ToList() ?? new List<KeyValuePair<string, object?>>();
            counter.Add(value, tagsList.ToArray());
            TryEnqueueFileRecord(() => ObservabilityRecordFactory.CreateMetric(
                GetFileOptions(),
                name,
                value,
                "1",
                tagsList));
        }
        catch (Exception exception)
        {
            RecordTelemetryFault("counter", exception);
        }
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

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            if (_fileSink is not null)
            {
                Interlocked.Increment(ref _droppedFileRecordCount);
            }
            return;
        }

        try
        {
            var histogram = _histograms.GetOrAdd(name, n =>
                _meter.CreateHistogram<double>(n, unit: "ms", description: $"Histogram for {n}"));

            var tagsList = tags?.ToList() ?? new List<KeyValuePair<string, object?>>();
            histogram.Record(value, tagsList.ToArray());
            TryEnqueueFileRecord(() => ObservabilityRecordFactory.CreateMetric(
                GetFileOptions(),
                name,
                value,
                "ms",
                tagsList));
        }
        catch (Exception exception)
        {
            RecordTelemetryFault("histogram", exception);
        }
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

        if (_fileSink is not null)
        {
            TryEnqueueFileRecord(() => ObservabilityRecordFactory.CreateLog(
                GetFileOptions(),
                eventType,
                details,
                properties));
            return;
        }

        // Legacy/custom sinks remain available for compatibility and tests.
        // Without an injected sink or an explicit remote-export flag, drop the
        // event rather than opening a network path implicitly.
        if (!_injectedExportSink && !_config.RemoteExportEnabled)
        {
            Interlocked.Increment(ref _droppedEventCount);
            return;
        }

        try
        {
            var evt = new TelemetryEvent(
                DateTimeOffset.UtcNow,
                eventType,
                details,
                properties?.ToImmutableDictionary() ?? ImmutableDictionary<string, object?>.Empty);

            TryEnqueue(evt);
        }
        catch (Exception exception)
        {
            RecordTelemetryFault("log", exception);
        }
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
        TryEnqueueFileRecord(() => ObservabilityRecordFactory.CreateSpan(
            GetFileOptions(),
            "dataguard.validation",
            totalDuration,
            "ok",
            new[]
            {
                new KeyValuePair<string, object?>("result", errorCount > 0 ? "validation_failure" : "success"),
            }));
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
        var hasFileWork = !_fileRecordQueue.IsEmpty;
        var hasRemoteWork = !_eventQueue.IsEmpty;
        if ((!allowStopping && LifecycleState != TelemetryLifecycleState.Active)
            || LifecycleState == TelemetryLifecycleState.Stopped
            || !_config.Enabled || (!hasFileWork && !hasRemoteWork))
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
            if (_fileSink is not null && hasFileWork)
            {
                return await FlushFileRecordsAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_config.RemoteExportEnabled && !_injectedExportSink)
            {
                DropQueuedEvents();
                return TelemetryFlushResult.RemoteExportDisabled;
            }

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
                RequeueEventsOrRecordTerminalLoss(events);
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

    private void TryEnqueueFileRecord(Func<ObservabilityRecord> recordFactory)
    {
        if (_fileSink is null || LifecycleState != TelemetryLifecycleState.Active)
        {
            return;
        }

        try
        {
            TryEnqueueFileRecord(recordFactory());
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _droppedFileRecordCount);
            System.Diagnostics.Debug.WriteLine($"[Telemetry] Local record creation failed: {exception.Message}");
        }
    }

    private void TryEnqueueFileRecord(ObservabilityRecord record)
    {
        if (_fileSink is null || LifecycleState != TelemetryLifecycleState.Active)
        {
            return;
        }

        var maxQueuedRecords = NormalizePositive(_config.MaxQueuedEvents, DefaultMaxQueuedEvents);
        while (true)
        {
            var current = Volatile.Read(ref _queuedFileRecordCount);
            if (current >= maxQueuedRecords)
            {
                Interlocked.Increment(ref _droppedFileRecordCount);
                return;
            }

            if (Interlocked.CompareExchange(ref _queuedFileRecordCount, current + 1, current) == current)
            {
                if (LifecycleState != TelemetryLifecycleState.Active)
                {
                    Interlocked.Decrement(ref _queuedFileRecordCount);
                    Interlocked.Increment(ref _terminalLossCount);
                    return;
                }

                _fileRecordQueue.Enqueue(record);
                return;
            }
        }
    }

    private async Task<TelemetryFlushResult> FlushFileRecordsAsync(CancellationToken cancellationToken)
    {
        if (_fileSink is null || _fileRecordQueue.IsEmpty)
        {
            return TelemetryFlushResult.NoWork;
        }

        if (_consecutiveFileWriteFailures >= MaxConsecutiveExportFailures)
        {
            return TelemetryFlushResult.CircuitOpen;
        }

        var records = new List<ObservabilityRecord>();
        var payloadBytes = 0;
        var maxBatchEvents = NormalizePositive(_config.MaxBatchEvents, DefaultMaxBatchEvents);
        var maxPayloadBytes = NormalizePositive(_config.MaxPayloadBytes, DefaultMaxPayloadBytes);
        while (records.Count < maxBatchEvents && _fileRecordQueue.TryDequeue(out var record))
        {
            var line = FileObservabilitySink.SerializeLine(record);
            var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (lineBytes > maxPayloadBytes)
            {
                Interlocked.Decrement(ref _queuedFileRecordCount);
                Interlocked.Increment(ref _droppedFileRecordCount);
                continue;
            }

            if (records.Count > 0 && payloadBytes + lineBytes > maxPayloadBytes)
            {
                _fileRecordQueue.Enqueue(record);
                break;
            }

            records.Add(record);
            payloadBytes += lineBytes;
        }

        if (records.Count == 0)
        {
            return TelemetryFlushResult.NoWork;
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(NormalizePositive(_config.ExportTimeoutSeconds, 5));
            var writeTask = _fileSink.WriteBatchAsync(records, cancellationToken);
            var result = await writeTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _queuedFileRecordCount, -records.Count);
            if (result.DroppedRecords > 0)
            {
                Interlocked.Add(ref _droppedFileRecordCount, result.DroppedRecords);
            }

            _consecutiveFileWriteFailures = 0;
            return TelemetryFlushResult.Exported;
        }
        catch (Exception exception)
        {
            RequeueFileRecordsOrRecordTerminalLoss(records);

            _consecutiveFileWriteFailures++;
            System.Diagnostics.Debug.WriteLine(
                $"[Telemetry] Local observability archive write failed ({_consecutiveFileWriteFailures}/{MaxConsecutiveExportFailures}): {exception.Message}");
            return cancellationToken.IsCancellationRequested ? TelemetryFlushResult.Cancelled : TelemetryFlushResult.Failed;
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

    private void RequeueEventsOrRecordTerminalLoss(IEnumerable<TelemetryEvent> events)
    {
        foreach (var evt in events)
        {
            if (LifecycleState == TelemetryLifecycleState.Active)
            {
                _eventQueue.Enqueue(evt);
                continue;
            }

            Interlocked.Decrement(ref _queuedEventCount);
            Interlocked.Increment(ref _droppedEventCount);
            Interlocked.Increment(ref _terminalLossCount);
        }
    }

    private void DropFileRecords(bool terminalLoss = false)
    {
        while (_fileRecordQueue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queuedFileRecordCount);
            Interlocked.Increment(ref _droppedFileRecordCount);
            if (terminalLoss)
            {
                Interlocked.Increment(ref _terminalLossCount);
            }
        }
    }

    private void RequeueFileRecordsOrRecordTerminalLoss(IEnumerable<ObservabilityRecord> records)
    {
        foreach (var record in records)
        {
            if (LifecycleState == TelemetryLifecycleState.Active)
            {
                _fileRecordQueue.Enqueue(record);
                continue;
            }

            Interlocked.Decrement(ref _queuedFileRecordCount);
            Interlocked.Increment(ref _droppedFileRecordCount);
            Interlocked.Increment(ref _terminalLossCount);
        }
    }

    private ObservabilityFileOptions GetFileOptions() => new()
    {
        DirectoryPath = _config.FileSinkDirectory,
        FilePrefix = _config.FileSinkFilePrefix,
        IncludeEventDetails = _config.IncludeEventDetails,
        ServiceName = _config.ServiceName,
        ServiceVersion = _config.ServiceVersion,
        EnvironmentName = _config.EnvironmentName,
        MaxRecordBytes = NormalizePositive(_config.MaxRecordBytes, FileObservabilitySinkDefaults.MaxRecordBytes),
    };

    private void RecordTelemetryFault(string signal, Exception exception)
    {
        if (_fileSink is not null)
        {
            Interlocked.Increment(ref _droppedFileRecordCount);
        }

        System.Diagnostics.Debug.WriteLine($"[Telemetry] {signal} recording failed: {exception.Message}");
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

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
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
        if (Interlocked.CompareExchange(ref _lifecycleState, (int)TelemetryLifecycleState.Stopping, (int)TelemetryLifecycleState.Active)
            == (int)TelemetryLifecycleState.Active)
        {
            _flushTimer?.Dispose();
            if (_fileSink is not null && !_fileRecordQueue.IsEmpty)
            {
                try
                {
                    var timeout = TimeSpan.FromSeconds(NormalizePositive(_config.ExportTimeoutSeconds, 5));
                    FlushAsyncCore(allowStopping: true, CancellationToken.None).Wait(timeout);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine($"[Telemetry] Final local archive flush failed: {exception.Message}");
                }
            }
            DropQueuedEvents(terminalLoss: true);
            DropFileRecords(terminalLoss: true);
            _fileSink?.Dispose();
            _meter.Dispose();
            Volatile.Write(ref _lifecycleState, (int)TelemetryLifecycleState.Stopped);
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
        DropFileRecords(terminalLoss: true);
        if (_fileSink is not null)
        {
            await _fileSink.DisposeAsync().ConfigureAwait(false);
        }
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
    RemoteExportDisabled,
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
    /// <summary>
    /// Enables the product-native local observability archive. This is the
    /// default sink for the CLI/library and never opens a network endpoint.
    /// </summary>
    public bool FileSinkEnabled { get; init; } = true;

    /// <summary>Optional root directory for UTC-day observability archives.</summary>
    public string? FileSinkDirectory { get; init; }

    /// <summary>File prefix for daily local observability archives.</summary>
    public string FileSinkFilePrefix { get; init; } = "observability";

    /// <summary>Stable service name written into the local resource envelope.</summary>
    public string ServiceName { get; init; } = "dataguard";

    /// <summary>Service version written into the local resource envelope.</summary>
    public string ServiceVersion { get; init; } = "unknown";

    /// <summary>Deployment environment written into the local resource envelope.</summary>
    public string EnvironmentName { get; init; } = "local";

    /// <summary>
    /// Explicit owner-gated compatibility switch for the legacy remote exporter.
    /// It is false by default and is ignored when the local file sink is active.
    /// </summary>
    public bool RemoteExportEnabled { get; init; }

    /// <summary>
    /// Allows redacted event bodies in the local archive. Disabled by default.
    /// </summary>
    public bool IncludeEventDetails { get; init; }

    /// <summary>Maximum events held across queued and in-flight batches.</summary>
    public int MaxQueuedEvents { get; init; } = TelemetryCollector.DefaultMaxQueuedEvents;

    /// <summary>Maximum events included in one export request.</summary>
    public int MaxBatchEvents { get; init; } = TelemetryCollector.DefaultMaxBatchEvents;

    /// <summary>Maximum UTF-8 payload size for one export request.</summary>
    public int MaxPayloadBytes { get; init; } = TelemetryCollector.DefaultMaxPayloadBytes;

    /// <summary>Maximum wait for one export attempt.</summary>
    public int ExportTimeoutSeconds { get; init; } = 5;

    /// <summary>Maximum serialized local record size in UTF-8 bytes.</summary>
    public int MaxRecordBytes { get; init; } = FileObservabilitySinkDefaults.MaxRecordBytes;
}

internal static class FileObservabilitySinkDefaults
{
    public const int MaxRecordBytes = 64 * 1024;
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
