// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>The versions of the Slice encoding supported by this IceRPC runtime.</summary>
    public enum SliceEncoding : byte
    {
        /// <summary>Version 1.1 of the Slice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        Slice11 = 1,

        /// <summary>Version 2.0 of the Slice encoding, supported by IceRPC.</summary>
        Slice20 = 2,
    }
}
