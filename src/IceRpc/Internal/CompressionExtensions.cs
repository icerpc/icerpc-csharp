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

        /// <summary>Decompresses the payload if it is compressed.</summary>
        internal static ReadOnlyMemory<byte> Decompress(
            this ReadOnlyMemory<byte> payload,
            CompressionFormat payloadCompressionFormat,
            int decompressedSize,
            int maxSize)
        {
            if (payloadCompressionFormat != CompressionFormat.Deflate)
            {
                throw new NotSupportedException($"cannot decompress compression format '{payloadCompressionFormat}'");
            }

            // Read the decompressed size that is written after the compression format byte when the payload is
            // compressed
            if (decompressedSize > maxSize)
            {
                throw new InvalidDataException(
                    @$"decompressed size of {decompressedSize
                    } bytes is greater than the configured IncomingFrameMaxSize value ({maxSize} bytes)");
            }

            // We are going to replace the payload with a new payload segment/array that contains a decompressed
            // payload.
            using var decompressedStream = new MemoryStream(decompressedSize);

            Stream compressedByteStream;
            if (MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> segment))
            {
                compressedByteStream = new MemoryStream(segment.Array!,
                                                        segment.Offset,
                                                        segment.Count,
                                                        writable: false);
            }
            else
            {
                throw new InvalidOperationException("the payload is not backed by an array");
            }

            using var compressed = new DeflateStream(compressedByteStream, CompressionMode.Decompress);
            compressed.CopyTo(decompressedStream);

            Memory<byte> decompressedData = decompressedStream.GetBuffer();
            decompressedData = decompressedData.Slice(0, (int)decompressedStream.Position);
            if (decompressedData.Length != decompressedSize)
            {
                throw new InvalidDataException(
                    @$"received Deflate compressed payload with a decompressed size of only {decompressedData.
                    Length} bytes; expected {decompressedSize} bytes");
            }

            return decompressedData;
        }

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

            int payloadSize = frame.Payload.GetByteCount();

            (CompressionFormat? format, ReadOnlyMemory<byte> compressedPayload) =
                frame.Payload.Compress(payloadSize,
                                       options.CompressionLevel,
                                       options.CompressionMinSize);
            if (format != null)
            {
                var header = new CompressionField(format.Value, (ulong)payloadSize);
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
                    frame.Payload = frame.Payload.Decompress(compressionField.Format,
                                                             (int)compressionField.UncompressedSize,
                                                             frame.Connection.Options.IncomingFrameMaxSize);
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
