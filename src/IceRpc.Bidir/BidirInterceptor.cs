// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Security.Cryptography;

namespace IceRpc.Bidir;

/// <summary>An interceptor that encodes a connection ID field with each request, the connection ID can be used
/// to identify connections from a given client.</summary>
public class BidirInterceptor : IInvoker
{
    private readonly IInvoker _next;
    // The value for the relative origin ID field is a 128 bit field randomly initialized during initialization.
    private readonly byte[] _connectionId;

    /// <summary>Constructs a bidir interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public BidirInterceptor(IInvoker next)
    {
        _next = next;
        _connectionId = new byte[16];
        using var provider = RandomNumberGenerator.Create();
        provider.GetBytes(_connectionId);
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        request.Fields = request.Fields.With(
            RequestFieldKey.ConnectionId,
            (ref SliceEncoder encoder) => encoder.WriteByteSpan(new ReadOnlySpan<byte>(_connectionId)));
        return _next.InvokeAsync(request, cancel);
    }
}
