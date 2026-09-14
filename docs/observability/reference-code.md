# Reference operation adapters

These examples are complete, vendor-neutral application/infrastructure slices. They compile
against the three checked-in projects once the host supplies the transport contracts shown
below. Kafka, RabbitMQ, gRPC and Redis packages were not present in discovery; Npgsql `10.0.3`
exists in the existing PostgreSQL validation adapter, but the service data-source and telemetry
path are unknown. No unverified client API is smuggled into the shared package. A service owner
can implement the small contracts with its already-approved client version and keep the
propagation/operation policy unchanged.

## Host registration and result classification

```csharp
using DataGuard.Observability;
using DataGuard.Observability.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCoreObservability(builder.Configuration);
builder.Services.AddSingleton<IBusinessOperationResultClassifier, BankingResultClassifier>();
builder.Services.AddControllers().AddObservedOperationFilters();
builder.Services.AddScoped<TransferApplicationService>();
builder.Services.AddScoped<PostgresTransferRepository>();
builder.Services.AddScoped<RedisBalanceCache>();

var app = builder.Build();
app.UseTraceLogScope();
app.UseObservedOperations();
app.MapControllers();
app.Run();

public sealed class BankingResultClassifier : IBusinessOperationResultClassifier
{
    public string Classify(ObservedOperationDescriptor operation, object? result) => result switch
    {
        TransferAccepted => "accepted",
        TransferCompleted => "eventually_completed",
        TransferRejected { Reason: "validation_failure" } => "validation_failure",
        TransferRejected => "business_rejection",
        TransferDuplicate => "idempotent_no_op",
        TransferTechnicalFailure => "technical_failure",
        _ => "success"
    };
}
```

`BankingResultClassifier` never returns an identifier or arbitrary exception text. The core
observer clamps a custom classifier to its finite result vocabulary and falls back to
`unknown` if the classifier throws or returns an unsupported value.

## Transfer controller and application service

```csharp
using DataGuard.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("transfers")]
public sealed class TransferController(TransferApplicationService service) : ControllerBase
{
    [HttpPost]
    [ObservedOperation(Name = "banking.transfer.initiate", SloClass = "critical", Kind = ObservedOperationKind.Command)]
    public async Task<ActionResult<TransferAccepted>> Initiate(
        [FromBody] InitiateTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.InitiateAsync(request, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            TransferAccepted accepted => Accepted(accepted),
            TransferRejected rejected => UnprocessableEntity(rejected),
            TransferDuplicate duplicate => Ok(duplicate),
            TransferTechnicalFailure => StatusCode(StatusCodes.Status500InternalServerError),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}

public sealed class TransferApplicationService(
    IBusinessOperationObserver observer,
    PostgresTransferRepository repository,
    ITransferPublisher publisher)
{
    public Task<TransferOutcome> InitiateAsync(InitiateTransferRequest request, CancellationToken cancellationToken)
        => observer.ExecuteAsync(
            new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command),
            async ct =>
            {
                if (request.Amount <= 0) return new TransferRejected("validation_failure");
                var outcome = await repository.CreatePendingAsync(request, ct).ConfigureAwait(false);
                if (outcome is TransferStoreDuplicate duplicate) return new TransferDuplicate(duplicate.Reference);
                if (outcome is not TransferStoreCreated created) return new TransferTechnicalFailure("persistence_unknown");
                await publisher.PublishAsync(new TransferRequested(request.SourceAccount, request.DestinationAccount, request.Amount), ct)
                    .ConfigureAwait(false);
                return new TransferAccepted(created.Reference);
            },
            cancellationToken);
}

public sealed record InitiateTransferRequest(string SourceAccount, string DestinationAccount, decimal Amount);
public sealed record TransferRequested(string SourceAccount, string DestinationAccount, decimal Amount);
public abstract record TransferOutcome;
public sealed record TransferAccepted(string Reference) : TransferOutcome;
public sealed record TransferCompleted(string Reference) : TransferOutcome;
public sealed record TransferRejected(string Reason) : TransferOutcome;
public sealed record TransferDuplicate(string Reference) : TransferOutcome;
public sealed record TransferTechnicalFailure(string Reason) : TransferOutcome;
```

The controller attribute is metadata only. The application service owns the selected business
boundary; repository and DTO helpers do not create another business span. Account values are
domain data and never become tags, log fields, baggage or metric labels.

## Version-neutral gRPC endpoint

The generated gRPC base class is client-version-specific. Keep this endpoint body independent
of generated types, then adapt it in the approved gRPC host:

```csharp
using DataGuard.Observability;

public sealed class TransferGrpcEndpoint(TransferApplicationService service)
{
    public async Task<TransferReply> InitiateAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        var result = await service.InitiateAsync(
            new InitiateTransferRequest(request.SourceAccount, request.DestinationAccount, request.Amount),
            cancellationToken).ConfigureAwait(false);
        return result switch
        {
            TransferAccepted accepted => new TransferReply("accepted", accepted.Reference),
            TransferDuplicate duplicate => new TransferReply("idempotent_no_op", duplicate.Reference),
            TransferRejected rejected => new TransferReply(rejected.Reason, string.Empty),
            _ => new TransferReply("technical_failure", string.Empty)
        };
    }
}

public sealed record TransferRequest(string SourceAccount, string DestinationAccount, decimal Amount);
public sealed record TransferReply(string Result, string Reference);
```

The concrete gRPC interceptor should create the transport `Server`/`Client` spans only if the
discovered package does not already emit native `Activity` instances. It must pass the gRPC
cancellation token through, preserve streaming parent/link semantics and use the same
`banking.transfer.initiate` operation descriptor at exactly one business boundary.

## Generic messaging contracts and Kafka/RabbitMQ adapters

These interfaces are the seam for the discovered broker client. They deliberately do not
inspect payloads or expose a package-specific `BasicProperties`/Kafka header type in the shared
framework.

```csharp
using System.Diagnostics;
using System.Text.Json;
using DataGuard.Observability;
using DataGuard.Observability.Messaging;

public interface ITransferPublisher
{
    Task PublishAsync(TransferRequested message, CancellationToken cancellationToken);
}

public interface IMessagePublisher
{
    Task PublishAsync(string destination, ReadOnlyMemory<byte> payload, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

public interface IMessageConsumer
{
    Task AckAsync(CancellationToken cancellationToken);
    Task RetryAsync(CancellationToken cancellationToken);
    Task DeadLetterAsync(CancellationToken cancellationToken);
}

public sealed class KafkaTransferProducer(
    IBusinessOperationObserver observer,
    IMessagePublisher publisher) : ITransferPublisher
{
    private static readonly IReadOnlySet<string> AllowedBaggage = new HashSet<string>(StringComparer.Ordinal) { "tenant.class" };

    public Task PublishAsync(TransferRequested message, CancellationToken cancellationToken)
        => observer.ExecuteAsync(
            new ObservedOperationDescriptor("banking.payment.instruction.process", "critical", ObservedOperationKind.Event),
            ct => PublishCoreAsync("banking-transfers", message, ct),
            cancellationToken);

    private async Task PublishCoreAsync(string topic, TransferRequested message, CancellationToken cancellationToken)
    {
        using var activity = new ActivitySource("DataGuard.Observability.Messaging")
            .StartActivity("messaging.publish", ActivityKind.Producer);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        W3CMessagePropagation.Inject(activity ?? Activity.Current, headers, AllowedBaggage);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await publisher.PublishAsync(topic, payload, headers, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class RabbitTransferProducer(
    IBusinessOperationObserver observer,
    IMessagePublisher publisher) : ITransferPublisher
{
    public Task PublishAsync(TransferRequested message, CancellationToken cancellationToken)
        => observer.ExecuteAsync(
            new ObservedOperationDescriptor("banking.payment.instruction.process", "critical", ObservedOperationKind.Event),
            ct => PublishCoreAsync("transfers.exchange", message, ct),
            cancellationToken);

    private async Task PublishCoreAsync(string routingKey, TransferRequested message, CancellationToken cancellationToken)
    {
        using var activity = new ActivitySource("DataGuard.Observability.Messaging")
            .StartActivity("messaging.publish", ActivityKind.Producer);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        W3CMessagePropagation.Inject(activity ?? Activity.Current, headers, new HashSet<string>(StringComparer.Ordinal));
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await publisher.PublishAsync(routingKey, payload, headers, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class TransferConsumer(
    IBusinessOperationObserver observer,
    IMessageConsumer consumer,
    ITransferHandler handler)
{
    public async Task ConsumeAsync(
        string messageId,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var propagation = W3CMessagePropagation.Extract(
            headers,
            new HashSet<string>(StringComparer.Ordinal) { "tenant.class" });
        using var activity = new ActivitySource("DataGuard.Observability.Messaging")
            .StartActivity("messaging.consume", ActivityKind.Consumer, propagation.ActivityContext);
        var operation = new ObservedOperationDescriptor("banking.message.process", "critical", ObservedOperationKind.Event);
        try
        {
            await observer.ExecuteAsync(operation, async ct =>
            {
                var message = JsonSerializer.Deserialize<TransferRequested>(payload.Span)
                    ?? throw new InvalidOperationException("Message payload is invalid.");
                await handler.HandleAsync(message, ct).ConfigureAwait(false);
                await consumer.AckAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await consumer.RetryAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}

public interface ITransferHandler
{
    Task HandleAsync(TransferRequested message, CancellationToken cancellationToken);
}
```

The approved Kafka implementation maps `IMessagePublisher` to producer headers and delivery
result, while the RabbitMQ implementation maps it to message properties and ack/nack. A retry
or DLQ attempt must create a new consumer attempt and a span link to the original context; the
`messageId` is retained only by the domain/ledger and is intentionally unused by telemetry.
`AddCoreObservability` registers the `DataGuard.Observability.Messaging` source when tracing is
enabled. If a host composes its own tracing provider instead, it must register that source too;
otherwise only the business observer span is emitted.

## PostgreSQL repository

`DbConnection` keeps the shared package independent of the service's Npgsql integration version.
The owner adapter supplies a connection factory that uses parameterized SQL; the observer never
sees the parameters.

```csharp
using System.Data;
using System.Data.Common;

public interface IDbConnectionFactory
{
    ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken);
}

public sealed class PostgresTransferRepository(IDbConnectionFactory connections)
{
    public async Task<TransferStoreOutcome> CreatePendingAsync(
        InitiateTransferRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into banking_transfer (source_account, destination_account, amount, state)
            values (@source_account, @destination_account, @amount, 'pending')
            on conflict (source_account, destination_account, amount) do nothing
            returning transfer_reference;
            """;
        AddParameter(command, "@source_account", request.SourceAccount, DbType.String);
        AddParameter(command, "@destination_account", request.DestinationAccount, DbType.String);
        AddParameter(command, "@amount", request.Amount, DbType.Decimal);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null
            ? new TransferStoreDuplicate("existing")
            : new TransferStoreCreated(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "created");
    }

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

public abstract record TransferStoreOutcome;
public sealed record TransferStoreCreated(string Reference) : TransferStoreOutcome;
public sealed record TransferStoreDuplicate(string Reference) : TransferStoreOutcome;
```

The real Npgsql adapter must enable only the verified client instrumentation and must suppress
parameter values, SQL literals and result payloads from dependency spans/logs.

## Redis cache adapter

```csharp
public interface IStringCache
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken);
}

public sealed class RedisBalanceCache(IStringCache cache)
{
    public async Task<decimal?> GetBalanceAsync(string accountReference, CancellationToken cancellationToken)
    {
        var value = await cache.GetAsync(CacheKey(accountReference), cancellationToken).ConfigureAwait(false);
        return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var balance)
            ? balance
            : null;
    }

    public Task PutBalanceAsync(string accountReference, decimal balance, CancellationToken cancellationToken)
        => cache.SetAsync(
            CacheKey(accountReference),
            balance.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(30),
            cancellationToken);

    private static string CacheKey(string accountReference) => $"banking:balance:{accountReference}";
}
```

The cache key and value stay in Redis. They are not copied to `Activity` tags, baggage,
structured logs or metrics. The approved Redis client adapter owns timeout/pool metrics and
must not enable value capture.

## Background reconciliation worker

```csharp
using DataGuard.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public interface IReconciliationRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed class ReconciliationWorker(
    IBusinessOperationObserver observer,
    IReconciliationRunner runner,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await observer.ExecuteAsync(
                    new ObservedOperationDescriptor("banking.reconciliation.execute", "critical", ObservedOperationKind.Job),
                    runner.RunAsync,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Reconciliation job failed with {ErrorType}", exception.GetType().Name);
            }
        }
    }
}
```

The worker logs one stable event without an exception message and lets the observer record the
technical failure. Shutdown cancellation is not counted as a technical failure. Register the
worker as a hosted service and give it an explicit timeout/idempotency policy in the domain.
