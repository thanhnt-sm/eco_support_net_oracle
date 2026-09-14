using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Frozen;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DataGuard.Observability;

public sealed class CoreObservabilityOptions
{
    public bool Enabled { get; set; } = false;
    public bool TracingEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;
    public bool LogsEnabled { get; set; } = true;
    public bool RuntimeInstrumentationEnabled { get; set; } = true;
    public string ServiceName { get; set; } = string.Empty;
    public string? ServiceNamespace { get; set; }
    public string ServiceVersion { get; set; } = "0.0.0";
    public string? ServiceInstanceId { get; set; }
    public string? DeploymentEnvironment { get; set; }
    public string? ClusterName { get; set; }
    public string? KubernetesNamespace { get; set; }
    public string? WorkloadIdentity { get; set; }

    /// <summary>
    /// Explicit owner-gated switch for OTLP network exporters. The DataGuard
    /// CLI/library does not open or call an observability endpoint by default.
    /// </summary>
    public bool RemoteExportEnabled { get; set; } = false;
    public Uri? OtlpEndpoint { get; set; }
    public int MaxQueueSize { get; set; } = 2048;
    public double TraceSamplingRatio { get; set; } = 0.10;

    /// <summary>
    /// Sends every root trace to the Collector so gateway tail-sampling can make
    /// error/latency/critical-operation decisions. Keep this disabled unless the
    /// gateway policy and its capacity budget have been approved.
    /// </summary>
    public bool UseAlwaysOnHeadSamplingForTailSampling { get; set; } = false;
    public bool CaptureExceptionDetails { get; set; } = false;
    public HashSet<string> AllowedOperations { get; } =
    [
        "banking.transfer.initiate",
        "banking.transfer.approve",
        "banking.transfer.complete",
        "banking.reconciliation.execute",
        "banking.account.balance.read",
        "banking.payment.instruction.process",
        "banking.account.open",
        "banking.message.process"
    ];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceName)) throw new InvalidOperationException("ServiceName is required.");
        ValidateResourceValue(nameof(ServiceName), ServiceName);
        if (MaxQueueSize is < 128 or > 100_000) throw new InvalidOperationException("MaxQueueSize must be 128..100000.");
        if (double.IsNaN(TraceSamplingRatio) || double.IsInfinity(TraceSamplingRatio) || TraceSamplingRatio is < 0 or > 1)
            throw new InvalidOperationException("TraceSamplingRatio must be a finite value in the range 0..1.");
        if (UseAlwaysOnHeadSamplingForTailSampling && TraceSamplingRatio != 1.0)
            throw new InvalidOperationException("UseAlwaysOnHeadSamplingForTailSampling requires TraceSamplingRatio=1.0 so the selected head-sampling policy is explicit.");
        if (OtlpEndpoint is not null
            && (OtlpEndpoint.Scheme is not "https" || string.IsNullOrWhiteSpace(OtlpEndpoint.Host)
                || !string.IsNullOrEmpty(OtlpEndpoint.UserInfo)
                || !string.IsNullOrEmpty(OtlpEndpoint.Query)
                || !string.IsNullOrEmpty(OtlpEndpoint.Fragment)))
            throw new InvalidOperationException("OTLP endpoint must be an HTTPS URI with a host and no inline credentials, query or fragment.");
        ValidateResourceValue(nameof(ServiceNamespace), ServiceNamespace);
        ValidateResourceValue(nameof(ServiceVersion), ServiceVersion);
        ValidateResourceValue(nameof(ServiceInstanceId), ServiceInstanceId);
        ValidateResourceValue(nameof(DeploymentEnvironment), DeploymentEnvironment);
        ValidateResourceValue(nameof(ClusterName), ClusterName);
        ValidateResourceValue(nameof(KubernetesNamespace), KubernetesNamespace);
        ValidateResourceValue(nameof(WorkloadIdentity), WorkloadIdentity);
        if (CaptureExceptionDetails && (string.Equals(DeploymentEnvironment, "production", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DeploymentEnvironment, "prod", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("CaptureExceptionDetails must remain disabled in production.");
        if (AllowedOperations.Count is < 1 or > 500) throw new InvalidOperationException("AllowedOperations must contain 1..500 entries.");
        foreach (var operation in AllowedOperations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation) || operation.Length > 96
                || operation.Any(char.IsWhiteSpace) || operation.Any(char.IsControl)
                || operation.Contains('{') || operation.Contains('}'))
                throw new InvalidOperationException($"Allowed operation '{operation}' is not stable and bounded.");
        }
    }

    private static void ValidateResourceValue(string propertyName, string? value)
    {
        if (value is null) return;
        if (value.Length is 0 or > 128 || value.Any(char.IsControl))
            throw new InvalidOperationException($"{propertyName} must be 1..128 characters without control characters.");
    }
}

public enum ObservedOperationKind { Command, Query, Event, Job }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ObservedOperationAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
    public string SloClass { get; set; } = "standard";
    public ObservedOperationKind Kind { get; set; } = ObservedOperationKind.Command;

    public ObservedOperationDescriptor ToDescriptor() => new(Name, SloClass, Kind);
}
public sealed record ObservedOperationDescriptor(string Name, string SloClass, ObservedOperationKind Kind)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 96 || Name.Any(char.IsWhiteSpace)
            || Name.Any(char.IsControl) || Name.Contains('{') || Name.Contains('}'))
            throw new ArgumentException("Operation name must be stable and bounded.", nameof(Name));
        if (SloClass is not ("critical" or "standard" or "low"))
            throw new ArgumentException("SloClass must be critical, standard or low.", nameof(SloClass));
    }
}

public interface IBusinessOperationObserver
{
    Task<T> ExecuteAsync<T>(ObservedOperationDescriptor operation, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
    Task ExecuteAsync(ObservedOperationDescriptor operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}

/// <summary>Classifies a successful operation result without exporting the result itself.</summary>
public interface IBusinessOperationResultClassifier
{
    string Classify(ObservedOperationDescriptor operation, object? result);
}

/// <summary>Safe default classifier used when a host has no business-specific classifier.</summary>
public sealed class DefaultBusinessOperationResultClassifier : IBusinessOperationResultClassifier
{
    public string Classify(ObservedOperationDescriptor operation, object? result) => "success";
}

internal sealed class BusinessOperationObserver(
    ILogger<BusinessOperationObserver> logger,
    IOptions<CoreObservabilityOptions> configuredOptions,
    IBusinessOperationResultClassifier classifier) : IBusinessOperationObserver
{
    private static readonly AsyncLocal<HashSet<string>?> ActiveOperations = new();
    private readonly CoreObservabilityOptions options = configuredOptions.Value;
    private readonly FrozenSet<string> allowedOperations = configuredOptions.Value.AllowedOperations.ToFrozenSet(StringComparer.Ordinal);
    private static readonly ActivitySource Source = new("DataGuard.Observability.Business");
    private static readonly Meter Meter = new("DataGuard.Observability", "1.0.0");
    private static readonly Counter<long> Count = Meter.CreateCounter<long>("business.operation.count");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("business.operation.failure.count");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("business.operation.duration", "ms");
    private static readonly EventId OperationFailureEvent = new(7001, "ObservedOperationFailure");
    private static readonly EventId ResultClassifierFailureEvent = new(7002, "ObservedResultClassifierFailure");
    private static readonly FrozenSet<string> AllowedResults = new HashSet<string>(StringComparer.Ordinal)
    {
        "success", "accepted", "eventually_completed", "business_rejection", "validation_failure",
        "authentication_failure", "authorization_denial", "technical_failure", "timeout",
        "dependency_unavailable", "cancellation", "concurrency_conflict", "duplicate",
        "idempotent_no_op", "unknown"
    }.ToFrozenSet(StringComparer.Ordinal);
    private readonly IBusinessOperationResultClassifier resultClassifier = classifier;
    public async Task<T> ExecuteAsync<T>(ObservedOperationDescriptor operation, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        operation.Validate();
        if (!allowedOperations.Contains(operation.Name))
            throw new ArgumentException($"Operation '{operation.Name}' is not in the configured allowlist.", nameof(operation));
        var parentOperations = ActiveOperations.Value;
        if (parentOperations?.Contains(operation.Name) == true)
            return await action(cancellationToken).ConfigureAwait(false);
        var currentOperations = parentOperations is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(parentOperations, StringComparer.Ordinal);
        currentOperations.Add(operation.Name);
        ActiveOperations.Value = currentOperations;
        try
        {
            var activity = TryStartActivity(operation.Name);
            TrySetTag(activity, "banking.operation.name", operation.Name);
            TrySetTag(activity, "banking.slo.class", operation.SloClass);
            var start = Stopwatch.GetTimestamp();
            var resultLabel = "unknown";
            try
            {
                var result = await action(cancellationToken).ConfigureAwait(false);
                var resultClassification = ClassifyResult(operation, result);
                resultLabel = resultClassification;
                TryRecordCount(operation.Name, resultClassification);
                if (IsTechnicalFailure(resultClassification))
                {
                    var errorType = ErrorTypeForResult(resultClassification);
                    TryRecordFailure(operation.Name, resultClassification, errorType);
                    TrySetTag(activity, "error.type", errorType);
                    TrySetStatus(activity, ActivityStatusCode.Error);
                }
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                resultLabel = "cancellation";
                TryRecordCount(operation.Name, "cancellation");
                throw;
            }
            catch (Exception ex)
            {
                var error = ClassifyError(ex);
                resultLabel = error.Result;

                // Count every valid operation attempt so the SLI denominator includes
                // technical failures; the failure counter is a diagnostic subset.
                TryRecordCount(operation.Name, error.Result);
                TryRecordFailure(operation.Name, error.Result, error.ErrorType);
                TryRecordException(activity, ex, options.CaptureExceptionDetails);
                TrySetStatus(activity, ActivityStatusCode.Error);
                TryLogError(operation.Name, error.ErrorType);
                throw;
            }
            finally
            {
                TryRecordDuration(operation.Name, resultLabel, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                TryStopActivity(activity);
            }
        }
        finally
        {
            ActiveOperations.Value = parentOperations;
        }
    }
    public Task ExecuteAsync(ObservedOperationDescriptor operation, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) => ExecuteAsync<object?>(operation, async ct => { await action(ct).ConfigureAwait(false); return null; }, cancellationToken);

    private string ClassifyResult(ObservedOperationDescriptor operation, object? result)
    {
        try
        {
            var classification = resultClassifier.Classify(operation, result);
            return classification is not null && AllowedResults.Contains(classification) ? classification : "unknown";
        }
        catch (Exception exception)
        {
            TryLogWarning(operation.Name, exception.GetType().Name);
            return "unknown";
        }
    }

    private static Activity? TryStartActivity(string operation)
    {
        try
        {
            return Source.StartActivity(operation, ActivityKind.Internal);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TrySetTag(Activity? activity, string key, object? value)
    {
        try
        {
            activity?.SetTag(key, value);
        }
        catch (Exception)
        {
            // A faulty Activity listener is outside the business failure contract.
        }
    }

    private static void TryRecordException(Activity? activity, Exception exception, bool captureDetails)
    {
        try
        {
            if (captureDetails)
            {
                activity?.AddException(exception);
            }
            else
            {
                activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    ["exception.type"] = SafeExceptionType(exception),
                    ["exception.escaped"] = true
                }));
            }
        }
        catch (Exception)
        {
            // Exception recording is diagnostic best effort and cannot replace the original exception.
        }
    }

    private static void TrySetStatus(Activity? activity, ActivityStatusCode status)
    {
        try
        {
            activity?.SetStatus(status);
        }
        catch (Exception)
        {
            // A faulty Activity listener is outside the business failure contract.
        }
    }

    private static void TryStopActivity(Activity? activity)
    {
        try
        {
            activity?.Stop();
        }
        catch (Exception)
        {
            // ActivityStopped callbacks are telemetry-only and must not fail the operation.
        }
    }

    private static void TryRecordCount(string operation, string result)
    {
        try
        {
            Count.Add(1, new KeyValuePair<string, object?>("operation", operation), new("result", result));
        }
        catch (Exception)
        {
            // Telemetry listeners/exporters are never allowed to replace a business result.
        }
    }

    private static void TryRecordFailure(string operation, string result, string errorType)
    {
        try
        {
            Failures.Add(
                1,
                new("operation", operation),
                new("result", result),
                new("error.type", errorType));
        }
        catch (Exception)
        {
            // Telemetry listeners/exporters are never allowed to replace the original exception.
        }
    }

    private static void TryRecordDuration(string operation, string result, double milliseconds)
    {
        try
        {
            Duration.Record(
                milliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new("result", result));
        }
        catch (Exception)
        {
            // Telemetry listeners/exporters are best effort and bounded by the host SDK.
        }
    }

    private static bool IsTechnicalFailure(string result) => result is
        "technical_failure" or "timeout" or "dependency_unavailable" or "unknown";

    private static string ErrorTypeForResult(string result) => result switch
    {
        "timeout" => "timeout",
        "dependency_unavailable" => "dependency",
        "unknown" => "unknown",
        _ => "technical"
    };

    private void TryLogError(string operation, string errorType)
    {
        try
        {
            logger.LogError(OperationFailureEvent, "Observed operation {Operation} failed with {ErrorType}", operation, errorType);
        }
        catch (Exception)
        {
            // A faulty logging provider must not change business exception semantics.
        }
    }

    private void TryLogWarning(string operation, string errorType)
    {
        try
        {
            logger.LogWarning(ResultClassifierFailureEvent, "Result classifier failed for {Operation} with {ErrorType}", operation, errorType);
        }
        catch (Exception)
        {
            // A faulty logging provider must not change business result semantics.
        }
    }

    private static string SafeExceptionType(Exception exception)
    {
        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        return typeName.Length is 0 or > 128 || typeName.Any(char.IsControl) ? "unknown" : typeName;
    }

    private static (string Result, string ErrorType) ClassifyError(Exception exception) => exception switch
    {
        TimeoutException => ("timeout", "timeout"),
        TaskCanceledException => ("timeout", "timeout"),
        HttpRequestException => ("dependency_unavailable", "dependency"),
        SocketException => ("dependency_unavailable", "dependency"),
        UnauthorizedAccessException => ("authorization_denial", "authorization"),
        ArgumentException => ("validation_failure", "validation"),
        _ => ("technical_failure", "technical")
    };
}

public static class CoreObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCoreObservability(this IServiceCollection services, IConfiguration configuration)
        => services.AddCoreObservability(configuration.GetSection("Observability").Get<CoreObservabilityOptions>() ?? new());
    public static IServiceCollection AddCoreObservability(this IServiceCollection services, Action<CoreObservabilityOptions> configure)
    { var options = new CoreObservabilityOptions(); configure(options); return services.AddCoreObservability(options); }
    private static IServiceCollection AddCoreObservability(this IServiceCollection services, CoreObservabilityOptions options)
    {
        options.Validate();
        if (services.Any(d => d.ServiceType == typeof(ObservabilityRegistrationMarker))) return services;
        services.AddSingleton<ObservabilityRegistrationMarker>();
        services.TryAddSingleton<IOptions<CoreObservabilityOptions>>(Options.Create(options));
        services.TryAddSingleton<IBusinessOperationResultClassifier, DefaultBusinessOperationResultClassifier>();
        services.TryAddSingleton<IBusinessOperationObserver, BusinessOperationObserver>();
        if (!options.Enabled) return services;
        var otel = services.AddOpenTelemetry().ConfigureResource(resource =>
        {
            resource.AddService(
                options.ServiceName,
                serviceNamespace: options.ServiceNamespace,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: options.ServiceInstanceId);
            var attributes = new List<KeyValuePair<string, object>>();
            AddResourceAttribute(attributes, "deployment.environment.name", options.DeploymentEnvironment);
            AddResourceAttribute(attributes, "k8s.cluster.name", options.ClusterName);
            AddResourceAttribute(attributes, "k8s.namespace.name", options.KubernetesNamespace);
            AddResourceAttribute(attributes, "dataguard.workload.identity", options.WorkloadIdentity);
            if (attributes.Count > 0) resource.AddAttributes(attributes);
        });
        if (options.TracingEnabled)
        {
            otel.WithTracing(t =>
            {
                Sampler headSampler = options.UseAlwaysOnHeadSamplingForTailSampling
                    ? new AlwaysOnSampler()
                    : new TraceIdRatioBasedSampler(options.TraceSamplingRatio);
                t.AddSource("DataGuard.Observability.Business")
                    .AddSource("DataGuard.Observability.Messaging")
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.Filter = context => !IsProbe(context.Request.Path);
                        o.RecordException = false;
                    })
                    .AddHttpClientInstrumentation(o =>
                    {
                        o.RecordException = false;
                    })
                    .SetSampler(new ParentBasedSampler(headSampler));

                // Remote exporter registration is intentionally disabled for the
                // non-server product. Re-enable only with an owner-approved
                // endpoint and a validated transport/capacity contract.
                if (options.RemoteExportEnabled && options.OtlpEndpoint is not null)
                {
                    t.AddOtlpExporter(o =>
                    {
                        o.Endpoint = options.OtlpEndpoint;
                        o.BatchExportProcessorOptions.MaxQueueSize = options.MaxQueueSize;
                        o.BatchExportProcessorOptions.MaxExportBatchSize = Math.Min(512, options.MaxQueueSize);
                        o.BatchExportProcessorOptions.ExporterTimeoutMilliseconds = 5000;
                    });
                }
            });
        }
        if (options.MetricsEnabled)
        {
            otel.WithMetrics(m =>
            {
                m.AddMeter("DataGuard.Observability")
                    .AddMeter("DataGuard.Observability.Messaging")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (options.RuntimeInstrumentationEnabled)
                {
                    m.AddRuntimeInstrumentation();
                }

                // Keep the network exporter behind the same explicit switch as
                // tracing; local metrics remain available without egress.
                if (options.RemoteExportEnabled && options.OtlpEndpoint is not null)
                {
                    m.AddOtlpExporter(o => o.Endpoint = options.OtlpEndpoint);
                }
            });
        }
        if (options.LogsEnabled && options.RemoteExportEnabled && options.OtlpEndpoint is not null)
        {
            otel.WithLogging(
                logging => logging.AddOtlpExporter((exporter, processor) =>
                {
                    exporter.Endpoint = options.OtlpEndpoint;
                    processor.BatchExportProcessorOptions.MaxQueueSize = options.MaxQueueSize;
                    processor.BatchExportProcessorOptions.MaxExportBatchSize = Math.Min(512, options.MaxQueueSize);
                    processor.BatchExportProcessorOptions.ExporterTimeoutMilliseconds = 5000;
                }),
                loggingOptions =>
                {
                    loggingOptions.IncludeFormattedMessage = false;
                    loggingOptions.IncludeScopes = true;
                    loggingOptions.ParseStateValues = true;
                });
        }
        return services;
    }

    private static bool IsProbe(PathString path) => path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics");

    private static void AddResourceAttribute(ICollection<KeyValuePair<string, object>> attributes, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) attributes.Add(new(key, value));
    }
}

internal sealed class ObservabilityRegistrationMarker
{
}
