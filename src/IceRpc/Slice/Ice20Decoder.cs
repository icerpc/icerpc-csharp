// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Decoder for the Ice 2.0 encoding.</summary>
    public static class Ice20Decoder
    {
        /// <summary>Decodes a buffer.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="buffer">The byte buffer encoded with the Ice 2.0 encoding.</param>
        /// <param name="decodeFunc">The decode function for buffer.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc"/> finds invalid data.
        /// </exception>
        internal static T DecodeBuffer<T>(ReadOnlyMemory<byte> buffer, Func<IceDecoder, T> decodeFunc)
        {
            var decoder = new IceDecoder(buffer, Encoding.Ice20);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }

        internal static (int Size, int SizeLength) DecodeSize(ReadOnlySpan<byte> from)
        {
            ulong size = (from[0] & 0x03) switch
            {
                0 => (uint)from[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(from) >> 2,
                2 => BitConverter.ToUInt32(from) >> 2,
                _ => BitConverter.ToUInt64(from) >> 2
            };

            checked // make sure we don't overflow
            {
                return ((int)size, DecodeSizeLength(from[0]));
            }
        }

        internal static int DecodeSizeLength(byte b) => IceDecoder.DecodeVarLongLength(b);
    }
}
