// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Creates a binder interceptor. A binder is no-op when the request carries a connection; otherwise
        /// it retrieves a connection from its connection provider and sets the request's connection.</summary>
        /// <param name="connectionProvider">The connection provider.</param>
        /// <param name="cacheConnection">When <c>true</c> (the default), the binder stores the connection it retrieves
        /// from its connection provider in the proxy that created the request.</param>
        /// <returns>A new binder interceptor.</returns>
        public static Func<IInvoker, IInvoker> Binder(
            IConnectionProvider connectionProvider,
            bool cacheConnection = true) =>
            next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Connection == null)
                    {
                        // Filter out endpoint we cannot connect to.
                        if (request.Endpoint != null && !request.Endpoint.HasConnect)
                        {
                            request.Endpoint = null;
                        }
                        request.AltEndpoints = request.AltEndpoints.Where(e => e.HasConnect);

                        if (!request.IsOneway)
                        {
                            // Filter-out datagram endpoints
                            if (request.Endpoint != null && request.Endpoint.IsDatagram)
                            {
                                request.Endpoint = null;
                            }
                            request.AltEndpoints = request.AltEndpoints.Where(e => !e.IsDatagram);
                        }

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
                            throw new NoEndpointException(request.Proxy);
                        }

                        return PerformAsync(connectionProvider.GetConnectionAsync(request.Endpoint,
                                                                                  request.AltEndpoints,
                                                                                  cancel));
                    }
                    return next.InvokeAsync(request, cancel);

                    async Task<IncomingResponse> PerformAsync(ValueTask<Connection> task)
                    {
                        request.Connection = await task.ConfigureAwait(false);
                        if (cacheConnection)
                        {
                            request.Proxy.Connection = request.Connection;
                        }
                        return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                    }
                });
    }
}
