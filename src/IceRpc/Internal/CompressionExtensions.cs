// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods to compress and decompress payloads.</summary>
    internal static class CompressionExtensions
    {
        // TODO: move to class OutgoingFrameExtensions
        internal static void UsePayloadCompressor(this OutgoingFrame frame, Configure.CompressOptions options)
        {
            // We don't compress if the payload is already compressed.
            if (frame.Protocol.HasFieldSupport && !frame.Fields.ContainsKey((int)FieldKey.Compression))
            {
                frame.PayloadSink = PipeWriter.Create(
                    new DeflateStream(
                        frame.PayloadSink.AsStream(),
                        options.CompressionLevel == CompressionLevel.Fastest ?
                            System.IO.Compression.CompressionLevel.Fastest :
                            System.IO.Compression.CompressionLevel.Optimal));

                // TODO: eliminate payloadSize in CompressionField
                var header = new CompressionField(CompressionFormat.Deflate, 0);
                frame.Fields.Add((int)FieldKey.Compression, encoder => header.Encode(encoder));
            }
        }

        // TODO: move to class IncomingFrameExtensions
        internal static void UsePayloadDecompressor(this IncomingFrame frame)
        {
            if (frame.Protocol.HasFieldSupport)
            {
                // TODO: switch to class for CompressionField?
                CompressionField compressionField = frame.Fields.Get(
                    (int)FieldKey.Compression,
                    decoder => new CompressionField(decoder));

                if (compressionField != default) // default means not set
                {
                    if (compressionField.Format != CompressionFormat.Deflate)
                    {
                        throw new NotSupportedException(
                            $"cannot decompress compression format '{compressionField.Format}'");
                    }

                    frame.Payload = PipeReader.Create(
                        new DeflateStream(frame.Payload.AsStream(), CompressionMode.Decompress));
                }
            }
        }
    }
}
