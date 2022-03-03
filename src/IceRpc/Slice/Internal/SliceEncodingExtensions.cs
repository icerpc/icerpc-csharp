// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods for class <see cref="SliceEncoding"/>.</summary>
    internal static class SliceEncodingExtensions
    {
        /// <summary>Decodes a buffer.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="decodeFunc">The decode function for buffer.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc"/> finds invalid data.
        /// </exception>
        internal static T DecodeBuffer<T>(
            this SliceEncoding encoding,
            ReadOnlySequence<byte> buffer,
            DecodeFunc<T> decodeFunc)
        {
            var decoder = new SliceDecoder(buffer, encoding);
            T result = decodeFunc(ref decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }

        /// <summary>Encodes a variable-length size into a span.</summary>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination span. This method uses all its bytes.</param>
        internal static void EncodeSize(this SliceEncoding encoding, int size, Span<byte> into)
        {
            if (encoding == Encoding.Slice11)
            {
                if (size < 0)
                {
                    throw new ArgumentException("a size must be positive", nameof(size));
                }

                if (into.Length == 1)
                {
                    if (size >= 255)
                    {
                        throw new ArgumentException("size value is too large for into", nameof(size));
                    }

                    into[0] = (byte)size;
                }
                else if (into.Length == 5)
                {
                    into[0] = 255;
                    SliceEncoder.EncodeInt(size, into[1..]);
                }
                else
                {
                    throw new ArgumentException("into's size must be 1 or 5", nameof(into));
                }
            }
            else
            {
                Slice20Encoding.EncodeSize(size, into);
            }
        }
    }
}
