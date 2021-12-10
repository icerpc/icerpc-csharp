// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods for OutgoingFrame.</summary>
    internal static class OutgoingFrameExtensions
    {
        /// <summary>Installs a compressor on the frame's PayloadSink.</summary>
        internal static void UsePayloadCompressor(this OutgoingFrame frame, Configure.CompressOptions options)
        {
            // We don't compress if the payload is already compressed.
            if (frame.Protocol.HasFieldSupport && !frame.Fields.ContainsKey((int)FieldKey.Compression))
            {
                frame.PayloadSink = PipeWriter.Create(
                    new DeflateStream(
                        frame.PayloadSink.AsStream(),
                        options.CompressionLevel));

                var header = new CompressionField(CompressionFormat.Deflate);
                frame.Fields.Add((int)FieldKey.Compression, encoder => header.Encode(encoder));
            }
        }
    }
}
