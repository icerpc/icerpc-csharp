// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>The Ice 2.0 encoding class.</summary>
    public sealed class Ice20Encoding : IceEncoding
    {
        /// <summary>The Ice 2.0 encoding singleton.</summary>
        internal static Ice20Encoding Instance { get; } = new();

        private static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> _emptyPayload = new ReadOnlyMemory<byte>[]
        {
            new byte[] { 0 }
        };

        /// <inheritdoc/>
        public override ReadOnlyMemory<ReadOnlyMemory<byte>> CreateEmptyPayload() => _emptyPayload;

        internal override IceEncoder CreateIceEncoder(BufferWriter bufferWriter) => new Ice20Encoder(bufferWriter);

        internal override async ValueTask<int> DecodeSegmentSizeAsync(PipeReader reader, CancellationToken cancel)
        {
            ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            if (readResult.IsCompleted && readResult.Buffer.Length == 0)
            {
                return 0;
            }

            int sizeLength = Ice20Decoder.DecodeSizeLength(readResult.Buffer.FirstSpan[0]);
            if (sizeLength > readResult.Buffer.Length)
            {
                readResult = await reader.ReadAtLeastAsync(sizeLength, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (readResult.IsCompleted && readResult.Buffer.Length < sizeLength)
                {
                    throw new InvalidDataException("too few bytes in payload segment");
                }
            }

            ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(0, sizeLength);
            int size = DecodeSize(buffer, sizeLength);
            reader.AdvanceTo(buffer.End);
            return size;

            static int DecodeSize(ReadOnlySequence<byte> buffer, int sizeLength)
            {
                Span<byte> span = stackalloc byte[sizeLength];
                buffer.CopyTo(span);
                return Ice20Decoder.DecodeSize(span).Size;
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
