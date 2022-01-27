// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Slice 1.1 encoding class.</summary>
    internal sealed class Slice11Encoding : SliceEncoding
    {
        /// <summary>The Slice 1.1 encoding singleton.</summary>
        internal static SliceEncoding Instance { get; } = new Slice11Encoding();

        internal static int DecodeFixedLengthSize(ReadOnlySpan<byte> buffer) =>
            SliceDecoder.DecodeInt(buffer) is int size && size >= 0 ? size :
                throw new InvalidDataException("received invalid negative size");

        private Slice11Encoding()
            : base(Slice11Name)
        {
        }
    }
}
