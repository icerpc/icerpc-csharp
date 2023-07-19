// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>An invoker handles outgoing requests and returns incoming responses.</summary>
/// <remarks><para>There are two invoker types:
/// <list type="bullet"><item><term>Leaf invokers</term><description>A leaf invoker sends a request and receives the
/// corresponding response from a peer. It's typically an <see cref="IProtocolConnection" />
/// connection.</description>
/// </item>
/// <item><term>Interceptors</term><description>An interceptor intercepts the outgoing request and forwards it to
/// another invoker. An interceptor can provide request logging, payload compression,
/// etc.</description></item></list>
/// </para></remarks>
public interface IInvoker
{
    /// <summary>Handles an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default);
}
