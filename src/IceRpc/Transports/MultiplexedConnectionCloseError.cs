// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>The error supplied to <see cref="IMultiplexedConnection.CloseAsync" />.</summary>
public enum MultiplexedConnectionCloseError : byte
{
    /// <summary>The connection was closed without error.</summary>
    NoError = 0,

    /// <summary>The connection was aborted for some unspecified reason.</summary>
    Aborted = 1,

    /// <summary>The server rejected the connection establishment attempt because it already has too many connections.
    /// </summary>
    /// <seealso cref="IceRpcError.ServerBusy" />
    ServerBusy = 2,
}
