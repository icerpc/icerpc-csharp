// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Describes the versions of the Slice encoding supported by this IceRPC runtime.</summary>
public enum SliceEncoding : byte
{
    /// <summary>Slice encoding version 1, supported by IceRPC and Ice 3.5 or greater. In Ice, it's called Ice encoding
    /// version 1.1.</summary>
    Slice1 = 1,

    /// <summary>Slice encoding version 2, supported by IceRPC.</summary>
    Slice2 = 2,
}
