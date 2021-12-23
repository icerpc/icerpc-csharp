// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>The Ice 2.0 encoding class.</summary>
    public sealed class Ice20Encoding : IceEncoding
    {
        /// <summary>The Ice 2.0 encoding singleton.</summary>
        internal static Ice20Encoding Instance { get; } = new();

        private static readonly ReadOnlySequence<byte> _payloadWithZeroSize = new(new byte[] { 0 });

        /// <inheritdoc/>
        public override PipeReader CreateEmptyPayload(bool hasStream = true) =>
            hasStream ? PipeReader.Create(_payloadWithZeroSize) : EmptyPipeReader.Instance;

        // <summary>Decodes a buffer.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="decodeFunc">The decode function for buffer.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc"/> finds invalid data.
        /// </exception>
        internal static T DecodeBuffer<T>(ReadOnlyMemory<byte> buffer, DecodeFunc<T> decodeFunc)
        {
            var decoder = new IceDecoder(buffer, Ice20);
            T result = decodeFunc(ref decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }

        internal override async ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            PipeReader reader,
            CancellationToken cancel)
        {
            ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return (-1, true, false);
            }

            if (readResult.Buffer.IsEmpty)
            {
                Debug.Assert(readResult.IsCompleted);

                return (0, false, true);
            }
            else
            {
                int sizeLength = DecodeSizeLength(readResult.Buffer.FirstSpan[0]);
                Debug.Assert(sizeLength > 0);

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

                ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(readResult.Buffer.Start, sizeLength);
                int size = DecodeSizeFromSequence(buffer);
                bool isCompleted = readResult.IsCompleted && readResult.Buffer.Length == sizeLength;
                reader.AdvanceTo(buffer.End);
                return (size, false, isCompleted);
            }

            static int DecodeSizeFromSequence(ReadOnlySequence<byte> buffer)
            {
                if (buffer.IsSingleSegment)
                {
                    return DecodeSize(buffer.FirstSpan).Size;
                }
                else
                {
                    Span<byte> span = stackalloc byte[(int)buffer.Length];
                    buffer.CopyTo(span);
                    return DecodeSize(span).Size;
                }
            }
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

        internal static int DecodeSizeLength(byte b) => DecodeVarLongLength(b);

        internal override void EncodeFixedLengthSize(int size, Span<byte> into) =>
            IceEncoder.EncodeSize20(size, into);

        internal override void EncodeSize(int size, Span<byte> into) =>
            IceEncoder.EncodeSize20(size, into);

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the 2.0 encoding.
        /// </summary>
        /// <remarks>The parameter is a long and not a varulong because sizes and size-like values are usually passed
        /// around as signed integers, even though sizes cannot be negative and are encoded like varulong values.
        /// </remarks>
        internal static int GetSizeLength(long size) => IceEncoder.GetVarULongEncodedSize(checked((ulong)size));

        private Ice20Encoding()
            : base(Ice20Name)
        {
        }
    }
}
