// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>The Ice 1.1 encoding class.</summary>
    public sealed class Ice11Encoding : IceEncoding
    {
        /// <summary>The Ice 1.1 encoding singleton.</summary>
        internal static IceEncoding Instance { get; } = new Ice11Encoding();

        internal static int DecodeFixedLengthSize(ReadOnlySpan<byte> buffer)
        {
            int size = IceDecoder.DecodeInt(buffer);
            if (size < 0)
            {
                throw new InvalidDataException("received invalid negative size");
            }
            return size;
        }

        private Ice11Encoding()
            : base(Ice11Name)
        {
        }
    }
}
