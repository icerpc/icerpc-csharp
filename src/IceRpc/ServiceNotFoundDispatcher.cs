// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc;

/// <summary>A trivial dispatcher that always throws a <see cref="DispatchException"/> with error code
/// <see cref="DispatchErrorCode.ServiceNotFound"/>.</summary>
public class ServiceNotFoundDispatcher : IDispatcher
{
    /// <summary>Gets the unique instance of this class.</summary>
    public static ServiceNotFoundDispatcher Instance { get; } = new();

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel = default) =>
        throw new DispatchException(DispatchErrorCode.ServiceNotFound, RetryPolicy.OtherReplica);

    private ServiceNotFoundDispatcher()
    {
        // Ensures it's a singleton.
    }
}
