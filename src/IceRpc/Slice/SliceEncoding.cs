// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>The versions of the Slice encoding supported by this IceRPC runtime.</summary>
public enum SliceEncoding : byte
{
    /// <summary>Version 1.1 of the Slice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
    Slice1 = 1,

    /// <summary>Version 2.0 of the Slice encoding, supported by IceRPC.</summary>
    Slice2 = 2,
}
