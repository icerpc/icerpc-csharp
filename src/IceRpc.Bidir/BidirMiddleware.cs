// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Bidir;

/// <summary>A middleware that applies the deflate compression algorithm to the payload of a response depending on
/// the <see cref="ICompressFeature"/> feature.</summary>
public class BidirMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly Dictionary<Guid, IConnection> _connections = new();

    /// <summary>Constructs a compressor middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public BidirMiddleware(IDispatcher next)
    {
        _next = next;
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancel = default)
    {
        if (request.Protocol.HasFields && request.Fields.ContainsKey(RequestFieldKey.ConnectionId))
        {
            Guid connectionID = new Guid(
                request.Fields.DecodeValue(
                    RequestFieldKey.ConnectionId,
                    (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>())!);

            var bidirConnection = new Internal.BidirConnection(request.Connection);
            _connections.Add(connectionID, bidirConnection);
            request.Connection = bidirConnection;
        }

        OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);

        return response;
    }
}
