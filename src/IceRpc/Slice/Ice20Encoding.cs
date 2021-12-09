// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
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

        // Must use a payload with 0-size as opposed to nothing in case there is a Slice stream afterwards.
        private static readonly ReadOnlyMemory<byte> _emptyPayload = new byte[] { 0 };

        /// <inheritdoc/>
        public override PipeReader CreateEmptyPayload() => PipeReader.Create(new ReadOnlySequence<byte>(_emptyPayload));

        internal override IceEncoder CreateIceEncoder(BufferWriter bufferWriter) => new Ice20Encoder(bufferWriter);

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
                int sizeLength = Ice20Decoder.DecodeSizeLength(readResult.Buffer.FirstSpan[0]);
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
                int size = DecodeSize(buffer);
                bool isCompleted = readResult.IsCompleted && readResult.Buffer.Length == sizeLength;
                reader.AdvanceTo(buffer.End);
                return (size, false, isCompleted);
            }

            static int DecodeSize(ReadOnlySequence<byte> buffer)
            {
                if (buffer.IsSingleSegment)
                {
                    return Ice20Decoder.DecodeSize(buffer.FirstSpan).Size;
                }
                else
                {
                    Span<byte> span = stackalloc byte[(int)buffer.Length];
                    buffer.CopyTo(span);
                    return Ice20Decoder.DecodeSize(span).Size;
                }
            }
        }

        internal override IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            FeatureCollection features,
            DefaultIceDecoderFactories defaultIceDecoderFactories) =>
            features.Get<IIceDecoderFactory<Ice20Decoder>>() ?? defaultIceDecoderFactories.Ice20DecoderFactory;

        private Ice20Encoding()
            : base(Ice20Name)
        {
        }
    }
}
