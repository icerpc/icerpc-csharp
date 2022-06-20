// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;

namespace IceRpc.Bidir;

/// <summary>An interceptor that applies the deflate compression algorithm to the payload of a request depending on
/// the <see cref="ICompressFeature"/> feature.</summary>
public class BidirInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly byte[] _sessionId;

    /// <summary>Constructs a compressor interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public BidirInterceptor(IInvoker next)
    {
        _next = next;
        var bytes = new byte[16];
        using var provider = RandomNumberGenerator.Create();
        provider.GetBytes(bytes);
        _sessionId = new Guid(bytes).ToByteArray();
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        request.Fields = request.Fields.With(
            RequestFieldKey.Deadline,
            (ref SliceEncoder encoder) => encoder.EncodeSequence(_sessionId));
        return _next.InvokeAsync(request, cancel);
    }
}
