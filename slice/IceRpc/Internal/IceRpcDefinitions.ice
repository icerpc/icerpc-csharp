// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Internal
{
    // These definitions help with the encoding of icerpc frames.

    /// Each icerpc control frame has a type identified by this enumeration.
    enum IceRpcControlFrameType : byte
    {
        /// The initialize frame is sent by each side the icerpc connection on connection establishment
        /// to exchange icerpc parameters.
        Initialize = 0,

        /// The ping frame is sent to keep alive the icerpc connection.
        Ping = 5,

        /// The go away frame is sent to notify the peer that the connection is being shutdown. The shutdown initiator
        /// sends it as soon as the connection is shutdown. The receiver sends back a go away frame in return.
        GoAway = 6,

        /// The go away completed frame is sent after receiving a go away frame and once the invocations and dispatches
        /// are completed. The connection is closed once the local invocations and dispatches are completed and once
        /// this frame is received.
        GoAwayCompleted = 7,
    }

    /// Keys of reserved icerpc connection parameters.
    unchecked enum IceRpcParameterKey : int
    {
        /// The incoming frame maximum size.
        IncomingFrameMaxSize = 0
    }

    /// Each icerpc request frame consists of:
    /// - a request header size (varulong)
    /// - a request header (below)
    /// - a request payload
    [cs:readonly]
    struct IceRpcRequestHeader
    {
        string path;
        string fragment;
        string operation;
        bool \idempotent;
        varlong deadline;
        string payloadEncoding; // empty equivalent to "2.0"
        // Fields fields; (encoded/decoded manually for now)
    }

    /// Each icerpc response frame consists of:
    /// - a response header size (varulong)
    /// - a response header (below)
    /// - a response payload
    [cs:readonly]
    struct IceRpcResponseHeader
    {
        ResultType resultType;
        string payloadEncoding; // empty equivalent to "2.0"
        // Fields fields; (encoded/decoded manually for now)
    }

    /// The go away frame is sent on connection shutdown to notify the peer that it shouldn't perform new invocations
    /// and to provide the stream IDs of the invocations being dispatched. Invocations with stream IDs superior to
    /// these stream IDs can safely be retried.
    [cs:readonly]
    struct IceRpcGoAwayBody
    {
        varlong lastBidirectionalStreamId;
        varlong lastUnidirectionalStreamId;
        string message;
    }
}
