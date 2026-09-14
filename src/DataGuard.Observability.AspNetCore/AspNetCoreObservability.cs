using System.Diagnostics;
using System.Reflection;
using DataGuard.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataGuard.Observability.AspNetCore;

public sealed record ObservedOperationMetadata(ObservedOperationDescriptor Descriptor);

public sealed class ObservedOperationMiddleware(RequestDelegate next, IBusinessOperationObserver observer)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<ObservedOperationMetadata>();
        if (metadata is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        await observer.ExecuteAsync(metadata.Descriptor, _ => next(context), context.RequestAborted).ConfigureAwait(false);
    }
}

public sealed class ObservedOperationEndpointFilter(IBusinessOperationObserver observer) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ObservedOperationMetadata>();
        if (metadata is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        return await observer.ExecuteAsync<object?>(
            metadata.Descriptor,
            _ => next(context).AsTask(),
            context.HttpContext.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>Executes an <see cref="ObservedOperationAttribute"/> at an MVC action boundary.</summary>
public sealed class ObservedOperationActionFilter(IBusinessOperationObserver observer) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = FindAttribute(context);
        if (attribute is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var descriptor = attribute.ToDescriptor();
        await observer.ExecuteAsync(
            descriptor,
            async _ =>
            {
                await next().ConfigureAwait(false);
            },
            context.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private static ObservedOperationAttribute? FindAttribute(ActionExecutingContext context)
    {
        var endpointAttribute = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ObservedOperationAttribute>();
        if (endpointAttribute is not null) return endpointAttribute;
        if (context.ActionDescriptor is not ControllerActionDescriptor controllerAction) return null;
        return controllerAction.MethodInfo.GetCustomAttribute<ObservedOperationAttribute>(inherit: true)
            ?? controllerAction.ControllerTypeInfo.GetCustomAttribute<ObservedOperationAttribute>(inherit: true);
    }
}

public sealed class TraceLogScopeMiddleware(RequestDelegate next, ILogger<TraceLogScopeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["trace_id"] = activity.TraceId.ToHexString(),
            ["span_id"] = activity.SpanId.ToHexString()
        }))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}

public static class AspNetCoreObservabilityExtensions
{
    public static IApplicationBuilder UseObservedOperations(this IApplicationBuilder app) => app.UseMiddleware<ObservedOperationMiddleware>();

    public static IApplicationBuilder UseTraceLogScope(this IApplicationBuilder app) => app.UseMiddleware<TraceLogScopeMiddleware>();

    public static IMvcBuilder AddObservedOperationFilters(this IMvcBuilder builder)
    {
        builder.AddMvcOptions(options => options.Filters.Add<ObservedOperationActionFilter>());
        return builder;
    }

    public static TBuilder WithObservedOperation<TBuilder>(this TBuilder builder, string name, string sloClass, ObservedOperationKind kind = ObservedOperationKind.Command)
        where TBuilder : IEndpointConventionBuilder
    {
        var descriptor = new ObservedOperationDescriptor(name, sloClass, kind);
        descriptor.Validate();
        return builder.WithMetadata(new ObservedOperationMetadata(descriptor));
    }

    public static TBuilder AddObservedOperationFilter<TBuilder>(this TBuilder builder, ObservedOperationDescriptor descriptor)
        where TBuilder : IEndpointConventionBuilder
    {
        descriptor.Validate();
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(new ObservedOperationMetadata(descriptor)));
        return builder;
    }

    /// <summary>
    /// Adds endpoint metadata and a runtime filter to a minimal API route handler.
    /// </summary>
    public static RouteHandlerBuilder AddObservedOperationEndpointFilter(
        this RouteHandlerBuilder builder,
        ObservedOperationDescriptor descriptor)
    {
        descriptor.Validate();
        builder.WithMetadata(new ObservedOperationMetadata(descriptor));
        return builder.AddEndpointFilter<ObservedOperationEndpointFilter>();
    }
}
