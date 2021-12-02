// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    typealias Fields = dictionary<varint, sequence<byte>>;

    /// Keys of fields reserved for IceRPC request and response headers.
    unchecked enum FieldKey : int
    {
        /// The string-string dictionary field (for request headers).
        Context = 0,

        /// The retry policy field (for response headers).
        RetryPolicy = -1,

        /// The W3C Trace Context field (for the telemetry interceptor and middleware).
        TraceContext = -2,

        /// The payload compression field (for the compression interceptor and middleware).
        Compression = -3,
    }
}
