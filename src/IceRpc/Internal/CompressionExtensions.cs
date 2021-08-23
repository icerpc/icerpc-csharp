// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Compression;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Indicates the result of the <see cref="CompressionExtensions.Compress"/> operation.</summary>
    internal enum CompressionResult
    {
        /// <summary>The payload was successfully compressed.</summary>
        Success,

        /// <summary>The payload size is smaller than the configured compression threshold.</summary>
        PayloadTooSmall,

        /// <summary>The payload was not compressed, compressing it would increase its size.</summary>
        PayloadNotCompressible
    }

    /// <summary>Extensions methods to compress and decompress 2.0 encoded payloads.</summary>
    internal static class CompressionExtensions
    {
        /// <summary>Compresses the payload using the specified compression format (by default, deflate).
        /// Compressed payloads are only supported with the 2.0 encoding.</summary>
        /// <returns>A <see cref="CompressionResult"/> value indicating the result of the compression operation.
        /// </returns>
        internal static (CompressionResult, CompressionFormat format, ReadOnlyMemory<byte>) Compress(
            this ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            int payloadSize,
            CompressionLevel compressionLevel,
            int compressionMinSize)
        {
            if (payloadSize < compressionMinSize)
            {
                return (CompressionResult.PayloadTooSmall, CompressionFormat.NotCompressed, default);
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
                return (CompressionResult.PayloadNotCompressible, CompressionFormat.NotCompressed, default);
            }

            Memory<byte> compressedData = memoryStream.GetBuffer();
            compressedData = compressedData.Slice(0, (int)memoryStream.Position);
            return (CompressionResult.Success, CompressionFormat.Deflate, compressedData);
        }

        internal static void CompressPayload(this OutgoingFrame frame, Configure.CompressOptions options)
        {
            if (frame.PayloadEncoding != Encoding.Ice20)
            {
                return;
            }
            else if (frame.Fields.ContainsKey((int)Ice2FieldKey.CompressionPolicy))
            {
                throw new InvalidOperationException("the payload is already compressed");
            }

            (CompressionResult result, CompressionFormat format, ReadOnlyMemory<byte> compressedPayload) =
                frame.Payload.Compress(frame.PayloadSize,
                                        options.CompressionLevel,
                                        options.CompressionMinSize);
            if (result == CompressionResult.Success)
            {
                var header = new CompressionPolicyField(format, (ulong)frame.PayloadSize);
                frame.Fields.Add((int)Ice2FieldKey.CompressionPolicy, encoder => header.Encode(encoder));
                frame.Payload = new ReadOnlyMemory<byte>[] { compressedPayload };
            }
        }

        internal static (CompressionFormat, Stream) CompressStream(
            this Stream stream,
            CompressionLevel compressionLevel) =>
            (CompressionFormat.Deflate, new DeflateStream(
                    stream,
                    compressionLevel == CompressionLevel.Fastest ?
                        System.IO.Compression.CompressionLevel.Fastest :
                        System.IO.Compression.CompressionLevel.Optimal));

        /// <summary>Decompresses the payload if it is compressed. Compressed payloads are only supported with the 2.0
        /// encoding.</summary>
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

        internal static void DecompressPayload(this IncomingFrame frame)
        {
            if (frame.PayloadEncoding == Encoding.Ice20 &&
                frame.Fields.TryGetValue((int)Ice2FieldKey.CompressionPolicy,
                                         out ReadOnlyMemory<byte> CompressionPolicyField))
            {
                var decoder = new Ice20Decoder(CompressionPolicyField);
                var compressionPolicy = new CompressionPolicyField(decoder);
                frame.Payload = frame.Payload.Decompress(
                    compressionPolicy.Format,
                    (int)compressionPolicy.UncompressedSize,
                    frame.Connection.IncomingFrameMaxSize);
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
