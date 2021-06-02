// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>
#include <IceRpc/Encoding.ice>

module IceRpc
{
    // These definitions help with the encoding of ice2 frames.

    /// Each ice2 frame has a type identified by this enumeration.
    enum Ice2FrameType : byte
    {
        Initialize = 0,
        Request = 1,
        Response = 2,
        GoAway = 3
    }

    dictionary<varint, ByteSeq> Fields;

    /// Keys of reserved fields in ice2 request and response headers.
    unchecked enum Ice2FieldKey : int
    {
        /// The string-string dictionary field (for request headers).
        Context = 0,

        /// The retry policy field (for response headers).
        RetryPolicy = -1,

        /// The W3C Trace Context field.
        TraceContext = -2,
    }

    /// Keys of reserved ice2 connection parameters.
    unchecked enum Ice2ParameterKey : int
    {
        IncomingFrameMaxSize = 0
    }

    /// The priority of this request.
    // TODO: describe semantics.
    unchecked enum Priority : byte
    {
    }

    // See Ice2RequestHeader below.
    [cs:readonly]
    struct Ice2RequestHeaderBody
    {
        string path;
        string operation;
        bool? \idempotent;       // null equivalent to false
        Priority? priority;      // null equivalent to 0
        varlong deadline;
    }

    /// Each ice2 request frame has:
    /// - a frame prologue, with the frame type and (for now) the overall frame size
    /// - a request header (below)
    /// - a request payload
    /// We put various members of the header in the Ice2RequestHeaderBody struct because the marshaling and unmarshaling
    /// of Fields is often custom.
    [cs:readonly]
    struct Ice2RequestHeader
    {
        varulong headerSize;
        Ice2RequestHeaderBody body;
        Fields fields;
        Encoding payloadEncoding;
        varulong payloadSize;
    }

    /// The type of result carried by an ice2 response frame.
    enum ResultType : byte
    {
        /// The request succeeded.
        Success = 0,

        /// The request failed.
        Failure = 1
    }

    /// Each ice2 response frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a response header (below)
    /// - a response payload
    [cs:readonly]
    struct Ice2ResponseHeader
    {
        varulong headerSize;
        Fields fields;
        ResultType resultType;
        Encoding payloadEncoding;
        varulong payloadSize;
    }

    // The possible error codes to describe the reason of a stream reset.
    enum StreamResetErrorCode : byte
    {
        /// The caller canceled the request.
        RequestCanceled = 0,

        /// The peer no longer wants to receive data from the stream.
        StopStreamingData = 1,
    }

    [cs:readonly]
    struct Ice2GoAwayBody
    {
        varulong lastBidirectionalStreamId;
        varulong lastUnidirectionalStreamId;
        string message;
    }
}
