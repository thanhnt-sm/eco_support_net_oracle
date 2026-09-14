using System.Collections.Frozen;
using System.Diagnostics.Metrics;

namespace DataGuard.Observability.Messaging;

/// <summary>Records bounded messaging dimensions without collecting message payloads or keys.</summary>
public interface IMessagingOperationMetrics
{
    void RecordProcessingDuration(string operation, string result, string messagingSystem, double milliseconds);
    void RecordDeliveryLag(string operation, string messagingSystem, double milliseconds);
    void RecordRedelivery(string messagingSystem, string result);
}

/// <summary>Default messaging metrics implementation for client-specific producer/consumer adapters.</summary>
public sealed class MessagingOperationMetrics : IMessagingOperationMetrics
{
    private static readonly Meter Meter = new("DataGuard.Observability.Messaging", "1.0.0");
    private static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>("messaging.processing.duration", "ms");
    private static readonly Histogram<double> DeliveryLag = Meter.CreateHistogram<double>("messaging.delivery.lag", "ms");
    private static readonly Counter<long> Redelivery = Meter.CreateCounter<long>("messaging.redelivery.count", "{redelivery}");
    private static readonly FrozenSet<string> Systems = new HashSet<string>(StringComparer.Ordinal)
    {
        "kafka", "rabbitmq", "unknown"
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> Results = new HashSet<string>(StringComparer.Ordinal)
    {
        "success", "accepted", "eventually_completed", "business_rejection", "technical_failure",
        "timeout", "dependency_unavailable", "duplicate", "idempotent_no_op", "unknown"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DefaultOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "banking.transfer.initiate",
        "banking.transfer.approve",
        "banking.transfer.complete",
        "banking.reconciliation.execute",
        "banking.account.balance.read",
        "banking.payment.instruction.process",
        "banking.account.open",
        "banking.message.process"
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly FrozenSet<string> operations;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingOperationMetrics"/> class.
    /// </summary>
    /// <remarks>
    /// Custom names must be declared explicitly; unrecognized names are dropped instead of
    /// becoming unbounded metric dimensions.
    /// </remarks>
    public MessagingOperationMetrics(IReadOnlySet<string>? allowedOperations = null)
    {
        var candidate = allowedOperations ?? DefaultOperations;
        if (candidate.Count is < 1 or > 500 || candidate.Any(operation => !IsStable(operation)))
        {
            throw new ArgumentException("Messaging operation allowlist must contain 1..500 stable names.", nameof(allowedOperations));
        }

        operations = candidate.ToFrozenSet(StringComparer.Ordinal);
    }

    public void RecordProcessingDuration(string operation, string result, string messagingSystem, double milliseconds)
    {
        if (!operations.Contains(operation) || milliseconds < 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
        try
        {
            ProcessingDuration.Record(
                milliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new("result", Normalize(result, Results)),
                new("messaging.system", Normalize(messagingSystem, Systems)));
        }
        catch (Exception)
        {
            // A telemetry listener/exporter failure must not fail message processing.
        }
    }

    public void RecordDeliveryLag(string operation, string messagingSystem, double milliseconds)
    {
        if (!operations.Contains(operation) || milliseconds < 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
        try
        {
            DeliveryLag.Record(
                milliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new("messaging.system", Normalize(messagingSystem, Systems)));
        }
        catch (Exception)
        {
            // A telemetry listener/exporter failure must not fail message processing.
        }
    }

    public void RecordRedelivery(string messagingSystem, string result)
    {
        try
        {
            Redelivery.Add(
                1,
                new KeyValuePair<string, object?>("messaging.system", Normalize(messagingSystem, Systems)),
                new("result", Normalize(result, Results)));
        }
        catch (Exception)
        {
            // A telemetry listener/exporter failure must not fail message processing.
        }
    }

    private static bool IsStable(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 96
        && !value.Any(char.IsWhiteSpace)
        && !value.Any(char.IsControl)
        && !value.Contains('{')
        && !value.Contains('}')
        && !value.Any(char.IsControl);

    private static string Normalize(string? value, FrozenSet<string> allowlist)
        => value is not null && allowlist.Contains(value) ? value : "unknown";
}
