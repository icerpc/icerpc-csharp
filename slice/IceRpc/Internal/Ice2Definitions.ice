// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Internal
{
    // These definitions help with the encoding of ice2 frames.

    /// Each ice2 frame has a type identified by this enumeration.
    // TODO: rename Ice2ControlFrameType
    enum Ice2FrameType : byte
    {
        /// The initialize frame is sent by each side the Ice2 connection on connection establishment
        /// to exchange Ice2 parameters.
        Initialize = 0,

        /// The ping frame is sent to keep alive the Ice2 connection.
        Ping = 5,

        /// The go away frame is sent to notify the peer that the connection is being shutdown. The shutdown initiator
        /// sends it as soon as the connection is shutdown. The receiver sends back a go away frame in return.
        GoAway = 6,

        /// The go away completed frame is sent after receiving a go away frame and once the invocations and dispatches
        /// are completed. The connection is closed once the local invocations and dispatches are completed and once
        /// this frame is received.
        GoAwayCompleted = 7,
    }

    /// Keys of reserved ice2 connection parameters.
    unchecked enum Ice2ParameterKey : int
    {
        /// The incoming frame maximum size.
        IncomingFrameMaxSize = 0,
    }

    /// Each ice2 request frame consists of:
    /// - a request header size (varulong)
    /// - a request header (below)
    /// - a request payload
    [cs:readonly]
    struct Ice2RequestHeader
    {
        path: string,
        fragment: string,
        operation: string,
        \idempotent: bool,
        deadline: varlong,
        payloadEncoding: string, // empty equivalent to "2.0"
        // fields: Fields, (encoded/decoded manually for now)
    }

    /// Each ice2 response frame consists of:
    /// - a response header size (varulong)
    /// - a response header (below)
    /// - a response payload
    [cs:readonly]
    struct Ice2ResponseHeader
    {
        resultType: ResultType,
        payloadEncoding: string, // empty equivalent to "2.0"
        // fields: Fields, (encoded/decoded manually for now)
    }

    /// The go away frame is sent on connection shutdown to notify the peer that it shouldn't perform new invocations
    /// and to provide the stream IDs of the invocations being dispatched. Invocations with stream IDs superior to
    /// these stream IDs can safely be retried.
    [cs:readonly]
    struct Ice2GoAwayBody
    {
        lastBidirectionalStreamId: varlong,
        lastUnidirectionalStreamId: varlong,
        message: string,
    }
}
