// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>An invoker sends outgoing requests and returns incoming responses.</summary>
public interface IInvoker
{
    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The corresponding <see cref="IncomingResponse" />.</returns>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default);
}
