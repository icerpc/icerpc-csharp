// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Connection-related events shared by all Ice protocols.</summary>
    public enum ConnectionEventIds
    {
        /// <summary>The exception that triggered the closure of a connection.</summary>
        ConnectionClosedReason = Internal.BaseEventIds.Connection,

        /// <summary>Established a protocol connection.</summary>
        CreateProtocolConnection,

        /// <summary>Sent a ping.</summary>
        Ping,

        /// <summary>The protocol connection was disposed.</summary>
        ProtocolConnectionDispose,

        /// <summary>The protocol connection was shut down.</summary>
        ProtocolConnectionShutdown,

        /// <summary>The protocol connection shut down was canceled.</summary>
        ProtocolConnectionShutdownCanceled,

        /// <summary>Received a request.</summary>
        ReceiveRequest,

        /// <summary>Received a response.</summary>
        ReceiveResponse,

        /// <summary>A request was sent successfully.</summary>
        SendRequest,

        /// <summary>A response was sent successfully.</summary>
        SendResponse
    }
}
