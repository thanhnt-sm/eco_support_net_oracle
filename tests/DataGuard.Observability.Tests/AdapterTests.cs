using System.Diagnostics;
using System.Diagnostics.Metrics;
using DataGuard.Observability;
using DataGuard.Observability.AspNetCore;
using DataGuard.Observability.Messaging;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataGuard.Observability.Tests;

public sealed class AdapterTests
{
    [Fact]
    public void MessagePropagation_InjectsW3CAndAllowlistedBaggage()
    {
        using var activity = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity!.SetBaggage("tenant.id", "tenant-a");
        activity.SetBaggage("customer.id", "must-not-cross");
        var headers = new Dictionary<string, string>();

        W3CMessagePropagation.Inject(activity, headers, new HashSet<string>(["tenant.id"]));

        headers.Should().ContainKey("traceparent");
        headers["baggage"].Should().Be("tenant.id=tenant-a");
        headers.Should().NotContainKey("customer.id");

        var extracted = W3CMessagePropagation.Extract(headers, new HashSet<string>(["tenant.id"]));
        extracted.IsValid.Should().BeTrue();
        extracted.ActivityContext.TraceId.Should().Be(activity.TraceId);
        extracted.Baggage["tenant.id"].Should().Be("tenant-a");
    }

    [Fact]
    public void MessagePropagation_RejectsOversizedHeadersAndMalformedTrace()
    {
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "not-a-w3c-context",
            ["baggage"] = new string('x', 9000)
        };

        var result = W3CMessagePropagation.Extract(headers);

        result.IsValid.Should().BeFalse();
        result.Baggage.Should().BeEmpty();
    }

    [Fact]
    public void MessagePropagation_FailsClosedWhenBaggageAllowlistIsMissing()
    {
        using var activity = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity!.SetBaggage("tenant.id", "tenant-a");
        var headers = new Dictionary<string, string>();
        W3CMessagePropagation.Inject(activity, headers, new HashSet<string>(["tenant.id"]));

        var result = W3CMessagePropagation.Extract(headers);

        result.IsValid.Should().BeTrue();
        result.Baggage.Should().BeEmpty();
    }

    [Fact]
    public void MessagePropagation_EscapesBaggageValuesAndRejectsOversizedValues()
    {
        using var activity = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity!.SetBaggage("tenant.id", "tenant=a,b");
        activity.SetBaggage("branch.id", new string('x', 257));
        var headers = new Dictionary<string, string>();

        W3CMessagePropagation.Inject(activity, headers, new HashSet<string>(["tenant.id", "branch.id"]));

        headers["baggage"].Should().Be("tenant.id=tenant%3Da%2Cb");
        var result = W3CMessagePropagation.Extract(headers, new HashSet<string>(["tenant.id", "branch.id"]));
        result.Baggage["tenant.id"].Should().Be("tenant=a,b");
        result.Baggage.Should().NotContainKey("branch.id");
    }

    [Fact]
    public void MessagePropagation_DropsMalformedEscapedBaggageWithoutThrowing()
    {
        using var activity = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = activity!.Id!,
            ["baggage"] = "tenant.id=%ZZ"
        };

        var result = W3CMessagePropagation.Extract(headers, new HashSet<string>(["tenant.id"]));

        result.IsValid.Should().BeTrue();
        result.Baggage.Should().BeEmpty();
    }

    [Fact]
    public void EndpointMetadataDescriptor_RejectsDynamicName()
    {
        var descriptor = new ObservedOperationDescriptor("transfer-{id}", "critical", ObservedOperationKind.Command);

        var action = () => descriptor.Validate();

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CoreObserver_IsResolvableWithAspNetCoreAdapter()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "adapter-test")
            .BuildServiceProvider();

        provider.GetRequiredService<IBusinessOperationObserver>().Should().NotBeNull();
    }

    [Fact]
    public void MessagingMetricsNormalizeDimensionsAndDropDynamicNames()
    {
        var systems = new HashSet<string>(StringComparer.Ordinal);
        var results = new HashSet<string>(StringComparer.Ordinal);
        var operations = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability.Messaging")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "messaging.processing.duration")
            {
                return;
            }
            foreach (var tag in tags)
            {
                if (tag.Key == "operation" && tag.Value is string operation)
                {
                    operations.Add(operation);
                }
                if (tag.Key == "messaging.system" && tag.Value is string system)
                {
                    systems.Add(system);
                }
                if (tag.Key == "result" && tag.Value is string result)
                {
                    results.Add(result);
                }
            }
        });
        listener.Start();
        var metrics = new MessagingOperationMetrics();

        metrics.RecordProcessingDuration("banking.message.process", "unexpected-user-value", "untrusted-broker", 2);
        metrics.RecordProcessingDuration("transfer-{id}", "success", "kafka", 2);
        metrics.RecordProcessingDuration($"banking.message.process.{Guid.NewGuid():N}", "success", "kafka", 2);

        operations.Should().Equal(["banking.message.process"]);
        systems.Should().Equal(["unknown"]);
        results.Should().Equal(["unknown"]);
    }

    [Fact]
    public void MessagingMetricsRequireFiniteExplicitCustomOperationAllowlist()
    {
        var invalid = () => new MessagingOperationMetrics(new HashSet<string>(["banking.message.process-{id}"]));
        invalid.Should().Throw<ArgumentException>();

        var allowed = new MessagingOperationMetrics(new HashSet<string>(["banking.custom.process"]));
        var action = () => allowed.RecordProcessingDuration("banking.custom.process", "success", "kafka", 1);
        action.Should().NotThrow();
    }

    [Fact]
    public async Task TraceLogScope_UsesActiveActivityIds()
    {
        var scopes = new List<object?>();
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ScopeCaptureProvider(scopes)));
        var middleware = new TraceLogScopeMiddleware(
            _ => Task.CompletedTask,
            loggerFactory.CreateLogger<TraceLogScopeMiddleware>());

        await middleware.InvokeAsync(new DefaultHttpContext());

        scopes.Should().HaveCount(1);
        var values = scopes[0] as IReadOnlyDictionary<string, object?>;
        values.Should().NotBeNull();
        values!["trace_id"].Should().Be(activity!.TraceId.ToHexString());
        values["span_id"].Should().Be(activity.SpanId.ToHexString());
    }

    [Fact]
    public async Task MinimalApiEndpointFilter_UsesEndpointMetadataAndPreservesResult()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "endpoint-filter-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor(
            "banking.transfer.initiate",
            "critical",
            ObservedOperationKind.Command);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ObservedOperationMetadata(descriptor)),
            "test-endpoint"));
        var filter = new ObservedOperationEndpointFilter(observer);
        var invoked = false;

        var result = await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(context),
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult<object?>("ok");
            });

        invoked.Should().BeTrue();
        result.Should().Be("ok");
    }

    private sealed class ScopeCaptureProvider(List<object?> scopes) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ScopeCaptureLogger(scopes);

        public void Dispose()
        {
        }
    }

    private sealed class ScopeCaptureLogger(List<object?> scopes) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            scopes.Add(state);
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
