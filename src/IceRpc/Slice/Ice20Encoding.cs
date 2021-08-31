// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>The Ice 2.0 encoding class.</summary>
    public class Ice20Encoding : Encoding
    {
        /// <summary>The Ice 2.0 encoding singleton.</summary>
        internal static Ice20Encoding Instance { get; } = new();

        internal override void CheckSupportedIceEncoding()
        {
            // supported
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
