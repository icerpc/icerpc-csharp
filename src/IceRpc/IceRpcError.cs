// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>The errors carried by an <see cref="IceRpcException" />.</summary>
public enum IceRpcError
{
    /// <summary>An uncategorized IceRpc error.</summary>
    IceRpcError = -1,

    /// <summary>The listener local address is in use.</summary>
    AddressInUse = 1,

    /// <summary>The connection was aborted, typically by the peer. The abort can also be caused by a network failure,
    /// such as an intermediary router going down.</summary>
    ConnectionAborted,

    /// <summary>The peer closed the connection without reporting any error.</summary>
    ConnectionClosedByPeer,

    /// <summary>The connection was idle and timed-out.</summary>
    ConnectionIdle,

    /// <summary>The peer refused the connection.</summary>
    ConnectionRefused,

    /// <summary>A limit was exceeded, such as the <see cref="ConnectionOptions.MaxIceRpcHeaderSize" /> sent by the peer
    /// during connection establishment.</summary>
    LimitExceeded,

    /// <summary>An invoker failed to send a request because it could not establish or locate a connection.</summary>
    NoConnection,

    /// <summary>The operation was aborted because an underlying resource (connection, stream) was disposed while this
    /// operation was running.</summary>
    OperationAborted,

    /// <summary>The operation was refused because the target resource (for example a connection) is closed or shutting
    /// down or no longer available prior to the start of this operation.</summary>
    OperationRefused,

    /// <summary>The server rejected the connection establishment attempt because it already has too many connections.
    /// </summary>
    ServerBusy,

    /// <summary>The reading of a transport stream completed with incomplete data.</summary>
    TruncatedData,
}
