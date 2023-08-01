// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

/// <summary>A trivial dispatcher that always returns an <see cref="OutgoingResponse"/> with
/// <see cref="StatusCode.ServiceNotFound" />.</summary>
internal class ServiceNotFoundDispatcher : IDispatcher
{
    /// <summary>Gets the unique instance of this class.</summary>
    internal static ServiceNotFoundDispatcher Instance { get; } = new();

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default) =>
        new(new OutgoingResponse(request, StatusCode.ServiceNotFound));

    private ServiceNotFoundDispatcher()
    {
        // Ensures it's a singleton.
    }
}
