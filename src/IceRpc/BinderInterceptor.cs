// Copyright (c) ZeroC, Inc. All rights reserved.

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
                EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
                if (endpointSelection == null)
                {
                    endpointSelection = new EndpointSelection(request.Proxy);
                    request.Features = request.Features.With(endpointSelection);
                }

                // Filter-out excluded endpoints
                if (endpointSelection.ExcludedEndpoints.Any())
                {
                    if (endpointSelection.Endpoint is Endpoint endpoint && endpointSelection.ExcludedEndpoints.Contains(endpoint))
                    {
                        endpointSelection.Endpoint = null;
                    }
                    endpointSelection.AltEndpoints = endpointSelection.AltEndpoints.Except(endpointSelection.ExcludedEndpoints);
                }

                if (endpointSelection.Endpoint == null && endpointSelection.AltEndpoints.Any())
                {
                    endpointSelection.Endpoint = endpointSelection.AltEndpoints.First();
                    endpointSelection.AltEndpoints = endpointSelection.AltEndpoints.Skip(1);
                }

                if (endpointSelection.Endpoint == null)
                {
                    throw new NoEndpointException(request.Proxy);
                }

                return PerformAsync(_connectionProvider.GetConnectionAsync(endpointSelection.Endpoint.Value,
                                                                           endpointSelection.AltEndpoints,
                                                                           cancel));
            }
            return _next.InvokeAsync(request, cancel);

            async Task<IncomingResponse> PerformAsync(ValueTask<Connection> task)
            {
                request.Connection = await task.ConfigureAwait(false);
                if (_cacheConnection)
                {
                    request.Proxy.Connection = request.Connection;
                }

                return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
        }
    }
}
