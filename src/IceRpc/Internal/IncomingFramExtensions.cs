// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods for IncomingFrame.</summary>
    internal static class IncomingFrameExtensions
    {
        /// <summary>Installs a payload decompress on the frame's Payload.</summary>
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
