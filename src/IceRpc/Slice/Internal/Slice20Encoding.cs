// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Slice 2.0 encoding class.</summary>
    internal sealed class Slice20Encoding : SliceEncoding
    {
        /// <summary>The Slice 2.0 encoding singleton.</summary>
        internal static SliceEncoding Instance { get; } = new Slice20Encoding();

        internal static int DecodeSizeLength(byte b) => SliceDecoder.DecodeVarLongLength(b);

        /// <summary>Encodes a size into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        internal static void EncodeSize(int size, Span<byte> into)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "size must be positive");
            }
            SliceEncoder.EncodeVarULong((ulong)size, into);
        }

        private Slice20Encoding()
            : base(Slice20Name)
        {
        }
    }
}
