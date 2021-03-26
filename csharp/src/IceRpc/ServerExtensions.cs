// Copyright (c) ZeroC, Inc. All rights reserved.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    public static class ServerUseExtensions
    {
        /// <summary>Adds a simple dispatch interceptor to the request dispatch pipeline. This is an adapter for <see
        /// cref="Server.Use"/>.</summary>
        /// <param name="server">The server.</param>
        /// <param name="dispatchInterceptor">A simple dispatch interceptor.</param>
        /// <returns>The <c>server</c> argument.</returns>
        public static Server Use(
            this Server server,
            Func<Current, Func<ValueTask<OutgoingResponseFrame>>, CancellationToken, ValueTask<OutgoingResponseFrame>> dispatchInterceptor) =>
            server.Use(next => new Dispatcher(
                (current, cancel) => dispatchInterceptor(current, () => next.DispatchAsync(current, cancel), cancel)));
    }
}
