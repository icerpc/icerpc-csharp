// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Ice 1.1 encoding class.</summary>
    internal sealed class Ice11Encoding : IceEncoding
    {
        /// <summary>The Ice 1.1 encoding singleton.</summary>
        internal static IceEncoding Instance { get; } = new Ice11Encoding();

        internal static int DecodeFixedLengthSize(ReadOnlySpan<byte> buffer) =>
            IceDecoder.DecodeInt(buffer) is int size && size >= 0 ? size :
                throw new InvalidDataException("received invalid negative size");

        private Ice11Encoding()
            : base(Slice11Name)
        {
        }
    }
}
