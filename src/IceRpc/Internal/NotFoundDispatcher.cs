// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

/// <summary>A trivial dispatcher that always returns an <see cref="OutgoingResponse"/> with
/// <see cref="StatusCode.NotFound" />.</summary>
internal class NotFoundDispatcher : IDispatcher
{
    /// <summary>Gets the unique instance of this class.</summary>
    internal static NotFoundDispatcher Instance { get; } = new();

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default) =>
        new(new OutgoingResponse(request, StatusCode.NotFound));

    private NotFoundDispatcher()
    {
        // Ensures it's a singleton.
    }
}
