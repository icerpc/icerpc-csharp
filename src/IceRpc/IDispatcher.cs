// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>A dispatcher accepts incoming requests and returns outgoing responses.</summary>
public interface IDispatcher
{
    /// <summary>Dispatches an incoming request and returns the corresponding outgoing response.</summary>
    /// <param name="request">The incoming request being dispatched.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The outgoing response.</returns>
    ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default);
}
