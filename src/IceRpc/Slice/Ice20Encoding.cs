// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Internal;
using System.Buffers;

namespace IceRpc.Slice
{
    /// <summary>The Ice 2.0 encoding class.</summary>
    public sealed class Ice20Encoding : IceEncoding
    {
        /// <summary>The Ice 2.0 encoding singleton.</summary>
        internal static Ice20Encoding Instance { get; } = new();

        /// <inheritdoc/>
        public override ReadOnlyMemory<ReadOnlyMemory<byte>> CreateEmptyPayload() => default;

        /// <inheritdoc/>
        public override async ValueTask<int> ReadPayloadSizeAsync(Stream payloadStream, CancellationToken cancel)
        {
            using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(8);
            Memory<byte> buffer = owner.Memory[0..1];

            int read = await payloadStream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            if (read == 0)
            {
                // end of stream == empty payload
                return 0;
            }
            else
            {
                int sizeLength = Ice20Decoder.DecodeSizeLength(buffer.Span[0]);
                if (sizeLength > 1)
                {
                    buffer = owner.Memory[0..sizeLength];
                    await payloadStream.ReadUntilFullAsync(buffer[1..], cancel).ConfigureAwait(false);
                }
                return Ice20Decoder.DecodeSize(buffer.Span).Size;
            }
        }

        internal override IceEncoder CreateIceEncoder(BufferWriter bufferWriter) => new Ice20Encoder(bufferWriter);

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
