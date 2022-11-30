// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>The possible error codes carried by a <see cref="IceRpcException" />. The error code specifies the
/// reason of the transport failure.</summary>
public enum IceRpcError
{
    /// <summary>The listener local address is in use.</summary>
    AddressInUse,

    /// <summary>The connection was aborted, typically by the peer. The abort can also be caused by a network failure,
    /// such as an intermediary router going down. With multiplexed transports, <see
    /// cref="IceRpcException.ApplicationErrorCode" /> is set to the error code provided to <see
    /// cref="IMultiplexedConnection.CloseAsync" />.</summary>
    ConnectionAborted,

    /// <summary>The connection was idle and timed-out.</summary>
    ConnectionIdle,

    /// <summary>The peer refused the connection.</summary>
    ConnectionRefused,

    /// <summary>An internal error occurred.</summary>
    InternalError,

    /// <summary>A call that was ongoing when the underlying resource (connection, stream) is aborted by the resource
    /// disposal.</summary>
    OperationAborted,

    /// <summary>An other unspecified error occurred.</summary>
    Unspecified,
}
