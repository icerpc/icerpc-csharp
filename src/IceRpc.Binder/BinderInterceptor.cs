// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Binder;

/// <summary>A binder interceptor is responsible for providing client connections to requests using an
/// <see cref="IClientConnectionProvider"/>, the binder is no-op when the request carries a connection; otherwise it
/// retrieves a connection from its client connection provider and sets the request's connection.</summary>
public class BinderInterceptor : IInvoker
{
    private readonly IClientConnectionProvider _clientConnectionProvider;
    private readonly IInvoker _next;

    /// <summary>Constructs a binder interceptor.</summary>
    /// <param name="next">The next invoker in the pipeline.</param>
    /// <param name="clientConnectionProvider">The client connection provider.</param>
    public BinderInterceptor(IInvoker next, IClientConnectionProvider clientConnectionProvider)
    {
        _next = next;
        _clientConnectionProvider = clientConnectionProvider;
    }

    Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.Features.Get<IEndpointFeature>() is IEndpointFeature endpointFeature)
        {
            if (endpointFeature.Connection is not null)
            {
                return _next.InvokeAsync(request, cancel);
            }
        }
        else
        {
            endpointFeature = new EndpointFeature(request.Proxy);
            request.Features = request.Features.With(endpointFeature);
        }
        return PerformBindAsync();

        async Task<IncomingResponse> PerformBindAsync()
        {
            if (endpointFeature.Endpoint is null)
            {
                throw new NoEndpointException(request.Proxy);
            }

            endpointFeature.Connection = await _clientConnectionProvider.GetClientConnectionAsync(
                endpointFeature.Endpoint.Value,
                endpointFeature.AltEndpoints,
                cancel).ConfigureAwait(false);

            return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }
}
