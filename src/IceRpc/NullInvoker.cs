// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>A trivial invoker that always throws <see cref="InvalidOperationException"/>.</summary>
public class NullInvoker : IInvoker
{
    /// <summary>Gets the unique instance of this class.</summary>
    public static NullInvoker Instance { get; } = new();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default) =>
        throw new InvalidOperationException("invoked null invoker");

    private NullInvoker()
    {
        // Ensures it's a singleton.
    }
}
