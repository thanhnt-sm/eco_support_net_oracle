using System.Diagnostics;
using System.Diagnostics.Metrics;
using DataGuard.Observability;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataGuard.Observability.Tests;

public sealed class SensitiveDataAndCardinalityTests
{
    [Fact]
    public async Task DefaultExceptionRecordingDoesNotCaptureMessageOrStackData()
    {
        const string operationName = "banking.transfer.initiate";
        Activity? stopped = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataGuard.Observability.Business",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,

            // The assembly runs tests in parallel; only retain this test's operation rather
            // than allowing a concurrent observer activity to overwrite the captured value.
            ActivityStopped = activity =>
            {
                if (activity.OperationName == operationName)
                {
                    stopped = activity;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "privacy-test")
            .BuildServiceProvider();

        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var sensitivePayload = "PAN-4111111111111111 bearer super-secret-token";
        var descriptor = new ObservedOperationDescriptor(operationName, "critical", ObservedOperationKind.Command);

        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.ExecuteAsync(descriptor, _ => Task.FromException(new InvalidOperationException(sensitivePayload))));

        stopped.Should().NotBeNull();
        var renderedEvents = string.Join('|', stopped!.Events.Select(item => item.Name + item.Tags));
        renderedEvents.Should().NotContain(sensitivePayload);
        renderedEvents.Should().NotContain("4111111111111111");
        renderedEvents.Should().NotContain("super-secret-token");
        stopped.Tags.Any(item =>
            item.Value is not null
            && item.Value.ToString()!.Contains(sensitivePayload, StringComparison.Ordinal)).Should().BeFalse();
        stopped.Tags.Any(item => item.Key == "banking.operation.name" && item.Value is string value && value == "banking.transfer.initiate").Should().BeTrue();
        stopped.Tags.Any(item => item.Key == "banking.slo.class" && item.Value is string value && value == "critical").Should().BeTrue();
    }

    [Fact]
    public async Task FailureMetricHasStableResultClassification()
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        var countResults = new HashSet<string>(StringComparer.Ordinal);
        var errorTypes = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "business.operation.failure.count"
                && instrument.Name != "business.operation.count")
            {
                return;
            }
            string? op = null;
            string? res = null;
            string? err = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    op = tag.Value as string;
                }
                if (tag.Key == "result")
                {
                    res = tag.Value as string;
                }
                if (tag.Key == "error.type")
                {
                    err = tag.Value as string;
                }
            }
            if (op == "banking.transfer.initiate")
            {
                lock (results)
                {
                    if (res is not null)
                    {
                        if (instrument.Name == "business.operation.failure.count")
                        {
                            results.Add(res);
                        }
                        else
                        {
                            countResults.Add(res);
                        }
                    }
                    if (instrument.Name == "business.operation.failure.count" && err is not null)
                    {
                        errorTypes.Add(err);
                    }
                }
            }
        });
        listener.Start();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "failure-metric-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command);

        await Assert.ThrowsAsync<TimeoutException>(() => observer.ExecuteAsync(descriptor, _ => Task.FromException(new TimeoutException("not emitted"))));

        results.Should().Contain("timeout");
        countResults.Should().Contain("timeout");
        errorTypes.Should().Contain("timeout");
    }

    [Fact]
    public async Task TechnicalFailureUsesCanonicalResultAndCountsTheAttempt()
    {
        var countResults = new HashSet<string>(StringComparer.Ordinal);
        var failureResults = new HashSet<string>(StringComparer.Ordinal);
        var errorTypes = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            string? op = null;
            string? res = null;
            string? err = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    op = tag.Value as string;
                }
                if (tag.Key == "result")
                {
                    res = tag.Value as string;
                }
                if (tag.Key == "error.type")
                {
                    err = tag.Value as string;
                }
            }
            if (op == "banking.transfer.approve")
            {
                lock (countResults)
                {
                    if (res is not null)
                    {
                        (instrument.Name == "business.operation.count" ? countResults : failureResults).Add(res);
                    }
                    if (err is not null)
                    {
                        errorTypes.Add(err);
                    }
                }
            }
        });
        listener.Start();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "technical-result-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.approve", "critical", ObservedOperationKind.Command);

        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.ExecuteAsync(
            descriptor,
            _ => Task.FromException(new InvalidOperationException("technical"))));

        countResults.Should().Contain("technical_failure");
        failureResults.Should().Contain("technical_failure");
        errorTypes.Should().Contain("technical");
    }

    [Fact]
    public async Task DurationMetricCarriesFiniteResultClassification()
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability"
                && instrument.Name == "business.operation.duration")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            string? op = null;
            string? res = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    op = tag.Value as string;
                }
                if (tag.Key == "result")
                {
                    res = tag.Value as string;
                }
            }
            if ((op == "banking.transfer.initiate" || op == "banking.transfer.approve") && res is not null)
            {
                lock (results)
                {
                    results.Add(res);
                }
            }
        });
        listener.Start();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "duration-result-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var success = new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command);
        var timeout = new ObservedOperationDescriptor("banking.transfer.approve", "critical", ObservedOperationKind.Command);

        await observer.ExecuteAsync(success, _ => Task.CompletedTask);
        await Assert.ThrowsAsync<TimeoutException>(() => observer.ExecuteAsync(timeout, _ => Task.FromException(new TimeoutException())));

        results.Should().Contain(new[] { "success", "timeout" });
    }

    [Fact]
    public async Task MetricsUseFiniteOperationDimensionForRepeatedCalls()
    {
        const string operationName = "banking.transfer.initiate";
        var operationValues = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? operation = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation" && tag.Value is string value)
                {
                    operation = value;
                }
            }
            if (operation == operationName)
            {
                operationValues.Add(operation);
            }
        });
        listener.Start();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "cardinality-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor(operationName, "critical", ObservedOperationKind.Command);

        for (var i = 0; i < 1000; i++)
        {
            await observer.ExecuteAsync(descriptor, _ => Task.CompletedTask);
        }

        operationValues.Should().Equal(["banking.transfer.initiate"]);
    }
}
