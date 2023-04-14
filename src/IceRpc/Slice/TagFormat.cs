// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>With Slice1, each tagged parameter has a specific tag format. This tag format describes how the data is
/// encoded and how it can be skipped by the decoding code if the tagged parameter is present in the buffer but is
/// not known to the receiver. The tag format is encoded on 3 bits; as a result, only values in the range 0 to 7 are
/// encoded.</summary>
public enum TagFormat : int
{
    /// <summary>A fixed size numeric encoded on 1 byte such as bool or uint8.</summary>
    F1 = 0,

    /// <summary>A fixed size numeric encoded on 2 bytes such as int16.</summary>
    F2 = 1,

    /// <summary>A fixed size numeric encoded on 4 bytes such as int32 or float32.</summary>
    F4 = 2,

    /// <summary>A fixed size numeric encoded on 8 bytes such as int64 or float64.</summary>
    F8 = 3,

    /// <summary>A variable-length size encoded on 1 or 5 bytes.</summary>
    Size = 4,

    /// <summary>A variable-length size followed by size bytes.</summary>
    VSize = 5,

    /// <summary>A fixed length size (encoded on 4 bytes) followed by size bytes.</summary>
    FSize = 6,

    /// <summary>Represents a class, but is no longer encoded or decoded.</summary>
    Class = 7,

    /// <summary>Pseudo non-encoded format: like VSize but the size is optimized out.</summary>
    OptimizedVSize = 8,
}
