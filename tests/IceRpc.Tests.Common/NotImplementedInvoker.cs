// Copyright (c) ZeroC, Inc.

namespace IceRpc.Tests.Common;

/// <summary>A trivial invoker that always throws <see cref="NotImplementedException" />.</summary>
public class NotImplementedInvoker : IInvoker
{
    /// <summary>Gets the unique instance of this class.</summary>
    public static NotImplementedInvoker Instance { get; } = new();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    private NotImplementedInvoker()
    {
        // Ensures it's a singleton.
    }
}
