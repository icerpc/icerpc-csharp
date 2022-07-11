// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

/// <summary>A trivial invoker that always returns an empty response with a fake connection context.</summary>
public class VoidInvoker : IInvoker
{
    /// <summary>Gets the unique instance of this class.</summary>
    public static VoidInvoker Instance { get; } = new();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default) =>
        Task.FromResult(new IncomingResponse(request, FakeConnectionContext.FromProtocol(request.Protocol)));

    private VoidInvoker()
    {
        // Ensures it's a singleton.
    }
}
