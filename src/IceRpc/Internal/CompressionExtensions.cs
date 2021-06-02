// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

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
        internal static (CompressionResult, ArraySegment<byte>) Compress(
            this IList<ArraySegment<byte>> payload,
            int payloadSize,
            CompressionLevel compressionLevel,
            int compressionMinSize)
        {
            var payloadCompressionFormat = (CompressionFormat)payload[0][0];

            if (payloadCompressionFormat != CompressionFormat.NotCompressed)
            {
                throw new InvalidOperationException("the payload is already compressed");
            }

            if (payloadSize < compressionMinSize)
            {
                return (CompressionResult.PayloadTooSmall, ArraySegment<byte>.Empty);
            }
            // Reserve memory for the compressed data, this should never be greater than the uncompressed data
            // otherwise we will just send the uncompressed data.
            byte[] compressedData = new byte[payloadSize];

            int offset = 0;
            // Set the compression status byte to Deflate compressed
            compressedData[offset++] = (byte)CompressionFormat.Deflate;
            // Write the size of the uncompressed data
            int sizeLength = OutputStream.GetSizeLength20(payloadSize);
            compressedData.AsSpan(offset, sizeLength).WriteFixedLengthSize20(payloadSize);
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
                foreach (ArraySegment<byte> segment in payload.Slice(1))
                {
                    deflateStream.Write(segment);
                }
                deflateStream.Flush();
            }
            catch (NotSupportedException)
            {
                // If the data doesn't fit in the memory stream NotSupportedException is thrown when DeflateStream
                // try to expand the fixed size MemoryStream.
                return (CompressionResult.PayloadNotCompressible, ArraySegment<byte>.Empty);
            }

            offset += (int)memoryStream.Position;
            var compressedPayload = new ArraySegment<byte>(compressedData, 0, offset);
            return (CompressionResult.Success, compressedPayload);
        }

        /// <summary>Decompresses the payload if it is compressed. Compressed payloads are only supported with the 2.0
        /// encoding.</summary>
        internal static ArraySegment<byte> Decompress(this ArraySegment<byte> payload, int maxSize)
        {
            ReadOnlySpan<byte> buffer = payload;

            var payloadCompressionFormat = (CompressionFormat)payload[0];
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
            (int decompressedSize, int decompressedSizeLength) = buffer[1..].ReadSize20();

            if (decompressedSize > maxSize)
            {
                throw new InvalidDataException(
                    @$"decompressed size of {decompressedSize
                    } bytes is greater than the configured IncomingFrameMaxSize value ({maxSize} bytes)");
            }

            // We are going to replace the Payload segment with a new Payload segment/array that contains a
            // decompressed payload.
            byte[] decompressedPayload = new byte[decompressedSize];

            int decompressedIndex = 0;

            // Set the payload's compression format to NotCompressed.
            decompressedPayload[decompressedIndex++] = (byte)CompressionFormat.NotCompressed;

            using var decompressedStream = new MemoryStream(decompressedPayload,
                                                            decompressedIndex,
                                                            decompressedPayload.Length - decompressedIndex);

            // Skip compression status and decompressed size in compressed payload.
            int compressedIndex = 1 + decompressedSizeLength;

            Debug.Assert(payload.Array != null);
            using var compressed = new DeflateStream(
                new MemoryStream(payload.Array,
                                 payload.Offset + compressedIndex,
                                 payload.Count - compressedIndex),
                CompressionMode.Decompress);
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
    }
}
