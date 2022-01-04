// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Slice 2.0 encoding class.</summary>
    internal sealed class Slice20Encoding : IceEncoding
    {
        /// <summary>The Slice 2.0 encoding singleton.</summary>
        internal static IceEncoding Instance { get; } = new Slice20Encoding();

        internal static (int Size, int SizeLength) DecodeSize(ReadOnlySpan<byte> from)
        {
            ulong size = (from[0] & 0x03) switch
            {
                0 => (uint)from[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(from) >> 2,
                2 => BitConverter.ToUInt32(from) >> 2,
                _ => BitConverter.ToUInt64(from) >> 2
            };

            try
            {
                return (checked((int)size), DecodeSizeLength(from[0]));
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("received invalid size", ex);
            }
        }

        internal static int DecodeSizeLength(byte b) => IceDecoder.DecodeVarLongLength(b);

        /// <summary>Encodes a size into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        internal static void EncodeSize(int size, Span<byte> into)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "size must be positive");
            }
            IceEncoder.EncodeVarULong((ulong)size, into);
        }

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the 2.0 encoding.
        /// </summary>
        internal static int GetSizeLength(int size) => IceEncoder.GetVarULongEncodedSize(checked((ulong)size));

        private Slice20Encoding()
            : base(Slice20Name)
        {
        }
    }
}
