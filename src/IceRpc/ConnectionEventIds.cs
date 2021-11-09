// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Connection-related events shared by all Ice protocols.</summary>
    public enum ConnectionEventIds
    {
        /// <summary>The exception that triggered the closure of a connection.</summary>
        ConnectionClosedReason = Internal.BaseEventIds.Connection,

        /// <summary>Sent a ping.</summary>
        Ping,

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
