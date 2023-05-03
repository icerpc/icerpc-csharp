// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection.Internal;

/// <summary>Adapts a middleware with a single service dependency to an IDispatcher.</summary>
internal class MiddlewareAdapter<TDep> : IDispatcher where TDep : notnull
{
    private readonly IMiddleware<TDep> _middleware;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        IServiceProviderFeature feature = request.Features.Get<IServiceProviderFeature>() ??
            throw new InvalidOperationException(
                $"There is no {nameof(IServiceProviderFeature)} in the request features.");

        return _middleware.DispatchAsync(
            request,
            feature.ServiceProvider.GetRequiredService<TDep>(),
            cancellationToken);
    }

    internal MiddlewareAdapter(IMiddleware<TDep> middleware) => _middleware = middleware;
}

/// <summary>Adapts a middleware with 2 service dependencies to an IDispatcher.</summary>
internal class MiddlewareAdapter<TDep1, TDep2> : IDispatcher
    where TDep1 : notnull
    where TDep2 : notnull
{
    private readonly IMiddleware<TDep1, TDep2> _middleware;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        IServiceProviderFeature feature = request.Features.Get<IServiceProviderFeature>() ??
            throw new InvalidOperationException(
                $"There is no {nameof(IServiceProviderFeature)} in the request features.");

        return _middleware.DispatchAsync(
            request,
            feature.ServiceProvider.GetRequiredService<TDep1>(),
            feature.ServiceProvider.GetRequiredService<TDep2>(),
            cancellationToken);
    }

    internal MiddlewareAdapter(IMiddleware<TDep1, TDep2> middleware) => _middleware = middleware;
}

/// <summary>Adapts a middleware with 3 service dependencies to an IDispatcher.</summary>
internal class MiddlewareAdapter<TDep1, TDep2, TDep3> : IDispatcher
    where TDep1 : notnull
    where TDep2 : notnull
    where TDep3 : notnull
{
    private readonly IMiddleware<TDep1, TDep2, TDep3> _middleware;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        IServiceProviderFeature feature = request.Features.Get<IServiceProviderFeature>() ??
            throw new InvalidOperationException(
                $"There is no {nameof(IServiceProviderFeature)} in the request features.");

        return _middleware.DispatchAsync(
            request,
            feature.ServiceProvider.GetRequiredService<TDep1>(),
            feature.ServiceProvider.GetRequiredService<TDep2>(),
            feature.ServiceProvider.GetRequiredService<TDep3>(),
            cancellationToken);
    }

    internal MiddlewareAdapter(IMiddleware<TDep1, TDep2, TDep3> middleware) => _middleware = middleware;
}
