// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        /// <summary>Reads a Slice segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless IsCanceled is true.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded.</exception>
        /// <remarks>The caller must call AdvanceTo on the reader, as usual. With encoding 1.1, this method reads all
        /// the remaining bytes in the reader; otherwise, this method reads the segment size in the segment and returns
        /// exactly segment size bytes.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(
            this PipeReader reader,
            SliceEncoding encoding,
            CancellationToken cancel)
        {
            // TODO: make maxSegmentSize configurable
            const int maxSegmentSize = 4 * 1024 * 1024;

            if (encoding == Encoding.Slice11)
            {
                // We read everything up to the max + 1.

                ReadResult readResult = await reader.ReadAtLeastAsync(maxSegmentSize + 1, cancel).ConfigureAwait(false);

                return readResult.IsCompleted && readResult.Buffer.Length <= maxSegmentSize ? readResult :
                    throw new InvalidDataException("segment size exceeds maximum value");
            }
            else
            {
                (int segmentSize, bool isCanceled, bool isCompleted) =
                    await reader.DecodeSegmentSizeAsync(cancel).ConfigureAwait(false);

                if (segmentSize > maxSegmentSize)
                {
                    throw new InvalidDataException($"segment size '{segmentSize}' exceeds maximum value");
                }

                if (isCanceled || segmentSize == 0)
                {
                    return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled, isCompleted);
                }

                if (isCompleted)
                {
                    throw new InvalidDataException($"no byte in segment with {segmentSize} bytes");
                }

                ReadResult readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    return readResult;
                }

                if (readResult.Buffer.Length < segmentSize)
                {
                    throw new InvalidDataException($"too few bytes in segment with {segmentSize} bytes");
                }

                return readResult.Buffer.Length == segmentSize ? readResult :
                    new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
            }
        }

        /// <summary>Decodes the size of a segment from a PipeReader.</summary>
        /// <remarks>The caller does not (and cannot) call AdvanceTo after calling this method. This method is not used
        /// when the segment is encoded with 1.1.</remarks>
        private static async ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            this PipeReader reader,
            CancellationToken cancel)
        {
            while (true)
            {
                ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    return (-1, true, false);
                }

                if (readResult.Buffer.IsEmpty)
                {
                    Debug.Assert(readResult.IsCompleted);
                    reader.AdvanceTo(readResult.Buffer.End);
                    return (0, false, true);
                }

                if (TryDecodeSize(readResult.Buffer, out int size, out long consumed))
                {
                    reader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                    return (size, false, readResult.IsCompleted && readResult.Buffer.Length == consumed);
                }
                else
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                }
            }

            static bool TryDecodeSize(ReadOnlySequence<byte> buffer, out int size, out long consumed)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                if (decoder.TryDecodeSize(out size))
                {
                    consumed = decoder.Consumed;
                    return true;
                }
                else
                {
                    consumed = 0;
                    return false;
                }
            }
        }
    }
}
