// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods for class <see cref="SliceEncoding"/>.</summary>
    internal static class IceEncodingExtensions
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
            ReadOnlyMemory<byte> buffer,
            DecodeFunc<T> decodeFunc)
        {
            var decoder = new SliceDecoder(buffer, encoding);
            T result = decodeFunc(ref decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }

        /// <summary>Decodes the size of a segment read from a PipeReader.</summary>
        internal static async ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            this SliceEncoding encoding,
            PipeReader reader,
            CancellationToken cancel)
        {
            int sizeLength = -1;
            ReadResult readResult;

            if (encoding == IceRpc.Encoding.Slice11)
            {
                sizeLength = 4;
                readResult = await reader.ReadAtLeastAsync(sizeLength, cancel).ConfigureAwait(false);
            }
            else
            {
                readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);
            }

            if (readResult.IsCanceled)
            {
                return (-1, true, false);
            }

            if (readResult.Buffer.IsEmpty)
            {
                Debug.Assert(readResult.IsCompleted);
                reader.AdvanceTo(readResult.Buffer.End);
                return (0, false, true);
            }

            if (sizeLength == -1)
            {
                sizeLength = Slice20Encoding.DecodeSizeLength(readResult.Buffer.FirstSpan[0]);
                if (sizeLength > readResult.Buffer.Length)
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    readResult = await reader.ReadAtLeastAsync(sizeLength, cancel).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        return (-1, true, false);
                    }

                    if (readResult.Buffer.Length < sizeLength)
                    {
                        throw new InvalidDataException("too few bytes in segment size");
                    }
                }
            }

            ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(readResult.Buffer.Start, sizeLength);
            int size = DecodeSizeFromSequence(buffer);
            bool isCompleted = readResult.IsCompleted && readResult.Buffer.Length == sizeLength;
            reader.AdvanceTo(buffer.End);
            return (size, false, isCompleted);

            int DecodeSizeFromSequence(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, encoding);
                return decoder.DecodeFixedLengthSize();
            }
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
