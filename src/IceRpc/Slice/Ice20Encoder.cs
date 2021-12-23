// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Encoder for the Ice 2.0 encoding.</summary>
    public static class Ice20Encoder
    {
        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the 2.0 encoding.
        /// </summary>
        /// <remarks>The parameter is a long and not a varulong because sizes and size-like values are usually passed
        /// around as signed integers, even though sizes cannot be negative and are encoded like varulong values.
        /// </remarks>
        internal static int GetSizeLength(long size) => IceEncoder.GetVarULongEncodedSize(checked((ulong)size));
    }
}
