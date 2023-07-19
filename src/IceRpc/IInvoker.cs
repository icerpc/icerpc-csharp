// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>An invoker handles outgoing requests and returns incoming responses.</summary>
/// <remarks>There are two invoker types:
/// <list type="bullet"><item><term>Leaf invokers</term><description>A leaf invoker sends a request and receives the
/// corresponding response from a peer. It's typically an <see cref="IProtocolConnection" /> connection. Its <see
/// cref="InvokeAsync"/> implementation provides the following guarantees:
/// <list type="bullet"><item><description>When the request is a two-way request, the returned task will not complete
/// successfully until after the request's <see cref="OutgoingFrame.Payload" /> is fully sent and the response is
/// received from the peer. When the request is a one-way request, the returned task completes successfully with an
/// empty response when the request's <see cref="OutgoingFrame.Payload" /> is fully sent.</description></item>
/// <item><description>For all requests (one-way and two-way), the sending of the request's <see
/// cref="OutgoingFrame.PayloadContinuation" /> can continue in a background task after the returned task has completed
/// successfully.</description></item></list></description></item>
/// <item><term>Interceptors</term><description>An interceptor intercepts the outgoing request and forwards it to
/// another invoker. An interceptor can provide request logging, payload compression,
/// etc.</description></item></list>
/// </remarks>
public interface IInvoker
{
    /// <summary>Handles an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default);
}
