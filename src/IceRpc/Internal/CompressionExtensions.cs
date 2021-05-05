// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace IceRpc.Internal
{
    /// <summary>Indicates the result of the <see cref="CompressionExtensions.CompressPayload"/> operation.</summary>
    internal enum CompressionResult
    {
        /// <summary>The payload was successfully compressed.</summary>
        Success,

        /// <summary>The payload size is smaller than the configured compression threshold.</summary>
        PayloadTooSmall,

        /// <summary>The payload was not compressed, compressing it would increase its size.</summary>
        PayloadNotCompressible
    }

    /// <summary>Extensions methods to compress and decompress 2.0 encoded encapsulation payload.</summary>
    internal static class CompressionExtensions
    {
        /// <summary>Compresses the encapsulation payload using the specified compression format (by default, deflate).
        /// Compressed encapsulation payload is only supported with the 2.0 encoding.</summary>
        /// <returns>A <see cref="CompressionResult"/> value indicating the result of the compression operation.
        /// </returns>
        internal static CompressionResult CompressPayload(
            this OutgoingFrame frame,
            CompressionLevel compressionLevel,
            int compressionMinSize)
        {
            if (frame.PayloadEncoding != Encoding.V20)
            {
                throw new NotSupportedException("payload compression is only supported with the 2.0 encoding");
            }
            else
            {
                if (frame.PayloadCompressionFormat != CompressionFormat.Decompressed)
                {
                    throw new InvalidOperationException("the payload is already compressed");
                }

                int encapsulationOffset = frame is OutgoingResponse ? 1 : 0;

                // The encapsulation always starts in the first segment of the payload (at position 0 or 1).
                Debug.Assert(encapsulationOffset < frame.Payload[0].Count);

                int sizeLength = frame.Protocol == Protocol.Ice2 ?
                    frame.Payload[0][encapsulationOffset].ReadSizeLength20() : 4;

                Debug.Assert(frame.Payload.GetByte(encapsulationOffset + sizeLength + 2) == 0); // i.e. Decompressed

                int encapsulationSize = frame.Payload.GetByteCount() - encapsulationOffset; // this includes the size length
                if (encapsulationSize < compressionMinSize)
                {
                    return CompressionResult.PayloadTooSmall;
                }
                // Reserve memory for the compressed data, this should never be greater than the uncompressed data
                // otherwise we will just send the uncompressed data.
                byte[] compressedData = new byte[encapsulationOffset + encapsulationSize];
                // Copy the byte before the encapsulation, if any
                if (encapsulationOffset == 1)
                {
                    compressedData[0] = frame.Payload[0][0];
                }
                // Write the encapsulation header
                int offset = encapsulationOffset + sizeLength;
                compressedData[offset++] = frame.PayloadEncoding.Major;
                compressedData[offset++] = frame.PayloadEncoding.Minor;
                // Set the compression status byte to Deflate compressed
                compressedData[offset++] = (byte)CompressionFormat.Deflate;
                // Write the size of the uncompressed data
                compressedData.AsSpan(offset, sizeLength).WriteFixedLengthSize20(encapsulationSize - sizeLength);

                offset += sizeLength;
                using var memoryStream = new MemoryStream(compressedData, offset, compressedData.Length - offset);
                using var deflateStream = new DeflateStream(
                    memoryStream,
                    compressionLevel == CompressionLevel.Fastest ?
                        System.IO.Compression.CompressionLevel.Fastest :
                        System.IO.Compression.CompressionLevel.Optimal);
                try
                {
                    // The data to compress starts after the compression status byte, + 3 corresponds to (Encoding 2
                    // bytes, Compression status 1 byte)
                    foreach (ArraySegment<byte> segment in frame.Payload.Slice(encapsulationOffset + sizeLength + 3))
                    {
                        deflateStream.Write(segment);
                    }
                    deflateStream.Flush();
                }
                catch (NotSupportedException)
                {
                    // If the data doesn't fit in the memory stream NotSupportedException is thrown when DeflateStream
                    // try to expand the fixed size MemoryStream.
                    return CompressionResult.PayloadNotCompressible;
                }

                var payload = new List<ArraySegment<byte>>();
                offset += (int)memoryStream.Position;
                payload.Add(new ArraySegment<byte>(compressedData, 0, offset));

                // Rewrite the encapsulation size
                compressedData.AsSpan(encapsulationOffset, sizeLength).WriteEncapsulationSize(
                    offset - sizeLength - encapsulationOffset,
                    frame.Protocol.GetEncoding());

                frame.Payload = payload;
                frame.PayloadCompressionFormat = CompressionFormat.Deflate;

                return CompressionResult.Success;
            }
        }

        /// <summary>Decompresses the encapsulation payload if it is compressed. Compressed encapsulations are only
        /// supported with the 2.0 encoding.</summary>
        internal static void DecompressPayload(this IncomingFrame frame, int maxSize)
        {
            if (frame.PayloadCompressionFormat == CompressionFormat.Decompressed)
            {
                throw new InvalidOperationException("the encapsulation's payload is not compressed");
            }
            else if (frame.PayloadCompressionFormat != CompressionFormat.Deflate)
            {
                throw new NotSupportedException(@$"cannot decompress compression format '{frame.PayloadCompressionFormat}'");
            }
            else
            {
                int encapsulationOffset = frame is IncomingResponse ? 1 : 0;

                ReadOnlySpan<byte> buffer = frame.Payload.Slice(encapsulationOffset);
                int sizeLength = frame.Protocol == Protocol.Ice2 ? buffer[0].ReadSizeLength20() : 4;

                // Read the decompressed size that is written after the compression status byte when the payload is
                // compressed +3 corresponds to (Encoding 2 bytes, Compression status 1 byte)
                (int decompressedSize, int decompressedSizeLength) = buffer.Slice(sizeLength + 3).ReadSize20();

                if (decompressedSize > maxSize)
                {
                    throw new InvalidDataException(
                        @$"decompressed size of {decompressedSize
                        } bytes is greater than the configured IncomingFrameMaxSize value ({maxSize} bytes)");
                }

                // We are going to replace the Payload segment with a new Payload segment/array that contains a
                // decompressed encapsulation.
                byte[] decompressedPayload = new byte[encapsulationOffset + decompressedSizeLength + decompressedSize];

                // Write the result type and the encapsulation header "by hand" in decompressedPayload.
                if (encapsulationOffset == 1)
                {
                    decompressedPayload[0] = frame.Payload[0]; // copy the result type.
                }

                decompressedPayload.AsSpan(encapsulationOffset, decompressedSizeLength).WriteEncapsulationSize(
                    decompressedSize,
                    frame.Protocol.GetEncoding());

                int compressedIndex = encapsulationOffset + sizeLength;
                int decompressedIndex = encapsulationOffset + decompressedSizeLength;

                // Keep same encoding
                decompressedPayload[decompressedIndex++] = frame.Payload[compressedIndex++];
                decompressedPayload[decompressedIndex++] = frame.Payload[compressedIndex++];

                // Set the payload's compression format to Decompressed.
                decompressedPayload[decompressedIndex++] = (byte)CompressionFormat.Decompressed;

                // Verify PayloadCompressionFormat was set correctly.
                Debug.Assert(frame.Payload[compressedIndex] == (byte)CompressionFormat.Deflate);

                using var decompressedStream = new MemoryStream(decompressedPayload,
                                                                decompressedIndex,
                                                                decompressedPayload.Length - decompressedIndex);

                // Skip compression status and decompressed size in compressed payload.
                compressedIndex += 1 + decompressedSizeLength;

                Debug.Assert(frame.Payload.Array != null);
                using var compressed = new DeflateStream(
                    new MemoryStream(frame.Payload.Array,
                                     frame.Payload.Offset + compressedIndex,
                                     frame.Payload.Count - compressedIndex),
                    CompressionMode.Decompress);
                compressed.CopyTo(decompressedStream);

                // "3" corresponds to (Encoding 2 bytes and Compression status 1 byte), that are part of the
                // decompressedSize but are not Deflate compressed.
                if (decompressedStream.Position + 3 != decompressedSize)
                {
                    throw new InvalidDataException(
                        @$"received Deflate compressed payload with a decompressed size of only {decompressedStream.
                        Position + 3} bytes; expected {decompressedSize} bytes");
                }

                frame.Payload = decompressedPayload;
                frame.PayloadCompressionFormat = CompressionFormat.Decompressed;
            }
        }
    }
}
