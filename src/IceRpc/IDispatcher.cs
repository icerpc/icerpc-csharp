// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>A dispatcher handles (dispatches) incoming requests and returns outgoing responses.</summary>
public interface IDispatcher
{
    /// <summary>Dispatches an incoming request and returns the corresponding outgoing response.</summary>
    /// <param name="request">The incoming request being dispatched.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The corresponding <see cref="OutgoingResponse"/>.</returns>
    ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default);
}
