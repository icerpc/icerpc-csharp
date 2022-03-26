// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        private const int MaxSegmentSize = 4 * 1024 * 1024; // TODO: make MaxSegmentSize configurable

        /// <summary>Reads a Slice segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless <see cref="ReadResult.IsCanceled"/> is
        /// <c>true</c>.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded or the segment size
        /// exceeds the max segment size.</exception>
        /// <remarks>The caller must call AdvanceTo on the reader, as usual. With encoding 1.1, this method reads all
        /// the remaining bytes in the reader; otherwise, this method reads the segment size in the segment and returns
        /// exactly segment size bytes. This method often examines the buffer it returns as part of ReadResult,
        /// therefore the caller should never examine less than Buffer.End.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(
            this PipeReader reader,
            SliceEncoding encoding,
            CancellationToken cancel)
        {
            if (encoding == Encoding.Slice11)
            {
                // We read everything up to the MaxSegmentSize + 1.
                // It's MaxSegmentSize + 1 and not MaxSegmentSize because if the segment's size is MaxSegmentSize,
                // we could get readResult.IsCompleted == false even though the full segment was read.

                ReadResult readResult = await reader.ReadAtLeastAsync(MaxSegmentSize + 1, cancel).ConfigureAwait(false);

                if (readResult.IsCompleted && readResult.Buffer.Length <= MaxSegmentSize)
                {
                    return readResult;
                }
                else
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw new InvalidDataException("segment size exceeds maximum value");
                }
            }
            else
            {
                ReadResult readResult;
                int segmentSize;

                while (true)
                {
                    readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                    try
                    {
                        if (TryReadSegment(ref readResult, out segmentSize, out long consumed))
                        {
                            return readResult;
                        }
                        else if (segmentSize >= 0)
                        {
                            Debug.Assert(segmentSize > 0);
                            Debug.Assert(consumed > 0);

                            // We decoded the segmentSize and examined the whole buffer but it was not sufficient.
                            reader.AdvanceTo(readResult.Buffer.GetPosition(consumed), readResult.Buffer.End);
                            break; // while
                        }
                        else if (readResult.IsCompleted)
                        {
                            // no point is looping to get more data to decode the segment size
                            throw new InvalidDataException("received invalid segment size");
                        }
                        else
                        {
                            reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                            // and continue loop with at least one additional byte
                        }
                    }
                    catch
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        throw;
                    }
                }

                readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    return readResult;
                }

                if (readResult.Buffer.Length < segmentSize)
                {
                    Debug.Assert(readResult.IsCompleted);
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw new InvalidDataException($"too few bytes in segment with {segmentSize} bytes");
                }

                return readResult.Buffer.Length == segmentSize ? readResult :
                    new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
            }
        }

        /// <summary>Attempts to read a Slice segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="readResult">The read result.</param>
        /// <returns><c>true</c> when <paramref name="readResult"/> contains the segment read synchronously, or the
        /// call was cancelled; otherwise, <c>false</c>.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded or the segment size
        /// exceeds the max segment size.</exception>
        /// <remarks>When this method returns <c>true</c>, the caller must call AdvanceTo on the reader, as usual. This
        /// method often examines the buffer it returns as part of ReadResult, therefore the caller should never
        /// examine less than Buffer.End when the return value is <c>true</c>. When this method returns <c>false</c>,
        /// the caller must call <see cref="ReadSegmentAsync"/>.</remarks>
        internal static bool TryReadSegment(this PipeReader reader, SliceEncoding encoding, out ReadResult readResult)
        {
            if (encoding == Encoding.Slice11)
            {
                if (reader.TryRead(out readResult))
                {
                    if (readResult.IsCanceled)
                    {
                        return true; // and the buffer does not matter
                    }

                    if (readResult.Buffer.Length > MaxSegmentSize)
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        throw new InvalidDataException(
                            $"segment size '{readResult.Buffer.Length}' exceeds maximum value");
                    }

                    if (readResult.IsCompleted)
                    {
                        return true;
                    }
                    else
                    {
                        // don't consume anything but mark the whole buffer as examined - we need more.
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    }
                }

                readResult = default;
                return false;
            }
            else
            {
                if (reader.TryRead(out readResult))
                {
                    try
                    {
                        if (TryReadSegment(ref readResult, out int _, out long _))
                        {
                            return true;
                        }
                        else
                        {
                            // we don't consume anything but examined the whole buffer since it's not sufficient.
                            reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                            readResult = default;
                            return false;
                        }
                    }
                    catch
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        throw;
                    }
                }
                else
                {
                    return false;
                }
            }
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

        private static bool TryReadSegment(ref ReadResult readResult, out int segmentSize, out long consumed)
        {
            consumed = 0;
            segmentSize = -1;

            if (readResult.IsCanceled)
            {
                return true; // and buffer does not matter
            }

            if (readResult.Buffer.IsEmpty)
            {
                Debug.Assert(readResult.IsCompleted);
                return true; // size == 0, the caller will AdvanceTo on this buffer.
            }

            if (TryDecodeSize(readResult.Buffer, out segmentSize, out consumed))
            {
                if (segmentSize > MaxSegmentSize)
                {
                    throw new InvalidDataException($"segment size '{segmentSize}' exceeds maximum value");
                }

                // When segmentSize is 0, the code below returns an empty buffer.
                if (readResult.Buffer.Length >= segmentSize + consumed)
                {
                    readResult = new ReadResult(
                        readResult.Buffer.Slice(readResult.Buffer.GetPosition(consumed), segmentSize),
                        isCanceled: false,
                        isCompleted: readResult.IsCompleted &&
                            readResult.Buffer.Length == consumed + segmentSize);

                    return true;
                }

                if (readResult.IsCompleted && consumed + segmentSize > readResult.Buffer.Length)
                {
                    throw new InvalidDataException(
                        $"payload has fewer than '{segmentSize}' bytes");
                }
                // else fall back as if TryDecodeSize returned false
            }
            return false;
        }
    }
}
