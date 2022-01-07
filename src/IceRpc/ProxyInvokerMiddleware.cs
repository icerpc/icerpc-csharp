// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A middleware that sets <see cref="IncomingFrame.ProxyInvoker"/> on incoming requests.</summary>
    public class ProxyInvokerMiddleware : IDispatcher
    {
        private readonly IInvoker _invoker;
        private readonly IDispatcher _next;

        /// <summary>Constructs a proxy invoker middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="invoker">The invoker of the proxies read from the requests payload.</param>
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
