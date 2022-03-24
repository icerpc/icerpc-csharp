﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc
{
    /// <summary>A binder interceptor is responsible for providing connections to requests using an
    /// <see cref="IConnectionProvider"/>, the binder is no-op when the request carries a connection; otherwise it
    /// retrieves a connection from its connection provider and sets the request's connection.</summary>
    public class BinderInterceptor : IInvoker
    {
        private readonly bool _cacheConnection;
        private readonly IConnectionProvider _connectionProvider;
        private readonly IInvoker _next;

        /// <summary>Constructs a binder interceptor.</summary>
        /// <param name="next">The next invoker in the pipeline.</param>
        /// <param name="connectionProvider">The connection provider.</param>
        /// <param name="cacheConnection">When <c>true</c> (the default), the binder stores the connection it retrieves
        /// from its connection provider in the proxy that created the request.</param>
        public BinderInterceptor(IInvoker next, IConnectionProvider connectionProvider, bool cacheConnection = true)
        {
            _next = next;
            _connectionProvider = connectionProvider;
            _cacheConnection = cacheConnection;
        }

        Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.Connection == null)
            {
                if (request.Features.Get<EndpointSelection>() is EndpointSelection endpointSelection)
                {
                    return PerformBindAsync(endpointSelection.Endpoint, endpointSelection.AltEndpoints);
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
                try
                {
                    if (endpoint == null)
                    {
                        throw new NoEndpointException(request.Proxy);
                    }

                    request.Connection = await _connectionProvider.GetConnectionAsync(
                        endpoint.Value,
                        altEndpoints,
                        cancel).ConfigureAwait(false);
                    if (_cacheConnection)
                    {
                        request.Proxy.Connection = request.Connection;
                    }
                }
                catch (Exception exception)
                {
                    await request.PayloadSource.CompleteAsync(exception).ConfigureAwait(false);
                    if (request.PayloadSourceStream != null)
                    {
                        await request.PayloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
                    }
                    await request.PayloadSink.CompleteAsync(exception).ConfigureAwait(false);
                    throw;
                }
                return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
        }
    }
}
