// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// The compression format of the Request or "Success" Response frame payload.
    unchecked enum CompressionFormat : byte
    {
        /// The payload is not compressed and can be read directly.
        // TODO: Remove NotCompressed once data frames suppport fields
        NotCompressed = 0,

        /// The payload is compressed using the deflate format.
        Deflate = 1,
    }

    /// The compression field of the payload carried by a frame.
    [cs:readonly]
    struct CompressionField
    {
        /// The compression format.
        CompressionFormat format;

        /// The uncompressed size.
        varulong uncompressedSize;
    }
}
