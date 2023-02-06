// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

/// <summary>A trivial dispatcher that always throws a <see cref="DispatchException" /> with status code
/// <see cref="StatusCode.ServiceNotFound" />.</summary>
internal class ServiceNotFoundDispatcher : IDispatcher
{
    /// <summary>Gets the unique instance of this class.</summary>
    internal static ServiceNotFoundDispatcher Instance { get; } = new();

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default) =>
        throw new DispatchException(StatusCode.ServiceNotFound);

    private ServiceNotFoundDispatcher()
    {
        // Ensures it's a singleton.
    }
}
