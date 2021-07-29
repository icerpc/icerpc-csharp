// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// With the 2.0 encoding, the payload of some frames such a Request and "Success" Response can be compressed.
    /// CompressionFormat is the first byte of such a payload.
    unchecked enum CompressionFormat : byte
    {
        /// The payload is not compressed and can be read directly.
        NotCompressed = 0,

        /// The payload is compressed using the deflate format.
        Deflate = 1,
    }
}
