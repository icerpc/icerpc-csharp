// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Returns a middleware that sets <see cref="IncomingRequest.ProxyInvoker"/> and indirectly
        /// <see cref="Dispatch.ProxyInvoker"/> to <paramref name="invoker"/>.</summary>
        /// <param name="invoker">The invoker of the proxies read from the request payloads.</param>
        /// <returns>The new middleware.</returns>
        public static Func<IDispatcher, IDispatcher> ProxyInvoker(IInvoker? invoker) =>
            next => new InlineDispatcher(
                (request, cancel) =>
                {
                    request.ProxyInvoker = invoker;
                    return next.DispatchAsync(request, cancel);
                });
    }
}
