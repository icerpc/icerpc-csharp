// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>Creates a binder interceptor. A binder is no-op when the request carries a connection; otherwise
        /// it retrieves a connection from the connection pool and sets the request's connection.</summary>
        /// <param name="pool">The connection pool.</param>
        /// <param name="cacheConnection">When true (the default), the binder stores the connection it retrieves from
        /// the connection pool in the proxy that created the request.</param>
        /// <returns>A new binder interceptor.</returns>
        public static Func<IInvoker, IInvoker> Binder(IConnectionPool pool, bool cacheConnection = true) =>
            next => new InlineInvoker(
                async (request, cancel) =>
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

                        request.Connection = await pool.GetConnectionAsync(request.Endpoint,
                                                                           request.AltEndpoints,
                                                                           cancel).ConfigureAwait(false);

                        if (cacheConnection)
                        {
                            request.Proxy.Connection = request.Connection;
                        }
                    }
                    return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                });
    }
}
