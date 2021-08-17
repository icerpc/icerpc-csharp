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
        internal static (CompressionResult, ReadOnlyMemory<byte>) Compress(
            this ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            int payloadSize,
            CompressionLevel compressionLevel,
            int compressionMinSize)
        {
            var payloadCompressionFormat = (CompressionFormat)payload.Span[0].Span[0];

            if (payloadCompressionFormat != CompressionFormat.NotCompressed)
            {
                throw new InvalidOperationException("the payload is already compressed");
            }

            if (payloadSize < compressionMinSize)
            {
                return (CompressionResult.PayloadTooSmall, default);
            }
            // Reserve memory for the compressed data, this should never be greater than the uncompressed data
            // otherwise we will just send the uncompressed data.
            byte[] compressedData = new byte[payloadSize];

            int offset = 0;
            // Set the compression status byte to Deflate compressed
            compressedData[offset++] = (byte)CompressionFormat.Deflate;
            // Write the size of the uncompressed data
            int sizeLength = Ice20Encoder.GetSizeLength(payloadSize);
            Ice20Encoder.EncodeFixedLengthSize(payloadSize, compressedData.AsSpan(offset, sizeLength));
            offset += sizeLength;

            using var memoryStream = new MemoryStream(compressedData, offset, compressedData.Length - offset);
            using var deflateStream = new DeflateStream(
                memoryStream,
                compressionLevel == CompressionLevel.Fastest ?
                    System.IO.Compression.CompressionLevel.Fastest :
                    System.IO.Compression.CompressionLevel.Optimal);
            try
            {
                // The data to compress starts after the compression status byte
                if (payload.Span[0].Length > 1)
                {
                    deflateStream.Write(payload.Span[0][1..].Span);
                }

                for (int i = 1; i < payload.Length; ++i) // skip first buffer that was written above
                {
                    deflateStream.Write(payload.Span[i].Span);
                }
                deflateStream.Flush();
            }
            catch (NotSupportedException)
            {
                // If the data doesn't fit in the memory stream NotSupportedException is thrown when DeflateStream
                // try to expand the fixed size MemoryStream.
                return (CompressionResult.PayloadNotCompressible, default);
            }

            offset += (int)memoryStream.Position;
            var compressedPayload = new ReadOnlyMemory<byte>(compressedData, 0, offset);
            return (CompressionResult.Success, compressedPayload);
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
        internal static ReadOnlyMemory<byte> Decompress(this ReadOnlyMemory<byte> payload, int maxSize)
        {
            ReadOnlySpan<byte> buffer = payload.Span;

            var payloadCompressionFormat = (CompressionFormat)buffer[0];
            if (payloadCompressionFormat == CompressionFormat.NotCompressed)
            {
                throw new InvalidOperationException("the payload is not compressed");
            }

            if (payloadCompressionFormat != CompressionFormat.Deflate)
            {
                throw new NotSupportedException($"cannot decompress compression format '{payloadCompressionFormat}'");
            }

            // Read the decompressed size that is written after the compression format byte when the payload is
            // compressed
            (int decompressedSize, int decompressedSizeLength) = buffer[1..].DecodeSize20();

            if (decompressedSize > maxSize)
            {
                throw new InvalidDataException(
                    @$"decompressed size of {decompressedSize
                    } bytes is greater than the configured IncomingFrameMaxSize value ({maxSize} bytes)");
            }

            // We are going to replace the payload with a new payload segment/array that contains a decompressed
            // payload.
            byte[] decompressedPayload = new byte[decompressedSize];

            int decompressedIndex = 0;

            // Set the payload's compression format to NotCompressed.
            decompressedPayload[decompressedIndex++] = (byte)CompressionFormat.NotCompressed;

            using var decompressedStream = new MemoryStream(decompressedPayload,
                                                            decompressedIndex,
                                                            decompressedPayload.Length - decompressedIndex);

            // Skip compression status and decompressed size in compressed payload.
            int compressedIndex = 1 + decompressedSizeLength;

            Stream compressedByteStream;
            if (MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> segment))
            {
                compressedByteStream = new MemoryStream(segment.Array!,
                                                        segment.Offset + compressedIndex,
                                                        segment.Count - compressedIndex,
                                                        writable: false);
            }
            else
            {
                throw new InvalidOperationException("the payload is not backed by an array");
            }

            using var compressed = new DeflateStream(compressedByteStream, CompressionMode.Decompress);
            compressed.CopyTo(decompressedStream);

            // "1" corresponds to the compress format that is in the decompressed size but is not compressed
            if (decompressedStream.Position + 1 != decompressedSize)
            {
                throw new InvalidDataException(
                    @$"received Deflate compressed payload with a decompressed size of only {decompressedStream.
                    Position + 1} bytes; expected {decompressedSize} bytes");
            }

            return decompressedPayload;
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
