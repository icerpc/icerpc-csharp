// Copyright (c) ZeroC, Inc. All rights reserved.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    public static class ServerUseExtensions
    {
        /// <summary>Adds a simple dispatch interceptor to the request dispatch pipeline. This is an adapter for <see
        /// cref="Server.Use"/>.</summary>
        /// <param name="adapter">The server.</param>
        /// <param name="dispatchInterceptor">A simple dispatch interceptor.</param>
        /// <returns>The <c>adapter</c> argument.</returns>
        public static Server Use(
            this Server adapter,
            Func<IncomingRequestFrame, Current, Func<ValueTask<OutgoingResponseFrame>>, CancellationToken, ValueTask<OutgoingResponseFrame>> dispatchInterceptor) =>
            adapter.Use(
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
