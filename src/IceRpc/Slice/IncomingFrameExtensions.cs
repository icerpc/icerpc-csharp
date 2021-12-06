// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extension methods for incoming frames.</summary>
    public static class IncomingFrameExtensions
    {
        /// <summary>Gets the frame Ice decoder factory for decoding the payload. The default Ice decoder factory
        /// for the payload encoding is used if no factory is set with the frame <see cref="Features"/>.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        public static IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            this IncomingFrame frame,
            DefaultIceDecoderFactories defaultIceDecoderFactories)
        {
            if (frame.PayloadEncoding is IceEncoding payloadEncoding)
            {
                return payloadEncoding.GetIceDecoderFactory(frame.Features, defaultIceDecoderFactories);
            }
            else
            {
                throw new NotSupportedException($"cannot decode payload encoded with {frame.PayloadEncoding}");
            }
        }

        /// <summary>Gets the frame Ice decoder factory for decoding the payload. The default Ice decoder factory
        /// is used if no factory is set with the frame <see cref="Features"/>.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="defaultIceDecoderFactory">The default Ice decoder factory.</param>
        public static IIceDecoderFactory<TDecoder> GetIceDecoderFactory<TDecoder>(
            this IncomingFrame frame,
            IIceDecoderFactory<TDecoder> defaultIceDecoderFactory) where TDecoder : IceDecoder =>
                frame.Features.Get<IIceDecoderFactory<TDecoder>>() ?? defaultIceDecoderFactory;
    }
}
