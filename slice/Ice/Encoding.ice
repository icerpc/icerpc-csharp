// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// The Ice encoding defines how Slice constructs are marshaled to and later unmarshaled from sequences of bytes.
    /// An Encoding struct holds a version of the Ice encoding.
    [cs:readonly]
    struct Encoding
    {
        /// The major version number of this version of the Ice encoding.
        byte major;

        /// The minor version number of this version of the Ice encoding.
        byte minor;
    }

    /// With the 2.0 encoding, the payload of an encapsulation (and by extension the payload of a protocol frame)
    /// may be compressed. CompressionFormat describes the format of such a payload.
    unchecked enum CompressionFormat : byte
    {
        /// The payload is not compressed and can be read directly.
        Decompressed = 0,

        /// The payload is compressed using the gzip format.
        GZip = 1,
    }
}
