// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Slice 1.1 encoding class.</summary>
    internal sealed class Slice11Encoding : IceEncoding
    {
        /// <summary>The Slice 1.1 encoding singleton.</summary>
        internal static IceEncoding Instance { get; } = new Slice11Encoding();

        internal static int DecodeFixedLengthSize(ReadOnlySpan<byte> buffer) =>
            IceDecoder.DecodeInt(buffer) is int size && size >= 0 ? size :
                throw new InvalidDataException("received invalid negative size");

        private Slice11Encoding()
            : base(Slice11Name)
        {
        }
    }
}
