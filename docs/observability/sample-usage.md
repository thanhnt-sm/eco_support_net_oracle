# Sample usage

## CLI/library default

No endpoint or container is required. Enable the core collector and point it at an operator-owned
archive directory:

```csharp
using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration
{
    EnableTelemetry = true,
    TelemetryFileDirectory = "/var/lib/dataguard/observability/archive",
    TelemetryServiceName = "dataguard-cli",
    TelemetryServiceVersion = "1.0.0",
});

// ValidationPipeline automatically records validation counters, duration and a
// bounded span summary. The files are partitioned by UTC day.
var result = await pipeline.ValidateAsync(contracts, cancellationToken);
```

`TelemetryConfig.RemoteExportEnabled` and `HealthHostOptions.ExposeEndpoints` remain false unless
an owner explicitly enables a compatibility host. The local sink omits event bodies by default.

Business code references only the observer abstraction:

```csharp
public sealed record TransferCommand(string SourceAccount, string DestinationAccount, decimal Amount);
public sealed record TransferResult(string Status);

public sealed class TransferService(IBusinessOperationObserver observer)
{
    public Task<TransferResult> InitiateAsync(TransferCommand command, CancellationToken cancellationToken)
        => observer.ExecuteAsync(
            new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command),
            _ => PersistAndPublishAsync(command, cancellationToken),
            cancellationToken);

    private static Task<TransferResult> PersistAndPublishAsync(TransferCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new TransferResult("accepted"));
}
```

The following is an optional gateway tail-sampling adapter for a different hosted service; it is
not part of the DataGuard CLI/library runtime. For a gateway tail-sampling deployment, the host may explicitly opt into a complete candidate
set (after capacity review):

```csharp
builder.Services.AddCoreObservability(options =>
{
    options.ServiceName = "banking-transfer";
    options.Enabled = true;
    options.RemoteExportEnabled = true;
    options.OtlpEndpoint = new Uri("https://otel-agent.observability.svc:4317");
    options.UseAlwaysOnHeadSamplingForTailSampling = true;
    options.TraceSamplingRatio = 1.0;
});
```

The default remains 10% head sampling; the gateway cannot retain traces that were never sent to it.

ASP.NET Core endpoints opt in explicitly:

```csharp
app.MapPost("/transfers", async (TransferCommand command, TransferService service, CancellationToken cancellationToken)
    => await service.InitiateAsync(command, cancellationToken))
   .WithObservedOperation("banking.transfer.initiate", "critical");
app.UseTraceLogScope();
app.UseObservedOperations();
```

If middleware is not desired, a `RouteHandlerBuilder` can attach the same metadata and runtime
endpoint filter directly:

```csharp
app.MapPost("/transfers", async (TransferCommand command, TransferService service, CancellationToken cancellationToken)
    => await service.InitiateAsync(command, cancellationToken))
   .AddObservedOperationEndpointFilter(
       new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command));
```

Messaging clients should map their native string/byte headers to `W3CMessagePropagation.Inject` and `Extract`. The adapter does not inspect payloads and does not claim Kafka/RabbitMQ-specific retry or acknowledgment semantics until those client versions are discovered.
