// Copyright (c) ZeroC, Inc. All rights reserved.

// TODO: use generated internal types once supported
module IceRpc::Internal
{
    // These definitions help with the encoding of ice2 frames.

    /// Each ice2 frame has a type identified by this enumeration.
    enum Ice2FrameType : byte
    {
        /// The initialize frame is sent by each side the Ice2 connection on connection establishment
        /// to exchange Ice2 parameters.
        Initialize = 0,

        /// The request frame.
        Request = 1,

        /// The response frame.
        Response = 2,

        /// The data frames.
        /// TODO: replace these 2 frames with a single data frame.
        BoundedData = 3,

        UnboundedData = 4,

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
        IncomingFrameMaxSize = 0
    }

    // See Ice2RequestHeader below.
    [cs:readonly]
    struct Ice2RequestHeaderBody
    {
        string path;
        string operation;
        bool? \idempotent;       // null equivalent to false
        varlong deadline;
        string? payloadEncoding;
    }

    /// Each ice2 request frame has:
    /// - a frame prologue, with the frame type and (for now) the overall frame size
    /// - a request header (below)
    /// - a request payload
    /// We put various members of the header in the Ice2RequestHeaderBody struct because the encoding and decoding of
    /// Fields is often custom.
    [cs:readonly]
    struct Ice2RequestHeader
    {
        varulong headerSize;
        Ice2RequestHeaderBody body;
        Fields fields;
        varulong payloadSize;
    }

    // See Ice2ResponseHeader below.
    [cs:readonly]
    struct Ice2ResponseHeaderBody
    {
        ResultType resultType;
        string? payloadEncoding;
    }

    /// Each ice2 response frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a response header (below)
    /// - a response payload
    /// We put various members of the header in the Ice2ResponseHeaderBody struct because the encoding and decoding of
    /// Fields is often custom.
    [cs:readonly]
    struct Ice2ResponseHeader
    {
        varulong headerSize;
        Ice2ResponseHeaderBody body;
        Fields fields;
        varulong payloadSize;
    }

    /// The go away frame is sent on connection shutdown to notify the peer that it shouldn't perform new invocations
    /// and to provide the stream IDs of the invocations being dispatched. Invocations with stream IDs superior to
    /// these stream IDs can safely be retried.
    [cs:readonly]
    struct Ice2GoAwayBody
    {
        varlong lastBidirectionalStreamId;
        varlong lastUnidirectionalStreamId;
        string message;
    }
}
