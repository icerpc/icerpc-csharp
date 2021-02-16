// Copyright (c) ZeroC, Inc. All rights reserved.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    public static class ObjectAdapterUseExtensions
    {
        /// <summary>Adds a simple dispatch interceptor to the request dispatch pipeline. This is an adapter for <see
        /// cref="ObjectAdapter.Use"/>.</summary>
        /// <param name="adapter">The object adapter.</param>
        /// <param name="dispatchInterceptor">A simple dispatch interceptor.</param>
        /// <returns>The <c>adapter</c> argument.</returns>
        public static ObjectAdapter Use(
            this ObjectAdapter adapter,
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
