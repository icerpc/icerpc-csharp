// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A middleware that sets <see cref="IncomingRequest.ProxyInvoker"/> and indirectly
    /// <see cref="Dispatch.ProxyInvoker"/>.</summary>
    public class ProxyInvokerMiddleware : IDispatcher
    {
        private readonly IInvoker _invoker;
        private readonly IDispatcher _next;

        /// <summary>Constructs a proxy invoker middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="invoker">The invoker of the proxies read from the request payloads.</param>
        public ProxyInvokerMiddleware(IDispatcher next, IInvoker invoker)
        {
            _next = next;
            _invoker = invoker;
        }

        ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            request.ProxyInvoker = _invoker;
            return _next.DispatchAsync(request, cancel);
        }
    }
}
