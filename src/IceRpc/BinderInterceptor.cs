// Copyright (c) ZeroC, Inc. All rights reserved.

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
                // Filter-out excluded endpoints
                if (request.ExcludedEndpoints.Any())
                {
                    if (request.Endpoint != null && request.ExcludedEndpoints.Contains(request.Endpoint))
                    {
                        request.Endpoint = null;
                    }
                    request.AltEndpoints = request.AltEndpoints.Except(request.ExcludedEndpoints);
                }

                if (request.Endpoint == null && request.AltEndpoints.Any())
                {
                    request.Endpoint = request.AltEndpoints.First();
                    request.AltEndpoints = request.AltEndpoints.Skip(1);
                }

                if (request.Endpoint == null)
                {
                    throw request.Proxy == null ? new NoEndpointException() : new NoEndpointException(request.Proxy);
                }

                return PerformAsync(_connectionProvider.GetConnectionAsync(request.Endpoint,
                                                                           request.AltEndpoints,
                                                                           cancel));
            }
            return _next.InvokeAsync(request, cancel);

            async Task<IncomingResponse> PerformAsync(ValueTask<Connection> task)
            {
                try
                {
                    request.Connection = await task.ConfigureAwait(false);
                    if (_cacheConnection && request.Proxy != null)
                    {
                        request.Proxy.Connection = request.Connection;
                    }
                }
                catch (TransportException exception) when (exception.RetryPolicy != RetryPolicy.NoRetry)
                {
                    // If obtaining a connection failed with a transport exception, the request can be retried.
                    request.Features = request.Features.With(exception.RetryPolicy);
                    throw;
                }
                return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
        }
    }
}
