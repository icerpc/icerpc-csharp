// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods for OutgoingFrame.</summary>
    internal static class OutgoingFrameExtensions
    {
        private static readonly ReadOnlySequence<byte> _encodedCompressionFormatValue =
            new(new byte[] { (byte)CompressionFormat.Deflate });

        /// <summary>Installs a compressor on the frame's PayloadSink.</summary>
        internal static void UsePayloadCompressor(this OutgoingFrame frame, Configure.CompressOptions options)
        {
            // We don't compress if the payload is already compressed.
            if (frame.Protocol.HasFields && !frame.Fields.ContainsKey((int)FieldKey.CompressionFormat))
            {
                frame.PayloadSink = PipeWriter.Create(
                    new DeflateStream(frame.PayloadSink.ToPayloadSinkStream(), options.CompressionLevel));

                frame.Fields = frame.Fields.With((int)FieldKey.CompressionFormat, _encodedCompressionFormatValue);
            }
        }
    }
}
