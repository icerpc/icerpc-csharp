// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Binder;

/// <summary>A binder interceptor is responsible for providing client connections to requests using an
/// <see cref="IClientConnectionProvider"/>, the binder is no-op when the request carries a connection; otherwise it
/// retrieves a connection from its client connection provider and sets the request's connection.</summary>
public class BinderInterceptor : IInvoker
{
    private readonly bool _cacheConnection;
    private readonly IClientConnectionProvider _clientConnectionProvider;
    private readonly IInvoker _next;

    /// <summary>Constructs a binder interceptor.</summary>
    /// <param name="next">The next invoker in the pipeline.</param>
    /// <param name="clientConnectionProvider">The client connection provider.</param>
    /// <param name="cacheConnection">When <c>true</c> (the default), the binder stores the connection it retrieves
    /// from its client connection provider in the proxy that created the request.</param>
    public BinderInterceptor(
        IInvoker next,
        IClientConnectionProvider clientConnectionProvider,
        bool cacheConnection = true)
    {
        _next = next;
        _clientConnectionProvider = clientConnectionProvider;
        _cacheConnection = cacheConnection;
    }

    Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.Connection == null)
        {
            if (request.Features.Get<IEndpointFeature>() is IEndpointFeature endpointFeature)
            {
                return PerformBindAsync(endpointFeature.Endpoint, endpointFeature.AltEndpoints);
            }
            else
            {
                return PerformBindAsync(request.Proxy.Endpoint, request.Proxy.AltEndpoints);
            }
        }
        else
        {
            return _next.InvokeAsync(request, cancel);
        }

        async Task<IncomingResponse> PerformBindAsync(Endpoint? endpoint, IEnumerable<Endpoint> altEndpoints)
        {
            if (endpoint == null)
            {
                throw new NoEndpointException(request.Proxy);
            }

            request.Connection = await _clientConnectionProvider.GetClientConnectionAsync(
                endpoint.Value,
                altEndpoints,
                cancel).ConfigureAwait(false);
            if (_cacheConnection)
            {
                request.Proxy.Connection = request.Connection;
            }
            return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }
}
