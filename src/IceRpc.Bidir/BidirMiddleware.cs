﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;

namespace IceRpc.Bidir;

/// <summary>A middleware that keeps the connection of relative proxies associated to the last known client connection.</summary>
public class BidirMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly object _mutex = new();
    private readonly Dictionary<Guid, BidirConnection> _connections = new();

    /// <summary>Constructs a compressor middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public BidirMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancel = default)
    {
        if (request.Protocol.HasFields && request.Fields.ContainsKey(RequestFieldKey.ConnectionId))
        {
            var connectionId = new Guid(
                request.Fields.DecodeValue(
                    RequestFieldKey.ConnectionId,
                    (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>())!);

            lock (_mutex)
            {
                if (_connections.TryGetValue(connectionId, out BidirConnection? bidirConnection))
                {
                    bidirConnection.Decoratee = request.Connection;
                }
                else
                {
                    bidirConnection = new BidirConnection(request.Connection);
                    _connections.Add(connectionId, bidirConnection);
                }
                request.Connection = bidirConnection;
            }
        }
        return await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
    }
}
