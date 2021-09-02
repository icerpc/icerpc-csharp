// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Slice
{
    /// With encoding 1.1, each tagged parameter has a specific tag format. This tag format describes how the data is
    /// encoded and how it can be skipped by the decoding code if the tagged parameter is present in the buffer but is
    /// not known to the receiver.
    enum TagFormat
    {
        /// A fixed size numeric encoded on 1 byte such as bool or byte.
        F1 = 0,

        /// A fixed size numeric encoded on 2 bytes such as short.
        F2 = 1,

        /// A fixed size numeric encoded on 4 bytes such as int or float.
        F4 = 2,

        /// A fixed size numeric encoded on 8 bytes such as long or double.
        F8 = 3,

        /// A variable-length size encoded on 1 or 5 bytes.
        Size = 4,

        /// A variable-length size followed by size bytes.
        VSize = 5,

        /// A fixed length size (encoded on 4 bytes) followed by size bytes.
        FSize = 6,

        /// Represents a class, but is no longer encoded or decoded.
        Class = 7,

        /// Pseudo non-encoded format that means one of F1, F2, F4 or F8.
        VInt = 8,

        /// Pseudo non-encoded format: like VSize but the size is optimized out.
        OVSize = 9
    }
}
