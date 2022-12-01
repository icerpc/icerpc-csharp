// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>The possible errors carried by an <see cref="IceRpcException" />.</summary>
public enum IceRpcError
{
    /// <summary>An uncategorized IceRpc error.</summary>
    IceRpcError = -1,

    /// <summary>The listener local address is in use.</summary>
    AddressInUse = 1,

    /// <summary>The connection was aborted, typically by the peer. The abort can also be caused by a network failure,
    /// such as an intermediary router going down. With multiplexed transports, <see
    /// cref="IceRpcException.ApplicationErrorCode" /> is set to the error code provided to <see
    /// cref="IMultiplexedConnection.CloseAsync" />.</summary>
    ConnectionAborted,

    /// <summary>The connection was idle and timed-out.</summary>
    ConnectionIdle,

    /// <summary>The peer refused the connection.</summary>
    ConnectionRefused,

    /// <summary>A call that was ongoing when the underlying resource (connection, stream) is aborted by the resource
    /// disposal.</summary>
    OperationAborted,
}
