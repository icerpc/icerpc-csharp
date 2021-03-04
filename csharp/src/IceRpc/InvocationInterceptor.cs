// Copyright (c) ZeroC, Inc. All rights reserved.
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An invoker is a delegate used to send an outgoing request.</summary>
    /// <param name="target">The proxy for the invocation.</param>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The incoming response frame.</returns>
    public delegate Task<IncomingResponseFrame> Invoker(
        IServicePrx target,
        OutgoingRequestFrame request,
        CancellationToken cancel);

    /// <summary>An invocation interceptor can be registered with a Communicator to intercept proxy invocations.
    /// </summary>
    /// <param name="target">The proxy for the invocation.</param>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="next">The next invoker in the invocation chain.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The incoming response frame.</returns>
    public delegate Task<IncomingResponseFrame> InvocationInterceptor(
        IServicePrx target,
        OutgoingRequestFrame request,
        Invoker next,
        CancellationToken cancel);
}
