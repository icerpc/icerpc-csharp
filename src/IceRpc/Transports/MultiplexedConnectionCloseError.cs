// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports;

/// <summary>The error supplied to <see cref="IMultiplexedConnection.CloseAsync" />.</summary>
public enum MultiplexedConnectionCloseError : byte
{
    /// <summary>The connection was closed without error.</summary>
    NoError = 0,

    /// <summary>The connection was aborted for some unspecified reason.</summary>
    Aborted,

    /// <summary>The server refused the connection, for example because it's shutting down.</summary>
    /// <seealso cref="IceRpcError.ConnectionRefused" />
    Refused,

    /// <summary>The server rejected the connection establishment attempt because it already has too many connections.
    /// </summary>
    /// <seealso cref="IceRpcError.ServerBusy" />
    ServerBusy,
}
