// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc
{
    dictionary<varint, ByteSeq> Fields;

    /// Keys of fields reserved for IceRPC request and response headers.
    unchecked enum FieldKey : int
    {
        /// The string-string dictionary field (for request headers).
        Context = 0,

        /// The retry policy field (for response headers).
        RetryPolicy = -1,

        /// The Ice1 reply status when bridging an Ice1 response (for response headers).
        ReplyStatus = -2,

        /// The W3C Trace Context field (for the telemetry interceptor and middleware).
        TraceContext = -3,

        /// The payload compression field (for the compression interceptor and middleware).
        Compression = -4
    }
}
