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
        /// <returns>A read result with the segment read from the reader unless <see cref="ReadResult.IsCanceled"/> is
        /// <c>true</c>.</returns>
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
                // We read everything up to the maxSegmentSize + 1.
                // It's maxSegmentSize + 1 and not maxSegmentSize because if the segment's size is maxSegmentSize,
                // we could get readResult.IsCompleted == false even though the full segment was read.

                ReadResult readResult = await reader.ReadAtLeastAsync(maxSegmentSize + 1, cancel).ConfigureAwait(false);

                return readResult.IsCompleted && readResult.Buffer.Length <= maxSegmentSize ? readResult :
                    throw new InvalidDataException("segment size exceeds maximum value");
            }
            else
            {
                ReadResult readResult;
                int segmentSize;

                while (true)
                {
                    readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        return readResult;
                    }
                    if (readResult.Buffer.IsEmpty)
                    {
                        Debug.Assert(readResult.IsCompleted);
                        return readResult; // size == 0, the caller will AdvanceTo on this buffer.
                    }

                    if (TryDecodeSize(readResult.Buffer, out segmentSize, out long consumed))
                    {
                        if (segmentSize == 0)
                        {
                            // The caller must consume this empty buffer.
                            return new ReadResult(
                                readResult.Buffer.Slice(readResult.Buffer.GetPosition(consumed), 0),
                                isCanceled: false,
                                isCompleted: readResult.IsCompleted && readResult.Buffer.Length == consumed);
                        }

                        if (segmentSize > maxSegmentSize)
                        {
                            throw new InvalidDataException($"segment size '{segmentSize}' exceeds maximum value");
                        }

                        if (readResult.IsCompleted && consumed + segmentSize > readResult.Buffer.Length)
                        {
                            throw new InvalidDataException(
                                $"payload stream has fewer than '{segmentSize}' bytes");
                        }

                        reader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                        break;
                    }
                    else if (readResult.IsCompleted)
                    {
                        reader.AdvanceTo(readResult.Buffer.End);
                        throw new InvalidDataException("received invalid segment size");
                    }
                    else
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        // and continue loop
                    }
                }

                readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

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

        internal static bool TryReadSegment(this PipeReader reader, SliceEncoding encoding, out ReadResult readResult)
        {
            readResult = default;
            return false;
        }

        private static bool TryDecodeSize(ReadOnlySequence<byte> buffer, out int size, out long consumed)
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
