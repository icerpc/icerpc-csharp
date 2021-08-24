// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc
{
    // These definitions help with the encoding of ice2 frames.

    /// Each ice2 frame has a type identified by this enumeration.
    enum Ice2FrameType : byte
    {
        Initialize = 0,
        Request = 1,
        Response = 2,
        BoundedData = 3,
        UnboundedData = 4,
        GoAway = 5,
        GoAwayCanceled = 6
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

        /// The Ice1 reply status when bridging an Ice1 response.
        ReplyStatus = -3,

        /// The payload compression field.
        Compression = -4
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

    /// The type of result carried by an ice2 response frame.
    enum ResultType : byte
    {
        /// The request succeeded.
        Success = 0,

        /// The request failed.
        Failure = 1
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

    [cs:readonly]
    struct Ice2GoAwayBody
    {
        varlong lastBidirectionalStreamId;
        varlong lastUnidirectionalStreamId;
        string message;
    }
}
