using System.Diagnostics;
using System.Diagnostics.Metrics;
using DataGuard.Observability;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataGuard.Observability.Tests;

public sealed class CoreObservabilityTests
{
    [Fact]
    public void RemoteExportAndEndpointSurfacesAreDisabledByDefault()
    {
        var options = new CoreObservabilityOptions { ServiceName = "default-lockdown" };

        options.RemoteExportEnabled.Should().BeFalse();
        options.OtlpEndpoint.Should().BeNull();
    }

    [Fact]
    public async Task Operation_ReturnsResult_AndPreservesCancellation()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "test-service")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command);

        var result = await observer.ExecuteAsync(descriptor, _ => Task.FromResult(42));

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExpectedCancellationIsRethrownAndClassifiedSeparately()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "cancellation-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.complete", "critical", ObservedOperationKind.Command);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = () => observer.ExecuteAsync(
            descriptor,
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(42);
            },
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Operation_RethrowsOriginalException()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "test-service")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.approve", "critical", ObservedOperationKind.Command);
        Func<Task> action = () => observer.ExecuteAsync(descriptor, _ => Task.FromException(new InvalidOperationException("secret-value")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);

        exception.Message.Should().Be("secret-value");
    }

    [Fact]
    public void Registration_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddCoreObservability(o => o.ServiceName = "test-service");
        services.AddCoreObservability(o => o.ServiceName = "ignored-second-registration");

        services.Count(x => x.ServiceType == typeof(IBusinessOperationObserver)).Should().Be(1);
    }

    [Fact]
    public void Registration_RejectsMissingServiceName()
    {
        var services = new ServiceCollection();

        var action = () => services.AddCoreObservability(_ => { });

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registration_RejectsNonHttpsExporter()
    {
        var services = new ServiceCollection();

        var action = () => services.AddCoreObservability(options =>
        {
            options.ServiceName = "test-service";
            options.OtlpEndpoint = new Uri("http://collector.invalid");
        });

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registration_RejectsInlineEndpointCredentials()
    {
        var action = () => new CoreObservabilityOptions
        {
            ServiceName = "test-service",
            OtlpEndpoint = new Uri("https://user:secret@collector.invalid:4317")
        }.Validate();

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registration_RejectsEndpointQueryAndFragment()
    {
        var queryAction = () => new CoreObservabilityOptions
        {
            ServiceName = "test-service",
            OtlpEndpoint = new Uri("https://collector.invalid:4317?token=secret")
        }.Validate();
        var fragmentAction = () => new CoreObservabilityOptions
        {
            ServiceName = "test-service",
            OtlpEndpoint = new Uri("https://collector.invalid:4317#secret")
        }.Validate();

        queryAction.Should().Throw<InvalidOperationException>();
        fragmentAction.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProductionConfigurationRejectsRawExceptionDetails()
    {
        var action = () => new CoreObservabilityOptions
        {
            ServiceName = "test-service",
            DeploymentEnvironment = "production",
            CaptureExceptionDetails = true
        }.Validate();

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InvalidOperationName_IsRejected()
    {
        var descriptor = new ObservedOperationDescriptor("transfer-{transactionId}", "critical", ObservedOperationKind.Command);

        var action = () => descriptor.Validate();

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AttributeProducesValidatedDescriptorMetadata()
    {
        var attribute = new ObservedOperationAttribute
        {
            Name = "banking.transfer.initiate",
            SloClass = "critical",
            Kind = ObservedOperationKind.Command
        };

        var descriptor = attribute.ToDescriptor();

        descriptor.Name.Should().Be("banking.transfer.initiate");
        descriptor.SloClass.Should().Be("critical");
        descriptor.Kind.Should().Be(ObservedOperationKind.Command);
        descriptor.Validate();
    }

    [Fact]
    public async Task NestedSameOperationUsesRecursionGuardWithoutDoubleWrapping()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "recursion-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command);

        var result = await observer.ExecuteAsync(
            descriptor,
            ct => observer.ExecuteAsync(descriptor, _ => Task.FromResult(7), ct));

        result.Should().Be(7);
    }

    [Fact]
    public async Task Operation_OutsideConfiguredAllowlist_IsRejected()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(o => o.ServiceName = "test-service")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.unbounded.operation", "critical", ObservedOperationKind.Command);

        Func<Task> action = () => observer.ExecuteAsync(descriptor, _ => Task.CompletedTask);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UnavailableTelemetryBackendDoesNotFailBusinessOperation()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(options =>
            {
                options.ServiceName = "fail-open-test";
                options.Enabled = true;
                options.OtlpEndpoint = new Uri("https://127.0.0.1:1");
                options.MaxQueueSize = 128;
            })
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.complete", "critical", ObservedOperationKind.Command);

        var result = await observer.ExecuteAsync(descriptor, _ => Task.FromResult("business-success"));

        result.Should().Be("business-success");
    }

    [Fact]
    public void QueueAndSamplingBoundsAreValidated()
    {
        var queueAction = () => new CoreObservabilityOptions
        {
            ServiceName = "bounds-test",
            MaxQueueSize = 127
        }.Validate();
        var samplingAction = () => new CoreObservabilityOptions
        {
            ServiceName = "bounds-test",
            TraceSamplingRatio = 1.1
        }.Validate();

        queueAction.Should().Throw<InvalidOperationException>();
        samplingAction.Should().Throw<InvalidOperationException>();

        var nanAction = () => new CoreObservabilityOptions
        {
            ServiceName = "bounds-test",
            TraceSamplingRatio = double.NaN
        }.Validate();

        nanAction.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TailSamplingCandidateModeRequiresExplicitFullHeadRatio()
    {
        var invalid = () => new CoreObservabilityOptions
        {
            ServiceName = "tail-sampling-test",
            UseAlwaysOnHeadSamplingForTailSampling = true,
            TraceSamplingRatio = 0.10
        }.Validate();
        var valid = () => new CoreObservabilityOptions
        {
            ServiceName = "tail-sampling-test",
            UseAlwaysOnHeadSamplingForTailSampling = true,
            TraceSamplingRatio = 1.0
        }.Validate();

        invalid.Should().Throw<InvalidOperationException>();
        valid.Should().NotThrow();
    }

    [Fact]
    public async Task InjectedResultClassifierUsesFiniteResultLabel()
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability" && instrument.Name == "business.operation.count")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "result" && tag.Value is string result)
                {
                    results.Add(result);
                }
            }
        });
        listener.Start();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IBusinessOperationResultClassifier, AcceptedResultClassifier>()
            .AddCoreObservability(o => o.ServiceName = "classifier-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command);

        await observer.ExecuteAsync(descriptor, _ => Task.CompletedTask);

        results.Should().Contain("accepted");
    }

    [Fact]
    public async Task ClassifiedDependencyResultFeedsFailureSubset()
    {
        const string operationName = "banking.observability.dependency.result";
        var failureResults = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "DataGuard.Observability"
                && instrument.Name == "business.operation.failure.count")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? operation = null;
            string? result = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    operation = tag.Value as string;
                }
                if (tag.Key == "result")
                {
                    result = tag.Value as string;
                }
            }
            if (operation == operationName && result is not null)
            {
                failureResults.Add(result);
            }
        });
        listener.Start();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IBusinessOperationResultClassifier, DependencyResultClassifier>()
            .AddCoreObservability(options =>
            {
                options.ServiceName = "dependency-result-test";
                options.AllowedOperations.Add(operationName);
            })
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor(operationName, "standard", ObservedOperationKind.Event);

        await observer.ExecuteAsync(descriptor, _ => Task.FromResult("ignored-export"));

        failureResults.Should().Contain("dependency_unavailable");
    }

    [Fact]
    public async Task ClassifiedTechnicalResultMarksActivityAsError()
    {
        const string operationName = "banking.observability.classified.error";
        ActivityStatusCode? status = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataGuard.Observability.Business",
            Sample = (ref ActivityCreationOptions<ActivityContext> creation) =>
                creation.Name == operationName ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == operationName)
                {
                    status = activity.Status;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IBusinessOperationResultClassifier, DependencyResultClassifier>()
            .AddCoreObservability(options =>
            {
                options.ServiceName = "classified-status-test";
                options.AllowedOperations.Add(operationName);
            })
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor(operationName, "standard", ObservedOperationKind.Event);

        await observer.ExecuteAsync(descriptor, _ => Task.FromResult("ignored-export"));

        status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task FaultyActivityListenerCannotFailBusinessOperation()
    {
        const string operationName = "banking.observability.listener.guard";
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataGuard.Observability.Business",
            Sample = (ref ActivityCreationOptions<ActivityContext> creation) =>
                creation.Name == operationName ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == operationName)
                {
                    throw new InvalidOperationException("faulty telemetry listener");
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddCoreObservability(options =>
            {
                options.ServiceName = "listener-guard-test";
                options.AllowedOperations.Add(operationName);
            })
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor(operationName, "standard", ObservedOperationKind.Job);

        var result = await observer.ExecuteAsync(descriptor, _ => Task.FromResult(7));

        result.Should().Be(7);
    }

    [Fact]
    public async Task FaultyResultClassifierCannotFailBusinessOperation()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IBusinessOperationResultClassifier, ThrowingResultClassifier>()
            .AddCoreObservability(o => o.ServiceName = "classifier-guard-test")
            .BuildServiceProvider();
        var observer = provider.GetRequiredService<IBusinessOperationObserver>();
        var descriptor = new ObservedOperationDescriptor("banking.transfer.initiate", "critical", ObservedOperationKind.Command);

        var result = await observer.ExecuteAsync(descriptor, _ => Task.FromResult("business-result"));

        result.Should().Be("business-result");
    }

    private sealed class AcceptedResultClassifier : IBusinessOperationResultClassifier
    {
        public string Classify(ObservedOperationDescriptor operation, object? result) => "accepted";
    }

    private sealed class ThrowingResultClassifier : IBusinessOperationResultClassifier
    {
        public string Classify(ObservedOperationDescriptor operation, object? result) => throw new InvalidOperationException("classifier failure");
    }

    private sealed class DependencyResultClassifier : IBusinessOperationResultClassifier
    {
        public string Classify(ObservedOperationDescriptor operation, object? result) => "dependency_unavailable";
    }
}
