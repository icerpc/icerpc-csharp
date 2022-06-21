// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Runtime.Intrinsics;

namespace IceRpc.Bidir;

/// <summary>A middleware that keeps the connection of relative proxies associated to the last known client connection.</summary>
public class BidirMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly object _mutex = new();
    private readonly Dictionary<Vector128<ulong>, BidirConnection> _connections = new();
    private readonly TimeSpan _reconnectTimeout;

    /// <summary>Constructs a compressor middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="reconnectTimeout">The timeout for reestablish the connection.</param>
    public BidirMiddleware(IDispatcher next, TimeSpan reconnectTimeout)
    {
        _next = next;
        _reconnectTimeout = reconnectTimeout;
    }

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancel = default)
    {
        if (request.Protocol.HasFields && request.Fields.ContainsKey(RequestFieldKey.RelativeOrigin))
        {
            Vector128<ulong> relativeOrigin =
                request.Fields.DecodeValue(
                    RequestFieldKey.RelativeOrigin,
                    (ref SliceDecoder decoder) => Vector128.Create(decoder.DecodeUInt64(), decoder.DecodeUInt64()));

            lock (_mutex)
            {
                if (_connections.TryGetValue(relativeOrigin, out BidirConnection? bidirConnection))
                {
                    bidirConnection.Decoratee = request.Connection;
                }
                else
                {
                    bidirConnection = new BidirConnection(request.Connection, _reconnectTimeout);
                    _connections.Add(relativeOrigin, bidirConnection);
                }
                request.Connection = bidirConnection;
            }
        }
        return _next.DispatchAsync(request, cancel);
    }
}
