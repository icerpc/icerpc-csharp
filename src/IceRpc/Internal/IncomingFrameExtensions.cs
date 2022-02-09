// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods for IncomingFrame.</summary>
    internal static class IncomingFrameExtensions
    {
        /// <summary>Installs a payload decompressor on the frame's Payload.</summary>
        internal static void UsePayloadDecompressor(this IncomingFrame frame)
        {
            if (frame.Protocol.HasFields)
            {
                CompressionFormat compressionFormat = frame.Fields.Get(
                    (int)FieldKey.CompressionFormat,
                    (ref SliceDecoder decoder) => decoder.DecodeCompressionFormat());

                if (compressionFormat == CompressionFormat.Deflate)
                {
                    frame.Payload = PipeReader.Create(
                        new DeflateStream(frame.Payload.AsStream(), CompressionMode.Decompress));
                }
                // else don't do anything
            }
        }
    }
}
