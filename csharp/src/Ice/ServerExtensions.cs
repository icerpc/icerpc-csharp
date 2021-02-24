// Copyright (c) ZeroC, Inc. All rights reserved.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    public static class ServerUseExtensions
    {
        /// <summary>Adds a simple dispatch interceptor to the request dispatch pipeline. This is an server for <see
        /// cref="Server.Use"/>.</summary>
        /// <param name="server">The server.</param>
        /// <param name="dispatchInterceptor">A simple dispatch interceptor.</param>
        /// <returns>The <c>server</c> argument.</returns>
        public static Server Use(
            this Server server,
            Func<IncomingRequestFrame, Current, Func<ValueTask<OutgoingResponseFrame>>, CancellationToken, ValueTask<OutgoingResponseFrame>> dispatchInterceptor) =>
            server.Use(
                next => (request, current, cancel) =>
                {
                    return dispatchInterceptor(request, current, SimpleNext, cancel);

                    // Parameterless version of next
                    ValueTask<OutgoingResponseFrame> SimpleNext()
                    {
                        return next(request, current, cancel);
                    }
                });
    }
}
