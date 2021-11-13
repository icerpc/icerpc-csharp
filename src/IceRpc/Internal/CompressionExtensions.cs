// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Compression;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Extensions methods to compress and decompress payloads.</summary>
    internal static class CompressionExtensions
    {
        /// <summary>Compresses the payload using the specified compression format (by default, deflate).
        /// Compressed payloads are only supported with protocols that support fields (Ice2).</summary>
        /// <returns>The <see cref="CompressionFormat"/> and compressed payload. The format is null if the payload
        /// was not compressed.</returns>
        internal static (CompressionFormat?, ReadOnlyMemory<byte>) Compress(
            this ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            int payloadSize,
            CompressionLevel compressionLevel,
            int compressionMinSize)
        {
            if (payloadSize < compressionMinSize)
            {
                return (default, default);
            }

            // Reserve memory for the compressed data, this should never be greater than the uncompressed data
            // otherwise we will just send the uncompressed data.
            using var memoryStream = new MemoryStream(payloadSize);
            using var deflateStream = new DeflateStream(
                memoryStream,
                compressionLevel == CompressionLevel.Fastest ?
                    System.IO.Compression.CompressionLevel.Fastest :
                    System.IO.Compression.CompressionLevel.Optimal);
            try
            {
                for (int i = 0; i < payload.Length; ++i)
                {
                    deflateStream.Write(payload.Span[i].Span);
                }
                deflateStream.Flush();
            }
            catch (NotSupportedException)
            {
                // If the data doesn't fit in the memory stream NotSupportedException is thrown when DeflateStream
                // try to expand the fixed size MemoryStream.
                return (default, default);
            }

            ReadOnlyMemory<byte> compressedData = memoryStream.GetBuffer();
            compressedData = compressedData.Slice(0, (int)memoryStream.Position);
            return (CompressionFormat.Deflate, compressedData);
        }

        internal static (CompressionFormat, Stream) CompressStream(
            this Stream stream,
            CompressionLevel compressionLevel) =>
            (CompressionFormat.Deflate, new DeflateStream(
                    stream,
                    compressionLevel == CompressionLevel.Fastest ?
                        System.IO.Compression.CompressionLevel.Fastest :
                        System.IO.Compression.CompressionLevel.Optimal));

        internal static void CompressPayload(this OutgoingFrame frame, Configure.CompressOptions options)
        {
            if (!frame.Protocol.HasFieldSupport)
            {
                // Don't compress the payload if the protocol doesn't support fields.
                return;
            }
            else if (frame.Fields.ContainsKey((int)FieldKey.Compression))
            {
                throw new InvalidOperationException("the payload is already compressed");
            }

            (CompressionFormat? format, ReadOnlyMemory<byte> compressedPayload) =
                frame.Payload.Compress(frame.PayloadSize,
                                       options.CompressionLevel,
                                       options.CompressionMinSize);
            if (format != null)
            {
                var header = new CompressionField(format.Value, (ulong)frame.PayloadSize);
                frame.Fields.Add((int)FieldKey.Compression, encoder => header.Encode(encoder));
                frame.Payload = new ReadOnlyMemory<byte>[] { compressedPayload };
            }
        }

        internal static void DecompressPayload(this IncomingFrame frame)
        {
            if (frame.Protocol.HasFieldSupport)
            {
                // TODO: switch to class for CompressionField?
                CompressionField compressionField = frame.Fields.Get((int)FieldKey.Compression,
                                                                     decoder => new CompressionField(decoder));

                if (compressionField != default) // default means not set
                {
                    frame.PayloadStream = frame.PayloadStream.DecompressStream(compressionField.Format);
                }
            }
        }

        internal static Stream DecompressStream(this Stream stream, CompressionFormat compressionFormat)
        {
            if (compressionFormat != CompressionFormat.Deflate)
            {
                throw new NotSupportedException($"cannot decompress compression format '{compressionFormat}'");
            }

            return new DeflateStream(stream, CompressionMode.Decompress);
        }
    }
}
