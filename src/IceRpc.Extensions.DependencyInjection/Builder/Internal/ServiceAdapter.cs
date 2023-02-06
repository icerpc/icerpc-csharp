// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Builder.Internal;

/// <summary>Adapts a service managed by the DI container to an IDispatcher.</summary>
internal class ServiceAdapter<TService> : IDispatcher where TService : notnull
{
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        IServiceProviderFeature feature = request.Features.Get<IServiceProviderFeature>() ??
            throw new InvalidOperationException("no service provider feature in request features");

        TService service = feature.ServiceProvider.GetRequiredService<TService>();

        return ((IDispatcher)service).DispatchAsync(request, cancellationToken);
    }
}
