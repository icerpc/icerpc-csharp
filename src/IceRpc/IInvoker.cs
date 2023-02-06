// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>An invoker sends outgoing requests and returns incoming responses.</summary>
public interface IInvoker
{
    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    /// <remarks>When <paramref name="request" /> is a twoway request, the returned task will not complete successfully
    /// until after the request's <see cref="OutgoingFrame.Payload" /> is fully sent and the response is received from
    /// the peer. When the request is a oneway request, the returned task completes successfully with an empty response
    /// when the request's <see cref="OutgoingFrame.Payload" /> is fully sent. For all requests (oneway and twoway), the
    /// sending of the request's <see cref="OutgoingFrame.PayloadContinuation" /> can continue in a background task
    /// after the returned task has completed successfully.</remarks>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default);
}
