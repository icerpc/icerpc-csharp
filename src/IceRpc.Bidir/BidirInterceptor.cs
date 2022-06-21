// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Security.Cryptography;

namespace IceRpc.Bidir;

/// <summary>An interceptor that encodes a relative origin field with each request, the relative origin is used by the
/// bidir middleware to identify connections from a given origin.</summary>
public class BidirInterceptor : IInvoker
{
    private readonly IInvoker _next;
    // The value for the relative origin ID field is a 128 bit field randomly initialized.
    private readonly byte[] _relativeOrigin;

    /// <summary>Constructs a bidir interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public BidirInterceptor(IInvoker next)
    {
        _next = next;
        _relativeOrigin = new byte[16];
        using var provider = RandomNumberGenerator.Create();
        provider.GetBytes(_relativeOrigin);
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.Protocol.HasFields)
        {
            request.Fields = request.Fields.With(
                RequestFieldKey.RelativeOrigin,
                (ref SliceEncoder encoder) => encoder.WriteByteSpan(new ReadOnlySpan<byte>(_relativeOrigin)));
        }
        return _next.InvokeAsync(request, cancel);
    }
}
