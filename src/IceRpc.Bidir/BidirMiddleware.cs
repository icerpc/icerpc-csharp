// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Bidir;

/// <summary>A middleware that keeps the connection of relative proxies associated to the last known client connection.
/// </summary>
public class BidirMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly object _mutex = new();
    private readonly Dictionary<byte[], BidirConnection> _connections = new(RelativeOriginEqualityComparer.Instance);
    private readonly TimeSpan _reconnectTimeout;

    /// <summary>Constructs a bidir middleware.</summary>
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
        if (request.Protocol.HasFields &&
            request.Fields.DecodeValue(
                RequestFieldKey.RelativeOrigin,
                (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>()) is byte[] relativeOrigin)
        {
            lock (_mutex)
            {
                if (_connections.TryGetValue(relativeOrigin, out BidirConnection? bidirConnection))
                {
                    bidirConnection.UpdateDecoratee(request.Connection);
                }
                else
                {
                    bidirConnection = new BidirConnection(request.Connection, _reconnectTimeout);
                    bidirConnection.OnShutdown(_ => RemoveConnection());
                    bidirConnection.OnAbort(_ => RemoveConnection());
                    _connections.Add(relativeOrigin, bidirConnection);
                }
                request.Connection = bidirConnection;
            }
        }
        return _next.DispatchAsync(request, cancel);

        void RemoveConnection()
        {
            lock (_mutex)
            {
                _connections.Remove(relativeOrigin);
            }
        }
    }

    private class RelativeOriginEqualityComparer : IEqualityComparer<byte[]>
    {
        internal static IEqualityComparer<byte[]> Instance = new RelativeOriginEqualityComparer();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.Length == y.Length && x.AsSpan().SequenceEqual(y.AsSpan());
        }

        public int GetHashCode([DisallowNull] byte[] obj)
        {
            var hashCode = new HashCode();
            foreach (byte value in obj)
            {
                hashCode.Add(value);
            }
            return hashCode.ToHashCode();
        }
    }
}
