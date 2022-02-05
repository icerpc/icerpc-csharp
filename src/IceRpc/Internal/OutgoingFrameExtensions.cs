// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods for OutgoingFrame.</summary>
    internal static class OutgoingFrameExtensions
    {
        private static readonly ReadOnlyMemory<byte> _encodedCompressionFieldValue = new byte[]
        {
            (byte)CompressionFormat.Deflate
        };

        /// <summary>Installs a compressor on the frame's PayloadSink.</summary>
        internal static void UsePayloadCompressor(this OutgoingFrame frame, Configure.CompressOptions options)
        {
            // We don't compress if the payload is already compressed.
            if (frame.Protocol.HasFields && !frame.FieldsOverrides.ContainsKey((int)FieldKey.Compression))
            {
                frame.PayloadSink = PipeWriter.Create(
                    new DeflateStream(frame.PayloadSink.ToPayloadSinkStream(), options.CompressionLevel));

                frame.Fields = frame.Fields.With((int)FieldKey.Compression, _encodedCompressionFieldValue);
            }
        }
    }
}
